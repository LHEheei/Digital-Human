using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class SpeechInputService
    {
        private readonly AppConfig config;
        private readonly AndroidNativeSpeechBridge androidSpeech;
        private readonly MicrophonePermissionService microphonePermission;

        public SpeechInputService(AppConfig config, AndroidNativeSpeechBridge androidSpeech, MicrophonePermissionService microphonePermission)
        {
            this.config = config;
            this.androidSpeech = androidSpeech;
            this.microphonePermission = microphonePermission;
        }

        public bool CanRecognize => config != null && config.preferAndroidNativeSpeech && androidSpeech != null && androidSpeech.CanRecognize;

        public async Task<string> ListenForUserTextAsync(Action<string> reportSubtitle)
        {
            if (CanRecognize)
            {
                if (!await microphonePermission.EnsureGrantedAsync())
                {
                    throw new InvalidOperationException("未获得麦克风权限，无法开始语音识别。请在系统设置中允许麦克风权限后重试。");
                }

                reportSubtitle?.Invoke("系统语音识别启动中，请说话...");
                var timeout = Mathf.Max(5f, config.speechRecognitionTimeoutSeconds);
                var text = await TaskTimeout.WithTimeout(androidSpeech.RecognizeAsync(config.speechLanguage), timeout, "系统语音识别超时。", androidSpeech.CancelRecognition);
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
    }
}
