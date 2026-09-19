using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class EmotionPerceptionController : MonoBehaviour
    {
        private const int VoiceSampleRate = 16000;
        private const int CameraMaximumDimension = 640;
        private const int CameraJpegQuality = 80;
        private static readonly Vector2 PreviewTextureRectSize = new Vector2(180f, 135f);
        private const float PreviewRightMargin = 26f;
        private const float PreviewTopMargin = 146f;

        private AppConfig config;
        private IEmoService emotionService;
        private IFusionEmoService fusionService;
        private FaceEmotionTemporalFilter nativeFaceFilter;
        private CancellationTokenSource lifetimeCancellation;
        private WebCamTexture cameraTexture;
        private Texture2D captureTexture;
        private Color32[] cameraPixels;
        private Color32[] orientedPixels;
        private float[] microphoneSamples;
        private float averageFrameSeconds;
        private bool applicationPaused;
        private RawImage cameraPreview;
        private int previewRotationAngle = int.MinValue;
        private bool previewVerticallyMirrored;
        private AudioClip microphoneClip;
        private string microphoneDevice;
        private float microphoneStartedAt;
        private float lastFaceSampleAt = float.NegativeInfinity;
        private float lastVoiceSampleAt = float.NegativeInfinity;
        private bool faceRequestInFlight;
        private bool voiceRequestInFlight;
        private bool fusionRequestInFlight;
        private bool fusionRefreshQueued;
        private bool audioCaptureSuspended;
        private int consecutiveFaceMisses;
        private bool initialized;
        private bool captureReady;
        private string faceSessionId;
        private string connectionStatus = "感知：未初始化";

        public UserEmo FaceEmotion { get; private set; }
        public UserEmo VoiceEmotion { get; private set; }
        public UserEmo FusionEmotion { get; private set; }
        public string ConnectionStatus => connectionStatus;
        public string FaceStatus { get; private set; } = "等待面部采集";
        public string VoiceStatus { get; private set; } = "等待语音采集";
        public event Action Updated;
        public event Action<UserEmo, UserEmo> FaceEmotionObserved;

        public void Initialize(AppConfig appConfig)
        {
            config = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            faceSessionId = Guid.NewGuid().ToString("N");
            emotionService = CreateEmotionService();
            fusionService = emotionService as IFusionEmoService;
            nativeFaceFilter = config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace
                ? new FaceEmotionTemporalFilter()
                : null;
            initialized = true;
            connectionStatus = config.enableEmotionPerception && config.emotionRuntimeMode != EmotionRuntimeMode.Disabled
                ? config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace ? "感知：手机端本地人脸分析" : "感知：等待权限"
                : "感知：已关闭";
            if (config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace)
            {
                VoiceStatus = "等待下一句对话的语音线索。";
            }
        }

        public TalkInput ApplyLatest(TalkInput input)
        {
            var target = input ?? throw new ArgumentNullException(nameof(input));
            var now = EmotionTime.UtcNowUnixMs;
            target.faceEmo = EmotionSamplingPolicy.FreshOrNull(FaceEmotion, now, EmotionSamplingPolicy.FaceFreshnessMs);
            target.voiceEmo = EmotionSamplingPolicy.FreshOrNull(VoiceEmotion, now, EmotionSamplingPolicy.VoiceFreshnessMs);
            target.fusionEmo = EmotionSamplingPolicy.FreshOrNull(FusionEmotion, now, EmotionSamplingPolicy.FusionFreshnessMs);
            return target;
        }

        public void SuspendAudioCapture()
        {
            audioCaptureSuspended = true;
            StopMicrophone();
        }

        public void ResumeAudioCapture()
        {
            audioCaptureSuspended = false;
            if (UsesContinuousVoiceCapture && captureReady && microphoneClip == null)
            {
                StartMicrophone();
            }
        }

        private void Start()
        {
            if (initialized && IsPerceptionEnabled)
            {
                StartCapture();
            }
        }

        private void OnEnable()
        {
            if (initialized && IsPerceptionEnabled)
            {
                StartCapture();
            }
        }

        private void Update()
        {
            if (!captureReady || !IsPerceptionEnabled)
            {
                return;
            }

            averageFrameSeconds = Mathf.Lerp(averageFrameSeconds, Time.unscaledDeltaTime, 0.05f);
            var now = Time.unscaledTime;
            RefreshCameraPreviewTransform();
            if (SupportsFaceCapture && cameraTexture != null && cameraTexture.didUpdateThisFrame && !faceRequestInFlight && EmotionSamplingPolicy.IsDue(now, lastFaceSampleAt, EmotionSamplingPolicy.FaceInterval(config.emotionCameraSampleIntervalSeconds, averageFrameSeconds)))
            {
                lastFaceSampleAt = now;
                _ = CaptureFaceAsync(lifetimeCancellation.Token);
            }

            if (UsesContinuousVoiceCapture && !audioCaptureSuspended && microphoneClip != null && !voiceRequestInFlight && EmotionSamplingPolicy.IsDue(now, lastVoiceSampleAt, config.emotionVoiceWindowSeconds))
            {
                lastVoiceSampleAt = now;
                _ = CaptureVoiceAsync(lifetimeCancellation.Token);
            }
        }

        private void OnDisable()
        {
            StopCapture();
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused) StopCapture();
            else if (initialized && isActiveAndEnabled && IsPerceptionEnabled) StartCapture();
        }

        private void OnDestroy()
        {
            StopCapture();
        }

        private void StartCapture()
        {
            if (lifetimeCancellation != null || !IsPerceptionEnabled || applicationPaused)
            {
                return;
            }

            lifetimeCancellation = new CancellationTokenSource();
            _ = RequestPermissionsAndStartAsync(lifetimeCancellation.Token);
        }

        private async Task RequestPermissionsAndStartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var cameraPermission = SupportsFaceCapture
                    ? await new CameraPermissionService().RequestAsync(cancellationToken)
                    : PermState.granted;
                var microphonePermission = UsesContinuousVoiceCapture
                    ? await new MicrophonePermissionService().RequestAsync(cancellationToken)
                    : PermState.granted;
                if (cameraPermission != PermState.granted || microphonePermission != PermState.granted)
                {
                    connectionStatus = "感知：相机或麦克风权限未授权";
                    NotifyUpdated();
                    return;
                }

                if (SupportsFaceCapture)
                {
                    StartCamera();
                }

                if (UsesContinuousVoiceCapture)
                {
                    StartMicrophone();
                }

                captureReady = cameraTexture != null || microphoneClip != null;
                connectionStatus = captureReady
                    ? config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace ? "感知：手机端本地人脸分析" : "感知：正在采集"
                    : "感知：设备不可用";
                NotifyUpdated();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                connectionStatus = "感知：启动失败 - " + ex.Message;
                NotifyUpdated();
            }
        }

        private void StartCamera()
        {
            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                return;
            }

            var selected = devices[0];
            if (config.preferFrontEmotionCamera)
            {
                for (var i = 0; i < devices.Length; i++)
                {
                    if (devices[i].isFrontFacing)
                    {
                        selected = devices[i];
                        break;
                    }
                }
            }

            cameraTexture = new WebCamTexture(selected.name, CameraMaximumDimension, 480, 15);
            cameraTexture.Play();
            CreateCameraPreviewIfEnabled();
        }

        private IEmoService CreateEmotionService()
        {
            if (config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace)
            {
                return GetComponent<AndroidNativeEmotionBridge>() ?? gameObject.AddComponent<AndroidNativeEmotionBridge>();
            }

            return new HttpEmoService(config, faceSessionId);
        }

        private bool IsPerceptionEnabled => config.enableEmotionPerception && config.emotionRuntimeMode != EmotionRuntimeMode.Disabled;

        private bool SupportsFaceCapture => !(emotionService is IEmotionCaptureCapabilities capabilities) || capabilities.SupportsFaceCapture;

        private bool SupportsVoiceCapture => !(emotionService is IEmotionCaptureCapabilities capabilities) || capabilities.SupportsVoiceCapture;

        // Android local mode receives the actual user utterance from the speech bridge.
        // It must not start a second microphone stream for ambient sampling.
        private bool UsesSpeechTurnVoiceCapture => config != null && config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace;
        private bool UsesContinuousVoiceCapture => SupportsVoiceCapture && !UsesSpeechTurnVoiceCapture;

        private string ActiveConnectionStatus => config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace
            ? "感知：手机端本地人脸分析"
            : "感知：服务连接正常";

        private string ActiveConnectionErrorStatus => config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace
            ? "感知：手机端本地分析异常"
            : "感知：服务连接异常";

        private void StartMicrophone()
        {
            if (audioCaptureSuspended || applicationPaused) return;
            var devices = Microphone.devices;
            microphoneDevice = devices != null && devices.Length > 0 ? devices[0] : null;
            if (microphoneDevice == null)
            {
                return;
            }

            var window = Mathf.Clamp(config.emotionVoiceWindowSeconds, 1f, 10f);
            microphoneClip = Microphone.Start(microphoneDevice, true, Mathf.CeilToInt(window) + 1, VoiceSampleRate);
            microphoneStartedAt = Time.unscaledTime;
        }

        private async Task CaptureFaceAsync(CancellationToken cancellationToken)
        {
            faceRequestInFlight = true;
            try
            {
                var jpeg = CaptureJpeg();
                if (jpeg == null || jpeg.Length == 0)
                {
                    return;
                }

                var rawResult = await emotionService.GetFaceAsync(jpeg, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var result = rawResult;
                if (rawResult.IsUsable && nativeFaceFilter != null)
                {
                    result = nativeFaceFilter.Observe(rawResult);
                }
                if (result.IsUsable)
                {
                    FaceEmotion = result;
                    consecutiveFaceMisses = 0;
                    FaceStatus = "";
                    connectionStatus = ActiveConnectionStatus;
                    FaceEmotionObserved?.Invoke(rawResult, result);
                    NotifyUpdated();
                    await RefreshFusionAsync(cancellationToken);
                }
                else
                {
                    consecutiveFaceMisses++;
                    var nowUnixMs = EmotionTime.UtcNowUnixMs;
                    FaceStatus = EmotionSamplingPolicy.IsTransientFaceLoss(FaceEmotion, consecutiveFaceMisses, nowUnixMs)
                        ? "暂时未检测到脸，保留最近结果"
                        : result.error;
                    connectionStatus = IsServiceUnavailable(result) ? ActiveConnectionErrorStatus : ActiveConnectionStatus;
                    NotifyUpdated();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                FaceStatus = "请求失败：" + ex.Message;
                connectionStatus = ActiveConnectionErrorStatus;
                NotifyUpdated();
            }
            finally
            {
                faceRequestInFlight = false;
            }
        }

        public async Task AnalyzeSpeechTurnAsync(byte[] wav, string transcript, CancellationToken cancellationToken)
        {
            if (!IsPerceptionEnabled || !UsesSpeechTurnVoiceCapture || wav == null || wav.Length == 0 || voiceRequestInFlight)
            {
                return;
            }

            voiceRequestInFlight = true;
            try
            {
                var rawResult = await emotionService.GetVoiceAsync(wav, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var calibrated = VoiceEmotionCalibrationPolicy.Calibrate(rawResult, transcript, FaceEmotion, EmotionTime.UtcNowUnixMs);
                await ApplyVoiceResultAsync(calibrated, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                VoiceStatus = "语音情绪分析暂不可用。";
                NotifyUpdated();
            }
            finally
            {
                voiceRequestInFlight = false;
            }
        }

        private async Task CaptureVoiceAsync(CancellationToken cancellationToken)
        {
            voiceRequestInFlight = true;
            try
            {
                var wav = CaptureLatestWav();
                if (wav == null || wav.Length == 0)
                {
                    return;
                }

                var rawResult = await emotionService.GetVoiceAsync(wav, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var calibrated = VoiceEmotionCalibrationPolicy.Calibrate(rawResult, string.Empty, FaceEmotion, EmotionTime.UtcNowUnixMs);
                await ApplyVoiceResultAsync(calibrated, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                VoiceStatus = "语音情绪分析暂不可用。";
                connectionStatus = ActiveConnectionErrorStatus;
                NotifyUpdated();
            }
            finally
            {
                voiceRequestInFlight = false;
            }
        }

        private async Task ApplyVoiceResultAsync(UserEmo result, CancellationToken cancellationToken)
        {
            if (result != null && result.IsUsable)
            {
                VoiceEmotion = result;
                VoiceStatus = "";
                connectionStatus = ActiveConnectionStatus;
                NotifyUpdated();
                await RefreshFusionAsync(cancellationToken);
                return;
            }

            VoiceStatus = DescribeVoiceFailure(result == null ? "" : result.error);
            connectionStatus = IsServiceUnavailable(result) ? ActiveConnectionErrorStatus : ActiveConnectionStatus;
            NotifyUpdated();
        }

        private static string DescribeVoiceFailure(string error)
        {
            if (!string.IsNullOrWhiteSpace(error) && error.IndexOf("未检测到清晰语音", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "本句语音较弱，未生成情绪线索。";
            }

            return string.IsNullOrWhiteSpace(error) ? "本句未生成语音情绪线索。" : error;
        }
        private async Task RefreshFusionAsync(CancellationToken cancellationToken)
        {
            if (fusionService == null)
            {
                return;
            }

            if (fusionRequestInFlight)
            {
                fusionRefreshQueued = true;
                return;
            }

            do
            {
                fusionRefreshQueued = false;
                fusionRequestInFlight = true;
                try
                {
                    var result = await fusionService.GetFusionAsync(FaceEmotion, VoiceEmotion, cancellationToken);
                    if (result.IsUsable)
                    {
                        FusionEmotion = result;
                        connectionStatus = ActiveConnectionStatus;
                        NotifyUpdated();
                    }
                }
                finally
                {
                    fusionRequestInFlight = false;
                }
            } while (fusionRefreshQueued && !cancellationToken.IsCancellationRequested);
        }

        private byte[] CaptureJpeg()
        {
            if (cameraTexture == null || !cameraTexture.isPlaying || cameraTexture.width < 16 || cameraTexture.height < 16)
            {
                return null;
            }

            if (!cameraTexture.didUpdateThisFrame) return null;
            if (cameraPixels == null || cameraPixels.Length != cameraTexture.width * cameraTexture.height)
                cameraPixels = new Color32[cameraTexture.width * cameraTexture.height];
            cameraTexture.GetPixels32(cameraPixels);
            CameraFrameTransform.OrientAndScale(cameraPixels, cameraTexture.width, cameraTexture.height,
                cameraTexture.videoRotationAngle, cameraTexture.videoVerticallyMirrored, CameraMaximumDimension,
                ref orientedPixels, out var targetWidth, out var targetHeight);
            if (orientedPixels.Length == 0) return null;
            if (captureTexture == null || captureTexture.width != targetWidth || captureTexture.height != targetHeight)
            {
                if (captureTexture != null) Destroy(captureTexture);
                captureTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            }
            captureTexture.SetPixels32(orientedPixels);
            // CPU encoding does not need a GPU texture upload.
            return captureTexture.EncodeToJPG(CameraJpegQuality);
        }

        private byte[] CaptureLatestWav()
        {
            if (microphoneClip == null || string.IsNullOrWhiteSpace(microphoneDevice))
            {
                return null;
            }

            var position = Microphone.GetPosition(microphoneDevice);
            if (position < 0)
            {
                return null;
            }

            var requestedFrames = Mathf.RoundToInt(Mathf.Clamp(config.emotionVoiceWindowSeconds, 1f, 10f) * VoiceSampleRate);
            if (Time.unscaledTime - microphoneStartedAt < config.emotionVoiceWindowSeconds)
            {
                return null;
            }

            var sampleCount = microphoneClip.samples * microphoneClip.channels;
            if (microphoneSamples == null || microphoneSamples.Length != sampleCount) microphoneSamples = new float[sampleCount];
            if (!microphoneClip.GetData(microphoneSamples, 0))
            {
                return null;
            }

            return Pcm16Wav.EncodeMono(Pcm16Wav.ExtractLatestMono(microphoneSamples, microphoneClip.channels, position, requestedFrames), VoiceSampleRate);
        }

        private void StopCapture()
        {
            captureReady = false;
            if (captureTexture != null) Destroy(captureTexture);
            captureTexture = null;
            cameraPixels = orientedPixels = null;
            microphoneSamples = null;
            averageFrameSeconds = 0f;
            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
            if (cameraTexture != null)
            {
                if (cameraTexture.isPlaying)
                {
                    cameraTexture.Stop();
                }

                Destroy(cameraTexture);
                cameraTexture = null;
            }

            if (cameraPreview != null)
            {
                cameraPreview.texture = null;
                previewRotationAngle = int.MinValue;
            }

            StopMicrophone();
        }

        private void StopMicrophone()
        {
            if (!string.IsNullOrWhiteSpace(microphoneDevice) && Microphone.IsRecording(microphoneDevice))
            {
                Microphone.End(microphoneDevice);
            }

            microphoneClip = null;
            microphoneDevice = null;
            microphoneStartedAt = 0f;
        }

        private void NotifyUpdated()
        {
            Updated?.Invoke();
        }


        private void CreateCameraPreviewIfEnabled()
        {
            if (!config.showEmotionCameraPreview || cameraTexture == null)
            {
                return;
            }

            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var existing = canvas.transform.Find("Emotion Camera Preview")?.GetComponent<RawImage>();
            if (existing != null)
            {
                cameraPreview = existing;
            }
            else
            {
                var previewObject = new GameObject("Emotion Camera Preview");
                previewObject.transform.SetParent(canvas.transform, false);
                cameraPreview = previewObject.AddComponent<RawImage>();
                cameraPreview.color = Color.white;
                var rect = cameraPreview.rectTransform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
            }

            cameraPreview.texture = cameraTexture;
            previewRotationAngle = int.MinValue;
            RefreshCameraPreviewTransform();
        }

        private void RefreshCameraPreviewTransform()
        {
            if (cameraPreview == null || cameraTexture == null || cameraPreview.texture != cameraTexture || cameraTexture.width < 16 || cameraTexture.height < 16)
            {
                return;
            }

            var rotation = cameraTexture.videoRotationAngle;
            var verticallyMirrored = cameraTexture.videoVerticallyMirrored;
            if (rotation == previewRotationAngle && verticallyMirrored == previewVerticallyMirrored)
            {
                return;
            }

            previewRotationAngle = rotation;
            previewVerticallyMirrored = verticallyMirrored;
            var rect = cameraPreview.rectTransform;
            var displayBounds = CameraPreviewTransform.GetDisplayBounds(PreviewTextureRectSize, rotation);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = PreviewTextureRectSize;
            rect.anchoredPosition = new Vector2(
                -PreviewRightMargin - displayBounds.x * .5f,
                -PreviewTopMargin - displayBounds.y * .5f);
            rect.localEulerAngles = new Vector3(0f, 0f, CameraPreviewTransform.GetZRotation(rotation));
            cameraPreview.uvRect = CameraPreviewTransform.GetUvRect(verticallyMirrored);
        }

        private static bool IsServiceUnavailable(UserEmo result)
        {
            return result != null
                && !string.IsNullOrWhiteSpace(result.error)
                && result.error.StartsWith("情绪服务不可用", StringComparison.Ordinal);
        }
    }
}
