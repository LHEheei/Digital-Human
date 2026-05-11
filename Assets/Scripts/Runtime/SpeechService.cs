using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class SpeechService
    {
        private readonly AppConfig config;
        private readonly AndroidNativeSpeechBridge androidSpeech;

        public SpeechService(AppConfig config, AndroidNativeSpeechBridge androidSpeech)
        {
            this.config = config;
            this.androidSpeech = androidSpeech;
        }

        public bool CanUseAndroidSpeech =>
            config != null
            && config.preferAndroidNativeSpeech
            && androidSpeech != null
            && androidSpeech.IsAvailable;

        public async Task<string> ListenForUserTextAsync(Action<string> reportSubtitle)
        {
            if (CanUseAndroidSpeech)
            {
                reportSubtitle?.Invoke("系统语音识别启动中，请说话...");
                var timeout = Mathf.Max(5f, config.speechRecognitionTimeoutSeconds);
                var text = await TaskTimeout.WithTimeout(
                    androidSpeech.RecognizeAsync(config.speechLanguage),
                    timeout,
                    "系统语音识别超时。",
                    androidSpeech.CancelRecognition);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var trimmed = text.Trim();
                    reportSubtitle?.Invoke($"你：{trimmed}");
                    return trimmed;
                }
            }

            if (config == null || config.useLocalDemoWhenDeepSeekKeyMissing || Application.isEditor)
            {
                var demoText = LocalDemoDialogue.UserText(config);
                reportSubtitle?.Invoke($"你：{demoText}");
                return demoText;
            }

            throw new InvalidOperationException("当前设备没有可用的系统语音识别服务。");
        }

        public async Task SpeakWithAndroidTtsAsync(string reply)
        {
            if (!CanUseAndroidSpeech)
            {
                throw new InvalidOperationException("当前环境没有可用的安卓系统 TTS。");
            }

            var timeout = ResolveTtsTimeout(reply);
            await TaskTimeout.WithTimeout(androidSpeech.SpeakAsync(reply, config.speechLanguage), timeout, "系统 TTS 播报超时。");
        }

        private float ResolveTtsTimeout(string reply)
        {
            var baseSeconds = config == null ? 5f : Mathf.Max(3f, config.ttsBaseTimeoutSeconds);
            var secondsPerCharacter = config == null ? 0.25f : Mathf.Max(0.05f, config.ttsSecondsPerCharacter);
            return Mathf.Clamp((reply ?? "").Length * secondsPerCharacter + baseSeconds, 8f, 30f);
        }
    }
}
