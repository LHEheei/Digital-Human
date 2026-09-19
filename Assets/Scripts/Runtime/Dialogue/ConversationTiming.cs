using System.Diagnostics;
using System.Globalization;

namespace SrtpAvatar
{
    public sealed class ConversationTiming
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private double emotionReady = -1, firstSentence = -1, modelReady = -1, firstAudio = -1;
        public void MarkEmotionReady() => emotionReady = watch.Elapsed.TotalMilliseconds;
        public void MarkFirstSentence() { if (firstSentence < 0) firstSentence = watch.Elapsed.TotalMilliseconds; }
        public void MarkModelReady() => modelReady = watch.Elapsed.TotalMilliseconds;
        public void MarkFirstAudio() { if (firstAudio < 0) firstAudio = watch.Elapsed.TotalMilliseconds; }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "Conversation timing after ASR (ms): emotion={0:F0}, firstSentence={1:F0}, modelComplete={2:F0}, firstAudio={3:F0}, total={4:F0}",
            emotionReady, firstSentence, modelReady, firstAudio, watch.Elapsed.TotalMilliseconds);
    }
}
