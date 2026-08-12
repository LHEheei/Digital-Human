using System;
using System.Collections.Generic;
using System.Text;

namespace SrtpAvatar
{
    public sealed class ConversationMemory
    {
        private static readonly string[] ClearRequestKeywords =
        {
            "清除记忆",
            "清空记忆",
            "清掉记忆",
            "删除记忆",
            "忘记我",
            "忘记刚才",
            "忘掉刚才",
            "重新开始对话"
        };

        private readonly List<Turn> turns = new List<Turn>();

        public int Count => turns.Count;

        public void Add(string userText, DialogueDecision decision, int maxTurns)
        {
            if (string.IsNullOrWhiteSpace(userText) || decision == null)
            {
                return;
            }

            turns.Add(new Turn
            {
                userText = userText.Trim(),
                reply = string.IsNullOrWhiteSpace(decision.reply) ? "我理解了。" : decision.reply.Trim(),
                emotion = decision.emotion,
                gesture = decision.gesture
            });

            Trim(maxTurns);
        }

        public void Clear()
        {
            turns.Clear();
        }

        public string BuildContext(int maxTurns)
        {
            if (turns.Count == 0)
            {
                return "";
            }

            var builder = new StringBuilder();
            builder.AppendLine("以下是本轮对话的最近历史，用于理解上下文，不要逐字复述：");
            var start = Math.Max(0, turns.Count - Math.Max(1, maxTurns));
            for (var i = start; i < turns.Count; i++)
            {
                var turn = turns[i];
                builder.Append(i - start + 1).Append(". 用户：").AppendLine(turn.userText);
                builder.Append("   数字人：").AppendLine(turn.reply);
            }

            return builder.ToString().Trim();
        }

        public static bool IsClearRequest(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
            {
                return false;
            }

            var text = userText.Trim();
            for (var i = 0; i < ClearRequestKeywords.Length; i++)
            {
                if (text.Contains(ClearRequestKeywords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void Trim(int maxTurns)
        {
            var safeMax = Math.Max(1, maxTurns);
            while (turns.Count > safeMax)
            {
                turns.RemoveAt(0);
            }
        }

        private sealed class Turn
        {
            public string userText;
            public string reply;
            public string emotion;
            public string gesture;
        }
    }
}
