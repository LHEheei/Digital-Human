using System;
using System.Collections.Generic;
using System.Text;

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
        private static readonly char[] QuoteOpeners = { '"', '“', '‘', '「', '『' };
        private readonly Dictionary<AvatarGesture, double> lastGestureAt = new Dictionary<AvatarGesture, double>();
        private double lastAnyGestureAt = double.NegativeInfinity;
        private char closingQuote;

        public SentencePresentation Plan(string sentence, int sentenceIndex, double now, DialogueDecision decision = null)
        {
            if (sentenceIndex == 0) closingQuote = '\0';
            var text = WithoutQuotedText((sentence ?? "").Trim());
            var emotion = decision?.ParsedEmotion ?? AvatarEmotion.neutral;
            var gesture = AvatarGesture.none;
            if (StartsWithPhrase(text, true, "你好", "您好", "早上好", "晚上好", "嗨", "再见"))
            {
                emotion = AvatarEmotion.joy;
                if (sentenceIndex == 0) gesture = AvatarGesture.wave;
            }
            else if (ContainsAffirmedPhrase(text, "不容易", "让你难过", "让你失望"))
            {
                emotion = AvatarEmotion.concerned;
                gesture = AvatarGesture.nod;
            }
            else if (ContainsAffirmedPhrase(text, "我理解你的", "陪着你", "慢慢来"))
            {
                emotion = AvatarEmotion.neutral;
                gesture = AvatarGesture.nod;
            }
            else if (StartsWithPhrase(text, false, "谢谢", "谢谢你", "谢谢您", "很高兴能帮上忙", "很高兴能帮到你"))
            {
                emotion = AvatarEmotion.softsmile;
                gesture = AvatarGesture.nod;
            }
            else if (StartsWithPhrase(text, false, "好的", "是的", "明白", "明白了", "没问题", "我理解"))
                gesture = AvatarGesture.nod;
            else if ((text.EndsWith("？", StringComparison.Ordinal) || text.EndsWith("?", StringComparison.Ordinal)) &&
                ContainsAffirmedPhrase(text, "你愿意说说", "你想先聊", "你想从哪里", "你更倾向于", "你觉得"))
                gesture = AvatarGesture.headtilt;
            else if (ContainsAffirmedPhrase(text, "可以试试", "不妨试试"))
                gesture = AvatarGesture.offer;
            else if (ContainsAffirmedPhrase(text, "因为", "所以", "例如", "比如", "首先", "其次", "建议", "可以先", "分为"))
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
        private static bool StartsWithPhrase(string text, bool greeting, params string[] phrases)
        {
            foreach (var phrase in phrases)
            {
                if (!text.StartsWith(phrase, StringComparison.Ordinal)) continue;
                var end = phrase.Length;
                if (greeting && end < text.Length && "呀啊哦".IndexOf(text[end]) >= 0) end++;
                if (end == text.Length) return true;
                if (!greeting && "？?".IndexOf(text[end]) >= 0) continue;
                if (char.IsWhiteSpace(text[end]) || "，,。.!！?？；;：:".IndexOf(text[end]) >= 0) return true;
            }
            return false;
        }
        private static bool ContainsAffirmedPhrase(string text, params string[] phrases)
        {
            foreach (var phrase in phrases)
            {
                for (var from = 0; from < text.Length;)
                {
                    var index = text.IndexOf(phrase, from, StringComparison.Ordinal);
                    if (index < 0) break;
                    var start = index;
                    while (start > 0 && index - start < 6 && "，,。.!！?？；;：:".IndexOf(text[start - 1]) < 0) start--;
                    var prefix = text.Substring(start, index - start);
                    if (!HasNegation(prefix)) return true;
                    from = index + phrase.Length;
                }
            }
            return false;
        }
        private static bool HasNegation(string prefix)
        {
            foreach (var word in new[] { "不是", "并非", "不要", "不用", "不必", "无需", "不需要", "不能", "别" })
                if (prefix.Contains(word)) return true;
            return false;
        }
        private string WithoutQuotedText(string text)
        {
            if (closingQuote == '\0' && text.IndexOfAny(QuoteOpeners) < 0) return text;
            var result = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (closingQuote != '\0')
                {
                    if (character == closingQuote) closingQuote = '\0';
                    continue;
                }
                switch (character)
                {
                    case '"': closingQuote = '"'; break;
                    case '“': closingQuote = '”'; break;
                    case '‘': closingQuote = '’'; break;
                    case '「': closingQuote = '」'; break;
                    case '『': closingQuote = '』'; break;
                    default: result.Append(character); continue;
                }
                // Preserve a boundary, so removing a quote cannot join separate words.
                result.Append(' ');
            }
            return result.ToString().Trim();
        }
    }
}
