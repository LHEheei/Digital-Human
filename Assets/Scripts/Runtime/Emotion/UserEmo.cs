using System;
using UnityEngine;

namespace SrtpAvatar
{
    [Serializable]
    public sealed class UserEmo
    {
        public string label = "neutral";
        public float score;
        public string source = "unknown";
        public string error = "";

        public bool IsUsable => string.IsNullOrWhiteSpace(error) && score > 0f && IsKnownSource(source);

        public UserEmo Normalize()
        {
            label = NormalizeLabel(label);
            score = float.IsNaN(score) || float.IsInfinity(score) ? 0f : Mathf.Clamp01(score);
            source = NormalizeSource(source);
            error = string.IsNullOrWhiteSpace(error) ? "" : error.Trim();
            return this;
        }

        public static UserEmo Failure(string source, string error)
        {
            return new UserEmo { label = "neutral", score = 0f, source = NormalizeSource(source), error = error ?? "识别失败。" }.Normalize();
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
    }
}
