using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public static class UnityWebRequestUtil
    {
        public static async Task SendAsync(UnityWebRequest request, int timeoutSeconds = 25, AppErr failureCode = AppErr.network, bool authOnUnauthorized = false, CancellationToken cancellationToken = default)
        {
            request.timeout = Math.Max(5, timeoutSeconds);
            var op = request.SendWebRequest();
            using (cancellationToken.Register(request.Abort))
            {
                while (!op.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                var code = authOnUnauthorized && (request.responseCode == 401 || request.responseCode == 403) ? AppErr.auth : failureCode;
                throw new AppException(code, $"HTTP {request.responseCode}: {request.error}");
            }
        }
    }
}
