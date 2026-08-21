using System;

namespace SrtpAvatar
{
    public static class EmotionDisclosureGuard
    {
        private static readonly string[] DisclosureMarkers =
        {
            "感觉你", "看起来你", "听起来你", "你似乎", "你显得", "我看出你", "我听出你", "识别出你", "心情不错"
        };

        public static string Sanitize(string reply)
        {
            var candidate = string.IsNullOrWhiteSpace(reply) ? "" : reply.Trim();
            foreach (var marker in DisclosureMarkers)
            {
                if (candidate.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return "你好呀，有什么想聊或需要一起处理的吗？";
                }
            }

            return candidate;
        }
    }
}
