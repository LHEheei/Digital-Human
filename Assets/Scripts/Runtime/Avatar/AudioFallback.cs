using UnityEngine;

namespace SrtpAvatar
{
    public static class AudioFallback
    {
        public static AudioClip CreatePromptTone(float seconds)
        {
            const int sampleRate = 24000;
            var length = Mathf.CeilToInt(sampleRate * Mathf.Max(0.25f, seconds));
            var samples = new float[length];

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)sampleRate;
                var envelope = Mathf.Clamp01(Mathf.Min(t * 6f, (seconds - t) * 6f));
                var modulation = 0.5f + 0.5f * Mathf.Sin(t * 8f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.18f * envelope * modulation;
            }

            var clip = AudioClip.Create("MockSpeechTone", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
