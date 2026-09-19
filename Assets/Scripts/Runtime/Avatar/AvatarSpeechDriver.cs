using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class AvatarSpeechDriver : MonoBehaviour
    {
        [SerializeField] private DigitalHumanAvatarBase avatarDriver;
        [SerializeField] private AudioSource audioSource;

        private Coroutine nativeMouthRoutine;
        private Coroutine audioMouthRoutine;
        private readonly float[] audioSamples = new float[128];

        public void Bind(DigitalHumanAvatarBase driver, AudioSource source)
        {
            avatarDriver = driver != null ? driver : throw new ArgumentNullException(nameof(driver), "Avatar driver is required.");
            audioSource = source != null ? source : throw new ArgumentNullException(nameof(source), "Audio source is required.");
        }

        public void SetEmotion(AvatarEmotion emotion) => avatarDriver.SetEmotion(emotion);
        public void PlayGesture(AvatarGesture gesture) => avatarDriver.PlayGesture(gesture);
        public void FinishGesture() => avatarDriver.FinishGesture();
        public void SetState(AvatarState state) => avatarDriver.SetState(state);
        public void SetSpeechLevel(float level) => avatarDriver.SetSpeechLevel(level);

        public void StartNativeSpeechMouth(AndroidNativeSpeechBridge nativeSpeech = null)
        {
            StopMouthRoutines();
            nativeMouthRoutine = StartCoroutine(DriveMouthForNativeTts(nativeSpeech));
        }

        public void StopNativeSpeechMouth()
        {
            if (nativeMouthRoutine != null)
            {
                StopCoroutine(nativeMouthRoutine);
                nativeMouthRoutine = null;
            }
            if (avatarDriver != null) SetSpeechLevel(0f);
        }

        public async Task PlayFallbackToneAsync(string reply, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopAll();
            var duration = Mathf.Clamp(reply.Length * 0.055f, 1.2f, 4.5f);
            var clip = AudioFallback.CreatePromptTone(duration);
            try { await PlayAudioClipAsync(clip, cancellationToken); }
            finally { Destroy(clip); }
        }

        public async Task PlayAudioClipAsync(AudioClip clip, CancellationToken cancellationToken = default)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            cancellationToken.ThrowIfCancellationRequested();
            StopAll();
            audioSource.clip = clip;
            audioSource.Play();
            audioMouthRoutine = StartCoroutine(DriveMouthFromAudio());
            try
            {
                while (audioSource != null && audioSource.clip == clip && audioSource.isPlaying)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                if (audioSource != null && audioSource.clip == clip)
                {
                    StopAll();
                    audioSource.clip = null;
                }
            }
        }

        public void StopAll()
        {
            StopMouthRoutines();
            if (audioSource != null) audioSource.Stop();
        }

        private void OnDisable() => StopAll();

        private void StopMouthRoutines()
        {
            StopNativeSpeechMouth();
            if (audioMouthRoutine != null)
            {
                StopCoroutine(audioMouthRoutine);
                audioMouthRoutine = null;
            }
        }

        private IEnumerator DriveMouthFromAudio()
        {
            while (audioSource.isPlaying)
            {
                audioSource.GetOutputData(audioSamples, 0);
                var energy = 0f;
                for (var i = 0; i < audioSamples.Length; i++) energy += audioSamples[i] * audioSamples[i];
                var rms = Mathf.Sqrt(energy / audioSamples.Length);
                SetSpeechLevel(Mathf.Clamp01((rms - 0.008f) * 14f));
                yield return null;
            }
            SetSpeechLevel(0f);
            audioMouthRoutine = null;
        }

        private IEnumerator DriveMouthForNativeTts(AndroidNativeSpeechBridge nativeSpeech)
        {
            var elapsed = 0f;
            var duration = 0.17f;
            var strength = 0.65f;
            var syllables = 0;
            var phraseLength = UnityEngine.Random.Range(5, 9);
            var pause = false;
            while (true)
            {
                // Android TTS plays outside Unity's AudioSource. Start only after its actual onStart callback.
                if (nativeSpeech != null && !nativeSpeech.IsTtsSpeaking)
                {
                    SetSpeechLevel(0f);
                    yield return null;
                    continue;
                }
                if (nativeSpeech != null && nativeSpeech.TryGetSpeechLevel(out var measuredLevel))
                {
                    SetSpeechLevel(measuredLevel);
                    yield return null;
                    continue;
                }
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= duration)
                {
                    elapsed = 0f;
                    pause = !pause && ++syllables >= phraseLength;
                    if (pause)
                    {
                        syllables = 0;
                        phraseLength = UnityEngine.Random.Range(5, 9);
                    }
                    duration = pause ? UnityEngine.Random.Range(0.12f, 0.25f) : UnityEngine.Random.Range(0.11f, 0.23f);
                    strength = UnityEngine.Random.Range(0.35f, 0.82f);
                }
                var phase = Mathf.Clamp01(elapsed / duration);
                var pulse = Mathf.Pow(Mathf.Sin(phase * Mathf.PI), 1.4f);
                SetSpeechLevel(pause ? 0f : strength * pulse);
                yield return null;
            }
        }
    }
}
