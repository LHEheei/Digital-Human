using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class SpeechService
    {
        public SpeechService(AppConfig config, AndroidNativeSpeechBridge androidSpeech)
        {
            Input = new SpeechInputService(config, androidSpeech, new MicrophonePermissionService());
            Output = new SpeechOutputService(config, androidSpeech);
        }

        public SpeechInputService Input { get; }
        public SpeechOutputService Output { get; }
        public bool CanRecognize => Input.CanRecognize;
        public bool CanSpeak => Output.CanSpeak;

        public Task<string> ListenForUserTextAsync(Action<string> reportSubtitle)
        {
            return Input.ListenForUserTextAsync(reportSubtitle);
        }

        public Task SpeakWithAndroidTtsAsync(string reply)
        {
            return Output.SpeakAsync(reply);
        }
    }
}
