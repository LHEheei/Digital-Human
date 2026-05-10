using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class DigitalHumanAppController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private OpenAICompatibleConfig config;

        [Header("Scene References")]
        [SerializeField] private DigitalHumanAvatarBase avatarDriver;
        [SerializeField] private AndroidNativeSpeechBridge androidSpeech;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Button microphoneButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Text hintText;

        private DeepSeekDialogueClient deepSeekClient;
        private bool busy;
        private bool nativeTtsDriving;
        private Coroutine nativeMouthRoutine;

        private bool CanUseAndroidSpeech =>
            config != null
            && CanUseDeepSeek
            && config.preferAndroidNativeSpeech
            && androidSpeech != null
            && androidSpeech.IsAvailable;

        private bool CanUseDeepSeek => config != null && config.HasDeepSeekKey;

        private void Awake()
        {
            LoadConfigIfNeeded();
            EnsureRuntimeComponents();
            deepSeekClient = new DeepSeekDialogueClient(config);
        }

        private void Start()
        {
            if (microphoneButton != null)
            {
                microphoneButton.onClick.AddListener(() => _ = RunConversationAsync());
            }

            ShowIdle();
        }

        public async Task RunConversationAsync()
        {
            if (busy)
            {
                return;
            }

            busy = true;
            SetButtonInteractable(false);

            try
            {
                var userText = await ListenForUserTextAsync();
                var decision = await DecideReplyAsync(userText);
                await PresentReplyAsync(decision);
                ShowIdle();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowRecoverableError(ex.Message);
            }
            finally
            {
                SetButtonInteractable(true);
                busy = false;
            }
        }

        private async Task<string> ListenForUserTextAsync()
        {
            SetState(AvatarState.listening, "倾听");
            SetSubtitle("请说话...");

            if (CanUseAndroidSpeech)
            {
                SetSubtitle("系统语音识别启动中，请说话...");
                var timeout = config == null ? 20f : Mathf.Max(5f, config.speechRecognitionTimeoutSeconds);
                var text = await WithTimeout(androidSpeech.RecognizeAsync(config.speechLanguage), timeout, "系统语音识别超时。");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    SetSubtitle($"你：{text}");
                    return text.Trim();
                }
            }

            if (config == null || config.useLocalDemoWhenDeepSeekKeyMissing || Application.isEditor)
            {
                var demoText = LocalDemoDialogue.UserText(config);
                SetSubtitle($"你：{demoText}");
                return demoText;
            }

            throw new InvalidOperationException("当前设备没有可用的系统语音识别服务。");
        }

        private async Task<DialogueDecision> DecideReplyAsync(string userText)
        {
            SetState(AvatarState.thinking, "思考");
            SetSubtitle("正在理解你的语音...");

            if (!CanUseDeepSeek)
            {
                return LocalDemoDialogue.Decide(userText);
            }

            return await deepSeekClient.CompleteAsync(userText);
        }

        private async Task PresentReplyAsync(DialogueDecision decision)
        {
            var safeDecision = decision == null ? new DialogueDecision() : decision.Normalize();

            SetEmotion(safeDecision.ParsedEmotion);
            PlayGesture(safeDecision.ParsedGesture);
            SetState(AvatarState.speaking, "说话");
            SetSubtitle(safeDecision.reply);
            SetHint($"emotion={safeDecision.emotion}  gesture={safeDecision.gesture}  carIntent={safeDecision.carIntent}");

            if (CanUseAndroidSpeech)
            {
                await SpeakWithAndroidTtsAsync(safeDecision.reply);
                return;
            }

            await PlayFallbackToneAsync(safeDecision.reply);
        }

        private async Task SpeakWithAndroidTtsAsync(string reply)
        {
            try
            {
                nativeTtsDriving = true;
                nativeMouthRoutine = StartCoroutine(DriveMouthForNativeTts());

                var timeout = ResolveTtsTimeout(reply);
                await WithTimeout(androidSpeech.SpeakAsync(reply, config.speechLanguage), timeout, "系统 TTS 播报超时。");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Android native TTS failed, falling back to local tone: {ex.Message}");
                SetHint($"系统 TTS 播报失败，已用本地提示音替代。{ex.Message}");
                await PlayFallbackToneAsync(reply);
            }
            finally
            {
                nativeTtsDriving = false;
                if (nativeMouthRoutine != null)
                {
                    StopCoroutine(nativeMouthRoutine);
                    nativeMouthRoutine = null;
                }

                SetSpeechLevel(0f);
            }
        }

        private async Task PlayFallbackToneAsync(string reply)
        {
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

        private void LoadConfigIfNeeded()
        {
            if (config == null)
            {
                config = Resources.Load<OpenAICompatibleConfig>("OpenAIConfig");
            }
        }

        private void EnsureRuntimeComponents()
        {
            if (androidSpeech == null)
            {
                androidSpeech = gameObject.GetComponent<AndroidNativeSpeechBridge>();
            }

            if (androidSpeech == null)
            {
                androidSpeech = gameObject.AddComponent<AndroidNativeSpeechBridge>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        private void ShowIdle()
        {
            SetState(AvatarState.idle, "空闲");
            SetSubtitle("点击麦克风开始对话");
            SetHint(ResolveStartupHint());
        }

        private void ShowRecoverableError(string message)
        {
            SetEmotion(AvatarEmotion.sorrow);
            PlayGesture(AvatarGesture.nod);
            SetState(AvatarState.idle, "空闲");
            SetSubtitle("连接或语音交互失败，请检查权限、网络和 DeepSeek 配置。");
            SetHint(message);
        }

        private string ResolveStartupHint()
        {
            if (!CanUseDeepSeek)
            {
                return "演示模式：未配置 DeepSeek Key，将使用本地模拟回复。";
            }

            return CanUseAndroidSpeech
                ? "云端模式：安卓系统语音 + DeepSeek LLM 已接入。"
                : "DeepSeek 已配置：当前环境无安卓系统语音，将使用本地模拟输入和提示音。";
        }

        private float ResolveTtsTimeout(string reply)
        {
            var baseSeconds = config == null ? 5f : Mathf.Max(3f, config.ttsBaseTimeoutSeconds);
            var secondsPerCharacter = config == null ? 0.25f : Mathf.Max(0.05f, config.ttsSecondsPerCharacter);
            return Mathf.Clamp((reply ?? "").Length * secondsPerCharacter + baseSeconds, 8f, 30f);
        }

        private void SetState(AvatarState state, string label)
        {
            if (avatarDriver != null)
            {
                avatarDriver.SetState(state);
            }

            if (statusText != null)
            {
                statusText.text = label;
            }
        }

        private void SetSubtitle(string message)
        {
            if (subtitleText != null)
            {
                subtitleText.text = message;
            }
        }

        private void SetHint(string message)
        {
            if (hintText != null)
            {
                hintText.text = message;
            }
        }

        private void SetButtonInteractable(bool interactable)
        {
            if (microphoneButton != null)
            {
                microphoneButton.interactable = interactable;
            }
        }

        private void SetEmotion(AvatarEmotion emotion)
        {
            if (avatarDriver != null)
            {
                avatarDriver.SetEmotion(emotion);
            }
        }

        private void PlayGesture(AvatarGesture gesture)
        {
            if (avatarDriver != null)
            {
                avatarDriver.PlayGesture(gesture);
            }
        }

        private void SetSpeechLevel(float level)
        {
            if (avatarDriver != null)
            {
                avatarDriver.SetSpeechLevel(level);
            }
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, float seconds, string timeoutMessage)
        {
            var timeout = DelaySeconds(seconds);
            var completed = await Task.WhenAny(task, timeout);
            if (completed == task)
            {
                return await task;
            }

            throw new TimeoutException(timeoutMessage);
        }

        private static async Task WithTimeout(Task task, float seconds, string timeoutMessage)
        {
            var timeout = DelaySeconds(seconds);
            var completed = await Task.WhenAny(task, timeout);
            if (completed == task)
            {
                await task;
                return;
            }

            throw new TimeoutException(timeoutMessage);
        }

        private static async Task DelaySeconds(float seconds)
        {
            var end = Time.realtimeSinceStartup + Mathf.Max(0.1f, seconds);
            while (Time.realtimeSinceStartup < end)
            {
                await Task.Yield();
            }
        }
    }
}
