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

            // JsonUtility applies field initializers when a field is absent, so verify the required reply field first.
            if (json.IndexOf("\"reply\"", StringComparison.Ordinal) < 0)
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
