using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SrtpAvatar
{
    public sealed class EmotionEvaluationRecord
    {
        public int sampleNumber;
        public string intendedLabel;
        public string rawLabel;
        public float rawScore;
        public float joyScore;
        public float angryScore;
        public float sorrowScore;
        public float surprisedScore;
        public float neutralScore;
        public string finalLabel;
        public float finalScore;
        public long capturedAtUnixMs;
    }

    public sealed class EmotionEvaluationSession
    {
        public const int DefaultTargetSamplesPerLabel = 10;
        public static readonly string[] Labels = { "joy", "angry", "sorrow", "surprised", "neutral" };

        private readonly List<EmotionEvaluationRecord> records = new List<EmotionEvaluationRecord>();
        private readonly int targetSamplesPerLabel;

        public EmotionEvaluationSession(int targetSamplesPerLabel = DefaultTargetSamplesPerLabel)
        {
            this.targetSamplesPerLabel = Math.Max(1, targetSamplesPerLabel);
        }

        public IReadOnlyList<EmotionEvaluationRecord> Records => records;
        public string ActiveLabel { get; private set; } = "";
        public bool IsCollecting => !string.IsNullOrEmpty(ActiveLabel);
        public int TargetSamplesPerLabel => targetSamplesPerLabel;

        public bool Start(string intendedLabel)
        {
            var normalized = UserEmo.NormalizeLabel(intendedLabel);
            if (!IsSupportedLabel(intendedLabel) || Count(normalized) >= targetSamplesPerLabel)
            {
                ActiveLabel = "";
                return false;
            }

            ActiveLabel = normalized;
            return true;
        }

        public void Stop()
        {
            ActiveLabel = "";
        }

        public void Clear()
        {
            Stop();
            records.Clear();
        }

        public bool Observe(UserEmo raw, UserEmo final)
        {
            if (!IsCollecting || raw == null || final == null || !raw.IsUsable || !final.IsUsable)
            {
                return false;
            }

            var intended = ActiveLabel;
            if (Count(intended) >= targetSamplesPerLabel)
            {
                Stop();
                return false;
            }

            records.Add(new EmotionEvaluationRecord
            {
                sampleNumber = Count(intended) + 1,
                intendedLabel = intended,
                rawLabel = UserEmo.NormalizeLabel(raw.label),
                rawScore = raw.score,
                joyScore = Score(raw, "joy"),
                angryScore = Score(raw, "angry"),
                sorrowScore = Score(raw, "sorrow"),
                surprisedScore = Score(raw, "surprised"),
                neutralScore = Score(raw, "neutral"),
                finalLabel = UserEmo.NormalizeLabel(final.label),
                finalScore = final.score,
                capturedAtUnixMs = raw.capturedAtUnixMs
            });

            if (Count(intended) >= targetSamplesPerLabel)
            {
                Stop();
            }
            return true;
        }

        public int Count(string intendedLabel)
        {
            var normalized = UserEmo.NormalizeLabel(intendedLabel);
            var result = 0;
            foreach (var record in records)
            {
                if (record.intendedLabel == normalized)
                {
                    result++;
                }
            }
            return result;
        }

        public string ToCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("sample_number,intended_label,raw_label,raw_score,joy_score,angry_score,sorrow_score,surprised_score,neutral_score,final_label,final_score,captured_at_unix_ms,captured_at_utc");
            foreach (var record in records)
            {
                Append(builder, record.sampleNumber);
                Append(builder, record.intendedLabel);
                Append(builder, record.rawLabel);
                Append(builder, record.rawScore);
                Append(builder, record.joyScore);
                Append(builder, record.angryScore);
                Append(builder, record.sorrowScore);
                Append(builder, record.surprisedScore);
                Append(builder, record.neutralScore);
                Append(builder, record.finalLabel);
                Append(builder, record.finalScore);
                Append(builder, record.capturedAtUnixMs);
                builder.Append(record.capturedAtUnixMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(record.capturedAtUnixMs).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                    : "");
                builder.AppendLine();
            }
            return builder.ToString();
        }

        private static float Score(UserEmo emotion, string label)
        {
            if (emotion.scores != null)
            {
                foreach (var score in emotion.scores)
                {
                    if (score != null && UserEmo.NormalizeLabel(score.label) == label)
                    {
                        return score.score;
                    }
                }
            }
            return UserEmo.NormalizeLabel(emotion.label) == label ? emotion.score : 0f;
        }

        private static bool IsSupportedLabel(string label)
        {
            var value = (label ?? "").Trim().ToLowerInvariant();
            foreach (var supported in Labels)
            {
                if (value == supported)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Append(StringBuilder builder, object value)
        {
            if (value is float floating)
            {
                builder.Append(floating.ToString("0.000000", CultureInfo.InvariantCulture));
            }
            else if (value is IFormattable formattable)
            {
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(value?.ToString() ?? "");
            }
            builder.Append(',');
        }
    }
}
