using System;

namespace SrtpAvatar
{
    /// <summary>
    /// Keeps automatic speech turn-taking rules independent from Android callbacks so they
    /// can be tested without a device. A no-speech result is normal while waiting for a user.
    /// </summary>
    public static class AutoConversationPolicy
    {
        private const float DefaultRetryDelaySeconds = 1.2f;

        public static bool HasUsableText(string text)
        {
            return !string.IsNullOrWhiteSpace(text);
        }

        public static float NormalizeRetryDelay(float seconds)
        {
            return Math.Max(0.25f, seconds <= 0f ? DefaultRetryDelaySeconds : seconds);
        }

        public static bool ShouldQuietlyRetry(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (ShouldQuietlyRetry(current.Message))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ShouldQuietlyRetry(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.IndexOf("没有听到说话", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("没有识别到语音", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("speech timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("no match", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("超时", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
