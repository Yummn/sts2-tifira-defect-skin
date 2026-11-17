# 蒂菲拉故障机器人皮肤增强版 / TifiraDefectSkin

<!-- MOD-DOWNLOAD-ZH-START -->
## 中文下载与安装（先看这里）

- **蒂菲拉故障机器人皮肤**：请进入 [发布下载 / Releases](https://github.com/Yummn/sts2-tifira-defect-skin/releases)，展开 **Assets**，选择对应平台和游戏版本的中文安装包。
- **不要下载** Code → Download ZIP 或 Source code (zip/tar.gz)：那是源码，不含可直接安装的编译产物。
- 手动上传的源码压缩包已撤下，今后只发布安装包、说明和验证资料；项目代码仍保留在仓库。

### 本轮对应安装包

- [手机 v0.113.0](https://github.com/Yummn/sts2-tifira-defect-skin/releases/download/mobile-v113-pc-v111-20261117/TifiraDefectSkin-1.1.9-Android-v113.0.zip)
- [电脑 v0.111.0（尚未实机回归）](https://github.com/Yummn/sts2-tifira-defect-skin/releases/download/mobile-v113-pc-v111-20261117/TifiraDefectSkin-1.1.9-PC-v111.0.zip)

以上只是本轮构建目标，其他游戏版本请查看相应历史发布；不能混装手机和电脑版。旧文档中的版本号、源码附件指引如与本节冲突，以本节及对应Release说明为准。

### 电脑版安装

1. 退出游戏，解压**电脑版安装包**。
2. 将整个 `TifiraDefectSkin` 文件夹放到游戏根目录的 `mods` 文件夹中；没有 `mods` 就新建。
3. 重启游戏，在模组列表中启用。不要只把ZIP或源码目录放在游戏根目录。

```text
Slay the Spire 2/mods/TifiraDefectSkin/TifiraDefectSkin.dll
Slay the Spire 2/mods/TifiraDefectSkin/TifiraDefectSkin.json
```

手机版请使用对应游戏版本的启动器导入安装包，不要拿电脑版代替；保留原有存档和模组配置。

GitHub 自动生成的 Source code 链接不能单独删除，因此请认准上面的中文安装包链接。
<!-- MOD-DOWNLOAD-ZH-END -->

> 项目分类：个人维护项目 / 《杀戮尖塔 2》Mod / 角色皮肤与演出增强 / 持续维护

将故障机器人替换为《卡厄斯梦境》的蒂菲拉，并在原始资源包基础上补充选角、战斗 Spine、Battle Ready 演出、语音触发和移动端性能优化。

当前版本：`v1.1.9`

## 下载与安装

1. 打开 [Releases](https://github.com/Yummn/sts2-tifira-defect-skin/releases)。
2. 下载与自己的游戏平台和游戏版本一致的 ZIP。
3. 解压后，将 `TifiraDefectSkin` 文件夹完整复制到游戏 `mods/` 目录。

不同游戏版本的 DLL 请不要混用，具体兼容范围以对应 Release 文件名和说明为准。

## 主要功能

- 将故障机器人战斗形象替换为蒂菲拉 Spine；
- 替换选角界面形象，并提供“切换背景”按钮；
- 卡牌进入出牌区 / 目标选择流程后显示左侧 Battle Ready 演出；
- 普通点牌、查看卡牌不会触发 Battle Ready；
- 为攻击、施法、防御、充能球、受击、胜利等动作绑定对应动画和语音；
- 一张卡牌只允许一次主要战斗语音，避免多 Hook / 多球牌重复发声；
- 不修改卡牌数值，不改变玩法。

## 手机端优化

从 v1.1.7 起持续针对 Android 做性能与触发优化，包括：

- 隐藏的 Battle Ready Spine 暂停处理；
- 语音播放器复用，减少节点分配和 GC 峰值；
- Battle Ready 按需延迟创建，快速出牌时不加载大切入骨骼；
- 对同一卡牌触发的多次充能球动作进行合并；
- 出牌时错开 Battle Ready、卡牌移动、VFX 和角色 body 动作的峰值；
- v1.1.9 恢复完整待机动画，同时继续保留隐藏节点休眠与语音去重。

## 当前版本 v1.1.9

- 恢复手机和 PC 的完整 `idle_loop`；
- Android 提交卡牌前会取消 / 隐藏 Battle Ready，降低同帧负载；
- body 攻击 / 施法动作延后 0.10 秒，并合并同一突发窗口内的重复请求；
- 手机 Battle Ready 延迟提高，快速点击 / 拖出不会实例化大骨骼；
- 常用攻击 / 施法语音在进入战斗后错峰预热；
- 胜利阶段屏蔽残留动作语音，胜利动画与语音每场只触发一次。

更早版本的性能调整和修复记录见：

- [GitHub Releases](https://github.com/Yummn/sts2-tifira-defect-skin/releases)

## 资源来源与署名

- 原始资源包 / Mod：`蒂菲拉故障机器人皮肤-v1.0.14.zip`
- 原作者 / 来源标注：`B站/抖音：异色缸墨灵`

本增强版只在原资源基础上补充触发逻辑、manifest、打包和兼容性处理；角色图片、Spine、音频等素材仍来自原包。

## 主要资源绑定

- 战斗 body：`res://TifiraDefectSkin/tifirabody/Tifira.tres`
- 左侧 Battle Ready：`res://TifiraDefectSkin/vfx/battle_ready_point.tscn`
- Battle Ready Spine：`res://TifiraDefectSkin/vfx/tifira_battle_ready.tres`
- 增强逻辑：`TifiraDefectSkinCode/MainFile.cs`

原包中部分资源仍会保留但不一定主动触发；具体行为以当前版本代码和 Release 说明为准。
