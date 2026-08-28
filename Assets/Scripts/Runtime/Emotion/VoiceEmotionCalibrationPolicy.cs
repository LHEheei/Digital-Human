using System;
using UnityEngine;

namespace SrtpAvatar
{
    /// <summary>
    /// Keeps the bundled speech-emotion model conservative for Chinese speech.
    /// Its eight-class checkpoint was not trained specifically for this app's
    /// Mandarin users, so an isolated "sorrow" score must be corroborated by
    /// the recognized sentence or a fresh face result before it affects UX.
    /// </summary>
    public static class VoiceEmotionCalibrationPolicy
    {
        private static readonly string[] SorrowCues =
        {
            "难过", "伤心", "悲伤", "委屈", "痛苦", "难受", "低落", "郁闷", "不开心", "失望", "想哭", "哭", "好累", "很累", "emo", "丧"
        };

        public static UserEmo Calibrate(UserEmo raw, string transcript, UserEmo face, long nowUnixMs)
        {
            if (raw == null)
            {
                return null;
            }

            raw.Normalize();
            if (!raw.IsUsable || !string.Equals(raw.label, "sorrow", StringComparison.Ordinal))
            {
                return raw;
            }

            if (HasSorrowCue(transcript) || IsFreshSorrow(face, nowUnixMs))
            {
                return raw;
            }

            return new UserEmo
            {
                label = "neutral",
                score = Mathf.Max(0.55f, ScoreFor(raw, "neutral")),
                source = raw.source,
                capturedAtUnixMs = raw.capturedAtUnixMs,
                scores = raw.scores
            }.Normalize();
        }

        private static bool HasSorrowCue(string transcript)
        {
            var text = (transcript ?? "").Trim().ToLowerInvariant();
            foreach (var cue in SorrowCues)
            {
                if (text.Contains(cue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFreshSorrow(UserEmo face, long nowUnixMs)
        {
            return face != null
                && face.IsFresh(nowUnixMs, EmotionSamplingPolicy.FaceFreshnessMs)
                && string.Equals(face.label, "sorrow", StringComparison.Ordinal)
                && face.score >= 0.55f;
        }

        private static float ScoreFor(UserEmo emotion, string label)
        {
            if (emotion.scores == null)
            {
                return 0f;
            }

            foreach (var score in emotion.scores)
            {
                if (score != null && string.Equals(score.label, label, StringComparison.OrdinalIgnoreCase))
                {
                    return score.score;
                }
            }

            return 0f;
        }
    }
}