using System;
using System.Text;

namespace SrtpAvatar
{
    [Serializable]
    public sealed class TalkInput
    {
        public string text = "";
        public UserEmo faceEmo;
        public UserEmo voiceEmo;
        public UserEmo fusionEmo;

        public TalkInput Normalize()
        {
            text = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
            faceEmo?.Normalize();
            voiceEmo?.Normalize();
            fusionEmo?.Normalize();
            return this;
        }

        public string BuildEmotionContext()
        {
            var builder = new StringBuilder();
            Append(builder, faceEmo, "用户面部情绪");
            Append(builder, voiceEmo, "用户语音情绪");
            Append(builder, fusionEmo, "用户整体情绪建议");
            return builder.ToString().Trim();
        }

        private static void Append(StringBuilder builder, UserEmo emotion, string label)
        {
            if (emotion == null || !emotion.Normalize().IsUsable)
            {
                return;
            }

            builder.Append(label).Append('：').Append(emotion.label).Append("，置信度 ").Append(emotion.score.ToString("0.00")).AppendLine("。识别可能不准确，仅作低风险辅助上下文；不要逐字向用户复述，也不要声称已识别用户情绪，更不要据低置信度做强烈判断。");
        }
    }
}
