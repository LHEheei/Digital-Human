# 电脑端对话调试与回归

日期：2026-09-20。Unity 6000.4.6f1。目标是无手机时验证文字输入到人物反馈的完整会话，并复现可控的故障。

## 使用

1. 在 Unity 打开项目，选择 **SRTP → Dialogue Workbench**。
2. 点击“打开演示场景并运行”。窗口使用现有 `DesktopAvatarDemo` 场景；若当前场景有未保存改动，Unity 会先提供保存选择。
3. 默认“离线模拟”，输入文字并发送。在 Game 窗口看人物与字幕，在调试窗口看当前回复、状态、耗时和记忆。
4. 模拟回复、表情、手势和要写入的记忆可在发送前配置；场景可选正常、慢速分包、首句后断流、认证失败、超时及非法 JSON。进行中的会话可取消，重复发送被阻止。
5. “真实模型”模式使用原有 API 配置。通过 Game 窗口右上角 API 面板保存 Key 后发送，才会进行真实网络调用。
6. 退出 Play Mode 结束本次调试。重新进入会创建新的调试会话。F2 可呼出或收起旧动作测试面板。

## 播放与记忆

- 默认是静音口型节奏预览，不提供电脑端语音合成。
- 可在“参考音频”指定已导入的 `AudioClip`。每句播放一次该片段，并用实际音量驱动嘴部。片段内容不会随回复变化；取消后停止播放并释放引用，不销毁输入音频资产。
- “模拟播放失败”用于验证保留文字和提示音回退。
- 模拟和真实模式拥有独立的短期上下文和长期记忆文件，不读取或清除正式记忆。文件位于 `Application.temporaryCachePath/DialogueWorkbench/<会话 ID>/`，仅用于调试。
- “忘记刚才”只清当前模式的短期上下文；“清除记忆”清当前模式两类记忆及备份。真实模式的对话和记忆仍会按原有客户端规则发送给所配置的模型服务。
- 耗时带 `[Desktop preview]` 标记，其中首次播放指本地预览开始，不代表手机 TTS 首音性能。

## 接入方式

- `IDialogueClient` 只替换外部模型边界；`DeepSeekDialogueClient` 和编辑器专用 `SimulatedDialogueClient` 共同使用 `DialogueStreamDecoder.CompleteDecision`。
- 模拟客户端按 SSE 分包，并故意拆开 UTF-8 字节。控制器继续使用原有 `DialogueService`、`ReplySpeechQueue`、`AvatarSpeechDriver`、字幕和记忆提交路径。
- `DigitalHumanAppController.Desktop` 在编辑器中克隆配置，关闭原生采集、自动 ASR 和小车通信，避免调试文字触发设备行为。源 `OpenAIConfig.asset` 不变。
- 调试窗口、模拟客户端及桌面入口不进入 Android Player；共享运行逻辑仍做 Android 构建回归。
- 完整响应通过验证后才提交记忆。若在响应完整后、播放期间取消，已提交的该轮记忆仍保留；若在流未完整时取消或发生断流，该轮不提交。

## 验证

基线为上一轮 86 项 EditMode、7 项 PlayMode 通过。首轮新增场景用例 7/7 通过；补充场景卸载和编辑器窗口生命周期检查后，完整回归 **86/86 EditMode、16/16 PlayMode 通过**。

新增 9 项 PlayMode 用例加载真实演示场景并复用控制器，覆盖：

- 首句先显示、完整响应后应用表情/动作并提交记忆。
- 多轮上下文、记忆窗口裁剪、否定清除、短期/全部清除与模拟/真实模式隔离。
- 首句后取消、阻止重复提交、字幕不被迟到结果改写、下一轮恢复。
- 模拟认证失败、超时、首句后断流和非法 JSON 后恢复，失败轮不提交记忆。
- 快速暂停/恢复、停用/启用，以及场景卸载时取消。
- 参考音频取消释放、播放失败回退及下一轮恢复。
- 编辑器窗口在 Play Mode 中打开、显示更新，以及控制器卸载后的存活。

这些故障来自可控客户端，不包含真实 DNS/TLS、Android JNI、手机录音或系统 TTS 验证。本轮没有发出真实模型请求。

运行：

```powershell
powershell -File Tools/Validate-EmotionPipeline.ps1 -SkipEmotionService
```

仅运行两类测试可加 `-SkipAndroidBuild`。测试前设置 `SRTP_DESKTOP_PREVIEW` 为输出目录，可在首句显示时保存实际 Game 场景预览图。

本轮源代码基线、测试过程与汇总存于 `Builds/Validation/desktop-workbench-20260920/`。

| 最终证据 | 结果 | 文件（相对上述目录） |
| --- | --- | --- |
| 全量逻辑测试 | 86/86 通过 | `editmode-validation-results.xml` |
| 全量场景/动画测试 | 16/16 通过 | `playmode-validation-results.xml` |
| 编辑器 UI 修正后专项回归 | 9/9 通过 | `playmode-final.xml` |
| 最终 Android 开发包 | Succeeded | `android-final.log`、`final.stdout.log` |
| 实际 Game 场景截图 | 字幕和人物显示；旧动作面板已默认收起，不再遮挡字幕 | `desktop-dialogue-game.png` |
| 构建后配置还原 | `OpenAIConfig.asset`、`ProjectSettings.asset` 与本轮开始前一致（比较文本内容） | `source-before.zip` |

编辑器 UI 修正包括：默认收起旧动作面板，以及按回复长度计算调试窗口的文字区域高度。窗口打开、关闭及控制器卸载完成了运行验证；截图为真实 Game 场景，不是调试窗口本身。Android 生成的运行程序集代码中未发现桌面模拟入口。

本轮归档 APK：`Builds/Validation/desktop-workbench-20260920/DesktopAvatarCar-development.apk`，258,217,394 字节；SHA256：`2972D699346342481D2C9815E2F939F7AD7775B37040064FBD55C63DAA16E591`。此 APK 用于确认共享代码兼容 Android，电脑调试入口位于 Unity 编辑器菜单。

本轮没有改动场景、Prefab、模型、包依赖或项目设置。全部新增脚本均有 `.meta`。Android 构建仍有此前的 URP MToon10 子着色器剔除警告，未新增相关渲染改动；手机显示效果仍未验证。
