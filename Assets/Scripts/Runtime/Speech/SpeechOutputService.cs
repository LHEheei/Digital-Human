using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class SpeechOutputService
    {
        private readonly AppConfig config;
        private readonly AndroidNativeSpeechBridge androidSpeech;

        public SpeechOutputService(AppConfig config, AndroidNativeSpeechBridge androidSpeech)
        {
            this.config = config;
            this.androidSpeech = androidSpeech;
        }

        public bool CanSpeak => config != null && config.preferAndroidNativeSpeech && androidSpeech != null && androidSpeech.CanSpeak;

        public async Task SpeakAsync(string reply, CancellationToken cancellationToken)
        {
            if (!CanSpeak)
            {
                throw new AppException(AppErr.tts, "当前环境没有可用的 Android 系统 TTS。");
            }

            var timeout = ResolveTtsTimeout(reply);
            try
            {
                await TaskTimeout.WithTimeout(androidSpeech.SpeakAsync(reply, config.speechLanguage), timeout, "系统 TTS 播报超时。", androidSpeech.StopSpeaking, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AppException(AppErr.tts, "系统 TTS 播报失败。", ex);
            }
        }

        public void Stop()
        {
            androidSpeech?.StopSpeaking();
        }

        private float ResolveTtsTimeout(string reply)
        {
            var baseSeconds = config == null ? 5f : Mathf.Max(3f, config.ttsBaseTimeoutSeconds);
            var secondsPerCharacter = config == null ? 0.25f : Mathf.Max(0.05f, config.ttsSecondsPerCharacter);
            return Mathf.Clamp((reply ?? "").Length * secondsPerCharacter + baseSeconds, 8f, 30f);
        }
    }
}
