using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    /// <summary>
    /// Android-only bridge for ML Kit face cropping and embedded HSEmotion ONNX
    /// inference. The image stays on the device; only compact scores return.
    /// </summary>
    public sealed class AndroidNativeEmotionBridge : MonoBehaviour, IEmoService, IFusionEmoService, IEmotionCaptureCapabilities
    {
        private const char CallbackSeparator = '\n';
        private const string FaceModelResourcePath = "Models/hsemotion_enet_b0_8_best_afew";
        private readonly Dictionary<string, TaskCompletionSource<UserEmo>> pendingRequests = new Dictionary<string, TaskCompletionSource<UserEmo>>();

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject bridge;
#endif

        public bool SupportsFaceCapture => true;
        public bool SupportsVoiceCapture => false;

        public Task<UserEmo> GetFaceAsync(byte[] image, CancellationToken cancellationToken)
        {
            if (image == null || image.Length == 0)
            {
                return Task.FromResult(UserEmo.Failure("face", "没有可供手机端分析的图像。"));
            }

            cancellationToken.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                EnsureBridge();
                if (bridge == null)
                {
                    return Task.FromResult(UserEmo.Failure("face", "手机端人脸分析组件不可用。"));
                }

                var requestId = Guid.NewGuid().ToString("N");
                var completion = new TaskCompletionSource<UserEmo>();
                pendingRequests[requestId] = completion;
                bridge.Call("analyzeFace", requestId, Convert.ToBase64String(image));
                return completion.Task;
            }
            catch (Exception ex)
            {
                return Task.FromResult(UserEmo.Failure("face", "手机端人脸分析启动失败：" + ex.Message));
            }
#else
            return Task.FromResult(UserEmo.Failure("face", "手机端人脸分析仅支持 Android 真机。"));
#endif
        }

        public Task<UserEmo> GetVoiceAsync(byte[] audio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(UserEmo.Failure("voice", "当前独立版尚未内置端侧声学情绪模型。"));
        }

        public Task<UserEmo> GetFusionAsync(UserEmo face, UserEmo voice, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LocalEmotionFusion.Fuse(face, voice, EmotionTime.UtcNowUnixMs));
        }

        public void OnNativeFaceResult(string callback)
        {
            if (!TryReadCallback(callback, out var requestId, out var payload) || !pendingRequests.TryGetValue(requestId, out var completion))
            {
                return;
            }

            pendingRequests.Remove(requestId);
            if (HttpEmoService.TryParseResult(payload, "face", out var result))
            {
                completion.TrySetResult(result);
            }
            else
            {
                completion.TrySetResult(UserEmo.Failure("face", "手机端人脸分析返回无效结果。"));
            }
        }

        public void OnNativeFaceError(string callback)
        {
            if (!TryReadCallback(callback, out var requestId, out var message) || !pendingRequests.TryGetValue(requestId, out var completion))
            {
                return;
            }

            pendingRequests.Remove(requestId);
            completion.TrySetResult(UserEmo.Failure("face", string.IsNullOrWhiteSpace(message) ? "手机端人脸分析失败。" : message));
        }

        private void OnDestroy()
        {
            foreach (var pending in pendingRequests.Values)
            {
                pending.TrySetCanceled();
            }

            pendingRequests.Clear();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.Call("shutdown");
                bridge.Dispose();
                bridge = null;
            }
#endif
        }

        private static bool TryReadCallback(string callback, out string requestId, out string payload)
        {
            requestId = "";
            payload = "";
            if (string.IsNullOrWhiteSpace(callback))
            {
                return false;
            }

            var separatorIndex = callback.IndexOf(CallbackSeparator);
            if (separatorIndex <= 0)
            {
                return false;
            }

            requestId = callback.Substring(0, separatorIndex);
            payload = callback.Substring(separatorIndex + 1);
            return !string.IsNullOrWhiteSpace(requestId);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void EnsureBridge()
        {
            if (bridge != null)
            {
                return;
            }

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            var model = Resources.Load<TextAsset>(FaceModelResourcePath);
            if (model == null)
            {
                throw new InvalidOperationException("未找到手机端 HSEmotion 模型资源。");
            }

            try
            {
                var modelBytes = model.bytes;
                if (modelBytes == null || modelBytes.Length == 0)
                {
                    throw new InvalidOperationException("手机端 HSEmotion 模型资源为空。");
                }

                // Unity 6 maps sbyte[] to Java byte[]. Passing byte[] still works,
                // but uses the obsolete JNI conversion path and emits a warning.
                var signedModelBytes = new sbyte[modelBytes.Length];
                Buffer.BlockCopy(modelBytes, 0, signedModelBytes, 0, modelBytes.Length);

                bridge = new AndroidJavaObject(
                    "com.srtp.avatar.NativeEmotionBridge",
                    activity,
                    gameObject.name,
                    signedModelBytes);
            }
            finally
            {
                Resources.UnloadAsset(model);
            }
        }
#endif
    }
}
