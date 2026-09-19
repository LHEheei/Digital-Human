using System;

namespace SrtpAvatar
{
    public static class EmotionSamplingPolicy
    {
        public const long FaceFreshnessMs = 3000;
        public const long VoiceFreshnessMs = 6000;
        public const long FusionFreshnessMs = VoiceFreshnessMs;
        public const int ConsecutiveFaceMissesBeforeUnavailable = 3;

        public static float FaceInterval(float configuredSeconds, float averageFrameSeconds)
        {
            var interval = Math.Max(0.1f, configuredSeconds);
            // Reduce capture pressure while sustained frame time is above a 24 FPS budget.
            return averageFrameSeconds > 1f / 24f ? interval * 2f : interval;
        }

        public static bool IsDue(float nowSeconds, float lastSampleSeconds, float intervalSeconds)
        {
            return nowSeconds - lastSampleSeconds >= Math.Max(0.1f, intervalSeconds);
        }

        public static bool HasFreshInput(UserEmo emotion, long nowUnixMs, long maxAgeMs)
        {
            return emotion != null && emotion.IsFresh(nowUnixMs, maxAgeMs);
        }

        public static UserEmo FreshOrNull(UserEmo emotion, long nowUnixMs, long maxAgeMs)
        {
            return HasFreshInput(emotion, nowUnixMs, maxAgeMs) ? emotion : null;
        }

        public static bool IsTransientFaceLoss(UserEmo lastFaceEmotion, int consecutiveMisses, long nowUnixMs)
        {
            return consecutiveMisses > 0
                && consecutiveMisses < ConsecutiveFaceMissesBeforeUnavailable
                && HasFreshInput(lastFaceEmotion, nowUnixMs, FaceFreshnessMs);
        }
    }
}
