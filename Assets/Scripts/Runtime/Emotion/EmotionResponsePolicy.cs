using System;

namespace SrtpAvatar
{
    public static class EmotionResponsePolicy
    {
        public static DialogueDecision Apply(DialogueDecision decision, UserEmo fusionEmotion)
        {
            if (decision == null)
            {
                throw new ArgumentNullException(nameof(decision));
            }

            var safeDecision = decision.Normalize();
            if (fusionEmotion == null || !fusionEmotion.IsUsable || fusionEmotion.score < 0.5f)
            {
                return safeDecision;
            }

            safeDecision.reply = EmotionDisclosureGuard.Sanitize(safeDecision.reply);
            if ((fusionEmotion.label == "angry" || fusionEmotion.label == "sorrow")
                && (safeDecision.emotion == "angry" || safeDecision.emotion == "fun"))
            {
                safeDecision.emotion = "neutral";
            }

            return safeDecision;
        }
    }
}
