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
        public IReadOnlyList<ConversationTurn> Turns => view;
        public ConversationTurn Latest => turns.Count == 0 ? null : turns[turns.Count - 1];
        public bool CanRetry => Latest != null && !Latest.Committed &&
            (Latest.Status == ConversationTurnStatus.Failed || Latest.Status == ConversationTurnStatus.Cancelled);
        public ConversationTurn Begin(string text)
        {
            var turn = new ConversationTurn(++nextId, text.Trim());
            turns.Add(turn);
            if (turns.Count > capacity) turns.RemoveAt(0);
            return turn;
        }
        public void UpdateReply(ConversationTurn turn, string reply) => turn.Reply = reply;
        public void Finish(ConversationTurn turn, AppErr error, string message, bool committed)
        {
            turn.Status = error == AppErr.none ? ConversationTurnStatus.Completed
                : error == AppErr.canceled ? ConversationTurnStatus.Cancelled : ConversationTurnStatus.Failed;
            turn.ErrorMessage = message ?? "";
            turn.Committed = committed;
        }
    }
}
