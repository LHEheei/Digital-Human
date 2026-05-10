using UnityEngine;

namespace SrtpAvatar
{
    public abstract class DigitalHumanAvatarBase : MonoBehaviour
    {
        public abstract void SetState(AvatarState next);
        public abstract void SetEmotion(AvatarEmotion next);
        public abstract void SetSpeechLevel(float level);
        public abstract void PlayGesture(AvatarGesture gesture);
    }
}
