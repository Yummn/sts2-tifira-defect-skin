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

    private sealed class AnimGateState
    {
        public string CurrentAnim = "";
        public int Priority;
        public ulong UntilMsec;
    }

    private static readonly Dictionary<string, ulong> LastAnimMsecByKey = new();
    private static readonly Dictionary<string, AnimGateState> AnimGateByKey = new();
    private static readonly Dictionary<int, ulong> MobileCardAnimTokens = new();
    private static readonly MethodInfo? TryGetAnimationStateMethod =
        typeof(MegaSprite).GetMethod("TryGetAnimationState", Type.EmptyTypes);
    private static Resource? _bodySkin;
    private static int _currentBgIndex;
    private static bool? _isMobileRuntime;

    public static void Init()
    {
        try
        {
            TifiraConfig.LoadSettings();
            MolingGlobalConfig.LoadSettings();
            PreloadCommonResources();
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
            Log.Info("[TifiraDefectSkin] loaded v1.1.9 card-play performance: restored idle animation, single-Spine mobile play path, deferred body action, and one victory voice.", 2);
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

        // Do not hook raw card press/release for Battle Ready.  On mobile the
        // same press event is used for viewing/selecting a card, so the large
        // left cut-in was being instantiated/replayed while simply reading or
        // dragging cards.  Start the overlay only after NCardPlay enters the
        // play-zone/targeting path instead.
        TryPatch(harmony, typeof(NCardPlay), "TryShowEvokingOrbs", postfix: nameof(OnCardPlayTargetingPostfix));
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
                TifiraAudioManager.BeginCardActionAudioGate(anim);
                BattleReadyOverlay.NotifyBeforeCardPlayed(cardPlay);
                ScheduleCardPlayBodyAnim(card.Owner, anim);
                // The attached Spine animation_started listener plays the
                // matching voice/SFX.  Calling PlayLogicalSound here as well
                // caused the same clip to be started twice on Android, heard
                // as a short echo/重音.
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
                {
                    // A played card already starts the matching body animation.
                    // On Android, replaying cast3 for every orb packet made
                    // multi-orb cards repeatedly reskin/update the same large
                    // Spine skeleton and caused the sharpest combat frame drops.
                    if (IsMobileRuntime() && TifiraAudioManager.IsCardActionGateActive())
                        return;

                    // Orb channel/evoke hooks are follow-up visuals.  They often
                    // fire after the card body animation and were causing one
                    // card to play multiple cast voices on Android.
                    TifiraAudioManager.SuppressNextAnimSound("cast3", 700);
                    PlayPlayerAnim(player, "cast3", returnIdle: true, cooldownMs: 250);
                }
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
                {
                    if (IsMobileRuntime() && TifiraAudioManager.IsCardActionGateActive())
                        return;

                    // Keep the orb/beam animation, but do not let every evoked
                    // orb start a fresh Tifira voice line.
                    TifiraAudioManager.SuppressNextAnimSound("cast4", 900);
                    PlayPlayerAnim(player, "cast4", returnIdle: true, cooldownMs: 250);
                }
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
                // Hook.AfterCombatVictory may be dispatched repeatedly while the
                // reward room settles.  Only the first dispatch may restart the
                // victory animation or open the victory voice gate.
                if (!TifiraAudioManager.BeginVictorySequence())
                    return;
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
        TifiraAudioManager.ResetForCombat();
        TifiraAudioManager.ScheduleCombatVoiceWarmup();
        // The Battle Ready skeleton is substantially larger than the body.
        // Instantiating it at every combat entry creates a visible Android hitch
        // even when the player never holds a card.  Mobile loads it only after a
        // card stays in the targeting zone long enough; PC keeps eager preload.
        if (!IsMobileRuntime())
            BattleReadyOverlay.InitializePreload();
        else
            BattleReadyOverlay.ResetForCombat();
    }

    public static void OnHandCardPressedPostfix(NHandCardHolder __instance, object[]? __args)
    {
        // Kept for binary/backport compatibility; not patched in v1.1.5+.
    }

    public static void OnHandCardReleasedPostfix(NHandCardHolder __instance, object[]? __args)
    {
        // Kept for binary/backport compatibility; not patched in v1.1.5+.
    }

    public static void OnCardPlayTargetingPostfix(NCardPlay __instance)
    {
        try
        {
            var card = __instance?.Holder?.CardModel;
            if (card?.Owner?.Character is Defect)
                BattleReadyOverlay.ShowForPlayZone(card);
        }
        catch { }
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
                Text = "\u5207\u6362\u80CC\u666F",
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
        var oldChildren = container.GetChildren().OfType<CanvasItem>().ToList();

        var scene = ResourceLoader.Load<PackedScene>(path, null, ResourceLoader.CacheMode.Reuse);
        var inst = scene?.Instantiate();
        if (inst == null)
        {
            foreach (var child in container.GetChildren())
                child.QueueFree();
            return;
        }

        container.AddChild(inst);
        inst.Owner = container;
        if (inst is CanvasItem newCanvas)
        {
            newCanvas.Modulate = WithAlpha(newCanvas.Modulate, 0f);
            newCanvas.Visible = true;
        }

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

        FadeCanvasItemIn(inst as CanvasItem, 0.16);
        foreach (var old in oldChildren)
            FadeCanvasItemOutAndFree(old, 0.12);
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
        var bodyFadeBase = body.Modulate;
        body.Modulate = WithAlpha(bodyFadeBase, 0f);
        body.Visible = true;
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
        // A 0.18 s ease-out made the model appear almost instantly on high-DPI
        // phones.  Keep the PC transition responsive, but give Android a softer
        // ease-in/out so the first visible frame no longer looks like a pop-in.
        FadeCanvasItemIn(body, IsMobileRuntime() ? 0.32 : 0.22,
            bodyFadeBase.A <= 0f ? 1f : bodyFadeBase.A,
            gentleStart: IsMobileRuntime());
        // v1.1.9 restores the complete idle loop requested by the user.  Only
        // the hidden Battle Ready cut-in sleeps; the visible combat body keeps
        // processing idle_loop continuously on mobile and PC.
        body.ProcessMode = Node.ProcessModeEnum.Inherit;
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
            var fadeBase = spine.Modulate;
            spine.Modulate = WithAlpha(fadeBase, 0f);
            spine.Visible = true;
            sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skin));
            spine.Scale = new Vector2(Mathf.Abs(spine.Scale.X) * scale, Mathf.Abs(spine.Scale.Y) * scale);
            spine.Position += offset;
            PlayOnSprite(sprite, preferredAnim, null, loopNext: loop, force: true);
            FadeCanvasItemIn(spine, IsMobileRuntime() ? 0.24 : 0.16,
                fadeBase.A <= 0f ? 1f : fadeBase.A,
                gentleStart: IsMobileRuntime());
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
            var fadeBase = spine.Modulate;
            spine.Modulate = WithAlpha(fadeBase, 0f);
            spine.Visible = true;
            sprite.SetSkeletonDataRes(new MegaSkeletonDataResource(skin));
            spine.Scale *= 1.3f;
            PlayOnSprite(sprite, sprite.HasAnimation("idle_loop") ? "idle_loop" : "b_idle", null, loopNext: true, force: true);
            FadeCanvasItemIn(spine, IsMobileRuntime() ? 0.24 : 0.16,
                fadeBase.A <= 0f ? 1f : fadeBase.A,
                gentleStart: IsMobileRuntime());
        }
    }

    private static Resource? GetBodySkin()
    {
        if (_bodySkin != null && GodotObject.IsInstanceValid(_bodySkin))
            return _bodySkin;

        _bodySkin = ResourceLoader.Load<Resource>(BodySkinPath, null, ResourceLoader.CacheMode.Reuse);
        return _bodySkin;
    }

    private static void PreloadCommonResources()
    {
        try
        {
            _bodySkin ??= ResourceLoader.Load<Resource>(BodySkinPath, null, ResourceLoader.CacheMode.Reuse);
        }
        catch { }

        // Audio is intentionally lazy-loaded.  Loading every voice during mod
        // initialization increased Android startup memory and created a large
        // one-frame spike.  GetStream still uses Godot's reuse cache, so every
        // clip is loaded at most once per process.
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

    private static void ScheduleCardPlayBodyAnim(Player player, string anim)
    {
        if (!IsMobileRuntime())
        {
            PlayPlayerAnim(player, anim, returnIdle: true, cooldownMs: 120);
            return;
        }

        try
        {
            // BeforeCardPlayed is shared with card movement, VFX and several
            // other mods.  Starting a large Spine action synchronously in that
            // prefix stacks all work into the release frame.  Move only the
            // cosmetic body transition a few frames later and coalesce bursts.
            var key = player.GetHashCode();
            var token = MobileCardAnimTokens.TryGetValue(key, out var current) ? current + 1UL : 1UL;
            MobileCardAnimTokens[key] = token;
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return;

            var timer = tree.CreateTimer(0.10);
            timer.Timeout += () =>
            {
                try
                {
                    if (!MobileCardAnimTokens.TryGetValue(key, out var latest) || latest != token)
                        return;
                    MobileCardAnimTokens.Remove(key);
                    PlayPlayerAnim(player, anim, returnIdle: true, cooldownMs: 120);
                }
                catch { }
            };
        }
        catch
        {
            PlayPlayerAnim(player, anim, returnIdle: true, cooldownMs: 120);
        }
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
            var now = Time.GetTicksMsec();

            // Reject duplicate/low-priority hook traffic before crossing into
            // native Spine HasAnimation/GetAnimationState calls.  Orb-heavy
            // cards can invoke these hooks many times in one frame on mobile.
            if (!force && !CanStartAnim(key, anim, now))
                return false;

            var requestedAnim = anim;
            if (!force && cooldownMs > 0)
            {
                var requestedKey = key + ":" + requestedAnim;
                if (LastAnimMsecByKey.TryGetValue(requestedKey, out var last) && now - last < cooldownMs)
                    return false;
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

            if (!force)
            {
                if (cooldownMs > 0)
                    LastAnimMsecByKey[key + ":" + requestedAnim] = now;
                MarkAnimStarted(key, anim, now);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("[TifiraDefectSkin] play anim failed: " + anim + " / " + ex.Message, 2);
            return false;
        }
    }

    private static bool CanStartAnim(string key, string anim, ulong now)
    {
        var gateKey = key + ":active";
        if (!AnimGateByKey.TryGetValue(gateKey, out var gate))
            return true;
        if (now >= gate.UntilMsec)
            return true;

        var priority = GetAnimPriority(anim);
        if (string.Equals(gate.CurrentAnim, anim, StringComparison.Ordinal) && now + 220UL < gate.UntilMsec)
            return false;

        return priority > gate.Priority;
    }

    private static void MarkAnimStarted(string key, string anim, ulong now)
    {
        var lockMs = GetAnimLockMsec(anim);
        if (lockMs <= 0)
            return;

        AnimGateByKey[key + ":active"] = new AnimGateState
        {
            CurrentAnim = anim,
            Priority = GetAnimPriority(anim),
            UntilMsec = now + (ulong)lockMs,
        };
    }

    private static int GetAnimPriority(string anim)
    {
        return anim switch
        {
            "victory_ready" or "victory" or "die" => 100,
            "hurt" => 85,
            "attack" or "attack2" or "card_attack" => 75,
            "cast4" or "card_casting" => 68,
            "cast3" => 60,
            "cast2" => 55,
            "cast" => 50,
            "enter" => 35,
            _ => 0,
        };
    }

    private static int GetAnimLockMsec(string anim)
    {
        return anim switch
        {
            "victory_ready" or "victory" or "die" => 1200,
            "hurt" => 420,
            "attack" or "attack2" or "card_attack" => 620,
            "cast4" or "card_casting" => 680,
            "cast3" => 520,
            "cast2" or "cast" => 460,
            "enter" => 850,
            _ => 0,
        };
    }

    private static void FadeCanvasItemIn(CanvasItem? item, double seconds, float targetAlpha = 1f, bool gentleStart = false)
    {
        try
        {
            if (item == null || !GodotObject.IsInstanceValid(item))
                return;

            item.Visible = true;
            var tween = item.CreateTween();
            tween.SetTrans(Tween.TransitionType.Sine);
            tween.SetEase(gentleStart ? Tween.EaseType.InOut : Tween.EaseType.Out);
            tween.TweenProperty(item, "modulate", WithAlpha(item.Modulate, targetAlpha), seconds);
        }
        catch { }
    }

    private static void FadeCanvasItemOutAndFree(CanvasItem? item, double seconds)
    {
        try
        {
            if (item == null || !GodotObject.IsInstanceValid(item))
                return;

            var tween = item.CreateTween();
            tween.SetTrans(Tween.TransitionType.Sine);
            tween.SetEase(Tween.EaseType.In);
            tween.TweenProperty(item, "modulate", WithAlpha(item.Modulate, 0f), seconds);
            tween.TweenCallback(Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(item))
                    item.QueueFree();
            }));
        }
        catch
        {
            try { item?.QueueFree(); } catch { }
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.R, color.G, color.B, alpha);
    }

    internal static bool IsMobileRuntime()
    {
        if (_isMobileRuntime.HasValue)
            return _isMobileRuntime.Value;

        try
        {
            var osName = Godot.OS.GetName();
            _isMobileRuntime =
                Godot.OS.HasFeature("android") ||
                osName.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                osName.Contains("iOS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            _isMobileRuntime = false;
        }
        return _isMobileRuntime.Value;
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
            if (TryGetAnimationStateMethod != null)
                return TryGetAnimationStateMethod.Invoke(sprite, null) as MegaAnimationState;

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
        private static CardModel? _pendingCard;
        private static bool _busy;
        private static bool _played;
        private static bool _fadingOut;
        private static ulong _token;
        private static ulong _pendingToken;
        private static ulong _lastOverlayStartMsec;
        private static Tween? _fadeTween;
        private const int PcHoldDelayMs = 160;
        private const int MobileHoldDelayMs = 360;
        private const int MobilePlayZoneDelayMs = 800;
        private const int OverlayReopenCooldownMs = 650;

        public static void ResetForCombat()
        {
            _pendingCard = null;
            _pendingToken++;
            _activeCard = null;
            _busy = false;
            _played = false;
            _fadingOut = false;
            _lastOverlayStartMsec = 0;
            KillFadeTween();

            // A previous combat owns the old scene node.  Never retain wrappers
            // across combat trees; they can become invalid and trigger repeated
            // exceptions/native lookups on the next card interaction.
            if (_node == null || !GodotObject.IsInstanceValid(_node))
            {
                _node = null;
                _sprite = null;
            }
        }

        public static void InitializePreload()
        {
            try
            {
                if (_node != null && GodotObject.IsInstanceValid(_node) && _sprite != null)
                    return;

                _node = null;
                _sprite = null;

                var scene = ResourceLoader.Load<PackedScene>(BattleReadyScenePath, null, ResourceLoader.CacheMode.Reuse);
                var inst = scene?.Instantiate();
                if (inst == null)
                    return;

                var parent = (Node?)NCombatRoom.Instance?.CombatVfxContainer ?? NCombatRoom.Instance;
                parent?.AddChild(inst);
                _node = inst;
                // A hidden SpineSprite still advances its skeleton unless its
                // process tree is paused.  Sleeping the cut-in while invisible
                // removes that permanent per-frame cost on Android.
                inst.ProcessMode = Node.ProcessModeEnum.Disabled;

                if (inst is CanvasItem ci)
                {
                    ci.Visible = false;
                    ci.Modulate = WithAlpha(ci.Modulate, 0f);
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
            if (!TifiraConfig.UseBattleReadyAnim || card.Owner?.Character is not Defect)
                return;

            if (_busy)
            {
                if (_activeCard == card && !_fadingOut)
                    return;
            }

            // On touch screens a normal tap/drag fires the same press hook used by
            // PC hover/hold.  Starting the large Spine cut-in immediately makes
            // every card inspection/play repaint the overlay and was the main
            // stutter visible in the supplied Android video.  Queue it first and
            // only materialize the cut-in if the card is still held after a short
            // delay; quick taps and fast plays only use the normal body animation.
            _pendingCard = card;
            var requestToken = ++_pendingToken;
            Schedule(IsMobileRuntime() ? MobileHoldDelayMs : PcHoldDelayMs, () => StartOverlayNow(card, requestToken));
        }

        public static void ShowForPlayZone(CardModel card)
        {
            if (!TifiraConfig.UseBattleReadyAnim || card.Owner?.Character is not Defect)
                return;

            if ((_pendingCard == card) || (_busy && _activeCard == card && !_fadingOut))
                return;

            _pendingCard = card;
            var requestToken = ++_pendingToken;
            if (IsMobileRuntime())
                Schedule(MobilePlayZoneDelayMs, () => StartOverlayNow(card, requestToken));
            else
                StartOverlayNow(card, requestToken);
        }

        private static void StartOverlayNow(CardModel card, ulong requestToken)
        {
            if (_pendingToken != requestToken || _pendingCard != card)
                return;
            _pendingCard = null;

            if (!TifiraConfig.UseBattleReadyAnim || card.Owner?.Character is not Defect)
                return;

            var now = Time.GetTicksMsec();
            if (_lastOverlayStartMsec != 0 && now - _lastOverlayStartMsec < OverlayReopenCooldownMs)
                return;

            if (_busy)
            {
                if (_activeCard == card && !_fadingOut)
                    return;

                HideImmediate(clearPending: false);
            }

            if (_node == null || _sprite == null || !GodotObject.IsInstanceValid(_node))
                InitializePreload();
            if (_node == null || _sprite == null)
                return;

            _lastOverlayStartMsec = now;
            _activeCard = card;
            _busy = true;
            _played = false;
            _fadingOut = false;
            _token++;
            var introAnim = _sprite.HasAnimation("b_into") && !IsMobileRuntime() ? "b_into" : "b_idle";
            PlayOnSprite(_sprite, introAnim, "b_idle", loopNext: true, force: true);
            // Select the first pose before exposing the canvas.  Doing this in
            // the old order could reveal one stale Spine frame as a flash.
            FadeIn(_token);
        }

        public static void NotifyReleased(CardModel card)
        {
            CancelPending(card);
            if (!_busy || _activeCard != card)
                return;
            WaitThenOut(_token);
        }

        public static void NotifyCanceled(CardModel card)
        {
            CancelPending(card);
            if (_busy && _activeCard == card && !_played)
                StartOut();
        }

        public static void NotifyBeforeCardPlayed(CardPlay cardPlay)
        {
            CancelPending(cardPlay.Card);
            if (!_busy || _activeCard != cardPlay.Card || _sprite == null)
                return;

            if (IsMobileRuntime())
            {
                // Never run the large Battle Ready action and the body action in
                // the same Android frame.  The cut-in still appears while the
                // player aims/holds, but it is put to sleep before card commit.
                HideImmediate();
                return;
            }

            _played = true;
            var anim = cardPlay.Card.Type == CardType.Attack ? "card_attack" : "card_casting";
            PlayOnSprite(_sprite, anim, null, loopNext: false, force: true);
            HideSoon(_token);
        }

        private static void CancelPending(CardModel card)
        {
            if (_pendingCard != card)
                return;

            _pendingCard = null;
            _pendingToken++;
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
            if (_fadingOut)
                return;

            _fadingOut = true;
            if (_sprite != null && _sprite.HasAnimation("b_out"))
            {
                _played = true;
                PlayOnSprite(_sprite, "b_out", null, loopNext: false, force: true);
                FadeOutAfterDelay(_token, 260);
            }
            else
            {
                FadeOutAndHide(_token);
            }
        }

        private static void Hide()
        {
            FadeOutAndHide(_token);
        }

        private static void HideImmediate(bool clearPending = true)
        {
            if (clearPending)
            {
                _pendingCard = null;
                _pendingToken++;
            }
            _activeCard = null;
            _busy = false;
            _played = false;
            _fadingOut = false;
            KillFadeTween();
            if (_node is CanvasItem ci)
            {
                ci.Visible = false;
                ci.Modulate = WithAlpha(ci.Modulate, 0f);
            }
            if (_node != null && GodotObject.IsInstanceValid(_node))
                _node.ProcessMode = Node.ProcessModeEnum.Disabled;
        }

        private static void HideSoon(ulong token)
        {
            Schedule(IsMobileRuntime() ? 560 : 820, () =>
            {
                if (_busy && _played && token == _token)
                    FadeOutAndHide(token);
            });
        }

        private static void FadeOutAfterDelay(ulong token, int delayMs)
        {
            Schedule(delayMs, () =>
            {
                if (_busy && token == _token)
                    FadeOutAndHide(token);
            });
        }

        private static void WaitThenOut(ulong token)
        {
            Schedule(120, () =>
            {
                if (_busy && !_played && token == _token)
                    StartOut();
            });
        }

        private static void FadeIn(ulong token)
        {
            if (token != _token)
                return;
            if (_node is not CanvasItem ci)
                return;

            KillFadeTween();
            if (_node != null && GodotObject.IsInstanceValid(_node))
                _node.ProcessMode = Node.ProcessModeEnum.Inherit;
            ci.Visible = true;
            ci.Modulate = WithAlpha(ci.Modulate, 0f);
            _fadeTween = ci.CreateTween();
            _fadeTween.SetTrans(Tween.TransitionType.Sine);
            _fadeTween.SetEase(IsMobileRuntime() ? Tween.EaseType.InOut : Tween.EaseType.Out);
            _fadeTween.TweenProperty(ci, "modulate", WithAlpha(ci.Modulate, 1f), IsMobileRuntime() ? 0.26 : 0.20);
        }

        private static void FadeOutAndHide(ulong token)
        {
            if (_node is not CanvasItem ci)
            {
                HideImmediate();
                return;
            }

            _fadingOut = true;
            KillFadeTween();
            _fadeTween = ci.CreateTween();
            _fadeTween.SetTrans(Tween.TransitionType.Sine);
            _fadeTween.SetEase(Tween.EaseType.In);
            _fadeTween.TweenProperty(ci, "modulate", WithAlpha(ci.Modulate, 0f), IsMobileRuntime() ? 0.16 : 0.28);
            _fadeTween.TweenCallback(Callable.From(() =>
            {
                if (token == _token)
                    HideImmediate();
            }));
        }

        private static void KillFadeTween()
        {
            try
            {
                if (_fadeTween != null && GodotObject.IsInstanceValid(_fadeTween))
                    _fadeTween.Kill();
            }
            catch { }
            _fadeTween = null;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        private static void Schedule(int delayMs, Action action)
        {
            try
            {
                var tree = Engine.GetMainLoop() as SceneTree;
                if (tree == null)
                {
                    action();
                    return;
                }

                var timer = tree.CreateTimer(Math.Max(0.001, delayMs / 1000.0));
                timer.Timeout += () =>
                {
                    try { action(); }
                    catch (Exception ex) { Log.Warn("[TifiraDefectSkin] delayed overlay callback failed: " + ex.Message, 2); }
                };
            }
            catch (Exception ex)
            {
                Log.Warn("[TifiraDefectSkin] schedule overlay callback failed: " + ex.Message, 2);
            }
        }

        private static bool IsMobileRuntime()
        {
            return MainFile.IsMobileRuntime();
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
    private const int AudioPlayerPoolSize = 3;
    private static readonly Dictionary<string, AudioStream> AudioCache = new();
    private static readonly Dictionary<string, ulong> LastSoundMsecByGroup = new();
    private static readonly Dictionary<string, ulong> SuppressedAnimSoundUntilMsec = new();
    private static readonly List<AudioStreamPlayer> AudioPlayers = new(AudioPlayerPoolSize);
    private static int _audioPlayerCursor;
    private static ulong _lastIdlePlayTime;
    private static ulong _lastShopPlayTime;
    private static ulong _lastCampfirePlayTime;
    private static ulong _cardActionAudioGateUntilMsec;
    private static string _cardActionPreferredAnim = "";
    private static bool _cardActionVoiceConsumed;
    private static bool _victorySequenceActive;
    private static bool _victoryVoiceConsumed;
    private static bool _combatVoiceWarmupQueued;

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
        if (string.IsNullOrEmpty(animName))
            return;

        var ticks = Time.GetTicksMsec();
        var group = GetAudioGroup(animName);

        // Once victory starts, late animation callbacks from the final card,
        // orb, hurt response or idle loop must not start another voice over the
        // victory line.  This also closes the race where combat hooks settle in
        // a different order on Android.
        if (_victorySequenceActive && group != "victory")
            return;

        if (ShouldMuteAnimSound(animName, ticks))
            return;

        if (!AllowCombatActionVoice(animName, group, ticks))
            return;

        var minInterval = GetAudioMinIntervalMs(group);
        if (minInterval > 0 &&
            LastSoundMsecByGroup.TryGetValue(group, out var lastGroupPlay) &&
            ticks - lastGroupPlay < (ulong)minInterval)
        {
            return;
        }

        if (group == "victory")
        {
            if (_victoryVoiceConsumed)
                return;
            _victoryVoiceConsumed = true;
        }

        string? path = animName switch
        {
            "enter" => "res://TifiraDefectSkin/audio/enter.ogg",
            "victory_ready" or "victory" => "res://TifiraDefectSkin/audio/victory.ogg",
            "attack" or "attack2" => AttackSounds[(int)(GD.Randi() % AttackSounds.Length)],
            "cast" or "cast2" or "cast3" or "cast4" => CastSounds[(int)(GD.Randi() % CastSounds.Length)],
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
        if (PlaySound(path, parent))
            LastSoundMsecByGroup[group] = ticks;
    }

    public static void BeginCardActionAudioGate(string preferredAnim)
    {
        try
        {
            _cardActionPreferredAnim = preferredAnim ?? "";
            _cardActionVoiceConsumed = false;
            _cardActionAudioGateUntilMsec = Time.GetTicksMsec() + 2600UL;
        }
        catch { }
    }

    public static void ResetForCombat()
    {
        _cardActionAudioGateUntilMsec = 0;
        _cardActionPreferredAnim = "";
        _cardActionVoiceConsumed = false;
        _victorySequenceActive = false;
        _victoryVoiceConsumed = false;
        _combatVoiceWarmupQueued = false;
    }

    public static bool BeginVictorySequence()
    {
        // AfterCombatVictory can be emitted more than once while rewards and
        // room transitions settle.  Do not reopen the voice gate on repeats.
        if (_victorySequenceActive)
            return false;
        _victorySequenceActive = true;
        _victoryVoiceConsumed = false;
        _cardActionAudioGateUntilMsec = 0;
        _cardActionPreferredAnim = "";
        _cardActionVoiceConsumed = true;

        // Stop the tail of the final card/orb voice before starting the single
        // victory line.  Otherwise two pooled players can overlap and sound
        // like the victory voice was triggered more than once.
        foreach (var player in AudioPlayers)
        {
            try
            {
                if (GodotObject.IsInstanceValid(player) && player.Playing)
                    player.Stop();
            }
            catch { }
        }
        return true;
    }

    public static void ScheduleCombatVoiceWarmup()
    {
        if (_combatVoiceWarmupQueued)
            return;
        _combatVoiceWarmupQueued = true;

        // Loading an OGG for the first time inside BeforeCardPlayed adds disk
        // decode work to the same frame as card movement, VFX and Spine state
        // changes.  Warm only the two common card clips, on the main thread and
        // after the combat scene is stable.  This avoids the unsafe threaded or
        // all-at-once startup preload paths used by earlier experiments.
        ScheduleWarmup(420, AttackSounds[0]);
        ScheduleWarmup(680, CastSounds[0]);
    }

    private static void ScheduleWarmup(int delayMs, string path)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return;
            var timer = tree.CreateTimer(delayMs / 1000.0);
            timer.Timeout += () =>
            {
                try
                {
                    if (!_victorySequenceActive && NCombatRoom.Instance != null)
                        GetStream(path);
                }
                catch { }
            };
        }
        catch { }
    }

    public static bool IsCardActionGateActive()
    {
        try
        {
            return _cardActionAudioGateUntilMsec != 0 && Time.GetTicksMsec() <= _cardActionAudioGateUntilMsec;
        }
        catch
        {
            return false;
        }
    }

    public static void SuppressNextAnimSound(string animName, int durationMs)
    {
        try
        {
            if (string.IsNullOrEmpty(animName))
                return;
            SuppressedAnimSoundUntilMsec[animName] = Time.GetTicksMsec() + (ulong)Math.Max(80, durationMs);
        }
        catch { }
    }

    public static void PlayLogicalSound(string animName, Node? parent)
    {
        PlayAnimSound(animName, parent ?? ((SceneTree)Engine.GetMainLoop()).Root);
    }

    public static void PreloadCoreSounds()
    {
        foreach (var path in EnumerateKnownAudioPaths())
            GetStream(path);
    }

    private static IEnumerable<string> EnumerateKnownAudioPaths()
    {
        yield return "res://TifiraDefectSkin/audio/enter.ogg";
        yield return "res://TifiraDefectSkin/audio/victory.ogg";
        yield return "res://TifiraDefectSkin/audio/hurt.ogg";
        yield return "res://TifiraDefectSkin/audio/die.ogg";
        yield return "res://TifiraDefectSkin/audio/idle_loop.ogg";
        yield return "res://TifiraDefectSkin/audio/b_idle.ogg";
        yield return "res://TifiraDefectSkin/audio/overgrowth_loop.ogg";
        foreach (var path in AttackSounds)
            yield return path;
        foreach (var path in CastSounds)
            yield return path;
    }

    private static AudioStream? GetStream(string path)
    {
        if (AudioCache.TryGetValue(path, out var stream))
            return stream;

        stream = ResourceLoader.Load<AudioStream>(path, null, ResourceLoader.CacheMode.Reuse);
        if (stream != null)
            AudioCache[path] = stream;
        return stream;
    }

    private static string GetAudioGroup(string animName)
    {
        return animName switch
        {
            "attack" or "attack2" => "attack",
            "cast" or "cast2" or "cast3" or "cast4" => "cast",
            "victory_ready" or "victory" => "victory",
            "idle_loop" => "idle",
            "b_idle" => "shop_idle",
            "overgrowth_loop" => "campfire_idle",
            _ => animName,
        };
    }

    private static int GetAudioMinIntervalMs(string group)
    {
        return group switch
        {
            "attack" => 950,
            "cast" => 950,
            "hurt" => 320,
            "enter" => 900,
            "victory" => 1200,
            "idle" => 12000,
            "shop_idle" => 60000,
            "campfire_idle" => 90000,
            _ => 180,
        };
    }

    private static bool ShouldMuteAnimSound(string animName, ulong ticks)
    {
        // Battle Ready cut-in animations are visual feedback only.  Let the
        // player's body animation provide the one audible card voice.
        if (animName is "card_attack" or "card_casting")
            return true;

        if (SuppressedAnimSoundUntilMsec.TryGetValue(animName, out var until))
        {
            if (ticks <= until)
            {
                SuppressedAnimSoundUntilMsec.Remove(animName);
                if (_cardActionAudioGateUntilMsec != 0 &&
                    ticks <= _cardActionAudioGateUntilMsec &&
                    !_cardActionVoiceConsumed &&
                    string.Equals(animName, _cardActionPreferredAnim, StringComparison.Ordinal))
                {
                    // A card whose main animation is cast4 should still get
                    // its single voice even if an orb hook queued a cast4 mute
                    // in the same frame.
                    return false;
                }
                return true;
            }
            SuppressedAnimSoundUntilMsec.Remove(animName);
        }

        return false;
    }

    private static bool AllowCombatActionVoice(string animName, string group, ulong ticks)
    {
        if (group is not ("attack" or "cast"))
            return true;

        if (_cardActionAudioGateUntilMsec != 0 && ticks <= _cardActionAudioGateUntilMsec)
        {
            if (_cardActionVoiceConsumed)
                return false;

            // If an orb follow-up somehow races ahead of the main body anim,
            // do not let it consume the card's single voice slot unless the
            // chosen card animation itself is the same high-tier cast.
            if ((animName is "cast3" or "cast4") &&
                !string.Equals(animName, _cardActionPreferredAnim, StringComparison.Ordinal))
            {
                return false;
            }

            _cardActionVoiceConsumed = true;
            return true;
        }

        if (_cardActionAudioGateUntilMsec != 0 && ticks > _cardActionAudioGateUntilMsec)
        {
            _cardActionAudioGateUntilMsec = 0;
            _cardActionPreferredAnim = "";
            _cardActionVoiceConsumed = false;
        }

        return true;
    }

    private static bool PlaySound(string path, Node parent)
    {
        try
        {
            if (parent == null || !GodotObject.IsInstanceValid(parent))
                return false;

            var volume = MolingGlobalConfig.GlobalVolume * TifiraConfig.TifiraVolume;
            if (volume <= 0.01f)
                return false;

            var stream = GetStream(path);
            if (stream == null)
                return false;

            var player = GetAudioPlayer(parent);
            if (player == null)
                return false;

            // Reusing a tiny pool avoids allocating and freeing a Godot node for
            // every card voice.  Those QueueFree bursts were visible as periodic
            // GC/frame-time spikes on Android during fast card/orb sequences.
            if (player.Playing)
                player.Stop();
            player.Stream = stream;
            player.VolumeDb = Mathf.LinearToDb(volume);
            player.Play();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("[TifiraDefectSkin] play sound failed: " + path + " / " + ex.Message, 2);
            return false;
        }
    }

    private static AudioStreamPlayer? GetAudioPlayer(Node fallbackParent)
    {
        for (var i = AudioPlayers.Count - 1; i >= 0; i--)
        {
            if (!GodotObject.IsInstanceValid(AudioPlayers[i]))
                AudioPlayers.RemoveAt(i);
        }

        foreach (var existing in AudioPlayers)
        {
            if (!existing.Playing)
                return existing;
        }

        if (AudioPlayers.Count < AudioPlayerPoolSize)
        {
            var player = new AudioStreamPlayer
            {
                ProcessMode = Node.ProcessModeEnum.Always,
            };
            var tree = Engine.GetMainLoop() as SceneTree;
            var host = tree?.Root ?? fallbackParent;
            host.AddChild(player);
            AudioPlayers.Add(player);
            return player;
        }

        _audioPlayerCursor %= AudioPlayers.Count;
        return AudioPlayers[_audioPlayerCursor++];
    }
}
