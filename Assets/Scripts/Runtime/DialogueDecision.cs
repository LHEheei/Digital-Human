using System;

namespace SrtpAvatar
{
    public enum AvatarEmotion
    {
        neutral,
        joy,
        angry,
        sorrow,
        fun,
        surprised
    }

    public enum AvatarGesture
    {
        none,
        nod,
        wave,
        think,
        explain
    }

    public enum AvatarState
    {
        idle,
        listening,
        thinking,
        speaking
    }

    [Serializable]
    public sealed class DialogueDecision
    {
        public string reply = "好的，我在。";
        public string emotion = "neutral";
        public string gesture = "none";
        public string state = "speaking";
        public string carIntent = "none";
        public string memory = "";

        public AvatarEmotion ParsedEmotion => ParseEnum(emotion, AvatarEmotion.neutral);
        public AvatarGesture ParsedGesture => ParseEnum(gesture, AvatarGesture.none);
        public AvatarState ParsedState => ParseEnum(state, AvatarState.speaking);

        public DialogueDecision Normalize()
        {
            if (string.IsNullOrWhiteSpace(reply))
            {
                reply = "我理解了。";
            }

            emotion = NormalizeEnum(emotion, AvatarEmotion.neutral);
            gesture = NormalizeEnum(gesture, AvatarGesture.none);
            state = NormalizeEnum(state, AvatarState.speaking);
            carIntent = string.IsNullOrWhiteSpace(carIntent) ? "none" : carIntent.Trim();
            memory = string.IsNullOrWhiteSpace(memory) ? "" : memory.Trim();
            return this;
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        private static string NormalizeEnum<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, true, out T parsed) ? parsed.ToString().ToLowerInvariant() : fallback.ToString().ToLowerInvariant();
        }
    }
}
