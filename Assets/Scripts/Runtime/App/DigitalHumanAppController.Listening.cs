using System;
using System.Threading;

namespace SrtpAvatar
{
    public enum SpeechListeningPhase { Inactive, Preparing, Ready, Failed }

    public sealed partial class DigitalHumanAppController
    {
        private int consecutiveListeningFailures;
        private bool listeningNeedsManualRetry;
        public SpeechListeningPhase ListeningPhase { get; private set; }
        public string ListeningErrorMessage { get; private set; } = "";
        public bool CanRetryListening => CanSubmitText && config.enableAutoConversation && config.preferAndroidNativeSpeech &&
            CanUseDeepSeek && !textInputFocused && !memoryPanelOpen && ListeningPhase != SpeechListeningPhase.Preparing &&
            !string.IsNullOrEmpty(ListeningErrorMessage);
        private float ListeningRetryDelay => AutoConversationPolicy.RetryDelay(config.autoConversationRetryDelaySeconds, consecutiveListeningFailures);

        public void RetryListening()
        {
            if (!CanRetryListening) return;
            StopAutoListening();
            ResetListeningRecovery();
            ListeningPhase = SpeechListeningPhase.Preparing;
            ScheduleAutoListening();
            NotifyConversationChanged();
        }

        private void ResetListeningRecovery()
        {
            consecutiveListeningFailures = 0;
            listeningNeedsManualRetry = false;
        }

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
            listeningNeedsManualRetry = false;
            NotifyConversationChanged();
        }

        private void ReportListeningError(Exception ex)
        {
            if (isDestroyed || !isApplicationActive) return;
            var quietlyRetry = AutoConversationPolicy.ShouldQuietlyRetry(ex);
            ListeningPhase = quietlyRetry ? SpeechListeningPhase.Inactive : SpeechListeningPhase.Failed;
            if (quietlyRetry) ResetListeningRecovery();
            else
            {
                consecutiveListeningFailures = Math.Min(20, consecutiveListeningFailures + 1);
                listeningNeedsManualRetry = AutoConversationPolicy.RequiresListeningAction(ex);
                ListeningErrorMessage = ex.Message;
                UnityEngine.Debug.LogWarning("Automatic speech recognition failed: " + ex.Message);
                if (listeningNeedsManualRetry) automaticConversation?.Stop();
                SetSubtitle(listeningNeedsManualRetry ? "语音输入已暂停，请处理权限后重试。" : "自动语音识别暂不可用，稍后重试...");
                SetHint(ListeningErrorMessage);
            }
            SetState(AvatarState.idle, "空闲");
        }
    }
}
