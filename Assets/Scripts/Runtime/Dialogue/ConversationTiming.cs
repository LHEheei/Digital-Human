using System;
using System.Globalization;

namespace SrtpAvatar
{
    public sealed class ConversationTiming
    {
        private readonly Func<long> nowMilliseconds;
        private readonly SpeechRecognitionBackend backend;
        private readonly long inputAtMs;
        private readonly long speechEndToAsrMs;
        private long emotionReady = -1, firstSentence = -1, modelReady = -1, firstAudio = -1;

        public ConversationTiming(SpeechRecognitionTurn turn, Func<long> nowMilliseconds)
        {
            this.nowMilliseconds = nowMilliseconds;
            backend = turn.Backend;
            var now = nowMilliseconds();
            var hasRecognitionTime = turn.RecognitionCompletedAtMs >= 0 && turn.RecognitionCompletedAtMs <= now;
            inputAtMs = hasRecognitionTime ? turn.RecognitionCompletedAtMs : now;
            speechEndToAsrMs = hasRecognitionTime ? Between(turn.SpeechEndedAtMs, inputAtMs) : -1;
        }

        public void MarkEmotionReady() => emotionReady = SinceInput();
        public void MarkFirstSentence() { if (firstSentence < 0) firstSentence = SinceInput(); }
        public void MarkModelReady() => modelReady = SinceInput();
        public void MarkFirstAudio(long? startedAtMs = null)
        {
            if (firstAudio < 0) firstAudio = SinceInput(startedAtMs);
        }

        private long SinceInput(long? atMs = null) => Between(inputAtMs, atMs ?? nowMilliseconds());
        private static long Between(long startMs, long endMs) => startMs >= 0 && endMs >= startMs ? endMs - startMs : -1;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "Conversation timing (ms) asr={0} speechEndToAsr={1} inputToEmotionReady={2} inputToFirstSentence={3} " +
            "inputToModelComplete={4} inputToFirstTts={5} speechEndToFirstTts={6} inputToEnd={7}",
            backend, speechEndToAsrMs, emotionReady, firstSentence, modelReady, firstAudio,
            speechEndToAsrMs >= 0 && firstAudio >= 0 ? speechEndToAsrMs + firstAudio : -1, SinceInput());
    }
}
