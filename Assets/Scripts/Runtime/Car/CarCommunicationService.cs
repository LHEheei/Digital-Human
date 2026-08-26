using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public sealed class CarCommunicationService
    {
        private readonly AppConfig config;
        private readonly AndroidBluetoothCarBridge bluetoothBridge;

        public CarCommunicationService(AppConfig config, AndroidBluetoothCarBridge bluetoothBridge)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.bluetoothBridge = bluetoothBridge;
        }

        public async Task<CarCommunicationResult> SendAsync(DialogueDecision decision, CancellationToken cancellationToken = default)
        {
            if (!CarCommand.TryFromDecision(decision, config, out var command))
            {
                if (!CarCommand.IsNone(decision))
                {
                    throw new AppException(AppErr.car, "小车指令不在本地白名单中，已拒绝发送。");
                }

                return CarCommunicationResult.Hidden();
            }

            if (!config.enableCarCommunication || config.carCommunicationMode == CarCommunicationMode.Disabled)
            {
                return CarCommunicationResult.Visible($"小车指令已识别：{command.Intent}，通讯未启用。");
            }

            switch (config.carCommunicationMode)
            {
                case CarCommunicationMode.LogOnly:
                    Debug.Log($"Car command validated locally: {command.Intent}");
                    return CarCommunicationResult.Visible($"小车指令：{command.Intent}，日志模拟发送。");
                case CarCommunicationMode.HttpPost:
                    await SendHttpCommandAsync(command, cancellationToken);
                    return CarCommunicationResult.Visible($"小车指令：{command.Intent}，HTTP 已发送。");
                case CarCommunicationMode.AndroidBluetoothSpp:
                    await SendBluetoothCommandAsync(command, cancellationToken);
                    return CarCommunicationResult.Visible($"小车指令：{command.Intent}，蓝牙已发送。");
                default:
                    return CarCommunicationResult.Visible($"小车指令已识别：{command.Intent}，通讯模式未支持。");
            }
        }

        private async Task SendHttpCommandAsync(CarCommand command, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(config.carHttpEndpoint, UriKind.Absolute, out var endpoint)
                || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException(AppErr.car, "小车 HTTP 地址必须配置为 HTTPS，明文 HTTP 已禁用。");
            }

            var body = JsonUtility.ToJson(new CarHttpCommand
            {
                intent = command.Intent,
                command = command.Payload,
                timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            using var request = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            await UnityWebRequestUtil.SendAsync(request, ResolveTimeoutSeconds(), AppErr.car, false, cancellationToken);
        }

        private async Task SendBluetoothCommandAsync(CarCommand command, CancellationToken cancellationToken)
        {
            if (bluetoothBridge == null)
            {
                throw new AppException(AppErr.car, "Android 蓝牙通讯桥未初始化。");
            }

            var payload = command.Payload + DecodeSuffix(config.carBluetoothCommandSuffix);
            await bluetoothBridge.SendCommandAsync(
                config.carBluetoothDeviceName,
                config.carBluetoothDeviceAddress,
                payload,
                ResolveTimeoutSeconds(),
                cancellationToken);
        }

        private int ResolveTimeoutSeconds()
        {
            return Mathf.Clamp(config.carCommunicationTimeoutSeconds, 2, 30);
        }

        public void CancelPendingCommand()
        {
            bluetoothBridge?.CancelPendingCommand();
        }

        public CarExecutionOrder GetExecutionOrder(DialogueDecision decision)
        {
            if (CarCommand.IsNone(decision))
            {
                return CarExecutionOrder.none;
            }

            if (!CarCommand.TryFromDecision(decision, config, out var command))
            {
                // Keep an unknown candidate in the flow so SendAsync can reject it visibly.
                return CarExecutionOrder.parallel;
            }

            return string.Equals(command.Intent, CarIntent.stop.ToString(), StringComparison.Ordinal)
                ? CarExecutionOrder.immediate
                : CarExecutionOrder.parallel;
        }

        private static string DecodeSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return "";
            }

            return suffix.Replace("\\r", "\r").Replace("\\n", "\n");
        }

        [Serializable]
        private sealed class CarHttpCommand
        {
            public string intent;
            public string command;
            public long timestampMs;
        }
    }

    public enum CarExecutionOrder
    {
        none,
        immediate,
        parallel
    }

    public sealed class CarCommunicationResult
    {
        private CarCommunicationResult(bool shouldDisplay, string message)
        {
            ShouldDisplay = shouldDisplay;
            Message = message;
        }

        public bool ShouldDisplay { get; }
        public string Message { get; }

        public static CarCommunicationResult Hidden()
        {
            return new CarCommunicationResult(false, "");
        }

        public static CarCommunicationResult Visible(string message)
        {
            return new CarCommunicationResult(true, string.IsNullOrWhiteSpace(message) ? "小车通讯已处理。" : message);
        }
    }

    public sealed class CarCommand
    {
        private CarCommand(string intent, string payload)
        {
            Intent = intent;
            Payload = payload;
        }

        public string Intent { get; }
        public string Payload { get; }
        public bool ShouldSend => Intent != CarIntent.none.ToString() && IsSafeProtocolToken(Payload);

        public static bool TryFromDecision(DialogueDecision decision, AppConfig config, out CarCommand command)
        {
            var intent = NormalizeIntent(decision == null ? "" : decision.carIntent);
            command = new CarCommand(intent, ResolvePayload(intent, config));
            return command.ShouldSend;
        }

        public static bool IsNone(DialogueDecision decision)
        {
            return string.IsNullOrWhiteSpace(decision == null ? "" : decision.carIntent)
                || string.Equals(decision.carIntent.Trim(), CarIntent.none.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeIntent(string rawIntent)
        {
            if (string.IsNullOrWhiteSpace(rawIntent))
            {
                return CarIntent.none.ToString();
            }

            var value = rawIntent.Trim().ToLowerInvariant()
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "");

            switch (value)
            {
                case "forward":
                case "goforward":
                case "moveforward":
                case "front":
                case "前进":
                case "向前":
                    return CarIntent.forward.ToString();
                case "back":
                case "backward":
                case "reverse":
                case "movebackward":
                case "后退":
                case "倒车":
                    return CarIntent.backward.ToString();
                case "left":
                case "turnleft":
                case "左转":
                case "向左":
                    return CarIntent.left.ToString();
                case "right":
                case "turnright":
                case "右转":
                case "向右":
                    return CarIntent.right.ToString();
                case "stop":
                case "brake":
                case "halt":
                case "停止":
                case "停车":
                case "刹车":
                    return CarIntent.stop.ToString();
                case "speedup":
                case "faster":
                case "加速":
                    return CarIntent.speedUp.ToString();
                case "slowdown":
                case "slower":
                case "减速":
                    return CarIntent.slowDown.ToString();
                default:
                    return CarIntent.none.ToString();
            }
        }

        private static string ResolvePayload(string intent, AppConfig config)
        {
            if (config == null)
            {
                return "";
            }

            switch (intent)
            {
                case "forward":
                    return config.carCommandForward;
                case "backward":
                    return config.carCommandBackward;
                case "left":
                    return config.carCommandLeft;
                case "right":
                    return config.carCommandRight;
                case "stop":
                    return config.carCommandStop;
                case "speedUp":
                    return config.carCommandSpeedUp;
                case "slowDown":
                    return config.carCommandSlowDown;
                default:
                    return "";
            }
        }

        private static bool IsSafeProtocolToken(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 32)
            {
                return false;
            }

            for (var i = 0; i < payload.Length; i++)
            {
                if (char.IsControl(payload[i]) || char.IsWhiteSpace(payload[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
