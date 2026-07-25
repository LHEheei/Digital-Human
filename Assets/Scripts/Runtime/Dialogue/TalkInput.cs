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

        public TalkInput Normalize()
        {
            text = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
            faceEmo?.Normalize();
            voiceEmo?.Normalize();
            return this;
        }

        public string BuildEmotionContext()
        {
            var builder = new StringBuilder();
            Append(builder, faceEmo, "用户面部情绪");
            Append(builder, voiceEmo, "用户语音情绪");
            return builder.ToString().Trim();
        }

        private static void Append(StringBuilder builder, UserEmo emotion, string label)
        {
            if (emotion == null || !emotion.Normalize().IsUsable)
            {
                return;
            }

            builder.Append(label).Append('：').Append(emotion.label).Append("，置信度 ").Append(emotion.score.ToString("0.00")).AppendLine("。识别可能不准确，仅作辅助上下文；不要逐字向用户复述，也不要据低置信度做强烈判断。");
        }
    }
}
