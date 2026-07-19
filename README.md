# 蒂菲拉故障机器人皮肤增强版 (TifiraDefectSkin) v1.1.7

Slay the Spire 2 故障机器人皮肤替换/增强 MOD。下载 Releases 里的 zip，解压后把 `TifiraDefectSkin` 文件夹复制到游戏 `mods/` 目录。

## 功能

- 将故障机器人替换为《卡厄斯梦境》的蒂菲拉。
- 选角界面替换为蒂菲拉主题形象，并提供“切换背景”按钮。
- 战斗 body 替换为蒂菲拉 Spine，进场后进入待机。
- 卡牌进入出牌区/目标选择流程后显示左侧 Battle Ready 立绘演出，普通点牌/查看卡牌不再触发。
- v1.1.6 起一张卡牌只允许一次战斗语音；Battle Ready 的 `card_attack` / `card_casting` 只保留演出不单独发声；充能球充能/激发后续动画静音，避免一张牌或球连击持续触发多段语音。
- v1.1.7 优化手机端常驻开销与入场观感：隐藏的 Battle Ready Spine 会休眠、语音播放器复用，角色 body 与左侧立绘以更柔和的渐变进入。
- 普通攻击、多段/群体攻击、防御/支援、充能球充能/激发、受击、胜利等动作绑定到对应资源。
- 绑定原资源包内的角色语音与战斗音效。
- 不改动卡牌数值，不影响玩法。

## v1.1.7 手机性能与入场优化

- 隐藏状态下把 Battle Ready 节点切换为 `ProcessMode.Disabled`，显示前恢复；避免不可见的 Spine 动画仍在每帧更新。
- 语音播放由“每次创建并 `QueueFree` 一个 `AudioStreamPlayer`”改为最多 3 个播放器循环复用，降低快速出牌/充能球连段时的节点分配和 GC 峰值。
- 取消启动时一次性预载全部语音，改用 Godot `CacheMode.Reuse` 按需加载，每个音频资源每个进程最多加载一次。
- 手机战斗 body 入场渐变由 0.18 秒延长到 0.32 秒，并改为缓入缓出。
- 手机左侧 Battle Ready 入场渐变由 0.10 秒延长到 0.26 秒；先切到正确动画首帧再显示，消除旧姿势闪现。

## v1.1.6 修复

- 视频音频分析显示重复峰值集中在出牌后球/光束连击阶段，判断为 body 动作、Battle Ready cut-in、球充能/激发 Hook 同时触发语音。
- 新增卡牌语音窗口：`BeforeCardPlayed` 开始后 2.6 秒内同一张卡只消耗一次 attack/cast 语音槽。
- `card_attack` / `card_casting` 改为纯视觉，不再从 Battle Ready cut-in 额外播放攻击/施法语音。
- `AfterOrbChanneled` / `AfterOrbEvoked` 仍播放 `cast3` / `cast4` 角色动作，但对应声音被静音，防止每个球都触发一遍 cast 语音。
- attack/cast 全局最小间隔从 260ms 提高到 950ms，兜底防止多 Hook 近距离连发。

## v1.1.5 修复

- 手机端性能：Battle Ready 不再绑定 raw card press，改为绑定 `NCardPlay.TryShowEvokingOrbs`，只有卡牌进入出牌区/目标选择流程后才拉起左侧大立绘。
- 查看卡牌：普通点牌/选中/查看卡牌停留 1 秒以上也不会再触发左侧 Battle Ready。
- 触发节流：Battle Ready 增加重开冷却；手机端跳过高频 `b_into` 重播，优先复用 `b_idle`。

## 资源来源

- 原始资源包 / Mod：`TifiraDefectSkin-v1.0.14.zip`
- 原作者 / 来源标注：`B站/抖音：异色缸墨灵`
- 本增强版只在原资源基础上补充触发逻辑、manifest、打包和兼容性处理；角色图片、Spine、音频等素材仍来自原包。

## 动作与资源绑定

- 战斗 body：`res://TifiraDefectSkin/tifirabody/Tifira.tres`；战斗进场播放 `enter -> idle_loop`。
- 商店/休息等通用 Spine 替换：同一个 `Tifira.tres`；商店偏向 `b_idle`，休息处偏向 `overgrowth_loop`。
- 选角背景：`res://scenes/character_select_bg/defect/character_select_bg.tscn` 与 `res://scenes/character_select_bg/defect2/characterselect_defect_live.tscn`。
- 左侧 Battle Ready：`res://TifiraDefectSkin/vfx/battle_ready_point.tscn`，内部引用 `res://TifiraDefectSkin/vfx/tifira_battle_ready.tres`；v1.1.5 起卡牌进入出牌区/目标选择流程后才触发，PC 播放 `b_into -> b_idle`，手机端优先直接进入 `b_idle` 以减少高频切入卡顿；`card_attack` / `card_casting` 自 v1.1.6 起只作为视觉演出，不再单独发声。
- 普通攻击：`attack`；多段/群体攻击：`attack2`；格挡/防御/支援：`cast2`；充能球充能：`cast3`；充能球激发、稀有牌、3 费以上或 UG/UX/大招类：`cast4`；受击：`hurt`；胜利：`victory_ready -> victory`。
- 音频：`attack*.ogg` 对应攻击，`cast*.ogg` 对应主卡牌施法/防御/大招；充能球后续 `cast3` / `cast4` 默认静音只保留动作，`enter.ogg` 对应入场，`hurt.ogg` 对应受击，`victory.ogg` 对应胜利，`idle_loop.ogg` / `b_idle.ogg` / `overgrowth_loop.ogg` 是带冷却的待机/商店/休息语音。

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
- v1.1.5：Battle Ready 不再绑定 raw card press，而是绑定 NCardPlay 进入出牌区/目标选择阶段，普通点牌/查看卡牌不再弹左侧大立绘。
- v1.1.6：修复一张牌和充能球连击触发多次语音；每张卡只允许一次战斗语音，Battle Ready cut-in 和球后续动画静音。
- v1.1.7：隐藏 Battle Ready 时暂停 Spine 处理，音频播放器三槽复用并按需加载；手机 body/Battle Ready 入场分别延长到 0.32/0.26 秒并修复首帧闪现。
