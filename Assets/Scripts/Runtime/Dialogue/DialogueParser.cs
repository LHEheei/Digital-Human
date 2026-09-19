using System;
using UnityEngine;

namespace SrtpAvatar
{
    public static class DialogueParser
    {
        public static bool TryFromPossiblyWrappedJson(string raw, out DialogueDecision decision)
        {
            decision = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var json = ExtractJsonObject(raw);
            if (string.IsNullOrWhiteSpace(json) || json[0] != '{')
            {
                return false;
            }

            // Keep a missing reply distinct from DialogueDecision's display default.
            var parsed = new DialogueDecision { reply = null };
            try
            {
                JsonUtility.FromJsonOverwrite(json, parsed);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(parsed.reply))
            {
                return false;
            }

            decision = parsed.Normalize();
            return true;
        }

        private static string ExtractJsonObject(string raw)
        {
            var text = raw.Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text;
        }
    }
}
