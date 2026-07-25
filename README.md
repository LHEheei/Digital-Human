# SRTP 数字人 Android 原型

Unity `6000.4.6f1` 项目。当前交付的是可独立运行的软件闭环：语音输入、DeepSeek 或本地演示对话、JSON 结构化决策、VRM 数字人表情/基础动作/基础说话口型模拟，以及 Android 系统 TTS。

> 蓝牙、小车和传感器不是本次已验证的产品能力。仓库保留了现有小车通信入口，默认关闭；任何 `carIntent` 都只是候选意图，必须经过本地白名单和固定协议转换后才会发送，模型不会直接控制硬件。

## 运行链路

```text
MIC
  -> SpeechInputService（麦克风权限 + Android ASR，或本地演示输入）
  -> DialogueService
  -> DeepSeekDialogueClient（或 LocalDemoDialogue）
  -> DialogueDecision: reply / emotion / gesture / carIntent / memory
  -> AvatarSpeechDriver
  -> SpeechOutputService（Android TTS，或本地提示音）
  -> 空闲状态
```

程序状态 `idle`、`listening`、`thinking`、`speaking` 只由 `DigitalHumanAppController` 管理，不由模型输出决定。

## 模块边界

- `App/`：应用编排、配置面板和移动端布局。
- `Speech/`：输入/输出服务、麦克风权限和 Android 语音桥接。ASR 与 TTS 能力分别探测，任一不可用不会禁用另一项。
- `Dialogue/`：DeepSeek HTTP 调用、提示词、JSON 解析和本地演示决策。
- `Avatar/`：VRM 表情、动作、基础说话口型模拟与编辑器测试面板。
- `Memory/`：短期对话记忆、JSON 长期记忆和敏感信息过滤。
- `Config/`：运行时 API 设置及安全 URL 校验。
- `Car/`：现有小车通信边界；仅接收已本地验证并转换后的固定协议命令。

## Android 语音与权限

点击 `MIC` 后，若 Android 识别服务可用，应用会先检查 `RECORD_AUDIO`：未授权时请求授权，拒绝后在 UI 显示简短提示，按钮会恢复可点击状态。没有 ASR 时可进入本地演示输入；没有 TTS 时仍可完成 ASR/对话并使用本地提示音。

每次识别与 TTS 都使用唯一 ID。开始新任务前会取消旧任务；超时会主动取消识别或停止原生 TTS；旧回调会被忽略。销毁对象时会取消未完成任务。

Manifest 保留 `INTERNET` 和 `RECORD_AUDIO`，并不再全局允许明文流量。

## DeepSeek 配置与安全

仓库提交的 [`Assets/Resources/OpenAIConfig.asset`](Assets/Resources/OpenAIConfig.asset) 是无 Key 的默认演示配置。缺失该资产时，运行时同样会创建无 Key 的默认配置，API 面板仍可打开。

不要将 API Key 写入该资产或提交到仓库。运行时设置保存在：

```text
Application.persistentDataPath/deepseek_api_settings.json
```

编辑器可临时使用 `DEEPSEEK_API_KEY` 环境变量。API 面板只接受 HTTPS Base URL；空值会使用 `https://api.deepseek.com`。非 `api.deepseek.com` 域名会明确警告，因为 Key 会被发送到该 HTTPS 服务。HTTP 地址会被拒绝，日志和 UI 不会输出 API Key。

## 记忆

- `ConversationMemory` 保存当前运行期间的最近对话。
- `LongTermMemoryStore` 保存到 `Application.persistentDataPath/avatar_long_term_memory.json`。
- 长期记忆会过滤 Key、Token、密码、手机号、邮箱等敏感信息，限制数量并去重；重复命中会更新时间并立即保存。
- 说“清除记忆”会同时清除短期与长期记忆。

模型 JSON 字段为：

```json
{
  "reply": "好的，我记住了。",
  "emotion": "joy",
  "gesture": "nod",
  "carIntent": "none",
  "memory": "用户负责 SRTP 项目的软件部分。"
}
```

这是 JSON 结构化输出，不是 Function Calling。口型能力为基础说话口型模拟，不是音素级实时唇形同步。

## 小车通信边界

现有代码保留 `carIntent`、日志、HTTPS HTTP POST 和 Android 蓝牙 SPP 入口，默认 `enableCarCommunication = false`。本地 `CarCommand` 只接受固定候选意图并映射为配置中的受限协议 token；无效意图、空指令、包含控制字符或空白的协议 token 都不会发送。蓝牙发送具有唯一命令 ID、超时和取消，迟到回调不能影响新命令。

尚未在实车验证蓝牙配对、Android 12+ 蓝牙权限、SPP 兼容性、HTTPS 小车端点或任何电机/传感器行为。不要把该入口当作安全控制系统。

## 新开发者启动

1. 使用 Unity `6000.4.6f1` 打开项目。
2. 等待 Unity 导入；无需运行场景生成工具。
3. 在编辑器进入播放模式：无 Key 时会使用本地演示对话并显示 API 面板。
4. 真机安装后，点击 `MIC` 并授予麦克风权限。
5. 通过右上角 `API` 面板填写 HTTPS Base URL、模型和 Key；Key 只保存在设备本地。
6. 依次测试 ASR、DeepSeek 或演示回复、表情/动作、TTS、本地记忆清除。

## 已做的代码级检查

- C# 引用、`state` 字段清理、Android Manifest 和 Unity `.meta` 移动关系。
- 默认配置不含 Key，且 `.gitignore` 不再排除安全默认资产。
- ASR/TTS 回调 ID、超时取消、长期记忆重复保存和 Base URL HTTPS 限制。

未在此环境运行 Unity 编辑器编译、Android Gradle 构建或真机测试；这些仍需在目标设备上验证。
