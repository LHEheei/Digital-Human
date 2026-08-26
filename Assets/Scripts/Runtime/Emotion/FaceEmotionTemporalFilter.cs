using System;
using System.Collections.Generic;
using UnityEngine;

namespace SrtpAvatar
{
    /// <summary>
    /// Smooths on-device one-frame model distributions without hiding a clear
    /// expression behind an artificial warm-up period. Strong current signals
    /// pass immediately; only genuinely ambiguous distributions become neutral.
    /// </summary>
    public sealed class FaceEmotionTemporalFilter
    {
        private const int WindowSize = 3;
        private const float MinimumCandidateScore = 0.40f;
        private const float MinimumCandidateMargin = 0.08f;
        private const float StrongSignalScore = 0.65f;
        private const float StrongSignalMargin = 0.15f;
        private static readonly string[] Labels = { "joy", "angry", "sorrow", "surprised", "neutral" };
        private readonly Queue<float[]> samples = new Queue<float[]>(WindowSize);

        public UserEmo Observe(UserEmo sample)
        {
            if (sample == null || !sample.Normalize().IsUsable)
            {
                return sample ?? UserEmo.Failure("face", "手机端表情结果为空。");
            }

            var current = ToDistribution(sample);
            samples.Enqueue(current);
            while (samples.Count > WindowSize)
            {
                samples.Dequeue();
            }

            var averaged = WeightedAverage(samples);
            FindTopTwo(current, out var currentBest, out var currentSecond);
            var currentIsStrong = current[currentBest] >= StrongSignalScore
                && current[currentBest] - current[currentSecond] >= StrongSignalMargin;
            var decision = currentIsStrong ? current : averaged;
            FindTopTwo(decision, out var best, out var second);

            var candidate = Labels[best];
            var margin = decision[best] - decision[second];
            var ambiguous = decision[best] < MinimumCandidateScore || margin < MinimumCandidateMargin;
            var outputLabel = ambiguous ? "neutral" : candidate;
            var outputScore = outputLabel == candidate
                ? Mathf.Min(0.95f, decision[best])
                : 0.65f;

            var scores = new EmotionScore[Labels.Length];
            for (var index = 0; index < Labels.Length; index++)
            {
                scores[index] = new EmotionScore { label = Labels[index], score = averaged[index] };
            }

            return new UserEmo
            {
                label = outputLabel,
                score = outputScore,
                source = "face",
                capturedAtUnixMs = sample.capturedAtUnixMs,
                scores = scores
            }.Normalize();
        }

        private static void FindTopTwo(float[] values, out int best, out int second)
        {
            best = 0;
            second = 1;
            if (values[second] > values[best])
            {
                (best, second) = (second, best);
            }

            for (var index = 2; index < values.Length; index++)
            {
                if (values[index] > values[best])
                {
                    second = best;
                    best = index;
                }
                else if (values[index] > values[second])
                {
                    second = index;
                }
            }
        }

        private static float[] ToDistribution(UserEmo sample)
        {
            var result = new float[Labels.Length];
            if (sample.scores != null)
            {
                foreach (var item in sample.scores)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var index = Array.IndexOf(Labels, UserEmo.NormalizeLabel(item.label));
                    if (index >= 0)
                    {
                        result[index] = Mathf.Max(result[index], Mathf.Clamp01(item.score));
                    }
                }
            }

            var total = Sum(result);
            if (total <= 0f)
            {
                var labelIndex = Array.IndexOf(Labels, sample.label);
                result[labelIndex >= 0 ? labelIndex : Labels.Length - 1] = Mathf.Clamp01(sample.score);
                total = Sum(result);
            }

            if (total <= 0f)
            {
                result[Labels.Length - 1] = 1f;
                return result;
            }

            for (var index = 0; index < result.Length; index++)
            {
                result[index] /= total;
            }
            return result;
        }

        private static float[] WeightedAverage(IEnumerable<float[]> values)
        {
            var result = new float[Labels.Length];
            var weight = 0f;
            var nextWeight = 1f;
            foreach (var value in values)
            {
                for (var index = 0; index < result.Length; index++)
                {
                    result[index] += value[index] * nextWeight;
                }
                weight += nextWeight;
                nextWeight += 1f;
            }

            for (var index = 0; index < result.Length; index++)
            {
                result[index] /= Mathf.Max(1f, weight);
            }
            return result;
        }

        private static float Sum(float[] values)
        {
            var result = 0f;
            foreach (var value in values)
            {
                result += value;
            }
            return result;
        }
    }
}
