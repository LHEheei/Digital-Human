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
            this.config = config;
            deepSeekClient = new DeepSeekDialogueClient(config);
            conversationMemory = new ConversationMemory();
            longTermMemory = new LongTermMemoryStore();
        }

        public bool CanUseDeepSeek => config != null && config.HasDeepSeekKey;

        public async Task<DialogueDecision> DecideAsync(string userText)
        {
            if (ConversationMemory.IsClearRequest(userText))
            {
                conversationMemory.Clear();
                longTermMemory.Clear();
                return new DialogueDecision
                {
                    reply = "记忆已经清除，我们可以重新开始。",
                    emotion = "neutral",
                    gesture = "nod",
                    state = "speaking",
                    carIntent = "none"
                };
            }

            var decision = CanUseDeepSeek
                ? await DecideWithDeepSeekAsync(userText)
                : LocalDemoDialogue.Decide(userText);

            Remember(userText, decision);
            return decision;
        }

        private async Task<DialogueDecision> DecideWithDeepSeekAsync(string userText)
        {
            var recentContext = config.enableConversationMemory
                ? conversationMemory.BuildContext(config.maxMemoryTurns)
                : "";
            var longTermContext = config.enableLongTermMemory
                ? longTermMemory.BuildContext(config.maxLongTermMemories)
                : "";

            return await deepSeekClient.CompleteAsync(userText, recentContext, longTermContext);
        }

        private void Remember(string userText, DialogueDecision decision)
        {
            if (decision == null)
            {
                return;
            }

            var safeDecision = decision.Normalize();
            if (config != null && config.enableConversationMemory)
            {
                conversationMemory.Add(userText, safeDecision, config.maxMemoryTurns);
            }

            if (config != null && config.enableLongTermMemory && longTermMemory.Remember(safeDecision.memory, config.maxLongTermMemories))
            {
                Debug.Log($"Long-term memory updated: {longTermMemory.FilePath}");
            }
        }
    }
}
