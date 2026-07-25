using System.Text.RegularExpressions;

namespace SrtpAvatar
{
    public static class MemoryFilter
    {
        private static readonly string[] SensitiveKeywords =
        {
            "api key",
            "apikey",
            "access key",
            "secret",
            "token",
            "bearer",
            "密码",
            "密钥",
            "令牌",
            "手机号",
            "手机号码",
            "电话号码",
            "邮箱",
            "身份证"
        };

        private static readonly Regex[] SensitivePatterns =
        {
            new Regex(@"sk-[A-Za-z0-9_\-]{12,}", RegexOptions.IgnoreCase),
            new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.IgnoreCase),
            new Regex(@"\b1[3-9]\d{9}\b"),
            new Regex(@"\b[A-Za-z0-9_\-]{32,}\b")
        };

        public static bool ShouldRemember(string memoryText)
        {
            if (string.IsNullOrWhiteSpace(memoryText))
            {
                return false;
            }

            if (memoryText.Length > 256)
            {
                return false;
            }

            var lower = memoryText.ToLowerInvariant();
            for (var i = 0; i < SensitiveKeywords.Length; i++)
            {
                if (lower.Contains(SensitiveKeywords[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < SensitivePatterns.Length; i++)
            {
                if (SensitivePatterns[i].IsMatch(memoryText))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
