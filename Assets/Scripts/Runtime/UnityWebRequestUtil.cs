using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public static class UnityWebRequestUtil
    {
        public static async Task SendAsync(UnityWebRequest request)
        {
            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                var body = request.downloadHandler == null ? "" : request.downloadHandler.text;
                throw new InvalidOperationException($"{request.responseCode} {request.error}\n{body}");
            }
        }
    }
}
