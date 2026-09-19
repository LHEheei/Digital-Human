# EmotionService

供 Unity / Android 局域网联调的 Python 情绪推理服务，提供面部、语音和融合接口。Android 端侧模式的运行方式见 [项目说明](../README.md)。

## 启动

使用 **Python 3.10 或 3.11（64 位）** 创建虚拟环境：

```powershell
cd D:\SRTP\EmotionService
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8000
```

模型依赖包括 `models/face_landmarker.task`、HSEmotion CPU 表情分类权重和 FunASR emotion2vec。模型文件与缓存排除在 Git 之外。HSEmotion 发布权重首次运行时缓存到 Python 用户目录，加载前校验固定 SHA-256。

## 推理流程

- **面部**：HSEmotion 提供分类，MediaPipe 负责单人脸检查、画面质量和时序辅助。过暗、过曝、模糊或过小的裁剪跳过分类。最近 5 帧中至少 4 帧出现相同且明确的非中性结果时输出该标签。连续 5 次未检测到人脸后清除时序状态。
- **基线**：启动后即可使用。服务在后台收集稳定画面，供分类器不可用时的 Blendshape 路径使用。
- **分数**：`scores` 保存当前帧的五类模型分布，`label` / `score` 保存时序处理后的结果，便于评估两者的差异。结果描述可观测的表情线索。
- **语音**：先按能量提取最长的连续有效语音，再交给模型。静音、明显噪声和过短片段返回采集错误。
- **融合**：两路一致时按权重合成分数；单路分数上限为 0.75；冲突时输出低置信度 `neutral`。

## 数据与健康检查

JPEG 与 WAV 在请求内存中处理；音频模型使用的临时文件在处理后删除。日志和错误响应排除原始媒体内容。

`GET /health` 查询服务与模型状态，以及当前进程的请求数、失败数和最近一次人脸处理结果。查询本身不下载模型。指标在进程重启后清零，不含媒体或会话标识。

## Unity / Android 联调

1. 允许电脑防火墙的 TCP `8000` 入站连接，手机与电脑连接同一 Wi-Fi。
2. 在 `Assets/Resources/OpenAIConfig.asset` 中启用 `Enable Emotion Perception`，将 `Emotion Runtime Mode` 设为 `LanService`，将 `Emotion Service Base Url` 设为 `http://<电脑局域网IP>:8000`。
3. 打开应用并授予相机和麦克风权限；对话还需在 API 面板配置 DeepSeek Key。
4. 自动聆听期间，持续感知释放麦克风供 Android ASR 使用；识别结束后恢复语音窗口采集，默认窗口为 4 秒。相机持续采集。

通过 USB 联调时，可执行 `adb reverse tcp:18000 tcp:8000`，并将应用的服务地址设为 `http://127.0.0.1:18000`。

## 接口

| 接口 | 输入 |
| --- | --- |
| `POST /v1/emotions/face` | multipart `file`（JPEG），可选 `X-SRTP-Emotion-Session` 请求头 |
| `POST /v1/emotions/voice` | multipart `file`（16 kHz 单声道 PCM16 WAV） |
| `POST /v1/emotions/fusion` | JSON `face` / `voice` 结果 |

识别质量按 [验收协议](../Docs/EmotionQualityEvaluation.md) 记录。
