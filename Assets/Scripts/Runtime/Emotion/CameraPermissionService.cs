using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class CameraPermissionService
    {
        public async Task<PermState> RequestAsync(CancellationToken cancellationToken)
        {
            if (Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                return PermState.granted;
            }

            var operation = Application.RequestUserAuthorization(UserAuthorization.WebCam);
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            return Application.HasUserAuthorization(UserAuthorization.WebCam) ? PermState.granted : PermState.denied;
        }
    }
}
