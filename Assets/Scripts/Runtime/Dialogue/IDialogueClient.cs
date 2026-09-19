using System;
using System.Threading;
using System.Threading.Tasks;

namespace SrtpAvatar
{
    public interface IDialogueClient
    {
        bool IsAvailable { get; }
        Task<DialogueDecision> CompleteAsync(TalkInput input, string recentContext, string longTermContext,
            CancellationToken cancellationToken, Action<string> onReplySentence = null);
    }
}
