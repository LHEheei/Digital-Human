using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed partial class DigitalHumanAppController
    {
        private bool manualSubmissionPending;
        private CancellationTokenSource manualSubmissionCancellation;
        private bool textInputFocused;
        private bool memoryPanelOpen;
        private ConversationTurn currentTurn;
        public ConversationHistory History { get; private set; } = new ConversationHistory();
        public string LastErrorMessage { get; private set; } = "";
        public bool CanSubmitText => isActiveAndEnabled && isApplicationActive && !isDestroyed && !IsBusy;
        public bool CanRetryLastTurn => CanSubmitText && History.CanRetry;
        public event Action ConversationChanged;

        private void InitializeConversationPanel()
        {
            var panel = GetOrAddComponent<ConversationPanel>(null);
            panel.Bind(this, statusText.canvas);
            statusText.gameObject.SetActive(false);
            subtitleText.gameObject.SetActive(false);
            hintText.gameObject.SetActive(false);
        }

        public async Task SubmitTextAsync(string text)
        {
            if (!CanSubmitText) throw new InvalidOperationException("请等待本轮完成或停止回答后再发送。");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请输入对话内容。", nameof(text));
            if (text.Length > 500) throw new ArgumentException("每条消息最多 500 个字符。", nameof(text));
            manualSubmissionPending = true;
            var submission = new CancellationTokenSource();
            manualSubmissionCancellation = submission;
            StopAutoListening();
            NotifyConversationChanged();
            try
            {
                // Release an in-flight ASR session before taking ownership for typed input.
                await automaticConversation.Completion;
                submission.Token.ThrowIfCancellationRequested();
                if (!isActiveAndEnabled || !isApplicationActive || isDestroyed) return;
                await RunConversationFromCapturedTurnAsync(new SpeechRecognitionTurn(text, null), submission.Token);
            }
            catch (OperationCanceledException) when (submission.IsCancellationRequested) { }
            finally
            {
                manualSubmissionCancellation = null;
                submission.Dispose();
                manualSubmissionPending = false;
                ScheduleAutoListening();
                NotifyConversationChanged();
            }
        }

        public Task RetryLastTurnAsync()
        {
            if (!CanRetryLastTurn) throw new InvalidOperationException("当前没有可以重试的未完成回复。");
            return SubmitTextAsync(History.Latest.UserText);
        }

        public void SetTextInputFocused(bool focused)
        {
            textInputFocused = focused;
            if (focused && !IsBusy) PauseListeningForInteraction();
            else if (!focused) ScheduleAutoListening();
        }

        public void SetMemoryPanelOpen(bool open)
        {
            memoryPanelOpen = open;
            if (open && !IsBusy) PauseListeningForInteraction();
            else if (!open) ScheduleAutoListening();
        }

        private void PauseListeningForInteraction()
        {
            StopAutoListening();
            if (CurrentState == AvatarState.listening) SetState(AvatarState.idle, "空闲");
        }

        public IReadOnlyList<MemoryEntry> GetMemoryEntries() => dialogueService.GetMemoryEntries();
        public void UpdateMemoryEntry(string id, string text)
        {
            if (IsBusy) throw new InvalidOperationException("请在本轮回答结束后修改记忆。");
            dialogueService.UpdateMemoryEntry(id, text);
            NotifyConversationChanged();
        }
        public void DeleteMemoryEntry(string id)
        {
            if (IsBusy) throw new InvalidOperationException("请在本轮回答结束后删除记忆。");
            dialogueService.DeleteMemoryEntry(id);
            NotifyConversationChanged();
        }
        private void BeginTurnRecord(string text)
        { currentTurn = History.Begin(text); NotifyConversationChanged(); }
        private void UpdateTurnReply(string reply)
        { if (currentTurn != null) History.UpdateReply(currentTurn, reply); NotifyConversationChanged(); }
        private void FinishTurnRecord()
        {
            if (currentTurn != null) History.Finish(currentTurn, LastConversationError, LastErrorMessage, LastDecision != null);
            currentTurn = null;
            NotifyConversationChanged();
        }
        private void NotifyConversationChanged() => ConversationChanged?.Invoke();
    }
}
