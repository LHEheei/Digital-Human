using System;
using System.Collections.Generic;

namespace SrtpAvatar
{
    public readonly struct SentencePresentation
    {
        public readonly AvatarEmotion Emotion;
        public readonly AvatarGesture Gesture;
        public SentencePresentation(AvatarEmotion emotion, AvatarGesture gesture) { Emotion = emotion; Gesture = gesture; }
    }

    public sealed class SentenceMotionPlanner
    {
        private readonly Dictionary<AvatarGesture, double> lastGestureAt = new Dictionary<AvatarGesture, double>();
        private double lastAnyGestureAt = double.NegativeInfinity;

        public SentencePresentation Plan(string sentence, int sentenceIndex, double now, DialogueDecision decision = null)
        {
            var text = (sentence ?? "").Trim();
            var emotion = decision?.ParsedEmotion ?? AvatarEmotion.neutral;
            var gesture = AvatarGesture.none;
            if (StartsWithAny(text, "你好", "您好", "早上好", "晚上好", "嗨", "再见"))
            {
                emotion = AvatarEmotion.joy;
                if (sentenceIndex == 0) gesture = AvatarGesture.wave;
            }
            else if (ContainsAny(text, "不容易", "我理解你的", "让你难过", "让你失望", "陪着你", "慢慢来"))
            {
                emotion = AvatarEmotion.neutral;
                gesture = AvatarGesture.nod;
            }
            else if (StartsWithAny(text, "好的", "是的", "明白", "没问题", "谢谢", "我理解"))
                gesture = AvatarGesture.nod;
            else if (ContainsAny(text, "因为", "所以", "例如", "比如", "首先", "其次", "建议", "可以先", "分为"))
                gesture = AvatarGesture.explain;
            else if (sentenceIndex == 0 && decision != null && decision.ParsedGesture != AvatarGesture.think)
                gesture = decision.ParsedGesture;

            if (gesture != AvatarGesture.none)
            {
                var repeatInterval = gesture == AvatarGesture.wave ? 12d : 7d;
                if (now - lastAnyGestureAt < 2.5d ||
                    (lastGestureAt.TryGetValue(gesture, out var previous) && now - previous < repeatInterval))
                    gesture = AvatarGesture.none;
                else { lastGestureAt[gesture] = now; lastAnyGestureAt = now; }
            }
            return new SentencePresentation(emotion, gesture);
        }

        public static IReadOnlyList<string> SplitSentences(string reply)
        {
            var sentences = new List<string>();
            var start = 0;
            for (var i = 0; i < reply.Length; i++)
            {
                if ("。！？!?；;\n".IndexOf(reply[i]) < 0) continue;
                var text = reply.Substring(start, i - start + 1);
                if (!string.IsNullOrWhiteSpace(text)) sentences.Add(text);
                start = i + 1;
            }
            if (start < reply.Length && !string.IsNullOrWhiteSpace(reply.Substring(start))) sentences.Add(reply.Substring(start));
            return sentences;
        }
        private static bool StartsWithAny(string text, params string[] words)
        { foreach (var word in words) if (text.StartsWith(word, StringComparison.Ordinal)) return true; return false; }
        private static bool ContainsAny(string text, params string[] words)
        { foreach (var word in words) if (text.Contains(word)) return true; return false; }
    }
}
