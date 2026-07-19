# v1.1.7

- Android：隐藏状态下暂停 Battle Ready Spine，每次显示前恢复，降低不可见 cut-in 的常驻每帧开销。
- 音频：改为 3 个 AudioStreamPlayer 循环复用，并取消启动时全量预载，减少节点分配、GC 和启动峰值。
- 入场：手机战斗 body 渐变延长到 0.32 秒；Battle Ready 渐变延长到 0.26 秒并先设定动画首帧再显示。
- 兼容：保留 v1.1.6 的一张卡一次语音和充能球后续动画静音逻辑。
- 验证：PC v107.1 Release 与 Android v103 引用均 0 警告/0 错误；REDMI K80 Pro v103 实机启动、继续游戏、进入战斗、拖牌触发 Battle Ready 均成功，录屏平均 56.68 FPS，无 Tifira 运行异常。
