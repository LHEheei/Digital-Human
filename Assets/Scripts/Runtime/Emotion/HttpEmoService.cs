using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public sealed class HttpEmoService : IEmoService, IFusionEmoService, IEmotionCaptureCapabilities
    {
        private readonly string baseUrl;
        private readonly int timeoutSeconds;
        private readonly string faceSessionId;

        public HttpEmoService(AppConfig config, string sessionId = "")
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            baseUrl = NormalizeBaseUrl(config.emotionServiceBaseUrl);
            timeoutSeconds = Mathf.Clamp(config.emotionRequestTimeoutSeconds, 5, 30);
            faceSessionId = sessionId?.Trim() ?? "";
        }

        public bool SupportsFaceCapture => true;
        public bool SupportsVoiceCapture => true;

        public Task<UserEmo> GetFaceAsync(byte[] image, CancellationToken cancellationToken)
        {
            return UploadMediaAsync("/v1/emotions/face", image, "image/jpeg", "face", cancellationToken);
        }

        public Task<UserEmo> GetVoiceAsync(byte[] audio, CancellationToken cancellationToken)
        {
            return UploadMediaAsync("/v1/emotions/voice", audio, "audio/wav", "voice", cancellationToken);
        }

        public Task<UserEmo> GetFusionAsync(UserEmo face, UserEmo voice, CancellationToken cancellationToken)
        {
            var body = new FusionRequest { face = face, voice = voice };
            return SendJsonAsync("/v1/emotions/fusion", JsonUtility.ToJson(body), "fusion", cancellationToken);
        }

        public static bool TryParseResult(string json, string expectedSource, out UserEmo result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                result = JsonUtility.FromJson<UserEmo>(json)?.Normalize();
                return result != null && string.Equals(result.source, expectedSource, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<UserEmo> UploadMediaAsync(string path, byte[] payload, string contentType, string source, CancellationToken cancellationToken)
        {
            if (payload == null || payload.Length == 0)
            {
                return UserEmo.Failure(source, "没有可上传的识别数据。");
            }

            var form = new WWWForm();
            form.AddBinaryData("file", payload, source + ".bin", contentType);
            using var request = UnityWebRequest.Post(ResolveUrl(path), form);
            if (source == "face" && !string.IsNullOrWhiteSpace(faceSessionId))
            {
                request.SetRequestHeader("X-SRTP-Emotion-Session", faceSessionId);
            }

            return await SendRequestAsync(request, source, cancellationToken);
        }

        private async Task<UserEmo> SendJsonAsync(string path, string json, string source, CancellationToken cancellationToken)
        {
            using var request = new UnityWebRequest(ResolveUrl(path), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            return await SendRequestAsync(request, source, cancellationToken);
        }

        private async Task<UserEmo> SendRequestAsync(UnityWebRequest request, string source, CancellationToken cancellationToken)
        {
            try
            {
                await UnityWebRequestUtil.SendAsync(request, timeoutSeconds, AppErr.network, false, cancellationToken);
                return TryParseResult(request.downloadHandler.text, source, out var result)
                    ? result
                    : UserEmo.Failure(source, "情绪服务返回格式无效。");
            }
            catch (AppException) when (request.responseCode == 422)
            {
                return UserEmo.Failure(source, TryParseErrorDetail(request.downloadHandler.text, out var detail)
                    ? detail
                    : "输入内容不适合进行情绪识别。");
            }
            catch (AppException) when (request.responseCode == 503)
            {
                // The server is reachable but its optional model is still
                // loading. Keep the last valid result and let the regular
                // sampling loop retry instead of reporting a network fault.
                return UserEmo.Failure(source, "情绪模型正在预热，请稍候。");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return UserEmo.Failure(source, $"情绪服务不可用：{ex.Message}");
            }
        }

        public static bool TryParseErrorDetail(string json, out string detail)
        {
            detail = "";
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                detail = JsonUtility.FromJson<ErrorResponse>(json)?.detail?.Trim() ?? "";
                return !string.IsNullOrWhiteSpace(detail);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string ResolveUrl(string path)
        {
            return baseUrl + path;
        }

        private static string NormalizeBaseUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Emotion Service Base Url 必须是有效 HTTP 地址。", nameof(value));
            }

            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        [Serializable]
        private sealed class FusionRequest
        {
            public UserEmo face;
            public UserEmo voice;
        }

        [Serializable]
        private sealed class ErrorResponse
        {
            public string detail;
        }
    }
}
