using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class SpeechService
    {
        public SpeechService(AppConfig config, AndroidNativeSpeechBridge androidSpeech)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (androidSpeech == null)
            {
                throw new ArgumentNullException(nameof(androidSpeech));
            }

            Input = new SpeechInputService(config, androidSpeech, new MicrophonePermissionService());
            Output = new SpeechOutputService(config, androidSpeech);
        }

        public SpeechInputService Input { get; }
        public SpeechOutputService Output { get; }
        public bool CanRecognize => Input.CanRecognize;
        public bool CanSpeak => Output.CanSpeak;

        public Task<SpeechRecognitionTurn> ListenForUserTurnAsync(Action<string> reportSubtitle, CancellationToken cancellationToken = default, Action onReady = null)
        {
            return Input.ListenForUserTurnAsync(reportSubtitle, cancellationToken, onReady);
        }

        public Task<string> ListenForUserTextAsync(Action<string> reportSubtitle, CancellationToken cancellationToken = default)
        {
            return Input.ListenForUserTextAsync(reportSubtitle, cancellationToken);
        }

        public Task SpeakWithAndroidTtsAsync(string reply, CancellationToken cancellationToken = default)
        {
            return Output.SpeakAsync(reply, cancellationToken);
        }

        public void CancelAll()
        {
            Input.Cancel();
            Output.Stop();
        }
    }
}
