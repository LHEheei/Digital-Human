# SRTP 数字人 Android 原型

基于 Unity `6000.4.6f1` 的多模态数字人应用：自动语音输入、DeepSeek 对话、面部与语音情绪感知、VRM 表情和动作、语音播报，以及短期和长期记忆。

当前资源配置启用 Android 端侧情绪感知。手机完成面部、语音情绪推理与融合；对话需要网络和使用者自己的 DeepSeek Key。局域网 `EmotionService` 用于开发联调，小车通信默认关闭。

## 开始使用

1. 使用 Unity `6000.4.6f1` 打开项目，等待导入与编译完成，运行 EditMode 和 PlayMode 测试。
2. 使用 `Assets/Scenes/DesktopAvatarDemo.unity` 构建 Android 应用，或从 [演示版本索引](Releases/ReleaseIndex.md) 选择已有安装包。
3. 在应用右上角 API 面板配置 HTTPS Base URL、模型和 Key，再授予麦克风及相机权限。
4. 等待“正在聆听”后直接说话。数字人播报期间暂停聆听，结束后自动恢复。未配置 Key 时，界面提示完成 API 设置。

## 运行链路

```text
Android ASR -> SpeechRecognitionTurn（文本与可用的语音音频）
                    |                 |
                    |                 -> 语音情绪分析
                    |                    + 最新面部结果 -> 情绪融合
                    v
TalkInput（文本与有效的情绪结果）
  -> DialogueService / DeepSeek
  -> DialogueDecision（reply / emotion / gesture / carIntent / memory）
  -> VRM 表情与动作 + Android TTS + 音频节奏口型
```

`DigitalHumanAppController` 负责初始化和会话编排，`AppUi` 管理状态与字幕。端侧语音情绪分析复用本轮 ASR 音频，对话最多等待其 150 ms，再使用当时可用的结果。切入后台、停用对象或销毁场景时，会话会取消 ASR、TTS 和请求；返回前台会在旧会话清理完后恢复。`enableAutoConversation` 控制是否自动聆听，API 设置保存后立即重新准备对话。

## 项目结构

| 路径 | 职责 |
| --- | --- |
| `Assets/Scripts/Runtime/App/` | 会话控制、UI、API 面板与配置 |
| `Assets/Scripts/Runtime/Speech/` | Android ASR、TTS、权限与回调管理 |
| `Assets/Scripts/Runtime/Dialogue/` | DeepSeek 请求、提示词、JSON 解析与 `TalkInput` |
| `Assets/Scripts/Runtime/Emotion/` | 感知采集、时序处理、融合与评测 |
| `Assets/Scripts/Runtime/Avatar/` | VRM 表情、动作、音频口型与提示音 |
| `Assets/Scripts/Runtime/Memory/` | 会话记忆、长期记忆与敏感信息过滤 |
| `Assets/Scripts/Runtime/Car/` | 指令校验、HTTP 与 Android 蓝牙 SPP 通信 |
| `Assets/Plugins/Android/` | 原生语音与情绪推理桥接 |
| `EmotionService/` | Python 局域网情绪服务 |
| `Assets/Tests/EditMode/` | Unity 运行逻辑测试 |
| `Tools/` | 模型导出、验证与版本归档脚本 |

## 情绪感知

| 模式 | 处理方式 | 使用条件 |
| --- | --- | --- |
| `AndroidOnDeviceFace` | 手机端面部模型、语音模型与本地融合 | Android 设备；当前默认配置 |
| `LanService` | 上传 JPEG / WAV 到 Python 服务，调用服务端融合 | 运行 [EmotionService](EmotionService/README.md)，配置可达地址 |
| `Disabled` 或关闭 `enableEmotionPerception` | 关闭情绪采集 | 保留语音对话流程 |

`UserEmo` 保存感知结果，类别为 `neutral`、`joy`、`angry`、`sorrow`、`surprised`，包括分数、来源、时间戳与错误信息。面部结果在 3 秒内、语音结果在 6 秒内参与输入和融合。端侧与服务端的融合规则见 [质量验收协议](Docs/EmotionQualityEvaluation.md)。

`TalkInput` 将有效的面部、语音和融合结果随文本传给对话服务。情绪线索用于调整语气和数字人表现，以用户的表达为主。`DialogueDecision.emotion` 为后续句子的展示表情提供默认值，问候、共情等句子可按本地规则调整；感知结果与展示表情分别建模。

相机像素、旋转缩放输出和编码纹理会复用，只处理新相机帧；持续低于约 24 FPS 时将面部采样间隔加倍。进入后台释放采集资源。

