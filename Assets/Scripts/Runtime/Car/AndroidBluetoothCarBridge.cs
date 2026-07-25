using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class AndroidBluetoothCarBridge : MonoBehaviour
    {
        private const char CallbackSeparator = '\n';
        private TaskCompletionSource<bool> sendCompletion;
        private string commandId;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject bridge;
#endif

        public Task SendCommandAsync(string deviceName, string deviceAddress, string command, float timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return Task.CompletedTask;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureBridge();
            if (bridge == null)
            {
                throw new InvalidOperationException("Android 蓝牙小车桥接初始化失败。");
            }

            CancelPendingCommand();
            commandId = Guid.NewGuid().ToString("N");
            sendCompletion = new TaskCompletionSource<bool>();
            bridge.Call("sendCommand", deviceName ?? "", deviceAddress ?? "", command, commandId);
            return TaskTimeout.WithTimeout(sendCompletion.Task, timeoutSeconds, "小车蓝牙通讯超时。", CancelPendingCommand);
#else
            Debug.Log($"Android Bluetooth car command simulated: {command.Trim()}");
            return Task.CompletedTask;
#endif
        }

        public void CancelPendingCommand()
        {
            var completion = sendCompletion;
            sendCompletion = null;
            commandId = null;
            completion?.TrySetCanceled();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.Call("cancelActiveCommand");
            }
#endif
        }

        public void OnAndroidCarCommandSent(string callback)
        {
            if (!TryReadCurrentCallback(callback, commandId, out _))
            {
                return;
            }

            sendCompletion?.TrySetResult(true);
            sendCompletion = null;
            commandId = null;
        }

        public void OnAndroidCarCommandError(string callback)
        {
            if (!TryReadCurrentCallback(callback, commandId, out var message))
            {
                return;
            }

            sendCompletion?.TrySetException(new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "小车蓝牙通讯失败。" : message));
            sendCompletion = null;
            commandId = null;
        }

        private void OnDestroy()
        {
            CancelPendingCommand();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null)
            {
                bridge.Dispose();
                bridge = null;
            }
#endif
        }

        private static bool TryReadCurrentCallback(string callback, string expectedId, out string payload)
        {
            payload = "";
            if (string.IsNullOrWhiteSpace(callback) || string.IsNullOrWhiteSpace(expectedId))
            {
                return false;
            }

            var separatorIndex = callback.IndexOf(CallbackSeparator);
            if (separatorIndex <= 0 || !string.Equals(callback.Substring(0, separatorIndex), expectedId, StringComparison.Ordinal))
            {
                return false;
            }

            payload = callback.Substring(separatorIndex + 1);
            return true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void EnsureBridge()
        {
            if (bridge != null)
            {
                return;
            }

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            bridge = new AndroidJavaObject("com.srtp.avatar.NativeCarBridge", activity, gameObject.name);
        }
#endif
    }
}
