using System;
using UnityEngine;

namespace SrtpAvatar
{
    public static class DialogueParser
    {
        public static DialogueDecision FromPossiblyWrappedJson(string raw)
        {
            if (TryFromPossiblyWrappedJson(raw, out var decision))
            {
                return decision;
            }

            return new DialogueDecision { reply = string.IsNullOrWhiteSpace(raw) ? "我理解了。" : raw.Trim() };
        }

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

            try
            {
                var parsed = JsonUtility.FromJson<DialogueDecision>(json);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.reply))
                {
                    return false;
                }

                decision = parsed.Normalize();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ExtractJsonObject(string raw)
        {
            var text = raw.Trim();
            var fenceIndex = text.IndexOf("```", StringComparison.Ordinal);
            if (fenceIndex >= 0)
            {
                text = text.Replace("```json", "").Replace("```JSON", "").Replace("```", "").Trim();
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text;
        }
    }
}
