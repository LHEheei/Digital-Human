using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public static class UnityWebRequestUtil
    {
        public static async Task SendAsync(UnityWebRequest request, int timeoutSeconds = 25)
        {
            request.timeout = Math.Max(5, timeoutSeconds);
            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"HTTP {request.responseCode}: {request.error}");
            }
        }
    }
}
