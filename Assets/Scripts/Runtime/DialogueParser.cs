using System;
using UnityEngine;

namespace SrtpAvatar
{
    public static class DialogueParser
    {
        public static DialogueDecision FromPossiblyWrappedJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new DialogueDecision();
            }

            var json = ExtractJsonObject(raw);
            try
            {
                var parsed = JsonUtility.FromJson<DialogueDecision>(json);
                return parsed == null ? new DialogueDecision { reply = raw.Trim() } : parsed.Normalize();
            }
            catch (Exception)
            {
                return new DialogueDecision { reply = raw.Trim(), emotion = "neutral", gesture = "explain", state = "speaking" };
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
