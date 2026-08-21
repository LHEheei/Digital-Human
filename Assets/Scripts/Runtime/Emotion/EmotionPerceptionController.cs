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
        private CancellationTokenSource lifetimeCancellation;
        private WebCamTexture cameraTexture;
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

        public void Initialize(AppConfig appConfig)
        {
            config = appConfig;
            faceSessionId = Guid.NewGuid().ToString("N");
            emotionService = CreateEmotionService();
            fusionService = emotionService as IFusionEmoService;
            initialized = true;
            connectionStatus = config != null && config.enableEmotionPerception && config.emotionRuntimeMode != EmotionRuntimeMode.Disabled
                ? config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace ? "感知：手机端本地人脸分析" : "感知：等待权限"
                : "感知：已关闭";
            if (config != null && config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace)
            {
                VoiceStatus = "端侧声学情绪模型待接入；对话仍会使用语句内容。";
            }
        }

        public TalkInput ApplyLatest(TalkInput input)
        {
            var target = input ?? new TalkInput();
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
            if (captureReady && microphoneClip == null)
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

            var now = Time.unscaledTime;
            RefreshCameraPreviewTransform();
            if (SupportsFaceCapture && !faceRequestInFlight && EmotionSamplingPolicy.IsDue(now, lastFaceSampleAt, config.emotionCameraSampleIntervalSeconds))
            {
                lastFaceSampleAt = now;
                _ = CaptureFaceAsync(lifetimeCancellation.Token);
            }

            if (SupportsVoiceCapture && !audioCaptureSuspended && microphoneClip != null && !voiceRequestInFlight && EmotionSamplingPolicy.IsDue(now, lastVoiceSampleAt, config.emotionVoiceWindowSeconds))
            {
                lastVoiceSampleAt = now;
                _ = CaptureVoiceAsync(lifetimeCancellation.Token);
            }
        }

        private void OnDisable()
        {
            StopCapture();
        }

        private void OnDestroy()
        {
            StopCapture();
        }

        private void StartCapture()
        {
            if (lifetimeCancellation != null || !IsPerceptionEnabled)
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
                var microphonePermission = SupportsVoiceCapture
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

                if (SupportsVoiceCapture)
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
            if (config != null && config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace)
            {
                return GetComponent<AndroidNativeEmotionBridge>() ?? gameObject.AddComponent<AndroidNativeEmotionBridge>();
            }

            return new HttpEmoService(config, faceSessionId);
        }

        private bool IsPerceptionEnabled => config != null && config.enableEmotionPerception && config.emotionRuntimeMode != EmotionRuntimeMode.Disabled;

        private bool SupportsFaceCapture => !(emotionService is IEmotionCaptureCapabilities capabilities) || capabilities.SupportsFaceCapture;

        private bool SupportsVoiceCapture => !(emotionService is IEmotionCaptureCapabilities capabilities) || capabilities.SupportsVoiceCapture;

        private string ActiveConnectionStatus => config != null && config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace
            ? "感知：手机端本地人脸分析"
            : "感知：服务连接正常";

        private string ActiveConnectionErrorStatus => config != null && config.emotionRuntimeMode == EmotionRuntimeMode.AndroidOnDeviceFace
            ? "感知：手机端本地分析异常"
            : "感知：服务连接异常";

        private void StartMicrophone()
        {
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

                var result = await emotionService.GetFaceAsync(jpeg, cancellationToken);
                if (result.IsUsable)
                {
                    FaceEmotion = result;
                    consecutiveFaceMisses = 0;
                    FaceStatus = "";
                    connectionStatus = ActiveConnectionStatus;
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
                FaceStatus = "请求失败：" + ex.Message;
                connectionStatus = ActiveConnectionErrorStatus;
                NotifyUpdated();
            }
            finally
            {
                faceRequestInFlight = false;
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

                var result = await emotionService.GetVoiceAsync(wav, cancellationToken);
                if (result.IsUsable)
                {
                    VoiceEmotion = result;
                    VoiceStatus = "";
                    connectionStatus = ActiveConnectionStatus;
                    NotifyUpdated();
                    await RefreshFusionAsync(cancellationToken);
                }
                else
                {
                    VoiceStatus = result.error;
                    connectionStatus = IsServiceUnavailable(result) ? ActiveConnectionErrorStatus : ActiveConnectionStatus;
                    NotifyUpdated();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                VoiceStatus = "请求失败：" + ex.Message;
                connectionStatus = ActiveConnectionErrorStatus;
                NotifyUpdated();
            }
            finally
            {
                voiceRequestInFlight = false;
            }
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

            Texture2D output = null;
            try
            {
                var pixels = CameraFrameTransform.OrientAndScale(
                    cameraTexture.GetPixels32(),
                    cameraTexture.width,
                    cameraTexture.height,
                    cameraTexture.videoRotationAngle,
                    cameraTexture.videoVerticallyMirrored,
                    CameraMaximumDimension,
                    out var targetWidth,
                    out var targetHeight);
                if (pixels.Length == 0)
                {
                    return null;
                }

                output = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                output.SetPixels32(pixels);
                output.Apply(false, false);
                return output.EncodeToJPG(CameraJpegQuality);
            }
            finally
            {
                if (output != null)
                {
                    Destroy(output);
                }
            }
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
            if (Time.unscaledTime - microphoneStartedAt < config.emotionVoiceWindowSeconds || position < 0)
            {
                return null;
            }

            var interleaved = new float[microphoneClip.samples * microphoneClip.channels];
            if (!microphoneClip.GetData(interleaved, 0))
            {
                return null;
            }

            return Pcm16Wav.EncodeMono(Pcm16Wav.ExtractLatestMono(interleaved, microphoneClip.channels, position, requestedFrames), VoiceSampleRate);
        }

        private void StopCapture()
        {
            captureReady = false;
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
            if (config == null || !config.showEmotionCameraPreview || cameraTexture == null)
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
