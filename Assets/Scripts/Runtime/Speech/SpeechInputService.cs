using System;
using System.Threading;
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

        public bool CanRecognize => config.preferAndroidNativeSpeech && androidSpeech.CanRecognize;

        public async Task<string> ListenForUserTextAsync(Action<string> reportSubtitle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CanRecognize)
            {
                var permission = await microphonePermission.RequestAsync(cancellationToken);
                if (permission != PermState.granted)
                {
                    throw new AppException(AppErr.micPerm, PermissionMessage(permission));
                }

                reportSubtitle?.Invoke("系统语音识别启动中，请说话...");
                var timeout = Mathf.Max(5f, config.speechRecognitionTimeoutSeconds);
                string text;
                try
                {
                    text = await TaskTimeout.WithTimeout(androidSpeech.RecognizeAsync(config.speechLanguage, config.preferOnDeviceSpeechRecognition), timeout, "系统语音识别超时。", androidSpeech.CancelRecognition, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var message = string.IsNullOrWhiteSpace(ex.Message) ? "系统语音识别失败。" : ex.Message;
                    throw new AppException(AppErr.speech, message, ex);
                }
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }


            throw new AppException(AppErr.speech, "当前设备没有可用的系统语音识别服务。");
        }

        public void Cancel()
        {
            androidSpeech.CancelRecognition();
        }

        private static string PermissionMessage(PermState state)
        {
            switch (state)
            {
                case PermState.blocked:
                    return "麦克风权限已被禁止，请前往系统设置开启。";
                case PermState.timeout:
                    return "麦克风权限请求超时，请重试。";
                default:
                    return "未获得麦克风权限，可重新授权后再试。";
            }
        }
    }
}
