using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace TifiraDefectSkin;

[ModInitializer(nameof(Init))]
public static class MainFile
{
    private const string HarmonyId = "sts2.moling.tifiradefectskin.enhanced";
    private const string BodySkinPath = "res://TifiraDefectSkin/tifirabody/Tifira.tres";
    private const string BattleReadyScenePath = "res://TifiraDefectSkin/vfx/battle_ready_point.tscn";
    private const string TargetCharacterId = "DEFECT";

    private static readonly string[] BackgroundScenes =
    [
        "res://scenes/character_select_bg/defect/character_select_bg.tscn",
        "res://scenes/character_select_bg/defect2/characterselect_defect_live.tscn",
    ];

    private static readonly Dictionary<string, float> LastAnimMsecByKey = new();
    private static Resource? _bodySkin;
    private static int _currentBgIndex;

    public static void Init()
    {
        try
        {
            TifiraConfig.LoadSettings();
            MolingGlobalConfig.LoadSettings();
        }
        catch
        {
            // Config is optional; animation patches should still load if settings are unreadable.
        }

        var harmony = new Harmony(HarmonyId);
        try
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            ApplySafeReflectionPatches(harmony);
            Log.Info("[TifiraDefectSkin] loaded v1.1.0 enhanced: select art, voice, combat body, battle ready, attack/cast/block/orb/victory/death animation triggers.", 2);
        }
        catch (Exception ex)
        {
            Log.Error("[TifiraDefectSkin] enhanced patch init failed: " + ex, 2);
        }
    }

    private static void ApplySafeReflectionPatches(Harmony harmony)
    {
        // These method names changed several times between PC/mobile builds. Patch opportunistically.
        TryPatch(harmony, typeof(NCombatRoom), "_Ready", postfix: nameof(OnCombatRoomReadyPostfix));

        foreach (var method in new[] { "OnMousePressed", "OnTouchPressed", "OnPointerPressed", "OnInputPressed", "OnCardPressed" })
            TryPatch(harmony, typeof(NHandCardHolder), method, postfix: nameof(OnHandCardPressedPostfix));

        foreach (var method in new[] { "OnMouseReleased", "OnTouchReleased", "OnPointerReleased", "OnInputReleased", "OnCardReleased" })
            TryPatch(harmony, typeof(NHandCardHolder), method, postfix: nameof(OnHandCardReleasedPostfix));

        TryPatch(harmony, typeof(NCardPlay), "CancelPlayCard", postfix: nameof(OnCancelPlayCardPostfix));

        // Optional/non-essential screens.
        foreach (var typeName in new[]
                 {
                     "MegaCrit.Sts2.Core.Nodes.Screens.GameOver.NGameOverCharacter",
                     "MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen.NGameOverCharacter",
                     "MegaCrit.Sts2.Core.Nodes.Screens.GameOver.NGameOverScreen",
                     "MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen.NGameOverScreen",
                     "MegaCrit.Sts2.Core.Nodes.Events.NEventCharacter",
                     "MegaCrit.Sts2.Core.Nodes.Events.NEventRoom",
                     "MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom",
                     "MegaCrit.Sts2.Core.Nodes.Screens.Events.NEventScreen",
                 })
        {
            var type = AccessTools.TypeByName(typeName);
            if (type != null)
                TryPatch(harmony, type, "_Ready", postfix: nameof(OnGenericSpineScreenReadyPostfix));
        }
    }

    private static void TryPatch(Harmony harmony, Type type, string method, string? prefix = null, string? postfix = null)
    {
        try
        {
            var original = AccessTools.Method(type, method);
            if (original == null)
                return;

            var pre = prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(MainFile), prefix));
            var post = postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(MainFile), postfix));
            harmony.Patch(original, pre, post);
            Log.Info($"[TifiraDefectSkin] patched {type.Name}.{method}", 2);
        }
        catch (Exception ex)
        {
            Log.Warn($"[TifiraDefectSkin] skip patch {type.Name}.{method}: {ex.Message}", 2);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "_Ready")]
    private static class CharacterSelectReadyPatch
    {
        private static void Postfix()
        {
            // Do not force-load the Defect background while the main menu pre-creates
            // submenus: Godot prints noisy UID fallback traces for the imported Spine
            // resources.  The selected Defect scene is loaded lazily in SelectCharacter.
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), "SelectCharacter")]
    private static class CharacterSelectPatch
    {
        private static void Postfix(NCharacterSelectScreen __instance, CharacterModel characterModel)
        {
            try
            {
                UpdateCharacterSelectBackground(__instance, characterModel);
            }
            catch (Exception ex)
            {
                Log.Warn("[TifiraDefectSkin] character select bg failed: " + ex.Message, 2);
            }
        }
    }

    [HarmonyPatch(typeof(NCreature), "_Ready")]
    [HarmonyPriority(Priority.Last)]
    private static class CombatCreatureReadyPatch
    {
        private static void Postfix(NCreature __instance)
        {
            try
            {
                if (__instance?.Entity?.Player?.Character is not Defect)
                    return;

                ApplyTifiraBody(__instance, 1.30f, new Vector2(0, 0), playEnter: NCombatRoom.Instance != null);
            }
            catch (Exception ex)
            {
                Log.Warn("[TifiraDefectSkin] combat body patch failed: " + ex.Message, 2);
            }
        }
    }

    [HarmonyPatch(typeof(NMerchantCharacter), "_Ready")]
    private static class MerchantReadyPatch
    {
        private static void Postfix(NMerchantCharacter __instance)
        {
            try
            {
                var player = ResolvePlayerForIndexedScene(NMerchantRoom.Instance, ((Node)__instance).GetIndex());
                if (player?.Character is not Defect)
                    return;

                ApplyFirstSpineChild((Node)__instance, 7.5f, Vector2.Zero, "b_idle", loop: true);
            }
            catch (Exception ex)
            {
                Log.Warn("[TifiraDefectSkin] merchant skin failed: " + ex.Message, 2);
            }
        }
    }

    [HarmonyPatch(typeof(NRestSiteCharacter), "_Ready")]
    private static class RestSiteReadyPatch
    {
        private static void Postfix(NRestSiteCharacter __instance)
        {
            try
            {
                if (__instance?.Player?.Character is not Defect)
                    return;

                ApplyFirstSpineChild((Node)__instance, 3.5f, new Vector2(-45, 290), "overgrowth_loop", loop: true);
            }
            catch (Exception ex)
            {
                Log.Warn("[TifiraDefectSkin] rest skin failed: " + ex.Message, 2);
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class BeforeCardPlayedPatch
    {
        private static void Prefix(object combatState, CardPlay cardPlay)
        {
            try
            {
                var card = cardPlay.Card;
                if (card?.Owner?.Character is not Defect)
                    return;

                var anim = ChooseCardAnim(card);
                BattleReadyOverlay.NotifyBeforeCardPlayed(cardPlay);
                PlayPlayerAnim(card.Owner, anim, returnIdle: true, cooldownMs: 120);
                TifiraAudioManager.PlayLogicalSound(anim, null);
            }
            catch (Exception ex)
            {
                Log.Warn("[TifiraDefectSkin] card anim failed: " + ex.Message, 2);
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
    private static class AfterBlockGainedPatch
    {
        private static void Prefix(object combatState, Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
        {
            try
            {
                if (amount <= 0 || creature?.Player?.Character is not Defect)
                    return;

                // Defense/support response. Use a small cooldown so multi-card block packets do not stutter.
                PlayPlayerAnim(creature.Player, "cast2", returnIdle: true, cooldownMs: 500);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterOrbChanneled))]
    private static class AfterOrbChanneledPatch
    {
        private static void Prefix(object combatState, PlayerChoiceContext choiceContext, Player player, OrbModel orb)
        {
            try
            {
                if (player?.Character is Defect)
                    PlayPlayerAnim(player, "cast3", returnIdle: true, cooldownMs: 250);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterOrbEvoked))]
    private static class AfterOrbEvokedPatch
    {
        private static void Prefix(PlayerChoiceContext choiceContext, object combatState, OrbModel orb, IEnumerable<Creature> targets)
        {
            try
            {
                var player = orb?.Owner;
                if (player?.Character is Defect)
                    PlayPlayerAnim(player, "cast4", returnIdle: true, cooldownMs: 250);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
    private static class AfterDamageReceivedPatch
    {
        private static void Prefix(PlayerChoiceContext choiceContext, IRunState runState, object? combatState, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
        {
            try
            {
                if (target?.Player?.Character is Defect)
                    PlayPlayerAnim(target.Player, "hurt", returnIdle: true, cooldownMs: 350);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
    private static class AfterCombatVictoryPatch
    {
        private static void Postfix(IRunState runState, object? combatState, CombatRoom room)
        {
            try
            {
                foreach (var node in NCombatRoom.Instance?.CreatureNodes ?? Array.Empty<NCreature>())
                {
                    if (node.Entity?.Player?.Character is Defect)
                    {
                        PlayOnNode(node, "victory_ready", "victory", loopNext: true, force: true);
                    }
                }
            }
            catch { }
        }
    }

    public static void OnCombatRoomReadyPostfix()
    {
        BattleReadyOverlay.InitializePreload();
    }

    public static void OnHandCardPressedPostfix(NHandCardHolder __instance, object[]? __args)
    {
        if (!IsPrimaryPress(__args))
            return;

        var card = ((NCardHolder)__instance).CardModel;
        if (card?.Owner?.Character is Defect)
            BattleReadyOverlay.TryStartHold(card);
    }

    public static void OnHandCardReleasedPostfix(NHandCardHolder __instance, object[]? __args)
    {
        if (!IsPrimaryPress(__args))
            return;

        var card = ((NCardHolder)__instance).CardModel;
        if (card?.Owner?.Character is Defect)
            BattleReadyOverlay.NotifyReleased(card);
    }

    public static void OnCancelPlayCardPostfix(NCardPlay __instance)
    {
        var card = __instance.Holder?.CardModel;
        if (card != null)
            BattleReadyOverlay.NotifyCanceled(card);
    }

    public static void OnGenericSpineScreenReadyPostfix(Node __instance)
    {
        try
        {
            ApplyGenericSpineSkin(__instance);
            var deferred = Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(__instance))
                    ApplyGenericSpineSkin(__instance);
            });
            deferred.CallDeferred();
        }
        catch
        {
            // Generic non-combat screens are optional.
        }
    }

    private static void UpdateCharacterSelectBackground(NCharacterSelectScreen screen, CharacterModel model)
    {
        var isDefect = string.Equals(model.Id.Entry, TargetCharacterId, StringComparison.OrdinalIgnoreCase);

        var vanillaBg = ((Node)screen).GetNodeOrNull<Control>("AnimatedBg");
        var container = ((Node)screen).GetNodeOrNull<Control>("TifiraCustomBg");
        var button = ((Node)screen).GetNodeOrNull<Button>("TifiraBgBtn");

        if (!isDefect)
        {
            if (button != null) button.Visible = false;
            if (container != null)
            {
                container.Visible = false;
                foreach (var child in container.GetChildren())
                    child.QueueFree();
            }
            if (vanillaBg != null) vanillaBg.Visible = true;
            return;
        }

        if (vanillaBg != null)
            vanillaBg.Visible = false;

        if (container == null)
        {
            container = new Control { Name = "TifiraCustomBg" };
            container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            ((Node)screen).AddChild(container);
            ((Node)screen).MoveChild(container, 0);
        }

        container.Visible = true;
        LoadCharacterSelectScene(container, BackgroundScenes[_currentBgIndex]);

        if (button == null)
        {
            button = new Button
            {
                Name = "TifiraBgBtn",
                Text = "切换背景",
                Position = new Vector2(300, 900),
                CustomMinimumSize = new Vector2(260, 72),
            };
            button.AddThemeFontSizeOverride("font_size", 32);
            button.Pressed += () =>
            {
                _currentBgIndex = (_currentBgIndex + 1) % BackgroundScenes.Length;
                LoadCharacterSelectScene(container, BackgroundScenes[_currentBgIndex]);
            };
            ((Node)screen).AddChild(button);
        }

        button.Visible = true;
    }

    private static void LoadCharacterSelectScene(Control container, string path)
    {
        foreach (var child in container.GetChildren())
            child.QueueFree();

        var scene = ResourceLoader.Load<PackedScene>(path, null, ResourceLoader.CacheMode.Reuse);
        var inst = scene?.Instantiate();
        if (inst == null)
            return;

        container.AddChild(inst);
        inst.Owner = container;

        foreach (var spine in CollectSpineNodes(inst))
        {
            AttachAudioListener(spine);
            var sprite = new MegaSprite(spine);
            var state = TryGetAnimationStateCompat(sprite);
            if (state == null)
                continue;
            var anim = "b_idle";
            if (!sprite.HasAnimation(anim) && sprite.HasAnimation("idle_loop")) anim = "idle_loop";
            if (!sprite.HasAnimation(anim) && sprite.HasAnimation("enter")) anim = "enter";
            if (sprite.HasAnimation(anim))
            {
                state.SetAnimation(anim, true);
                state.SetTimeScale(1.05f);
            }
        }
    }

    private static void ApplyTifiraBody(NCreature creatureNode, float scale, Vector2 offset, bool playEnter)
    {
        var sprite = creatureNode.Visuals?.SpineBody;
        var body = creatureNode.Body;
        var skin = GetBodySkin();
        if (sprite == null || body == null || skin == null)
            return;

        if (body.HasMeta("is_tifira_enhanced"))
            return;

        body.SetMeta("is_tifira_enhanced", Variant.From(true));
        AttachAudioListener(body);
        sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skin));
        // Defect's vanilla Spine node is authored very small; multiplying its
        // existing scale makes Tifira nearly invisible in combat.  The source
        // skin is authored for an absolute 1.3 combat scale.
        var sx = body.Scale.X < 0 ? -scale : scale;
        var sy = body.Scale.Y < 0 ? -scale : scale;
        body.Scale = new Vector2(sx, sy);
        body.Position += offset;

        if (playEnter && sprite.HasAnimation("enter"))
            PlayOnSprite(sprite, "enter", "idle_loop", loopNext: true, force: true);
        else
            PlayOnSprite(sprite, "idle_loop", null, loopNext: true, force: true);
    }

    private static void ApplyFirstSpineChild(Node root, float scale, Vector2 offset, string preferredAnim, bool loop)
    {
        var skin = GetBodySkin();
        if (skin == null)
            return;

        foreach (var spine in CollectSpineNodes(root))
        {
            if (spine.HasMeta("is_tifira_enhanced"))
                continue;

            spine.SetMeta("is_tifira_enhanced", Variant.From(true));
            AttachAudioListener(spine);
            var sprite = new MegaSprite(spine);
            sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skin));
            spine.Scale = new Vector2(Mathf.Abs(spine.Scale.X) * scale, Mathf.Abs(spine.Scale.Y) * scale);
            spine.Position += offset;
            PlayOnSprite(sprite, preferredAnim, null, loopNext: loop, force: true);
        }
    }

    private static void ApplyGenericSpineSkin(Node root)
    {
        var skin = GetBodySkin();
        if (skin == null)
            return;

        foreach (var spine in CollectSpineNodes(root))
        {
            if (spine.HasMeta("is_tifira_enhanced") || IsNodeInCombat(spine))
                continue;

            var sprite = new MegaSprite(spine);
            if (sprite.GetSkeleton()?.GetData() == null)
                continue;

            spine.SetMeta("is_tifira_enhanced", Variant.From(true));
            AttachAudioListener(spine);
            sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skin));
            spine.Scale *= 1.3f;
            PlayOnSprite(sprite, sprite.HasAnimation("idle_loop") ? "idle_loop" : "b_idle", null, loopNext: true, force: true);
        }
    }

    private static Resource? GetBodySkin()
    {
        if (_bodySkin != null && GodotObject.IsInstanceValid(_bodySkin))
            return _bodySkin;

        _bodySkin = ResourceLoader.Load<Resource>(BodySkinPath, null, ResourceLoader.CacheMode.Reuse);
        return _bodySkin;
    }

    private static Player? ResolvePlayerForIndexedScene(object? room, int index)
    {
        try
        {
            if (room == null)
                return null;
            var players = Traverse.Create(room).Field("_players").GetValue<List<Player>>();
            if (players != null && index >= 0 && index < players.Count)
                return players[index];
        }
        catch { }
        return null;
    }

    private static string ChooseCardAnim(CardModel card)
    {
        if (IsBigMove(card))
            return "cast4";

        if (card.OrbEvokeType != OrbEvokeType.None)
            return "cast4";

        if (card.Type == CardType.Attack)
            return IsMultiAttack(card) ? "attack2" : "attack";

        if (card.GainsBlock || card.Tags.Contains(CardTag.Defend))
            return "cast2";

        if (card.Type == CardType.Power)
            return "cast4";

        return "cast";
    }

    private static bool IsMultiAttack(CardModel card)
    {
        try
        {
            if (card.TargetType == TargetType.AllEnemies || card.TargetType == TargetType.AllAllies)
                return true;

            var id = card.Id.Entry.ToLowerInvariant();
            return id.Contains("multi") || id.Contains("sweeping") || id.Contains("rebound") || id.Contains("doom");
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBigMove(CardModel card)
    {
        try
        {
            if (card.Rarity == CardRarity.Rare)
                return true;
            if (card.EnergyCost.Canonical >= 3)
                return true;
            var id = card.Id.Entry.ToLowerInvariant();
            return id.Contains("meteor") || id.Contains("hyper") || id.Contains("thunder") || id.Contains("ultimate") || id.Contains("ux") || id.Contains("ug");
        }
        catch
        {
            return false;
        }
    }

    private static void PlayPlayerAnim(Player player, string anim, bool returnIdle, float cooldownMs)
    {
        if (player?.Character is not Defect)
            return;

        var node = NCombatRoom.Instance?.GetCreatureNode(player.Creature);
        if (node == null)
            return;

        PlayOnNode(node, anim, returnIdle ? "idle_loop" : null, loopNext: returnIdle, force: false, cooldownMs: cooldownMs);
    }

    private static void PlayOnNode(NCreature node, string anim, string? next, bool loopNext, bool force, float cooldownMs = 0)
    {
        var sprite = node.Visuals?.SpineBody;
        if (sprite == null)
            return;
        PlayOnSprite(sprite, anim, next, loopNext, force, cooldownMs, node.Entity?.Player?.ToString() ?? "player");
    }

    private static bool PlayOnSprite(MegaSprite sprite, string anim, string? next, bool loopNext, bool force, float cooldownMs = 0, string key = "global")
    {
        try
        {
            if (!force && cooldownMs > 0)
            {
                var now = Time.GetTicksMsec();
                var fullKey = key + ":" + anim;
                if (LastAnimMsecByKey.TryGetValue(fullKey, out var last) && now - last < cooldownMs)
                    return false;
                LastAnimMsecByKey[fullKey] = now;
            }

            if (!sprite.HasAnimation(anim))
                anim = FallbackAnim(sprite, anim);
            if (!sprite.HasAnimation(anim))
                return false;

            var state = TryGetAnimationStateCompat(sprite);
            if (state == null)
                return false;

            state.SetAnimation(anim, loopNext && next == null, 0);
            if (next != null && sprite.HasAnimation(next))
                state.AddAnimation(next, 0, loopNext, 0);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("[TifiraDefectSkin] play anim failed: " + anim + " / " + ex.Message, 2);
            return false;
        }
    }

    private static string FallbackAnim(MegaSprite sprite, string requested)
    {
        if ((requested == "cast3" || requested == "cast4") && sprite.HasAnimation("cast2")) return "cast2";
        if (requested.StartsWith("cast") && sprite.HasAnimation("cast")) return "cast";
        if (requested == "attack2" && sprite.HasAnimation("attack")) return "attack";
        if ((requested == "b_into" || requested == "b_out") && sprite.HasAnimation("b_idle")) return "b_idle";
        if (sprite.HasAnimation("idle_loop")) return "idle_loop";
        if (sprite.HasAnimation("b_idle")) return "b_idle";
        return requested;
    }

    private static MegaAnimationState? TryGetAnimationStateCompat(MegaSprite sprite)
    {
        try
        {
            // v0.107 added TryGetAnimationState(); v0.103 only has GetAnimationState().
            var tryMethod = typeof(MegaSprite).GetMethod("TryGetAnimationState", Type.EmptyTypes);
            if (tryMethod != null)
                return tryMethod.Invoke(sprite, null) as MegaAnimationState;

            return sprite.GetAnimationState();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<Node2D> CollectSpineNodes(Node root)
    {
        if (!GodotObject.IsInstanceValid(root))
            yield break;

        if (root is Node2D n2 && root.GetClass() == "SpineSprite")
            yield return n2;

        foreach (var child in root.GetChildren())
        {
            foreach (var spine in CollectSpineNodes(child))
                yield return spine;
        }
    }

    private static bool IsNodeInCombat(Node node)
    {
        for (var cur = node; cur != null; cur = cur.GetParent())
        {
            var name = cur.GetType().Name;
            if (name.Contains("CombatRoom", StringComparison.OrdinalIgnoreCase) || name.Contains("NCreature", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void AttachAudioListener(Node2D spineNode)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(spineNode) || spineNode.HasMeta("tifira_audio_listener"))
                return;

            spineNode.SetMeta("tifira_audio_listener", Variant.From(true));
            spineNode.Connect("animation_started",
                Callable.From<GodotObject, GodotObject, GodotObject>((_, _, trackObj) =>
                {
                    try
                    {
                        var animObj = trackObj?.Call("get_animation").AsGodotObject();
                        var animName = animObj?.Call("get_name").AsString() ?? "";
                        TifiraAudioManager.PlayAnimSound(animName, spineNode);
                    }
                    catch { }
                }));
        }
        catch { }
    }

    private static bool IsPrimaryPress(object[]? args)
    {
        if (args == null)
            return true;

        foreach (var arg in args)
        {
            if (arg is InputEventMouseButton mouse && mouse.ButtonIndex != MouseButton.Left)
                return false;
        }
        return true;
    }

    private static class BattleReadyOverlay
    {
        private static Node? _node;
        private static MegaSprite? _sprite;
        private static CardModel? _activeCard;
        private static bool _busy;
        private static bool _played;
        private static ulong _token;

        public static void InitializePreload()
        {
            try
            {
                _node = null;
                _sprite = null;

                var scene = ResourceLoader.Load<PackedScene>(BattleReadyScenePath, null, ResourceLoader.CacheMode.Reuse);
                var inst = scene?.Instantiate();
                if (inst == null)
                    return;

                var parent = (Node?)NCombatRoom.Instance?.CombatVfxContainer ?? NCombatRoom.Instance;
                parent?.AddChild(inst);
                _node = inst;

                if (inst is CanvasItem ci)
                {
                    ci.Visible = false;
                    ci.ZIndex = 30;
                }

                var spine = CollectSpineNodes(inst).FirstOrDefault();
                if (spine != null)
                {
                    AttachAudioListener(spine);
                    _sprite = new MegaSprite(spine);
                    _sprite.ConnectAnimationCompleted(Callable.From<GodotObject, GodotObject, GodotObject>((_, _, _) =>
                    {
                        if (_busy && _played)
                            Hide();
                    }));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[TifiraDefectSkin] battle ready preload failed: " + ex.Message, 2);
            }
        }

        public static void TryStartHold(CardModel card)
        {
            if (!TifiraConfig.UseBattleReadyAnim || _busy || card.Owner?.Character is not Defect)
                return;
            if (_node == null || _sprite == null || !GodotObject.IsInstanceValid(_node))
                InitializePreload();
            if (_node == null || _sprite == null)
                return;

            if (_node is CanvasItem ci)
                ci.Visible = true;

            _activeCard = card;
            _busy = true;
            _played = false;
            _token++;
            PlayOnSprite(_sprite, _sprite.HasAnimation("b_into") ? "b_into" : "b_idle", "b_idle", loopNext: true, force: true);
        }

        public static void NotifyReleased(CardModel card)
        {
            if (!_busy || _activeCard != card)
                return;
            _ = WaitThenOut(_token);
        }

        public static void NotifyCanceled(CardModel card)
        {
            if (_busy && _activeCard == card && !_played)
                StartOut();
        }

        public static void NotifyBeforeCardPlayed(CardPlay cardPlay)
        {
            if (!_busy || _activeCard != cardPlay.Card || _sprite == null)
                return;
            _played = true;
            var anim = cardPlay.Card.Type == CardType.Attack ? "card_attack" : "card_casting";
            PlayOnSprite(_sprite, anim, null, loopNext: false, force: true);
            _ = HideSoon(_token);
        }

        private static async Task WaitThenOut(ulong token)
        {
            await Task.Delay(120);
            if (_busy && !_played && token == _token)
                StartOut();
        }

        private static void OnActiveCardPlayed()
        {
            if (_activeCard == null || _sprite == null)
                return;

            _played = true;
            var anim = _activeCard.Type == CardType.Attack ? "card_attack" : "card_casting";
            PlayOnSprite(_sprite, anim, null, loopNext: false, force: true);
        }

        private static void StartOut()
        {
            if (_sprite != null && _sprite.HasAnimation("b_out"))
            {
                _played = true;
                PlayOnSprite(_sprite, "b_out", null, loopNext: false, force: true);
            }
            else
            {
                Hide();
            }
        }

        private static void Hide()
        {
            _activeCard = null;
            _busy = false;
            _played = false;
            if (_node is CanvasItem ci)
                ci.Visible = false;
        }

        private static async Task HideSoon(ulong token)
        {
            await Task.Delay(900);
            if (_busy && _played && token == _token)
                Hide();
        }
    }
}

public static class TifiraConfig
{
    private const string ConfigPath = "user://tifira_mod_settings.cfg";
    public static float TifiraVolume = 1f;
    public static float OverlayScale = 1f;
    public static float OverlayOffsetY = 0f;
    public static bool UseBattleReadyAnim = true;

    public static void LoadSettings()
    {
        if (!Godot.FileAccess.FileExists(ConfigPath))
            return;
        using var file = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Read);
        var parts = file?.GetAsText().Split(',') ?? Array.Empty<string>();
        if (parts.Length >= 4)
        {
            var offset = parts.Length >= 5 ? 1 : 0;
            float.TryParse(parts[offset], out TifiraVolume);
            float.TryParse(parts[offset + 1], out OverlayScale);
            float.TryParse(parts[offset + 2], out OverlayOffsetY);
            bool.TryParse(parts[offset + 3], out UseBattleReadyAnim);
        }
    }

    public static void SaveSettings()
    {
        using var file = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString($"{TifiraVolume},{OverlayScale},{OverlayOffsetY},{UseBattleReadyAnim}");
    }
}

public static class MolingGlobalConfig
{
    private const string ConfigPath = "user://moling_workshop_settings.cfg";
    public static float GlobalVolume = 1f;

    public static void LoadSettings()
    {
        if (!Godot.FileAccess.FileExists(ConfigPath))
            return;
        using var file = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Read);
        if (float.TryParse(file?.GetAsText(), out var value))
            GlobalVolume = Mathf.Clamp(value, 0f, 1f);
    }

    public static void SaveSettings()
    {
        using var file = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(GlobalVolume.ToString());
    }
}

public static class TifiraAudioManager
{
    private static readonly Dictionary<string, AudioStream> AudioCache = new();
    private static ulong _lastIdlePlayTime;
    private static ulong _lastShopPlayTime;
    private static ulong _lastCampfirePlayTime;

    private static readonly string[] AttackSounds =
    [
        "res://TifiraDefectSkin/audio/attack.ogg",
        "res://TifiraDefectSkin/audio/attack2.ogg",
        "res://TifiraDefectSkin/audio/attack3.ogg",
    ];

    private static readonly string[] CastSounds =
    [
        "res://TifiraDefectSkin/audio/cast.ogg",
        "res://TifiraDefectSkin/audio/cast2.ogg",
    ];

    public static void PlayAnimSound(string animName, Node parent)
    {
        var ticks = Time.GetTicksMsec();
        string? path = animName switch
        {
            "enter" => "res://TifiraDefectSkin/audio/enter.ogg",
            "victory_ready" or "victory" => "res://TifiraDefectSkin/audio/victory.ogg",
            "attack" or "attack2" or "card_attack" => AttackSounds[(int)(GD.Randi() % AttackSounds.Length)],
            "cast" or "cast2" or "cast3" or "cast4" or "card_casting" => CastSounds[(int)(GD.Randi() % CastSounds.Length)],
            "hurt" => "res://TifiraDefectSkin/audio/hurt.ogg",
            "die" => "res://TifiraDefectSkin/audio/die.ogg",
            "idle_loop" when ticks > _lastIdlePlayTime + 15000 && GD.Randf() < 0.30f => "res://TifiraDefectSkin/audio/idle_loop.ogg",
            "b_idle" when _lastShopPlayTime == 0 || ticks > _lastShopPlayTime + 90000 => "res://TifiraDefectSkin/audio/b_idle.ogg",
            "overgrowth_loop" when _lastCampfirePlayTime == 0 || (ticks > _lastCampfirePlayTime + 120000 && GD.Randf() < 0.40f) => "res://TifiraDefectSkin/audio/overgrowth_loop.ogg",
            _ => null,
        };

        if (path == null)
            return;
        if (animName == "idle_loop") _lastIdlePlayTime = ticks;
        if (animName == "b_idle") _lastShopPlayTime = ticks;
        if (animName == "overgrowth_loop") _lastCampfirePlayTime = ticks;
        PlaySound(path, parent);
    }

    public static void PlayLogicalSound(string animName, Node? parent)
    {
        PlayAnimSound(animName, parent ?? ((SceneTree)Engine.GetMainLoop()).Root);
    }

    private static void PlaySound(string path, Node parent)
    {
        var volume = MolingGlobalConfig.GlobalVolume * TifiraConfig.TifiraVolume;
        if (volume <= 0.01f)
            return;

        if (!AudioCache.TryGetValue(path, out var stream))
        {
            stream = ResourceLoader.Load<AudioStream>(path, null, ResourceLoader.CacheMode.Reuse);
            if (stream == null)
                return;
            AudioCache[path] = stream;
        }

        var player = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = Mathf.LinearToDb(volume),
        };
        player.Finished += player.QueueFree;
        parent.AddChild(player);
        player.Play();
    }
}
