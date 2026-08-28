using System;

namespace SrtpAvatar
{
    public sealed class SpeechRecognitionTurn
    {
        public SpeechRecognitionTurn(string text, byte[] audioWav)
        {
            Text = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
            AudioWav = audioWav ?? Array.Empty<byte>();
        }

        public string Text { get; }
        public byte[] AudioWav { get; }
        public bool HasAudio => AudioWav.Length > 0;
    }
}