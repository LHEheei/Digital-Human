namespace SrtpAvatar
{
    public sealed class MemoryEntry
    {
        public string Id { get; }
        public string Text { get; }
        public string CreatedAt { get; }
        public string UpdatedAt { get; }
        public MemoryEntry(string id, string text, string createdAt, string updatedAt)
        { Id = id; Text = text; CreatedAt = createdAt; UpdatedAt = updatedAt; }
    }
}
