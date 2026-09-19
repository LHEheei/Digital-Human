using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SrtpAvatar
{
    public sealed class ReplySpeechQueue : IDisposable
    {
        private readonly Queue<string> sentences = new Queue<string>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);
        private bool complete;
        public int EnqueuedCount { get; private set; }
        public void Enqueue(string sentence)
        {
            if (complete) throw new InvalidOperationException("Reply speech is already complete.");
            sentences.Enqueue(sentence);
            EnqueuedCount++;
            available.Release();
        }
        public void Complete()
        {
            if (complete) return;
            complete = true;
            available.Release();
        }
        public async Task PlayAsync(Func<string, CancellationToken, Task> speak, CancellationToken cancellationToken)
        {
            while (true)
            {
                await available.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (sentences.Count > 0) await speak(sentences.Dequeue(), cancellationToken);
                else if (complete) return;
            }
        }
        public void Dispose() => available.Dispose();
    }
}