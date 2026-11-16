using System;
using System.Collections.Generic;

namespace SrtpAvatar
{
    public enum ConversationTurnStatus { Pending, Completed, Failed, Cancelled }
    public sealed class ConversationTurn
    {
        public int Id { get; }
        public string UserText { get; }
        public string Reply { get; internal set; } = "";
        public ConversationTurnStatus Status { get; internal set; }
        public string ErrorMessage { get; internal set; } = "";
        public bool Committed { get; internal set; }
        public int Revision { get; internal set; }
        internal ConversationTurn(int id, string userText) { Id = id; UserText = userText; }
    }

    // Visible conversation history is distinct from the short prompt context window.
    public sealed class ConversationHistory
    {
        private readonly List<ConversationTurn> turns = new List<ConversationTurn>();
        private readonly IReadOnlyList<ConversationTurn> view;
        private readonly int capacity;
        private int nextId;
        public ConversationHistory(int capacity = 40)
        { this.capacity = Math.Max(1, capacity); view = turns.AsReadOnly(); }
        public int Revision { get; private set; }
        public IReadOnlyList<ConversationTurn> Turns => view;
        public ConversationTurn Latest => turns.Count == 0 ? null : turns[turns.Count - 1];
        public bool CanRetry => Latest != null && !Latest.Committed &&
            (Latest.Status == ConversationTurnStatus.Failed || Latest.Status == ConversationTurnStatus.Cancelled);
        public ConversationTurn Begin(string text)
        {
            var turn = new ConversationTurn(++nextId, text.Trim());
            turns.Add(turn);
            Revision++;
            if (turns.Count > capacity) turns.RemoveAt(0);
            return turn;
        }
        public void UpdateReply(ConversationTurn turn, string reply)
        {
            if (turn.Reply == reply) return;
            turn.Reply = reply;
            turn.Revision++;
            Revision++;
        }
        public void Finish(ConversationTurn turn, AppErr error, string message, bool committed)
        {
            var status = error == AppErr.none ? ConversationTurnStatus.Completed
                : error == AppErr.canceled ? ConversationTurnStatus.Cancelled : ConversationTurnStatus.Failed;
            message = message ?? "";
            if (turn.Status == status && turn.ErrorMessage == message && turn.Committed == committed) return;
            turn.Status = status;
            turn.ErrorMessage = message;
            turn.Committed = committed;
            turn.Revision++;
            Revision++;
        }

        internal List<SavedTurn> Capture()
        {
            var saved = new List<SavedTurn>(turns.Count);
            foreach (var turn in turns)
            {
                if (turn.Status == ConversationTurnStatus.Pending) continue;
                saved.Add(new SavedTurn { userText = turn.UserText, reply = turn.Reply, status = turn.Status,
                    errorMessage = turn.ErrorMessage, committed = turn.Committed });
            }
            return saved;
        }

        internal static ConversationHistory Restore(List<SavedTurn> saved)
        {
            var history = new ConversationHistory();
            for (var i = Math.Max(0, saved.Count - history.capacity); i < saved.Count; i++)
            {
                var entry = saved[i];
                history.turns.Add(new ConversationTurn(++history.nextId, entry.userText)
                {
                    Reply = entry.reply ?? "", Status = entry.status,
                    ErrorMessage = entry.errorMessage ?? "", Committed = entry.committed
                });
            }
            return history;
        }

        [Serializable]
        internal sealed class SavedTurn
        {
            public string userText, reply, errorMessage;
            public ConversationTurnStatus status;
            public bool committed;
        }
    }
}
