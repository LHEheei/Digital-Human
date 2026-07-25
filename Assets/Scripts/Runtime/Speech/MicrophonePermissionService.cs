using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace SrtpAvatar
{
    public enum PermState
    {
        granted,
        denied,
        blocked,
        timeout
    }

    public sealed class MicrophonePermissionService
    {
        public async Task<PermState> RequestAsync(CancellationToken cancellationToken)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                return PermState.granted;
            }

            var completion = new TaskCompletionSource<PermState>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => completion.TrySetResult(PermState.granted);
            callbacks.PermissionDenied += _ => completion.TrySetResult(PermState.denied);
            callbacks.PermissionDeniedAndDontAskAgain += _ => completion.TrySetResult(PermState.blocked);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            try
            {
                return await TaskTimeout.WithTimeout(completion.Task, 15f, "麦克风权限请求超时。", null, cancellationToken);
            }
            catch (TimeoutException)
            {
                return PermState.timeout;
            }
#else
            return PermState.granted;
#endif
        }
    }
}
