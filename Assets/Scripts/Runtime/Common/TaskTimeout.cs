using System;
using System.Threading;
using System.Threading.Tasks;

namespace SrtpAvatar
{
    public static class TaskTimeout
    {
        public static async Task<T> WithTimeout<T>(Task<T> task, float seconds, string timeoutMessage, Action onTimeout = null, CancellationToken cancellationToken = default)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            using (var timerCancellation = new CancellationTokenSource())
            using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timerCancellation.Token))
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(Math.Max(0.1f, seconds)), linkedCancellation.Token);
                var completed = await Task.WhenAny(task, timeout);
                if (completed == task)
                {
                    timerCancellation.Cancel();
                    return await task;
                }

                cancellationToken.ThrowIfCancellationRequested();
                onTimeout?.Invoke();
                throw new TimeoutException(timeoutMessage);
            }
        }

        public static async Task WithTimeout(Task task, float seconds, string timeoutMessage, Action onTimeout = null, CancellationToken cancellationToken = default)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            using (var timerCancellation = new CancellationTokenSource())
            using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timerCancellation.Token))
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(Math.Max(0.1f, seconds)), linkedCancellation.Token);
                var completed = await Task.WhenAny(task, timeout);
                if (completed == task)
                {
                    timerCancellation.Cancel();
                    await task;
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                onTimeout?.Invoke();
                throw new TimeoutException(timeoutMessage);
            }
        }
    }
}
