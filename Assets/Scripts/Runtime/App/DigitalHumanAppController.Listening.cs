using System;
using System.Threading;

namespace SrtpAvatar
{
    public enum SpeechListeningPhase { Inactive, Preparing, Ready, Failed }

    public sealed partial class DigitalHumanAppController
    {
        public SpeechListeningPhase ListeningPhase { get; private set; }
        public string ListeningErrorMessage { get; private set; } = "";

        private void BeginListeningPresentation()
        {
            ListeningPhase = SpeechListeningPhase.Preparing;
            SetState(AvatarState.listening, "倾听");
            SetSubtitle("正在准备语音服务...");
            SetHint("显示“正在聆听”后即可直接说话。");
        }

        private void OnRecognitionReady(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || ListeningPhase != SpeechListeningPhase.Preparing ||
                isDestroyed || !isApplicationActive || !isActiveAndEnabled) return;
            ListeningPhase = SpeechListeningPhase.Ready;
            ListeningErrorMessage = "";
            NotifyConversationChanged();
        }

        private void ReportListeningError(Exception ex)
        {
            if (isDestroyed || !isApplicationActive) return;
            var quietlyRetry = AutoConversationPolicy.ShouldQuietlyRetry(ex);
            ListeningPhase = quietlyRetry ? SpeechListeningPhase.Inactive : SpeechListeningPhase.Failed;
            if (!quietlyRetry)
            {
                ListeningErrorMessage = ex.Message;
                UnityEngine.Debug.LogWarning("Automatic speech recognition failed: " + ex.Message);
                SetSubtitle("自动语音识别暂不可用，正在重试...");
                SetHint(ListeningErrorMessage);
            }
            SetState(AvatarState.idle, "空闲");
        }
    }
}
