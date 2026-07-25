using System.Threading.Tasks;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace SrtpAvatar
{
    public sealed class MicrophonePermissionService
    {
        public Task<bool> EnsureGrantedAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                return Task.FromResult(true);
            }

            var completion = new TaskCompletionSource<bool>();
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => completion.TrySetResult(true);
            callbacks.PermissionDenied += _ => completion.TrySetResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ => completion.TrySetResult(false);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            return completion.Task;
#else
            return Task.FromResult(true);
#endif
        }
    }
}
