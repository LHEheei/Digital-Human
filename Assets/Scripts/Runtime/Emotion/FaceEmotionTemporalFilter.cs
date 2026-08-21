using System;
using System.Collections.Generic;
using UnityEngine;

namespace SrtpAvatar
{
    /// <summary>
    /// Smooths on-device one-frame model distributions. Ambiguous or short
    /// transients become a conservative neutral cue instead of driving the
    /// avatar immediately.
    /// </summary>
    public sealed class FaceEmotionTemporalFilter
    {
        private const int WindowSize = 5;
        private const int MinimumSamplesForNonNeutral = 3;
        private const float MinimumCandidateScore = 0.45f;
        private const float MinimumCandidateMargin = 0.10f;
        private static readonly string[] Labels = { "joy", "angry", "sorrow", "surprised", "neutral" };
        private readonly Queue<float[]> samples = new Queue<float[]>(WindowSize);

        public UserEmo Observe(UserEmo sample)
        {
            if (sample == null || !sample.Normalize().IsUsable)
            {
                return sample ?? UserEmo.Failure("face", "手机端表情结果为空。");
            }

            samples.Enqueue(ToDistribution(sample));
            while (samples.Count > WindowSize)
            {
                samples.Dequeue();
            }

            var averaged = WeightedAverage(samples);
            var best = 0;
            var second = 1;
            if (averaged[second] > averaged[best])
            {
                (best, second) = (second, best);
            }

            for (var index = 2; index < averaged.Length; index++)
            {
                if (averaged[index] > averaged[best])
                {
                    second = best;
                    best = index;
                }
                else if (averaged[index] > averaged[second])
                {
                    second = index;
                }
            }

            var candidate = Labels[best];
            var margin = averaged[best] - averaged[second];
            var ambiguous = averaged[best] < MinimumCandidateScore || margin < MinimumCandidateMargin;
            var insufficientHistory = candidate != "neutral" && samples.Count < MinimumSamplesForNonNeutral;
            var outputLabel = ambiguous || insufficientHistory ? "neutral" : candidate;
            var outputScore = outputLabel == candidate
                ? Mathf.Min(0.90f, averaged[best])
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
