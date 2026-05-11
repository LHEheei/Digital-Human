using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class AvatarSpeechDriver : MonoBehaviour
    {
        [SerializeField] private DigitalHumanAvatarBase avatarDriver;
        [SerializeField] private AudioSource audioSource;

        private bool nativeTtsDriving;
        private Coroutine nativeMouthRoutine;

        public void Bind(DigitalHumanAvatarBase driver, AudioSource source)
        {
            avatarDriver = driver;
            audioSource = source;
        }

        public void SetEmotion(AvatarEmotion emotion)
        {
            avatarDriver?.SetEmotion(emotion);
        }

        public void PlayGesture(AvatarGesture gesture)
        {
            avatarDriver?.PlayGesture(gesture);
        }

        public void SetState(AvatarState state)
        {
            avatarDriver?.SetState(state);
        }

        public void SetSpeechLevel(float level)
        {
            avatarDriver?.SetSpeechLevel(level);
        }

        public void StartNativeSpeechMouth()
        {
            StopNativeSpeechMouth();
            nativeTtsDriving = true;
            nativeMouthRoutine = StartCoroutine(DriveMouthForNativeTts());
        }

        public void StopNativeSpeechMouth()
        {
            nativeTtsDriving = false;
            if (nativeMouthRoutine != null)
            {
                StopCoroutine(nativeMouthRoutine);
                nativeMouthRoutine = null;
            }

            SetSpeechLevel(0f);
        }

        public async Task PlayFallbackToneAsync(string reply)
        {
            if (audioSource == null)
            {
                SetSpeechLevel(0f);
                return;
            }

            var duration = Mathf.Clamp((reply ?? "").Length * 0.055f, 1.2f, 4.5f);
            var clip = AudioFallback.CreatePromptTone(duration);
            audioSource.clip = clip;
            audioSource.Play();
            StartCoroutine(DriveMouthFromAudio());

            while (audioSource.isPlaying)
            {
                await Task.Yield();
            }

            SetSpeechLevel(0f);
        }

        private IEnumerator DriveMouthFromAudio()
        {
            var samples = new float[128];
            while (audioSource != null && audioSource.isPlaying)
            {
                audioSource.GetOutputData(samples, 0);
                var sum = 0f;
                for (var i = 0; i < samples.Length; i++)
                {
                    sum += Mathf.Abs(samples[i]);
                }

                SetSpeechLevel(Mathf.Clamp01(sum / samples.Length * 18f));
                yield return null;
            }
        }

        private IEnumerator DriveMouthForNativeTts()
        {
            while (nativeTtsDriving)
            {
                var wave = Mathf.Sin(Time.realtimeSinceStartup * 18f) * 0.5f + 0.5f;
                SetSpeechLevel(Mathf.Lerp(0.12f, 0.85f, wave));
                yield return null;
            }
        }
    }
}
