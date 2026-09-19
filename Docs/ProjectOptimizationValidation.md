# 项目优化与验证记录

日期：2026-09-20。环境：Windows、Unity 6000.4.6f1、Android IL2CPP / ARM64。保留此前的人物动作与文案改动，本轮完成以下六部分。

## 完成的优化

| 部分 | 具体行为 | 主要实现 |
| --- | --- | --- |
| 记忆 | 仅完整、明确的清除命令触发删除；否定、引用和问句不触发。“忘记刚才”“重新开始对话”只清短期上下文。长期文件先写入并刷新同目录临时文件，再替换主文件，保留上一份有效备份；清除时同时清空备份。 | `ConversationMemory`、`LongTermMemoryStore` |
| 自动对话 | 尊重自动聆听开关；切后台、停用和设置变更会取消旧会话，返回前台时等待旧 ASR/TTS 清理完成后再启动，避免重叠。 | `AutomaticConversationLoop`、`DigitalHumanAppController` |
| 人物反馈 | Android TTS 的实际 PCM 生成 20 ms 音量包络，按播放时间驱动嘴部；表情由回复意图决定，不再直接复制用户情绪。 | `NativeSpeechBridge.java`、`TtsMouthTimeline`、`EmotionResponsePolicy` |
| 回复延迟 | SSE 中完整句子到达后顺序播报；完整 JSON 验证通过后再提交记忆与动作元数据。语音情绪等待预算由 500 ms 改为 150 ms；开发包记录首句、首次发声等分段耗时。 | `DialogueStreamDecoder`、`ReplySpeechQueue`、`ConversationTiming` |
| 采集开销 | 复用相机像素、变换输出、纹理和麦克风缓冲；只处理新帧，CPU JPEG 编码不再重复上传 GPU；持续低于约 24 FPS 时降低面部采样频率。 | `EmotionPerceptionController`、`CameraFrameTransform` |
| 验证与界面 | 统一脚本运行 EditMode、PlayMode 和 Android 开发包构建；普通包隐藏技术诊断，开发包启用诊断与评测，构建后恢复资源配置。 | `Validate-EmotionPipeline.ps1`、`DemoSceneBuilder`、`AppConfig` |

## 回归证据

| 检查 | 结果 | 记录 |
| --- | --- | --- |
| 优化前的新增记忆回归用例 | 2/9 通过，7 个失败；覆盖误清除及损坏恢复 | `Builds/Validation/project-optimization-20260920/before.xml` |
| 最终 EditMode | **86/86 通过** | `Builds/Validation/project-optimization-20260920/editmode-validation-results.xml` |
| 最终 PlayMode | **7/7 通过**，包括已有动作衔接、闭嘴、骨长及关节范围检查 | `Builds/Validation/project-optimization-20260920/playmode-validation-results.xml` |
| Android 开发包 | **Succeeded**；统一验证脚本完成 | `Builds/Validation/project-optimization-20260920/android-validation-build.log`、`Builds/Validation/project-optimization-20260920/pipeline3.stdout.log` |
| 构建后配置 | 诊断与评测均恢复为关闭，流式回复开启；`ProjectSettings.asset` 与构建前快照 SHA256 一致 | `Builds/Validation/project-optimization-20260920/ProjectSettings.before-build.asset.txt` |

新增用例覆盖：否定/引用/问句、清除范围、备份恢复及清除后不恢复旧记忆；快速暂停恢复与关闭开关；播报顺序与取消；逐字节 UTF-8、JSON 转义、嵌套字段、非 SSE 兼容响应、缺失终止标记及 token 截断；音频合成早于播放时的时间轴；表情不镜像；像素复用与无需 GPU 上传的 JPEG 编码。

验证过程中发现并修复了原生 Java 的 `JSONArray.put(double)` 检查异常未捕获问题。此前失败日志保留在 `project-optimization-20260920/android-build-first-failure.log`，最终重新运行完整验证通过。Unity 项目锁检查也改为检查实际进程与锁文件占用，避免仅凭残留数据库标记阻止运行；没有删除数据库。

最终日志仍包含 URP MToon10 子着色器被剔除的警告；本轮未更改渲染管线或材质，APK 构建成功，手机渲染效果仍需实测。

## 性能测量的范围

Editor 中对同一组 640×480 像素连续执行 10 次旋转缩放，旧接口保留 10 个新结果数组，托管堆增量 **12,328,960 字节**。复用接口始终返回同一个数组，最终运行的堆增量为 **-4,096 字节（约 0，存在 GC 测量噪声）**，前一轮为 0。像素输出逐项一致。

这是像素变换结果缓冲的对照测量，不代表完整摄像头/JPEG/推理链路零分配，也不代表已经测得手机帧率或耗电改善。

## 构建产物

- 文件：`Builds/Validation/project-optimization-20260920/DesktopAvatarCar-development.apk`
- 大小：258,206,145 字节（约 246.2 MiB）。
- 包名：`com.srtp.desktopavatarcar`；版本：`0.1.0` / code `1`。
- ABI：`arm64-v8a`；min SDK：25；target SDK：34；Development / debuggable。
- SHA256：`6C9F1E7834F1135D8DD4DCF77B5026656090E330FF3CCECA629F707AA67FC1A0`。

本轮没有修改场景、Prefab、包依赖或 Player Settings。新增脚本均有 `.meta`。资源中只新增流式/诊断开关并关闭默认评测；构建产生的空字段尾空格已清理。构建后唯一源码调整是将采样阈值注释的 30 FPS 更正为与实现一致的 24 FPS，不影响执行行为。

## 复现与真机验收

```powershell
powershell -File Tools/Validate-EmotionPipeline.ps1 -SkipEmotionService
```

该命令跳过局域网服务依赖，执行 Unity 两类测试与 APK 构建。仅测试可加 `-SkipAndroidBuild`；局域网联调时去掉 `-SkipEmotionService`，运行服务契约测试和健康检查。

当前没有连接的 Android 设备，未进行真实 DeepSeek 请求，也未测量实际首句/首音延迟。本次已验证编译、构建及自动化逻辑；以下需要手机完成：

1. 真实中文对话的首句/首音时间，句间停顿及流中断后的恢复。
2. 手机 TTS 引擎提供音频回调时的口型同步；静音、暂停、取消时闭嘴。无音频回调时使用既有节奏模拟。
3. 多次快速切后台再恢复、修改设置及关闭自动聆听，确认没有并发录音和残留播报。
4. 相机帧率、内存、机身温度及端侧情绪推理，检查模型与材质显示。
5. 设备文件系统上的记忆写入、损坏恢复与明确清除。

口型目前同步音量与停顿，并非逐音素的唇形。Android 的音频回调可能早于实际播放，播放位置校准取决于引擎是否提供范围回调，因此实现使用独立音频时间轴。依据：[Android UtteranceProgressListener](https://developer.android.com/reference/android/speech/tts/UtteranceProgressListener)。

流式解析遵循 SSE 增量与 `[DONE]` 终止协议，并将 token 截断视为不完整回复。启用小车通信时仍走完整响应流程，保留已有指令执行顺序。依据：[DeepSeek Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)。流式播报可能已读出部分内容后才发现后续响应不完整，此时停止剩余播报、不提交该轮记忆；不会把不完整动作元数据发给小车。
