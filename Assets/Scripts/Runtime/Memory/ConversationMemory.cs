using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SrtpAvatar
{
    public enum MemoryClearScope { None, Recent, All }

    public sealed class ConversationMemory
    {
        private static readonly Regex ClearCommand = new Regex(
            @"^(?:请)?(?:帮我)?(?<command>清除记忆|清空记忆|清掉记忆|删除记忆|清除所有记忆|清空所有记忆|删除所有记忆|把记忆清空|忘记我|忘记刚才|忘掉刚才|重新开始对话)(?:吧)?[。！!]*$",
            RegexOptions.CultureInvariant);

        private readonly List<Turn> turns = new List<Turn>();

        public int Count => turns.Count;

        public void Add(string userText, DialogueDecision decision, int maxTurns)
        {
            if (string.IsNullOrWhiteSpace(userText) || decision == null || string.IsNullOrWhiteSpace(decision.reply))
            {
                return;
            }

            turns.Add(new Turn
            {
                userText = userText.Trim(),
                reply = decision.reply.Trim(),
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

        public static bool IsClearRequest(string userText) => GetClearScope(userText) != MemoryClearScope.None;

        public static MemoryClearScope GetClearScope(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return MemoryClearScope.None;
            var match = ClearCommand.Match(Regex.Replace(userText.Trim(), @"\s+", ""));
            if (!match.Success) return MemoryClearScope.None;
            var command = match.Groups["command"].Value;
            return command == "忘记刚才" || command == "忘掉刚才" || command == "重新开始对话"
                ? MemoryClearScope.Recent : MemoryClearScope.All;
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
