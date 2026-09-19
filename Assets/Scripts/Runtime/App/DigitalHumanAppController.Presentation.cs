using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed partial class DigitalHumanAppController
    {
        private readonly SentenceMotionPlanner sentenceMotion = new SentenceMotionPlanner();
        private string pendingSentence;
        private int sentenceIndex;
        private bool sentenceStarted;
        public SentencePresentation LastSentencePresentation { get; private set; }
        public event Action<string, SentencePresentation> SentencePlaybackStarted;

        private void ResetSentencePlayback() { EndSentencePresentation(true); sentenceIndex = 0; }
        private void PrepareSentencePresentation(string sentence)
        {
            pendingSentence = sentence;
            sentenceStarted = false;
        }
        private void StartPendingPresentation()
        {
            if (pendingSentence == null || sentenceStarted) return;
            sentenceStarted = true;
            LastSentencePresentation = sentenceMotion.Plan(pendingSentence, sentenceIndex++, Time.unscaledTimeAsDouble, LastDecision);
            SetState(AvatarState.speaking, "说话");
            avatarSpeechDriver.SetEmotion(LastSentencePresentation.Emotion);
            if (LastSentencePresentation.Gesture != AvatarGesture.none)
                avatarSpeechDriver.PlayGesture(LastSentencePresentation.Gesture);
            SentencePlaybackStarted?.Invoke(pendingSentence, LastSentencePresentation);
        }
        private void EndSentencePresentation(bool interrupted = false)
        {
            if (avatarSpeechDriver != null)
            {
                if (interrupted) avatarSpeechDriver.PlayGesture(AvatarGesture.none);
                else if (sentenceStarted) avatarSpeechDriver.FinishGesture();
            }
            sentenceStarted = false;
            pendingSentence = null;
        }
        private async Task PlayPresentedSentenceAsync(string sentence, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareSentencePresentation(sentence);
            avatarSpeechDriver.StartNativeSpeechMouth(androidSpeech);
            var completed = false;
            try { await PlayReplySentenceAsync(sentence, cancellationToken); completed = true; }
            finally
            {
                if (avatarSpeechDriver != null) avatarSpeechDriver.StopNativeSpeechMouth();
                EndSentencePresentation(!completed);
            }
        }
        private async Task PlayToneWithPresentationAsync(string reply, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareSentencePresentation(reply);
            StartPendingPresentation();
            var completed = false;
            try { await avatarSpeechDriver.PlayFallbackToneAsync(reply, cancellationToken); completed = true; }
            finally { EndSentencePresentation(!completed); }
        }
    }
}
