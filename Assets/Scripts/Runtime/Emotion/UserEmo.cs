using System;
using System.Collections.Generic;
using UnityEngine;

namespace SrtpAvatar
{
    [Serializable]
    public sealed class EmotionScore
    {
        public string label = "neutral";
        public float score;

        public EmotionScore Normalize()
        {
            label = UserEmo.NormalizeLabel(label);
            score = float.IsNaN(score) || float.IsInfinity(score) ? 0f : Mathf.Clamp01(score);
            return this;
        }
    }

    [Serializable]
    public sealed class UserEmo
    {
        public string label = "neutral";
        public float score;
        public string source = "unknown";
        public string error = "";
        public long capturedAtUnixMs;
        // Optional one-frame model distribution. The primary label/score can
        // differ because it has additionally passed calibration and temporal
        // stability rules on the service.
        public EmotionScore[] scores = Array.Empty<EmotionScore>();

        public bool IsUsable => string.IsNullOrWhiteSpace(error) && score > 0f && IsKnownSource(source);

        public UserEmo Normalize()
        {
            label = NormalizeLabel(label);
            score = float.IsNaN(score) || float.IsInfinity(score) ? 0f : Mathf.Clamp01(score);
            source = NormalizeSource(source);
            error = string.IsNullOrWhiteSpace(error) ? "" : error.Trim();
            capturedAtUnixMs = Math.Max(0L, capturedAtUnixMs);
            scores = NormalizeScores(scores);
            return this;
        }

        public static UserEmo Failure(string source, string error)
        {
            return new UserEmo
            {
                label = "neutral",
                score = 0f,
                source = NormalizeSource(source),
                error = error ?? "识别失败。",
                capturedAtUnixMs = EmotionTime.UtcNowUnixMs
            }.Normalize();
        }

        public bool IsFresh(long nowUnixMs, long maxAgeMs)
        {
            return IsUsable && capturedAtUnixMs > 0 && nowUnixMs >= capturedAtUnixMs && nowUnixMs - capturedAtUnixMs <= maxAgeMs;
        }

        public static string NormalizeLabel(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "joy":
                case "angry":
                case "sorrow":
                case "surprised":
                case "neutral":
                    return (value ?? "").Trim().ToLowerInvariant();
                default:
                    return "neutral";
            }
        }

        public static string NormalizeSource(string value)
        {
            return IsKnownSource(value) ? value.Trim().ToLowerInvariant() : "unknown";
        }

        private static bool IsKnownSource(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "face":
                case "voice":
                case "fusion":
                    return true;
                default:
                    return false;
            }
        }

        private static EmotionScore[] NormalizeScores(EmotionScore[] values)
        {
            if (values == null || values.Length == 0)
            {
                return Array.Empty<EmotionScore>();
            }

            var bestByLabel = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (value == null || !IsKnownLabel(value.label))
                {
                    continue;
                }

                var normalized = value.Normalize();
                if (!bestByLabel.TryGetValue(normalized.label, out var existing) || normalized.score > existing)
                {
                    bestByLabel[normalized.label] = normalized.score;
                }
            }

            var result = new List<EmotionScore>(bestByLabel.Count);
            foreach (var pair in bestByLabel)
            {
                result.Add(new EmotionScore { label = pair.Key, score = pair.Value });
            }

            result.Sort((left, right) => string.CompareOrdinal(left.label, right.label));
            return result.ToArray();
        }

        private static bool IsKnownLabel(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "joy":
                case "angry":
                case "sorrow":
                case "surprised":
                case "neutral":
                    return true;
                default:
                    return false;
            }
        }
    }

    public static class EmotionTime
    {
        public static long UtcNowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
