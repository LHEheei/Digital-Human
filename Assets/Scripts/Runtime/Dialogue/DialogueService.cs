using System;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class DialogueService
    {
        private readonly AppConfig config;
        private readonly IDialogueClient dialogueClient;
        private readonly ConversationMemory conversationMemory;
        private readonly LongTermMemoryStore longTermMemory;

        public DialogueService(AppConfig config, IDialogueClient client = null, LongTermMemoryStore memoryStore = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            dialogueClient = client ?? new DeepSeekDialogueClient(config);
            conversationMemory = new ConversationMemory();
            longTermMemory = memoryStore ?? new LongTermMemoryStore();
        }

        public bool CanUseDeepSeek => dialogueClient.IsAvailable;
        public int RecentTurnCount => conversationMemory.Count;
        public int LongTermMemoryCount => longTermMemory.Count;
        public string RecentContext => conversationMemory.BuildContext(config.maxMemoryTurns);
        public string LongTermContext => longTermMemory.BuildContext(config.maxLongTermMemories);

        public IReadOnlyList<MemoryEntry> GetMemoryEntries() => longTermMemory.GetEntries();
        public void UpdateMemoryEntry(string id, string text) => longTermMemory.UpdateEntry(id, text);
        public void DeleteMemoryEntry(string id) => longTermMemory.DeleteEntry(id);

        public Task<DialogueDecision> DecideAsync(string userText, CancellationToken cancellationToken = default)
        {
            return DecideAsync(new TalkInput { text = userText }, cancellationToken);
        }

        public async Task<DialogueDecision> DecideAsync(TalkInput input, CancellationToken cancellationToken = default, Action<string> onReplySentence = null)
        {
            input = (input ?? throw new ArgumentNullException(nameof(input))).Normalize();
            var userText = input.text;
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanUseDeepSeek)
            {
                throw new AppException(AppErr.aiUnavailable, "AI 未配置，请点击右上角 API 按钮填写 DeepSeek Key。");
            }

            var clearScope = ConversationMemory.GetClearScope(userText);
            if (clearScope != MemoryClearScope.None)
            {
                if (clearScope == MemoryClearScope.All) longTermMemory.Clear();
                conversationMemory.Clear();
                return new DialogueDecision
                {
                    reply = clearScope == MemoryClearScope.All ? "记忆已经清除，我们可以重新开始。" : "刚才的对话已经清除，长期记忆仍保留。",
                    emotion = "neutral",
                    gesture = "nod",
                    carIntent = "none"
                };
            }

            var spokenReply = new StringBuilder();
            Action<string> emit = onReplySentence == null ? null : sentence =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var safeSentence = input.fusionEmo != null && input.fusionEmo.IsUsable && input.fusionEmo.score >= 0.5f
                    ? EmotionDisclosureGuard.Sanitize(sentence) : sentence;
                spokenReply.Append(safeSentence);
                onReplySentence(safeSentence);
            };
            var decision = await DecideWithDeepSeekAsync(input, cancellationToken, emit);
            if (emit != null && spokenReply.Length > 0) decision.reply = spokenReply.ToString();

            cancellationToken.ThrowIfCancellationRequested();
            decision = EmotionResponsePolicy.Apply(decision, input.fusionEmo);
            Remember(userText, decision);
            return decision;
        }

        private async Task<DialogueDecision> DecideWithDeepSeekAsync(TalkInput input, CancellationToken cancellationToken, Action<string> onReplySentence)
        {
            var recentContext = config.enableConversationMemory
                ? conversationMemory.BuildContext(config.maxMemoryTurns)
                : "";
            var longTermContext = config.enableLongTermMemory
                ? longTermMemory.BuildContext(config.maxLongTermMemories)
                : "";

            return await dialogueClient.CompleteAsync(input, recentContext, longTermContext, cancellationToken, onReplySentence);
        }

        private void Remember(string userText, DialogueDecision decision)
        {
            if (config.enableLongTermMemory && longTermMemory.Remember(decision.memory, config.maxLongTermMemories))
            {
                Debug.Log($"Long-term memory updated: {longTermMemory.FilePath}");
            }

            if (config.enableConversationMemory)
            {
                conversationMemory.Add(userText, decision, config.maxMemoryTurns);
            }
        }
    }
}
