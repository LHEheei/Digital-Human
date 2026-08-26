using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class DialogueService
    {
        private readonly AppConfig config;
        private readonly DeepSeekDialogueClient deepSeekClient;
        private readonly ConversationMemory conversationMemory;
        private readonly LongTermMemoryStore longTermMemory;

        public DialogueService(AppConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            deepSeekClient = new DeepSeekDialogueClient(config);
            conversationMemory = new ConversationMemory();
            longTermMemory = new LongTermMemoryStore();
        }

        public bool CanUseDeepSeek => config.HasDeepSeekKey;

        public Task<DialogueDecision> DecideAsync(string userText, CancellationToken cancellationToken = default)
        {
            return DecideAsync(new TalkInput { text = userText }, cancellationToken);
        }

        public async Task<DialogueDecision> DecideAsync(TalkInput input, CancellationToken cancellationToken = default)
        {
            input = (input ?? throw new ArgumentNullException(nameof(input))).Normalize();
            var userText = input.text;
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanUseDeepSeek)
            {
                throw new AppException(AppErr.aiUnavailable, "AI 未配置，请点击右上角 API 按钮填写 DeepSeek Key。");
            }

            if (ConversationMemory.IsClearRequest(userText))
            {
                conversationMemory.Clear();
                longTermMemory.Clear();
                return new DialogueDecision
                {
                    reply = "记忆已经清除，我们可以重新开始。",
                    emotion = "neutral",
                    gesture = "nod",
                    carIntent = "none"
                };
            }

            var decision = await DecideWithDeepSeekAsync(input, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            decision = EmotionResponsePolicy.Apply(decision, input.fusionEmo);
            Remember(userText, decision);
            return decision;
        }

        private async Task<DialogueDecision> DecideWithDeepSeekAsync(TalkInput input, CancellationToken cancellationToken)
        {
            var recentContext = config.enableConversationMemory
                ? conversationMemory.BuildContext(config.maxMemoryTurns)
                : "";
            var longTermContext = config.enableLongTermMemory
                ? longTermMemory.BuildContext(config.maxLongTermMemories)
                : "";

            return await deepSeekClient.CompleteAsync(input, recentContext, longTermContext, cancellationToken);
        }

        private void Remember(string userText, DialogueDecision decision)
        {
            var safeDecision = decision.Normalize();
            if (config.enableConversationMemory)
            {
                conversationMemory.Add(userText, safeDecision, config.maxMemoryTurns);
            }

            if (config.enableLongTermMemory && longTermMemory.Remember(safeDecision.memory, config.maxLongTermMemories))
            {
                Debug.Log($"Long-term memory updated: {longTermMemory.FilePath}");
            }
        }
    }
}
