# EmotionService

局域网演示用的情绪推理服务。服务只在请求内存中处理 JPEG 与 WAV；音频模型使用临时文件后立即删除，不持久化用户媒体。

请使用 **Python 3.10 或 3.11（64 位）** 创建虚拟环境。

## 启动

```powershell
cd D:\SRTP\EmotionService
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8000
```

`models/face_landmarker.task` 是 MediaPipe Face Landmarker 的本地模型文件；它、HSEmotion 的 CPU 表情分类权重与 FunASR emotion2vec 模型都不纳入 Git。表情分类器的输出是主要依据，MediaPipe Blendshape 只负责单人脸检查、画面质量和时序辅助。HSEemotion 的官方发布权重在首次运行时缓存在 Python 用户目录，并在加载前校验固定 SHA-256；不匹配时拒绝加载。人脸模块开箱即用：不会要求用户先保持中性表情来建立基线。它只在内存中静默收集稳定画面，作为训练分类器不可用时的 Blendshape 兜底；最近 5 帧中至少 4 帧出现相同、且明确的非中性分类结果才会给出该标签。持续 5 次未检测到人脸会清除旧的时序状态和可选兜底基线。过暗、过曝、模糊或过小的人脸裁剪不参与分类。人脸响应的 `scores` 会保留该帧的五类模型分布；它与经过时序稳定后的 `label` / `score` 分开，供评估与调试使用，不应被解释为对用户内在情绪的结论。

`GET /health` 不会下载模型；它报告服务和模型状态，并提供当前进程内的请求数、失败数及最近一次人脸处理结果，便于联调排障。这些指标不含图像、音频、会话标识或其他用户内容，重启服务后会清零。语音请求会先在内存中使用轻量能量分段找出最长的连续有效语音，再交给模型；静音、明显噪声和过短片段会返回明确提示。错误响应不会记录或返回原始图像、音频内容。

## Unity / Android 联调

1. 在电脑防火墙中允许 Python 监听 TCP `8000`，并确认手机与电脑连接同一 Wi-Fi。
2. 在 Unity 中选中 `Assets/Resources/OpenAIConfig.asset`：勾选 `Enable Emotion Perception`，并将 `Emotion Service Base Url` 设置为 `http://<电脑局域网IP>:8000`；不要使用手机上的 `127.0.0.1`。
3. 打开应用后授权相机和麦克风。界面左上会展示面部、语音、整体情绪与连接状态。
4. 点击 MIC 时，持续感知会暂时释放麦克风给 Android 语音识别；识别结束后自动恢复 4 秒音频窗口采集。相机采集保持运行。

服务端接口：`POST /v1/emotions/face` 接收 multipart `file`（JPEG）和可选 `X-SRTP-Emotion-Session` 请求头；`POST /v1/emotions/voice` 接收 multipart `file`（16 kHz 单声道 PCM16 WAV）；`POST /v1/emotions/fusion` 接收 JSON 的 `face` / `voice` 结果。融合只在两项一致时提高置信度；单项结果封顶 0.75，冲突结果输出低置信度 `neutral`。
