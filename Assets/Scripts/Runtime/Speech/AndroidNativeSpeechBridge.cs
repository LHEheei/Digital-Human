using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class AndroidNativeSpeechBridge : MonoBehaviour
    {
        private const char CallbackSeparator = '\n';
        private TaskCompletionSource<SpeechRecognitionTurn> recognitionCompletion;
        private TaskCompletionSource<bool> ttsCompletion;
        private string recognitionId;
        private Action recognitionReadyCallback;
        private byte[] recognitionAudioWav;
        private string utteranceId;
        private bool ttsSpeaking;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject bridge;
#endif

        public bool CanRecognize
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                EnsureBridge();
                return bridge != null && bridge.Call<bool>("isSpeechRecognitionAvailable");
#else
                return false;
#endif
            }
        }

        public bool CanSpeak
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                EnsureBridge();
                return bridge != null && bridge.Call<bool>("isTextToSpeechAvailable");
#else
                return false;
#endif
            }
        }

        public bool IsTtsSpeaking => ttsSpeaking;

        public Task<SpeechRecognitionTurn> RecognizeAsync(string language, bool preferOnDevice, Action onReady)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureBridge();
            if (bridge == null)
            {
                throw new InvalidOperationException("Android 语音桥接初始化失败。");
            }

            CancelRecognition();
            recognitionId = Guid.NewGuid().ToString("N");
            recognitionCompletion = new TaskCompletionSource<SpeechRecognitionTurn>();
            recognitionReadyCallback = onReady;
            recognitionAudioWav = null;
            bridge.Call("startRecognition", NormalizeLanguage(language), recognitionId, preferOnDevice);
            return recognitionCompletion.Task;
#else
            return Task.FromException<SpeechRecognitionTurn>(new InvalidOperationException("当前环境没有可用的 Android 语音识别服务。"));
#endif
        }

        public void CancelRecognition()
        {
            var completion = recognitionCompletion;
            recognitionCompletion = null;
            recognitionId = null;
            recognitionReadyCallback = null;
            recognitionAudioWav = null;
            completion?.TrySetCanceled();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.Call("cancelRecognition");
            }
#endif
        }

        public Task SpeakAsync(string text, string language)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureBridge();
            if (bridge == null)
            {
                throw new InvalidOperationException("Android TTS 桥接初始化失败。");
            }

            StopSpeaking();
            utteranceId = Guid.NewGuid().ToString("N");
            ttsCompletion = new TaskCompletionSource<bool>();
            bridge.Call("speak", string.IsNullOrWhiteSpace(text) ? "好的。" : text, NormalizeLanguage(language), utteranceId);
            return ttsCompletion.Task;
#else
            return Task.FromException(new InvalidOperationException("当前环境没有可用的 Android TTS 服务。"));
#endif
        }

        public void StopSpeaking()
        {
            var completion = ttsCompletion;
            ttsCompletion = null;
            utteranceId = null;
            ttsSpeaking = false;
            completion?.TrySetCanceled();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.Call("stopSpeaking");
            }
#endif
        }

        public void OnAndroidSpeechAudio(string callback)
        {
            if (!TryReadCallback(callback, recognitionId, out var payload))
            {
                return;
            }

            try
            {
                recognitionAudioWav = string.IsNullOrWhiteSpace(payload) ? null : Convert.FromBase64String(payload);
            }
            catch (FormatException)
            {
                recognitionAudioWav = null;
            }
        }

        public void OnAndroidSpeechResult(string callback)
        {
            if (!TryReadCallback(callback, recognitionId, out var text))
            {
                return;
            }

            CompleteRecognition(text, null);
        }

        public void OnAndroidSpeechReady(string callback)
        {
            if (!TryReadCallback(callback, recognitionId, out _) || recognitionCompletion == null)
            {
                return;
            }

            recognitionReadyCallback?.Invoke();
        }

        public void OnAndroidSpeechError(string callback)
        {
            if (!TryReadCallback(callback, recognitionId, out var message))
            {
                return;
            }

            CompleteRecognition(null, string.IsNullOrWhiteSpace(message) ? "Android 语音识别失败。" : message);
        }

        public void OnAndroidTtsStarted(string callback)
        {
            if (TryReadCallback(callback, utteranceId, out _))
            {
                ttsSpeaking = true;
            }
        }

        public void OnAndroidTtsDone(string callback)
        {
            if (TryReadCallback(callback, utteranceId, out _))
            {
                CompleteTts(null);
            }
        }

        public void OnAndroidTtsError(string callback)
        {
            if (!TryReadCallback(callback, utteranceId, out var message))
            {
                return;
            }

            CompleteTts(string.IsNullOrWhiteSpace(message) ? "Android TTS 播报失败。" : message);
        }

        private void CompleteRecognition(string text, string error)
        {
            var completion = recognitionCompletion;
            var audioWav = recognitionAudioWav;
            recognitionCompletion = null;
            recognitionId = null;
            recognitionReadyCallback = null;
            recognitionAudioWav = null;
            if (completion == null)
            {
                return;
            }

            if (error == null)
            {
                completion.TrySetResult(new SpeechRecognitionTurn(text, audioWav));
            }
            else
            {
                completion.TrySetException(new InvalidOperationException(error));
            }
        }

        private void CompleteTts(string error)
        {
            var completion = ttsCompletion;
            ttsCompletion = null;
            utteranceId = null;
            ttsSpeaking = false;
            if (completion == null)
            {
                return;
            }

            if (error == null)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(new InvalidOperationException(error));
            }
        }

        public static bool TryReadCallback(string callback, string expectedId, out string payload)
        {
            payload = "";
            if (string.IsNullOrWhiteSpace(callback) || string.IsNullOrWhiteSpace(expectedId))
            {
                return false;
            }

            var separatorIndex = callback.IndexOf(CallbackSeparator);
            if (separatorIndex <= 0 || !string.Equals(callback.Substring(0, separatorIndex), expectedId, StringComparison.Ordinal))
            {
                return false;
            }

            payload = callback.Substring(separatorIndex + 1);
            return true;
        }

        private void OnDestroy()
        {
            CancelRecognition();
            StopSpeaking();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.Call("shutdown");
                bridge.Dispose();
                bridge = null;
            }
#endif
        }

        private static string NormalizeLanguage(string language)
        {
            return string.IsNullOrWhiteSpace(language) ? "zh-CN" : language.Replace("_", "-");
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
            bridge = new AndroidJavaObject("com.srtp.avatar.NativeSpeechBridge", activity, gameObject.name);
        }
#endif
    }
}