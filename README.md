# 蒂菲拉故障机器人皮肤增强版 (TifiraDefectSkin) v1.1.3

Slay the Spire 2 故障机器人皮肤替换/增强 MOD。下载 Releases 里的 zip，解压后把 `TifiraDefectSkin` 文件夹复制到游戏 `mods/` 目录。

## 功能

- 将故障机器人替换为《卡厄斯梦境》的蒂菲拉。
- 选角界面替换为蒂菲拉主题形象，并提供“切换背景”按钮。
- 战斗 body 替换为蒂菲拉 Spine，进场后进入待机。
- 长按/拖出手牌时显示左侧 Battle Ready 立绘演出，打出攻击/技能时切换对应 cut-in。
- 普通攻击、多段/群体攻击、防御/支援、充能球充能/激发、受击、胜利等动作绑定到对应资源。
- 绑定原资源包内的角色语音与战斗音效。
- 不改动卡牌数值，不影响玩法。

## v1.1.3 修复

- 动画衔接：给战斗 body、选角背景、Battle Ready 增加淡入/淡出，减少直接闪现。
- 动画节流：攻击、施法、受击等动作加优先级和短锁定，避免同一张牌/同一帧多个 Hook 互相打断造成卡顿。
- 资源预加载：启动时预热常用 Spine/Audio 资源，降低首次触发卡顿。
- 语音去重：移除出牌 Hook 中的第二路音频触发，统一由 Spine `animation_started` 监听播放；同时按音频组增加最小间隔，避免 Android 上重复播放产生重音。

## 资源来源

- 原始资源包 / Mod：`TifiraDefectSkin-v1.0.14.zip`
- 原作者 / 来源标注：`B站/抖音：异色缸墨灵`
- 本增强版只在原资源基础上补充触发逻辑、manifest、打包和兼容性处理；角色图片、Spine、音频等素材仍来自原包。

## 动作与资源绑定

- 战斗 body：`res://TifiraDefectSkin/tifirabody/Tifira.tres`；战斗进场播放 `enter -> idle_loop`。
- 商店/休息等通用 Spine 替换：同一个 `Tifira.tres`；商店偏向 `b_idle`，休息处偏向 `overgrowth_loop`。
- 选角背景：`res://scenes/character_select_bg/defect/character_select_bg.tscn` 与 `res://scenes/character_select_bg/defect2/characterselect_defect_live.tscn`。
- 左侧 Battle Ready：`res://TifiraDefectSkin/vfx/battle_ready_point.tscn`，内部引用 `res://TifiraDefectSkin/vfx/tifira_battle_ready.tres`；长按/拖出卡牌播放 `b_into -> b_idle`，打出攻击牌播放 `card_attack`，打出非攻击牌播放 `card_casting`，取消/松开播放 `b_out`，外层 CanvasItem 负责淡入淡出。
- 普通攻击：`attack`；多段/群体攻击：`attack2`；格挡/防御/支援：`cast2`；充能球充能：`cast3`；充能球激发、稀有牌、3 费以上或 UG/UX/大招类：`cast4`；受击：`hurt`；胜利：`victory_ready -> victory`。
- 音频：`attack*.ogg` 对应攻击，`cast*.ogg` 对应施法/防御/充能球/大招，`enter.ogg` 对应入场，`hurt.ogg` 对应受击，`victory.ogg` 对应胜利，`idle_loop.ogg` / `b_idle.ogg` / `overgrowth_loop.ogg` 是带冷却的待机/商店/休息语音。

## 当前保留但未主动触发的资源

- `TifiraDefectSkin/tifirabody/Tifira.tscn`：当前 DLL 直接把 `Tifira.tres` 注入原游戏 Spine 节点，没有实例化这个场景。
- `die.ogg`：已绑定到 `die` 动画名，但当前补丁没有单独挂玩家死亡 Hook；只有 Spine/游戏侧实际播放 `die` 时才会响。
- `Scripts/*.cs`：原 PCK 内脚本保留，当前增强逻辑由外部 DLL `TifiraDefectSkinCode/MainFile.cs` 实现。
- `icon.svg` / `mod_image.png`：作为项目/Mod 展示图资源保留，不参与战斗动作。

## 版本

- v1.0.14：原始替换角色版本留档。
- v1.1.0：增强演出版；电脑端进选角与战斗实测，Android v103 引用编译检查。
- v1.1.1：左侧 Battle Ready cut-in 增加入场/出场渐变；补全资源来源、动作绑定与未主动触发资源记录。
- v1.1.2：修复 v1.1.1 包内 `TifiraDefectSkin.json` 描述字段缺少结束引号导致 manifest 不是合法 JSON 的问题。
- v1.1.3：优化动画衔接、资源预加载、动作节流，并修复语音/战斗音效重复播放导致的重音。