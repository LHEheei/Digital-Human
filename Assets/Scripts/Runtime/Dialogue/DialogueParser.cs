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

            // A reply must be supplied by the model; metadata alone is not a response.
            var parsed = new DialogueDecision();
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
