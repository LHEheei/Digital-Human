using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public enum SpeechListeningPhase { Inactive, Preparing, Ready, Failed }
    public enum SpeechInputMode { Automatic, TapToSpeak, TextOnly }

    public sealed partial class DigitalHumanAppController
    {
        private const string InputModePreferenceKey = "speech_input_mode";
        private SpeechInputMode? inputModeOverride;
        private CancellationTokenSource dictationCancellation;
        private bool inputModePickerOpen;
        private Func<Action<string>, CancellationToken, Action, Task<SpeechRecognitionTurn>> recognizeSpeech;
        private int consecutiveListeningFailures;
        private bool listeningNeedsManualRetry;
        public SpeechListeningPhase ListeningPhase { get; private set; }
        public string ListeningErrorMessage { get; private set; } = "";
        public SpeechInputMode InputMode => inputModeOverride ?? (config.enableAutoConversation ? SpeechInputMode.Automatic : SpeechInputMode.TextOnly);
        public bool IsDictating => dictationCancellation != null;
        public bool CanChangeInputMode => isActiveAndEnabled && isApplicationActive && !isDestroyed && !IsBusy;
        public bool CanStartDictation => CanSubmitText && InputMode == SpeechInputMode.TapToSpeak && config.preferAndroidNativeSpeech &&
            !memoryPanelOpen && !sessionsPanelOpen && !inputModePickerOpen;
        public bool CanRetryListening => CanSubmitText && InputMode == SpeechInputMode.Automatic && config.preferAndroidNativeSpeech &&
            CanUseDeepSeek && !textInputFocused && !memoryPanelOpen && !sessionsPanelOpen && !inputModePickerOpen && ListeningPhase != SpeechListeningPhase.Preparing &&
            !string.IsNullOrEmpty(ListeningErrorMessage);
        private float ListeningRetryDelay => AutoConversationPolicy.RetryDelay(config.autoConversationRetryDelaySeconds, consecutiveListeningFailures);

        private void LoadInputMode()
        {
#if !UNITY_EDITOR
            var stored = PlayerPrefs.GetInt(InputModePreferenceKey, -1);
            if (Enum.IsDefined(typeof(SpeechInputMode), stored)) inputModeOverride = (SpeechInputMode)stored;
#endif
        }

        public void SetInputMode(SpeechInputMode mode)
        {
            if (!CanChangeInputMode || !Enum.IsDefined(typeof(SpeechInputMode), mode)) return;
            inputModeOverride = mode;
#if !UNITY_EDITOR
            PlayerPrefs.SetInt(InputModePreferenceKey, (int)mode);
            PlayerPrefs.Save();
#endif
            CancelDictation();
            PauseListeningForInteraction();
            ResetListeningRecovery();
            ListeningErrorMessage = "";
            SynchronizeContinuousCapture();
            ScheduleAutoListening();
            NotifyConversationChanged();
        }

        internal void SetInputModePickerOpen(bool open)
        {
            inputModePickerOpen = open;
            if (open)
            {
                CancelDictation();
                PauseListeningForInteraction();
            }
            else ScheduleAutoListening();
        }

        internal string DescribeInputMode() => InputMode switch
        {
            SpeechInputMode.Automatic => "自动识别",
            SpeechInputMode.TapToSpeak => "点按说话",
            _ => "仅文字"
        };

        private void SynchronizeContinuousCapture() =>
            emotionPerception?.SetContinuousAudioEnabled(InputMode == SpeechInputMode.Automatic && !IsDictating);

        internal async Task<SpeechRecognitionTurn> DictateAsync()
        {
            if (!CanStartDictation) return null;
            using var capture = new CancellationTokenSource();
            dictationCancellation = capture;
            var token = capture.Token;
            ListeningErrorMessage = "";
            StopAutoListening();
            try
            {
                await automaticConversation.Completion;
                token.ThrowIfCancellationRequested();
                var turn = await CaptureSpeechAsync(token);
                token.ThrowIfCancellationRequested();
                return turn;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !isDestroyed && isApplicationActive && isActiveAndEnabled)
                {
                    ListeningPhase = SpeechListeningPhase.Failed;
                    ListeningErrorMessage = ex is AppException ? ex.Message : "语音识别失败，请重试。";
                    Debug.LogWarning("Manual speech recognition failed: " + ex.Message);
                }
                return null;
            }
            finally
            {
                dictationCancellation = null;
                if (ListeningPhase != SpeechListeningPhase.Failed) ListeningPhase = SpeechListeningPhase.Inactive;
                if (!isDestroyed && isApplicationActive && isActiveAndEnabled)
                {
                    if (CurrentState == AvatarState.listening) SetState(AvatarState.idle, "空闲");
                    SynchronizeContinuousCapture();
                    ScheduleAutoListening();
                    NotifyConversationChanged();
                }
            }
        }

        internal void CancelDictation()
        {
            if (dictationCancellation == null || dictationCancellation.IsCancellationRequested) return;
            dictationCancellation.Cancel();
            speechService.Input.Cancel();
        }

        private async Task<SpeechRecognitionTurn> CaptureSpeechAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            BeginListeningPresentation();
            emotionPerception?.SuspendAudioCapture();
            try { return await recognizeSpeech(SetSubtitle, token, () => OnRecognitionReady(token)); }
            finally
            {
                if (isApplicationActive && !isDestroyed) emotionPerception?.ResumeAudioCapture();
            }
        }

        private byte[] GetRecentDictationAudio(string text, SpeechRecognitionTurn dictation)
        {
            if (InputMode != SpeechInputMode.TapToSpeak || dictation == null || !dictation.HasAudio ||
                dictation.RecognitionCompletedAtMs < 0 || !string.Equals(dictation.Text, text.Trim(), StringComparison.Ordinal)) return null;
            var age = androidSpeech.GetTimingTimestampMilliseconds() - dictation.RecognitionCompletedAtMs;
            return age >= 0 && age <= EmotionSamplingPolicy.VoiceFreshnessMs ? dictation.AudioWav : null;
        }

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
