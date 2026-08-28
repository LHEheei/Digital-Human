using System;

namespace SrtpAvatar
{
    public static class EmotionDisclosureGuard
    {
        private static readonly string[] TechnicalDisclosureMarkers =
        {
            "通过摄像头", "从摄像头", "通过镜头", "从镜头", "通过麦克风", "从麦克风",
            "语音分析", "面部表情分析", "系统识别出", "系统检测到", "算法识别出", "模型识别出",
            "我识别出你", "我检测到你"
        };

        public static string Sanitize(string reply)
        {
            var candidate = string.IsNullOrWhiteSpace(reply) ? "" : reply.Trim();
            foreach (var marker in TechnicalDisclosureMarkers)
            {
                if (candidate.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return "我可能理解得不完全准确，但愿意听你多说一点。";
                }
            }

            return candidate;
        }
    }
}
