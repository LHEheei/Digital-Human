using System.Collections.Generic;
using UnityEngine;

namespace SrtpAvatar
{
    // Synthesis can run ahead of playback. Samples are indexed on the audio timeline,
    // never applied at callback arrival time.
    public sealed class TtsMouthTimeline
    {
        private const int MaximumBins = 6000;
        private readonly List<float> levels = new List<float>(256);
        private float secondsPerBin = 0.02f;
        private double playbackOrigin;
        private bool playing;

        public void Reset() { levels.Clear(); playing = false; }
        public void Start(double now) { playbackOrigin = now; playing = true; }
        public void Align(double now, float audioSeconds)
        {
            if (playing && audioSeconds >= 0f) playbackOrigin = now - audioSeconds;
        }
        public void Append(int startIndex, float stepSeconds, float[] samples)
        {
            if (samples == null || startIndex < 0 || startIndex >= MaximumBins || !(stepSeconds > 0f) || float.IsInfinity(stepSeconds)) return;
            secondsPerBin = stepSeconds;
            var end = Mathf.Min(MaximumBins, startIndex + samples.Length);
            while (levels.Count < end) levels.Add(0f);
            for (var i = startIndex; i < end; i++)
                levels[i] = float.IsNaN(samples[i - startIndex]) ? 0f : Mathf.Clamp01(samples[i - startIndex]);
        }
        public bool TrySample(double now, out float level)
        {
            level = 0f;
            if (!playing || levels.Count == 0) return false;
            var position = (float)(now - playbackOrigin) / secondsPerBin;
            var index = Mathf.FloorToInt(position);
            if (index < 0 || index >= levels.Count) return true;
            level = Mathf.Lerp(levels[index], levels[Mathf.Min(index + 1, levels.Count - 1)], position - index);
            return true;
        }
    }
}
