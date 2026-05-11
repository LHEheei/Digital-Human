# SRTP 桌面级智能小车数字人 App 原型

这是一个 Unity 安卓端原型。当前阶段先聚焦软件闭环：手机端语音输入、DeepSeek 语言理解、VRM 数字人表情/动作/口型反馈，以及安卓系统 TTS 播报。

硬件小车、蓝牙控制和真实 `carIntent` 执行暂时不接入，等材料到位后再扩展。

## 当前主链路

用户点击 `MIC` 后，代码按这条路径运行：

```text
DigitalHumanAppController.RunConversationAsync
-> SpeechService.ListenForUserTextAsync
-> DialogueService.DecideAsync
-> DeepSeekDialogueClient.CompleteAsync 或 LocalDemoDialogue.Decide
-> DialogueDecision.Normalize
-> ConversationMemory / LongTermMemoryStore
-> AvatarSpeechDriver.SetEmotion / PlayGesture / SetSpeechLevel
-> SpeechService.SpeakWithAndroidTtsAsync
```

如果没有配置 DeepSeek Key，应用会进入本地演示模式，使用 `LocalDemoDialogue` 返回固定示例回复。

## 记忆机制

项目现在有两层记忆：

- `ConversationMemory`：保存本轮最近几轮对话，帮助 DeepSeek 理解上下文；关闭 App 后会消失。
- `LongTermMemoryStore`：保存长期事实、偏好和项目背景；写入手机本地文件，重新打开 App 后仍可使用。

长期记忆文件位置：

```text
Application.persistentDataPath/avatar_long_term_memory.json
```

DeepSeek 每次会返回一个额外字段 `memory`。只有当用户主动透露稳定信息时才写入，例如“我负责软件部分”“我喜欢简洁一点的界面”。普通闲聊、密码、API Key、联系方式等不会被要求记忆。

用户可以直接说：

```text
清除记忆
```

这会同时清空最近对话记忆和长期本地记忆。

## 关键脚本

- `DigitalHumanAppController`
  - App 主流程控制器。
  - 只负责编排“听、想、演、说”的顺序和 UI 状态。
- `SpeechService`
  - 语音输入和语音输出服务。
  - 负责安卓系统语音识别、安卓系统 TTS，以及编辑器/无 Key 时的本地模拟输入。
- `DialogueService`
  - 对话决策服务。
  - 负责在 DeepSeek 与本地演示兜底之间选择，并更新短期/长期记忆。
- `AvatarSpeechDriver`
  - 数字人表现驱动。
  - 负责表情、动作、口型，以及 TTS 播放时的嘴部模拟。
- `AndroidNativeSpeechBridge`
  - Unity C# 到 Android 原生语音能力的桥。
  - 调用系统语音识别和系统 TTS。
- `ApiConfigPanel`
  - App 内 API 设置面板。
  - 用户可以在手机上填写 DeepSeek Base URL、Model 和 API Key，并保存到本机。
- `DeepSeekDialogueClient`
  - 调用 DeepSeek `/chat/completions`。
  - 要求模型输出 `DialogueDecision` JSON。
- `DialoguePrompt`
  - 维护 DeepSeek 的系统提示词。
  - 后续接入小车真实意图时，优先改这里。
- `DialogueParser`
  - 负责把 DeepSeek 返回内容解析成 `DialogueDecision`。
  - `DialogueDecision` 本身只保留数据和枚举归一化。
- `DialogueDecision`
  - DeepSeek 输出的数据结构。
  - 字段包括 `reply`、`emotion`、`gesture`、`state`、`carIntent`、`memory`。
- `LongTermMemoryStore`
  - 本地长期记忆读写。
  - 使用 JSON 存在手机本地，不依赖云数据库。
- `LocalDemoDialogue`
  - 位于 `Assets/Scripts/Runtime/Demo/`。
  - 只作为无 Key 或编辑器场景下的演示兜底。
- `AppConfig`
  - 应用配置资产，对应 `Assets/Resources/OpenAIConfig.asset`。
- `VrmAvatarBridge`
  - 驱动 VRM 模型表情、口型和基础肢体动作。
- `AvatarTestPanel`
  - Unity 编辑器播放模式下的调试面板。
  - APK 中只保留空壳组件，不生成调试 UI。

## 配置

配置文件路径：

```text
Assets/Resources/OpenAIConfig.asset
```

当前主要字段：

```text
deepSeekBaseUrl
deepSeekApiKey
deepSeekModel
deepSeekRequestTimeoutSeconds
enableConversationMemory
maxMemoryTurns
enableLongTermMemory
maxLongTermMemories
preferAndroidNativeSpeech
speechLanguage
useLocalDemoWhenDeepSeekKeyMissing
localDemoUserText
```

`deepSeekApiKey` 为空时使用本地演示回复；填入 Key 后，安卓真机会走真实 DeepSeek 对话。

更推荐的 Key 来源：

```text
App 内 API 设置面板
```

保存后会写入手机本地：

```text
Application.persistentDataPath/deepseek_api_settings.json
```

旧版单独 Key 文件仍兼容：

```text
Application.persistentDataPath/deepseek_key.txt
```

编辑器里也可以临时使用环境变量：

```text
DEEPSEEK_API_KEY
```

不要把正式 Key 长期写在 `OpenAIConfig.asset` 中，因为它会被打进 APK。

## App 内配置 API

运行 App 后，右上角会出现 `API` 按钮。点开后可以填写：

```text
Base URL: https://api.deepseek.com
Model: deepseek-v4-flash
API Key: 你的 DeepSeek Key
```

点“保存”后，下一次点击 `MIC` 就会使用本机保存的配置。点“清除”会删除本机 API 配置，应用会回到演示模式或 Inspector 中的备用配置。

## DeepSeek 输出格式

模型应返回 JSON 对象：

```json
{
  "reply": "好的，我记住了。",
  "emotion": "joy",
  "gesture": "nod",
  "state": "speaking",
  "carIntent": "none",
  "memory": "用户负责 SRTP 项目的软件部分。"
}
```

可用值：

```text
emotion: neutral, joy, angry, sorrow, fun, surprised
gesture: none, nod, wave, think, explain
state: idle, listening, thinking, speaking
carIntent: none
```

当前 `carIntent` 只保留字段，不控制硬件。

## 常用菜单

- `SRTP > Use Selected VRM Avatar`
  - 选中场景里的 VRM 根节点后执行。
  - 自动绑定 VRM 数字人、测试面板、安卓语音桥和手机端布局。
- `SRTP > Apply Soft Blue Look`
  - 应用当前浅蓝半身构图和 UI 布局。
- `SRTP > Generate Demo Scene`
  - 重新生成干净演示场景。
  - 不再生成旧的程序占位数字人。
- `SRTP > Build Android APK`
  - 打包 Android APK。
  - 产物位于 `Builds/DesktopAvatarCar.apk`。

## 测试建议

1. Unity 编辑器播放，确认 Console 没有红色报错。
2. 用测试面板验证表情、动作和说话口型。
3. 填入 DeepSeek Key，打包 APK 到安卓手机。
4. 在手机上点右上角 `API`，填写并保存 DeepSeek 配置。
5. 点击 `MIC`，测试系统语音识别、DeepSeek 回复、系统 TTS 播报和数字人口型。
6. 说“请记住我负责软件部分”，再问“我负责什么”，确认长期记忆生效。
7. 说“清除记忆”，再问同样问题，确认记忆已被清掉。