端侧面部推理使用 [内置 HSEmotion 模型](Assets/Resources/Models/README.md)，语音推理使用内置 ONNX 模型。`DemoEmoService` 为测试提供固定结果。

## 语音与错误处理

演示界面区分语音服务准备与实际聆听；收到原生 ready 回调后才显示“正在聆听”。权限拒绝、权限禁止或授权超时会暂停自动聆听，在聊天面板保留原因；处理后点击“重试语音”。普通服务故障按 1.2、2.4、4.8 秒等间隔逐步重试，默认上限 30 秒，就绪后清除提示；开发包另外显示 ASR、TTS 与情绪诊断。首次使用麦克风请求 `RECORD_AUDIO` 权限，授权结果分为 `granted`、`denied`、`blocked` 和 `timeout`。

`TaskTimeout` 管理超时与取消，`AppErr` 统一区分权限、语音、网络、认证、JSON、配置和小车错误。TTS 失败时保留文字回复并播放一次本地提示音。会话取消按正常流程收尾，错误详情写入 Console。

普通聊天内容来自模型生成；空回复、无效 JSON、认证或网络失败只显示错误，不补写固定回答。情绪策略只调整展示表情，不替换回复文字；清除记忆等操作保留确定的确认提示。实现与回归记录见 [普通对话验证](Docs/GeneratedDialogueValidation.md)。

`enableStreamingReplies` 默认开启：完整句子到达后即可播报，完整 JSON 校验通过后再提交记忆。每句实际开始播放时选择表情与手势，动作设有跨句、跨轮冷却；完整回复的元数据不会在句中突然启动新动作。启用小车通信时使用整段回复流程，保留停止指令优先执行的顺序。兼容服务不支持 SSE 时可关闭此开关。开发包记录 ASR 返回后的情绪等待、首句、完整回复、首次发声与总耗时，日志不含对话文本。

## 人物动画

`VrmAvatarBridge` 在同一帧混合待机、聆听、思考和手势，按时间平滑切换骨骼姿势。呼吸与轻微身体摆动贯穿动作，手指保持轻度弯曲；头部和眼睛跟随视线目标，未配置目标时使用主相机。

表情渐变与眨眼、口型统一合成。口型使用 A / I / U / E / O 混合，静音时闭合。本地音频按实际音量驱动；Android TTS 将实际 PCM 音频转换为 20 ms 音量包络，在播放开始后驱动口型，并使用引擎提供的播放位置回调校准；没有音频回调时沿用节奏模拟。这是音量与停顿同步，不是逐音素口型。

手臂通过上下臂骨长联合求解手腕位置，以肘部提示点确定弯曲平面。肘关节沿当前规范化 VRM 骨架的弯曲轴运动，交谈手势避免完全折叠；待机时肘部轻弯。自然播报结束后，已开始的手势可在待机中完成；停止回答或播放失败时平滑收势。切换为聆听、思考时仍收回挥手和解释等手势，短促点头可以完成。

动画回归位于 `Assets/Tests/PlayMode/`，使用项目 VRM 模型验证手势打断、静音闭嘴、停用恢复、倾听收势、骨长及主要关节范围。设置 `SRTP_AVATAR_PREVIEW` 为输出目录可保存连续预览帧；设置 `SRTP_ANATOMY_REPORT` 可保存三视角姿势图和关节报告。运行记录见 [动画复核](Docs/AvatarMotionValidation.md)。

## 配置、记忆与小车

API 设置保存在 `Application.persistentDataPath/deepseek_api_settings.json`。Base URL 使用 HTTPS，空值采用 `https://api.deepseek.com`；自定义域名会提示 Key 的发送目标。API Key 保存在设备本地，提交资产和文档中保持为空。

`ConversationMemory` 保存会话上下文，`LongTermMemoryStore` 将长期记忆写入 `Application.persistentDataPath/avatar_long_term_memory.json`。保存前过滤 API Key、token、密码、手机号、邮箱和超长文本，重复记录更新 `updatedAt`。明确说“清除记忆”或“清空所有记忆”可清除两类记忆；“不要清除记忆”、引用和问句不会触发删除。“忘记刚才”或“重新开始对话”只清除短期对话。文件通过同目录临时文件刷新后替换，`.bak` 保存上一份有效记忆；清除时同时清空恢复副本。长期记忆支持在聊天界面的“记忆”面板逐条查看、编辑与删除；修改和删除也同步更新恢复副本。旧文件会获得条目 ID，下次成功写入升级为版本 2。

