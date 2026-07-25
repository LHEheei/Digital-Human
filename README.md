# SRTP 数字人 Android 原型

Unity `6000.4.6f1` 项目。当前交付的是一个可独立运行的软件闭环：语音输入、DeepSeek 或本地演示对话、结构化决策、VRM 数字人表情/动作、基础说话口型模拟、短期与长期记忆，以及保留但默认关闭的小车通信入口。

> 小车和蓝牙并非已验证的产品能力。`carIntent` 只是候选意图，必须经过本地白名单与固定协议 token 转换；模型原始输出不会直接发送给硬件。

## 运行链路

```text
MIC
  -> SpeechInputService (Android ASR 或本地模拟输入)
  -> DialogueService (DeepSeek 或本地演示)
  -> DialogueDecision (reply / avatar emotion / gesture / carIntent / memory)
  -> AvatarSpeechDriver + SpeechOutputService (Android TTS 或本地提示音)
```

`DigitalHumanAppController` 仅负责场景初始化与流程编排；`AppUi` 管理状态、字幕、提示和 MIC 按钮。每次对话都有 `CancellationTokenSource`：销毁场景或启动下一轮时，控制器会通过 `SpeechService.CancelAll()`、小车服务入口和会话 token 取消未完成工作，避免旧回调继续更新 UI、数字人或小车。

## 目录结构

- `Assets/Scripts/Runtime/App/`：控制器、UI 适配、配置面板与运行时配置。
- `Avatar/`：VRM 表情、动作、基础口型与本地提示音。
- `Speech/`：麦克风权限、Android ASR、Android TTS 和回调 ID 隔离。
- `Dialogue/`：DeepSeek 请求、JSON 解析、提示词、`TalkInput`。
- `Memory/`：短期记忆、长期 JSON 记忆和敏感信息过滤。
- `Emotion/`：用户情绪的数据模型、接口和本地演示实现。
- `Car/`：既有 HTTPS HTTP 与 Android 蓝牙 SPP 通信边界；不含配对页面或新协议。
- `Common/`：超时与统一错误类型。
- `Assets/Tests/EditMode/`：无需设备和网络的 Unity Test Framework 测试。

## 语音、对话与错误提示

ASR 与 TTS 是独立能力：启动提示分别显示输入（Android ASR/本地模拟）和输出（Android TTS/本地提示音），不会用一个笼统的“系统语音”状态代替两者。Android 首次使用麦克风会请求 `RECORD_AUDIO`；授权结果区分 `granted`、`denied`、`blocked` 和 `timeout`，编辑器中仍可使用本地演示输入。

`TaskTimeout` 支持 `Task` 与 `Task<T>`，主任务完成会立即取消计时器；超时会触发取消入口，异常会原样向上交给统一错误层。`AppErr` 区分麦克风权限、ASR、TTS、网络、认证、JSON、配置、小车、取消和未知错误。取消不会显示成系统故障，技术详情只写入 Console，且不会记录 API Key 或完整敏感请求。

## 配置与安全

提交的 [`Assets/Resources/OpenAIConfig.asset`](Assets/Resources/OpenAIConfig.asset) 是无 Key 的默认演示配置。运行时 API 设置仅保存在：

```text
Application.persistentDataPath/deepseek_api_settings.json
```

Base URL 仅接受 HTTPS；空值使用 `https://api.deepseek.com`。非官方 HTTPS 域名会明确警告，因为 Key 会发送至该服务；HTTP、带用户信息或非法 URL 会被拒绝。不要把 API Key 写入资产、README 或提交记录。

## 用户情绪与数字人表情

两者刻意分离：

- `UserEmo` 表示对用户的识别结果：`label`（`neutral`、`joy`、`angry`、`sorrow`、`surprised`）、`score`（0..1）、`source`（`face`、`voice`、`fusion`）和 `error`。非法标签回退为 `neutral`，非法来源为 `unknown`；`fun` 不属于用户情绪标签。
- `DialogueDecision.emotion` 表示数字人最终要展示的头像表情，保留现有 `AvatarEmotion` 的用途。
- `IEmoService` 定义 `GetFaceAsync(byte[])` 与 `GetVoiceAsync(byte[])`（含取消 token）。`DemoEmoService` 只返回稳定的联调数据；空输入给出可识别的错误结果。
- `TalkInput` 把文本与可选人脸/语音情绪放在一次对话输入中。只有有效结果会以固定、带“不确定性”说明的上下文传给 DeepSeek；当前主流程仍只传文本。

当前**没有**接入人脸检测、真实面部情绪识别、真实语音情感模型、Python 服务、摄像头、录音或模型下载。

## 记忆与小车边界

`ConversationMemory` 保存运行期上下文。`LongTermMemoryStore` 写入 `Application.persistentDataPath/avatar_long_term_memory.json`，会过滤 API Key、token、密码、手机号、邮箱和超长文本，并在重复命中时更新 `updatedAt` 后保存。说“清除记忆”会清除短期和长期记忆。

小车只接受固定候选意图并转换为配置中的受限 token；空/`none` 不发送，`stop` 在本地校验后优先立即发送，其他有效动作与 TTS 并行，未知或协议 token 不合法的指令明确拒绝。小车失败会立即单独反馈，不等待语音播报结束。默认 `enableCarCommunication = false`，且现有蓝牙 SPP、Android 12+ 权限、目标设备兼容性与真实 HTTP 端点均需真实设备验证。

基础口型是模拟开合，并非音素级实时唇形同步。

## 开始开发

1. 使用 Unity `6000.4.6f1` 打开项目并等待导入完成。
2. 先在 Console 确认没有脚本编译错误，再运行 EditMode 测试。
3. 无 Key 时运行本地演示；有 Key 时通过 API 面板配置 HTTPS Base URL、模型和 Key（只保存在设备本地）。
4. 在 Android 真机上分别验证首次麦克风授权、ASR、TTS 超时/取消，以及真实小车设备后再启用小车通信。

## 回归清单

- 无 Key 演示模式与有 Key 云端模式
- 麦克风首次授权、拒绝与授权超时
- ASR 超时、TTS 失败后的本地提示音降级
- 非法 DeepSeek JSON、连续点击 MIC、取消/切场景
- 清除记忆与敏感信息不过滤失败
- 小车关闭、非法小车指令、`stop` 与普通运动指令
- `DemoEmoService` 的稳定结果和空输入错误结果

本仓库当前尚未在此环境中完成 Unity Editor 编译、Android Gradle 构建或真机验证；请在目标 Unity 与设备环境执行上述检查。
