# SRTP 桌面级智能小车数字人 App 原型

这是一个 Unity 安卓端原型。当前阶段先聚焦软件闭环：手机端语音输入、DeepSeek 语言理解、VRM 数字人表情/动作/口型反馈，以及语音播报。

硬件小车、蓝牙控制和真实 `carIntent` 执行暂时不接入，等硬件材料到位后再扩展。

## 当前主链路

用户点击 `MIC` 后，代码按这条路径运行：

```text
DigitalHumanAppController.RunConversationAsync
-> AndroidNativeSpeechBridge.RecognizeAsync
-> DeepSeekDialogueClient.CompleteAsync
-> DialogueDecision.Normalize
-> VrmAvatarBridge.SetEmotion / PlayGesture / SetSpeechLevel
-> AndroidNativeSpeechBridge.SpeakAsync
```

如果没有配置 DeepSeek Key，应用会进入本地演示模式，使用 `LocalDemoDialogue` 返回固定示例回复。

## 关键脚本

- `DigitalHumanAppController`
  - App 主流程控制器。
  - 负责组织“听用户说话、请求 DeepSeek、驱动数字人、语音播报”。

- `AndroidNativeSpeechBridge`
  - Unity C# 到 Android 原生语音能力的桥。
  - 调用系统语音识别和系统 TTS。

- `DeepSeekDialogueClient`
  - 调用 DeepSeek `/chat/completions`。
  - 要求模型输出 `DialogueDecision` JSON。

- `DialogueDecision`
  - DeepSeek 输出的数据结构。
  - 字段包括 `reply`、`emotion`、`gesture`、`state`、`carIntent`。

- `VrmAvatarBridge`
  - 驱动 VRM 模型表情、口型和基础肢体动作。

- `AvatarTestPanel`
  - Unity 编辑器播放模式下的调试面板。
  - 用于快速测试表情、动作和说话口型。

## 配置

配置文件路径：

```text
Assets/Resources/OpenAIConfig.asset
```

当前需要关注的字段：

```text
deepSeekBaseUrl
deepSeekApiKey
deepSeekModel
preferAndroidNativeSpeech
speechLanguage
useLocalDemoWhenDeepSeekKeyMissing
localDemoUserText
```

`deepSeekApiKey` 为空时使用本地演示回复；填入 Key 后，安卓真机会走真实 DeepSeek 对话。

## DeepSeek 输出格式

模型应返回 JSON 对象：

```json
{
  "reply": "好的，可以继续对话。",
  "emotion": "joy",
  "gesture": "nod",
  "state": "speaking",
  "carIntent": "none"
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
  - 重新生成一个干净演示场景。
  - 不再生成旧的程序占位数字人。

- `SRTP > Build Android APK`
  - 打包 Android APK。
  - 产物位于 `Builds/DesktopAvatarCar.apk`。

## 测试建议

1. Unity 编辑器播放，确认 Console 没有红色报错。
2. 用测试面板验证表情、动作、说话模拟。
3. 填入 DeepSeek Key，打包 APK 到安卓手机。
4. 点击 `MIC`，测试系统语音识别、DeepSeek 回复、系统 TTS 播报和数字人口型。
5. 尝试说“挥挥手”“点点头”“你怎么看这个方案”，确认动作和表情会变化。
