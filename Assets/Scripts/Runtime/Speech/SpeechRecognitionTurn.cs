using System;

namespace SrtpAvatar
{
    public enum SpeechRecognitionBackend { None, Android, Vosk }

    public sealed class SpeechRecognitionTurn
    {
        public SpeechRecognitionTurn(string text, byte[] audioWav, SpeechRecognitionBackend backend = SpeechRecognitionBackend.None,
            long speechEndedAtMs = -1, long recognitionCompletedAtMs = -1)
        {
            Text = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
            AudioWav = audioWav ?? Array.Empty<byte>();
            Backend = backend;
            SpeechEndedAtMs = speechEndedAtMs;
            RecognitionCompletedAtMs = recognitionCompletedAtMs;
        }

        public string Text { get; }
        public byte[] AudioWav { get; }
        public bool HasAudio => AudioWav.Length > 0;
        public SpeechRecognitionBackend Backend { get; }
        public long SpeechEndedAtMs { get; }
        public long RecognitionCompletedAtMs { get; }
    }
}
