namespace SrtpAvatar
{
    public static class EmotionResponsePolicy
    {
        public static DialogueDecision Apply(DialogueDecision decision, UserEmo fusionEmotion)
        {
            var safeDecision = (decision ?? new DialogueDecision()).Normalize();
            if (fusionEmotion == null || !fusionEmotion.IsUsable || fusionEmotion.score < 0.5f)
            {
                return safeDecision;
            }

            safeDecision.reply = EmotionDisclosureGuard.Sanitize(safeDecision.reply);
            safeDecision.emotion = fusionEmotion.label;
            if (safeDecision.gesture == "none")
            {
                safeDecision.gesture = fusionEmotion.label == "surprised" ? "think" : "nod";
            }

            if (safeDecision.carIntent == "none")
            {
                safeDecision.reply = ResolveLocalDemoReply(safeDecision.reply, fusionEmotion.label);
            }

            return safeDecision;
        }

        private static string ResolveLocalDemoReply(string originalReply, string label)
        {
            switch (label)
            {
                case "sorrow":
                    return "我们可以放慢一点，一步一步把接下来的事情理清楚。";
                case "angry":
                    return "我会保持清晰和稳妥，和你一起处理当前情况。";
                case "surprised":
                    return "没关系，我们先确认一下当前情况，再决定下一步。";
                default:
                    return originalReply;
            }
        }
    }
}
