# TifiraDefectSkin v1.1.8

手机 v103 性能专项更新。

- Battle Ready 在手机端改为按需延迟加载；快速出牌不会实例化大型切入 Spine。
- 战斗角色完成入场/动作后休眠骨骼逐帧处理，攻击、施法、受击和胜利时自动唤醒。
- 合并同一卡牌动作窗口内重复触发的充能球充能/激发角色动作。
- 缓存 `TryGetAnimationState` 兼容反射，并把动画门控提前到原生 Spine 查询之前。
- 连续目标选择回调按同一卡牌去重，避免重复定时器和切入请求。
- 保留 v1.1.7 的音频池、语音去重和入场渐变。

验证：

- `dotnet build -c Release`：0 warning / 0 error。
- Android v0.103.2：Mod v1.1.8 成功加载，继续存档进入战斗，蒂菲拉 body 正常显示，执行卡牌交互后进程保持运行且日志无 Tifira 异常。
- 离线性能回归：7/7 通过。

发布包：`TifiraDefectSkin-v1.1.8-Mobile-v103.zip`
