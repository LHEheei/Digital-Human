# 项目复查修复记录

日期：2026-09-20。Unity 6000.4.6f1。基于 `Builds/Validation/health-check-20260920/report.md` 的四项发现修复；开始前基线为 97 项 EditMode、21 项 PlayMode 通过及 Android IL2CPP ARM64 构建成功。

## 修复结果

| 问题 | 修复后的行为 | 主要位置 |
| --- | --- | --- |
| H01：销毁情绪桥接时取消请求会修改正在枚举的字典 | 保存任务快照、清空请求字典，再取消每个任务；等待方同步退出不会中断清理 | `AndroidNativeEmotionBridge.OnDestroy` |
| H02：语音准备、权限或服务错误被新聊天界面隐藏 | 独立表示准备、就绪和失败；原生 ready 回调后才显示“正在聆听”。未解决的错误在重试中保留，并优先于上一轮播放错误显示 | `DigitalHumanAppController.Listening`、`SpeechInputService`、`ConversationPanel` |
| H03：短句提前取消手势 | 自然播放结束允许当前手势继续，后一句没有新动作时不会撤销它；停止、取消、播放异常仍平滑收势 | `DigitalHumanAppController.Presentation`、`VrmAvatarBridge.FinishGesture` |
| H04：桌面两种历史切换后行缓存乱序 | 按当前历史顺序重排可见消息行；历史对象切换时滚动到最新内容 | `ConversationPanel.Refresh` |

无系统语音识别服务时，现在也会进入错误反馈与重试流程；不再因入口条件直接跳过而只显示等待。关闭自动对话、关闭原生语音、编辑文字、打开记忆面板或进入后台仍会停止自动聆听。普通未听到说话沿用静默重试，不作为错误提示。

动作修复不延长音频、模型请求或回复完成的等待时间，不修改骨骼约束与姿态曲线。手势仅在自然句尾和待机中继续；开始新一轮、用户停止或切入聆听/思考时仍遵循已有打断规则。

## 验证

产物目录：`Builds/Validation/health-fixes-20260920/`。

新增 9 项 PlayMode 回归，加载实际演示场景，覆盖：

1. 销毁时有两个待处理原生请求，等待方同步清理以及过期回调。
2. 慢初始化时的可见准备提示、ready 后的聆听提示、停止或取消后的晚到 ready。
3. 权限拒绝、永久禁止与服务错误的可见提示，重试保留与恢复清除。
4. 自动对话实际入口在缺少识别服务时显示错误，不创建模型对话。
5. 上一轮播放失败后，新的麦克风错误仍能显示。
6. 未听到说话正常退出 ready 状态，不显示故障。
7. 一侧历史超过 40 轮后双向切换，检查真实 UI 子节点顺序。
8. 默认每字 0.045 秒的“你好。接着聊聊。”结束后，手腕继续抬高并最终回落；检查实际骨骼位置，非仅手势枚举。
9. 短句结束后切入后台，剩余手势被取消。

原始复现程序 `health-check-20260920/AuditProbe.exe` 使用当前编译程序集重跑：销毁前两个请求，销毁正常返回，剩余请求为零，两项任务均 Canceled。结果保存于 `cleanup-after.txt`。修复前相同程序抛出字典枚举异常并遗留一项任务。

初始 7 项专项全部通过；随后补充无识别服务入口与错误优先级的两项覆盖，纳入完整回归。

| 验证项 | 结果 | 证据文件 |
| --- | --- | --- |
| 全量 EditMode | 97/97 通过 | `editmode-validation-results.xml` |
| 全量 PlayMode | 30/30 通过，新增 9 项 | `playmode-validation-results.xml` |
| Android IL2CPP ARM64 开发包 | 构建成功 | `android-validation-build.log`、`full.stdout.log` |
| 原始请求清理复现 | 两个任务均取消，字典清空，无异常 | `cleanup-after.txt` |
| 视觉检查 | 三张动作截图、四张横竖屏界面截图已查看 | 本目录 PNG |
| 配置核对 | 与修复前一致 | `source-before.zip`、`summary.json` |

APK 已归档：`Builds/Validation/health-fixes-20260920/DesktopAvatarCar-development.apk`，258,748,176 字节。SHA256：`A726464951B0282A9646D27DFFE12538DC0E24E44323C2841ABA4F08240073E8`。中央产物目录也保留同一安装包。

构建结束后仅恢复 Unity 自动写入的配置空白字符，并校验 `OpenAIConfig.asset` 与 `ProjectSettings.asset` 和本轮开始前完全一致。全部新脚本均有 `.meta`。既有测试中的 FindObjectsByType 过时重载和 URP/MToon10 着色器剔除警告仍存在；没有编译错误。

实际摄像机截图：`greeting-before.png`、`greeting-wave-after-speech.png`、`greeting-returned.png`。已查看专项测试截图，短句完成后的挥手姿态与回落符合本次修复目的。横竖屏聊天与记忆截图也随完整场景测试导出并检查；它们用于验证实际场景布局，不代替真机键盘与安全区测试。

复现：

```powershell
$env:SRTP_HEALTH_PREVIEW = 'D:\SRTP\Builds\Validation\health-fixes-20260920'
$env:SRTP_CONVERSATION_PREVIEW = $env:SRTP_HEALTH_PREVIEW
powershell -File Tools/Validate-EmotionPipeline.ps1 -SkipEmotionService
```

本轮没有手机连接，也没有真实模型请求。原生 JNI/系统权限窗口、ASR/TTS 回调的设备时序、后台恢复和键盘遮挡仍需手机实测；没有据桌面通过结果声称这些已验收。局域网 EmotionService 未运行，本轮未改其代码。场景、Prefab、模型、包依赖和项目设置不在修复范围。
