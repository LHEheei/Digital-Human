using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public static class TaskTimeout
    {
        public static async Task<T> WithTimeout<T>(Task<T> task, float seconds, string timeoutMessage, Action onTimeout = null)
        {
            var timeout = DelaySeconds(seconds);
            var completed = await Task.WhenAny(task, timeout);
            if (completed == task)
            {
                return await task;
            }

            onTimeout?.Invoke();
            throw new TimeoutException(timeoutMessage);
        }

        public static async Task WithTimeout(Task task, float seconds, string timeoutMessage)
        {
            var timeout = DelaySeconds(seconds);
            var completed = await Task.WhenAny(task, timeout);
            if (completed == task)
            {
                await task;
                return;
            }

            throw new TimeoutException(timeoutMessage);
        }

        private static async Task DelaySeconds(float seconds)
        {
            var end = Time.realtimeSinceStartup + Mathf.Max(0.1f, seconds);
            while (Time.realtimeSinceStartup < end)
            {
                await Task.Yield();
            }
        }
    }
}