`carIntent` 经过本地白名单校验后转换成固定协议 token。空值和 `none` 跳过发送，`stop` 优先发送，其他有效动作与 TTS 并行。启用小车通信前，需在目标设备验证协议、蓝牙权限与连接；当前配置为 `enableCarCommunication = false`。

## 验证与回归

本地 `Logs/` 已有历史 EditMode 测试与 Android 构建记录，例如 `EditMode-OnDeviceEmotion-Final.xml`（39 项通过）和 `voice-timing-android-build.log`（构建成功）。这些记录对应此前版本；当前改动按以下范围回归：

- API 未配置、Key 有效、认证失败与网络超时。
- 首次授权、拒绝授权、自动聆听、播报后恢复与切换前后台。
- ASR 超时、TTS 失败、非法对话 JSON 与会话取消。
- 面部和语音采集、结果时效、融合、情绪辅助回复与数字人反馈。
- 清除记忆、重复记忆更新与敏感信息过滤。
- 小车关闭、非法指令、`stop` 与普通动作。

情绪质量按 [验收协议](Docs/EmotionQualityEvaluation.md) 在目标手机记录。局域网服务的契约测试、健康检查及 Unity 验证可通过 `Tools/Validate-EmotionPipeline.ps1` 执行；具体发布版本能力见 [演示版本索引](Releases/ReleaseIndex.md)。

## 无真机对话调试

在 Unity 菜单选择 **SRTP → Dialogue Workbench**，点击“打开演示场景并运行”，输入文字后发送。默认使用真实模型，需要在右上角 API 面板配置 Key；未配置时显示操作提示，不生成预设聊天回复。

调试台提供正常回复、慢速分包、首句后断流、认证失败、超时、非法 JSON 六种情景，以及取消本轮、播放失败模拟和记忆查看。手动切换到“离线测试”后，需自行填写测试回复，不内置聊天文本。真实模式只有点击发送才会请求模型，失败时不会自动切到离线测试。

默认播放为静音口型节奏预览；可指定一个 `AudioClip` 验证音量驱动口型，该参考音频不会随模型回复合成。模拟模式与真实模式各用独立临时记忆，重新进入 Play Mode 会新建会话。电脑预览会关闭原生设备采集和小车通信，Android 配置资产不被修改。旧动作面板默认收起，F2 可呼出。

操作、测试和边界见 [电脑端调试与回归记录](Docs/DesktopDialogueWorkbench.md)。

## 当前优化验证

运行 `powershell -File Tools/Validate-EmotionPipeline.ps1 -SkipEmotionService` 可独立验证端侧工作流：EditMode、PlayMode、Android 开发包。局域网联调时不传 `-SkipEmotionService`；仅检查测试可加 `-SkipAndroidBuild`。

普通 APK 隐藏评测面板和技术状态；Development APK 自动启用诊断和评测，构建完成后恢复项目资源配置。详细实现、测试及测量见 [本轮优化记录](Docs/ProjectOptimizationValidation.md)。

## 对话界面与逐句动作

运行 `DesktopAvatarDemo` 后，Game 窗口可直接输入文字、查看滚动聊天记录、停止当前回答，并重试未提交的失败或取消轮次。语音与文字使用同一个会话入口；编辑文字或打开记忆面板时暂停自动聆听。聊天界面适配横竖屏，记录保留本次运行的最近 40 轮；模型上下文仍是原有的 6 轮，两者与长期记忆分开。

“记忆”面板可选中一条记录，修改后保存，或删除该条。每条最多 80 字，仍应用现有敏感信息过滤；修改在当前轮结束后进行。电脑模拟/真实模式的聊天记录和记忆互相隔离，重新进入 Play Mode 创建新的调试会话。

逐句动作在播放开始时触发，使用问候、肯定、共情和解释等本地文本规则；问候和肯定匹配短语边界，引用内容及局部否定不作为本地触发依据；动作间隔至少 2.5 秒，同类至少 7 秒，挥手至少 12 秒。自然句尾与冷却期间的后续句子保留正在执行的动作，让短问候的挥手完成；停止回答时平滑收势。电脑为静音/参考音频预览，手机使用系统 TTS 开始回调；这不是逐词或音素级动作标注。

操作与验证见 [对话、动作和记忆功能](Docs/ConversationFeatures.md)。

最新复查的四项修复及完整回归证据见 [复查修复记录](Docs/HealthFixValidation.md)。

权限恢复、动作误触发修复与聊天刷新测量见 [交互细节优化记录](Docs/InteractionPolishValidation.md)。
