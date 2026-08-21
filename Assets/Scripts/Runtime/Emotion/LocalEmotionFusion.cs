using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    /// <summary>
    /// Runs the same conservative freshness rules as the server fusion endpoint,
    /// but never leaves the phone. It is used when a source is on-device.
    /// </summary>
    public static class LocalEmotionFusion
    {
        private const float VoiceWeightMultiplier = 0.85f;
        private const float MinimumUsableWeight = 0.5f;

        public static UserEmo Fuse(UserEmo face, UserEmo voice, long nowUnixMs)
        {
            var faceFresh = EmotionSamplingPolicy.FreshOrNull(face, nowUnixMs, EmotionSamplingPolicy.FaceFreshnessMs);
            var voiceFresh = EmotionSamplingPolicy.FreshOrNull(voice, nowUnixMs, EmotionSamplingPolicy.VoiceFreshnessMs);

            var labels = new[] { "joy", "angry", "sorrow", "surprised", "neutral" };
            var weights = new float[labels.Length];
            AddWeight(faceFresh, 1f, labels, weights);
            AddWeight(voiceFresh, VoiceWeightMultiplier, labels, weights);

            var bestIndex = 4;
            var bestWeight = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] > bestWeight)
                {
                    bestWeight = weights[i];
                    bestIndex = i;
                }
            }

            if (bestWeight < MinimumUsableWeight)
            {
                return new UserEmo
                {
                    label = "neutral",
                    score = 0.45f,
                    source = "fusion",
                    capturedAtUnixMs = nowUnixMs
                }.Normalize();
            }

            return new UserEmo
            {
                label = labels[bestIndex],
                score = Mathf.Clamp01(bestWeight),
                source = "fusion",
                capturedAtUnixMs = nowUnixMs
            }.Normalize();
        }

        private static void AddWeight(UserEmo emotion, float multiplier, string[] labels, float[] weights)
        {
            if (emotion == null || !emotion.IsUsable)
            {
                return;
            }

            for (var i = 0; i < labels.Length; i++)
            {
                if (string.Equals(emotion.label, labels[i], StringComparison.Ordinal))
                {
                    weights[i] += emotion.score * multiplier;
                    return;
                }
            }
        }
    }

    public sealed class LocalFusionEmoService : IFusionEmoService
    {
        public Task<UserEmo> GetFusionAsync(UserEmo face, UserEmo voice, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LocalEmotionFusion.Fuse(face, voice, EmotionTime.UtcNowUnixMs));
        }
    }
}
