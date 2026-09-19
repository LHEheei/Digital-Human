using System;
using System.Threading;
using System.Threading.Tasks;

namespace SrtpAvatar
{
    // Owns one ASR -> reply turn at a time, including cancellation cleanup before restart.
    public sealed class AutomaticConversationLoop : IDisposable
    {
        private readonly Func<bool> canRun;
        private readonly Func<CancellationToken, Task> runTurn;
        private readonly Func<float> retrySeconds;
        private readonly Action<Exception> reportError;
        private CancellationTokenSource cancellation;
        private bool requested;
        private bool disposed;
        public Task Completion { get; private set; } = Task.CompletedTask;

        public AutomaticConversationLoop(Func<bool> canRun, Func<CancellationToken, Task> runTurn,
            Func<float> retrySeconds, Action<Exception> reportError)
        {
            this.canRun = canRun;
            this.runTurn = runTurn;
            this.retrySeconds = retrySeconds;
            this.reportError = reportError;
        }

        public void Refresh()
        {
            requested = !disposed && canRun();
            if (!requested) { cancellation?.Cancel(); return; }
            if (cancellation != null) return;
            var current = new CancellationTokenSource();
            cancellation = current;
            Completion = RunAsync(current);
        }

        public void Stop()
        {
            requested = false;
            cancellation?.Cancel();
        }

        private async Task RunAsync(CancellationTokenSource current)
        {
            await Task.Yield();
            try
            {
                while (requested && !disposed && canRun())
                {
                    current.Token.ThrowIfCancellationRequested();
                    try { await runTurn(current.Token); }
                    catch (OperationCanceledException) when (current.IsCancellationRequested) { break; }
                    catch (Exception ex) { reportError(ex); }
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.01f, retrySeconds())), current.Token);
                }
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested) { }
            finally
            {
                cancellation = null;
                current.Dispose();
                // A resume may arrive while the previous turn is still releasing ASR/TTS.
                if (requested && !disposed) Refresh();
            }
        }

        public void Dispose()
        {
            disposed = true;
            Stop();
        }
    }
}
