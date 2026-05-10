using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class AndroidNativeSpeechBridge : MonoBehaviour
    {
        private TaskCompletionSource<string> recognitionCompletion;
        private TaskCompletionSource<bool> ttsCompletion;
        private bool ttsSpeaking;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject bridge;
#endif

        public bool IsAvailable
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

        public bool IsTtsSpeaking => ttsSpeaking;

        public Task<string> RecognizeAsync(string language)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureBridge();
            if (bridge == null)
            {
                throw new InvalidOperationException("Android 语音桥接初始化失败。");
            }

            recognitionCompletion = new TaskCompletionSource<string>();
            bridge.Call("startRecognition", NormalizeLanguage(language));
            return recognitionCompletion.Task;
#else
            return Task.FromResult("你好，请介绍一下你现在能做什么。");
#endif
        }

        public void CancelRecognition()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.Call("cancelRecognition");
            }
#endif
            recognitionCompletion?.TrySetCanceled();
            recognitionCompletion = null;
        }

        public Task SpeakAsync(string text, string language)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureBridge();
            if (bridge == null)
            {
                throw new InvalidOperationException("Android TTS 桥接初始化失败。");
            }

            ttsCompletion = new TaskCompletionSource<bool>();
            bridge.Call("speak", string.IsNullOrWhiteSpace(text) ? "好的。" : text, NormalizeLanguage(language));
            return ttsCompletion.Task;
#else
            return Task.CompletedTask;
#endif
        }

        public void OnAndroidSpeechResult(string text)
        {
            recognitionCompletion?.TrySetResult(string.IsNullOrWhiteSpace(text) ? "" : text.Trim());
            recognitionCompletion = null;
        }

        public void OnAndroidSpeechError(string message)
        {
            recognitionCompletion?.TrySetException(new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "安卓语音识别失败。" : message));
            recognitionCompletion = null;
        }

        public void OnAndroidTtsStarted(string _)
        {
            ttsSpeaking = true;
        }

        public void OnAndroidTtsDone(string _)
        {
            ttsSpeaking = false;
            ttsCompletion?.TrySetResult(true);
            ttsCompletion = null;
        }

        public void OnAndroidTtsError(string message)
        {
            ttsSpeaking = false;
            ttsCompletion?.TrySetException(new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "安卓语音合成失败。" : message));
            ttsCompletion = null;
        }

        private void OnDestroy()
        {
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
