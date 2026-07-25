using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class DigitalHumanAppController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private AppConfig config;

        [Header("Scene References")]
        [SerializeField] private DigitalHumanAvatarBase avatarDriver;
        [SerializeField] private AndroidNativeSpeechBridge androidSpeech;
        [SerializeField] private AndroidBluetoothCarBridge androidCar;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Button microphoneButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Text hintText;

        private SpeechService speechService;
        private DialogueService dialogueService;
        private CarCommunicationService carCommunicationService;
        private AvatarSpeechDriver avatarSpeechDriver;
        private bool busy;

        private bool CanUseDeepSeek => config != null && config.HasDeepSeekKey;

        private void Awake()
        {
            LoadConfigIfNeeded();
            EnsureRuntimeComponents();
            speechService = new SpeechService(config, androidSpeech);
            dialogueService = new DialogueService(config);
            carCommunicationService = new CarCommunicationService(config, androidCar);
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
                SetState(AvatarState.listening, "倾听");
                SetSubtitle("请说话...");

                var userText = await speechService.ListenForUserTextAsync(SetSubtitle);

                SetState(AvatarState.thinking, "思考");
                SetSubtitle("正在理解你的语音...");

                var decision = await dialogueService.DecideAsync(userText);
                var finalHint = await PresentReplyAsync(decision);

                SetState(AvatarState.idle, "空闲");
                SetHint(string.IsNullOrWhiteSpace(finalHint) ? ResolveStartupHint() : finalHint);
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

        private async Task<string> PresentReplyAsync(DialogueDecision decision)
        {
            var safeDecision = decision == null ? new DialogueDecision() : decision.Normalize();

            avatarSpeechDriver.SetEmotion(safeDecision.ParsedEmotion);
            avatarSpeechDriver.PlayGesture(safeDecision.ParsedGesture);
            SetState(AvatarState.speaking, "说话");
            SetSubtitle(safeDecision.reply);
            SetHint($"emotion={safeDecision.emotion}  gesture={safeDecision.gesture}  carIntent={safeDecision.carIntent}");
            var carTask = SendCarIntentAsync(safeDecision);

            if (speechService.CanSpeak)
            {
                await SpeakWithAndroidTtsAsync(safeDecision.reply);
                return await ShowCarCommunicationResultAsync(carTask);
            }

            await avatarSpeechDriver.PlayFallbackToneAsync(safeDecision.reply);
            return await ShowCarCommunicationResultAsync(carTask);
        }

        private async Task<string> SendCarIntentAsync(DialogueDecision decision)
        {
            try
            {
                var result = await carCommunicationService.SendAsync(decision);
                return result.ShouldDisplay ? result.Message : "";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Car communication failed: {ex.Message}");
                return $"小车通讯失败：{ex.Message}";
            }
        }

        private async Task<string> ShowCarCommunicationResultAsync(Task<string> carTask)
        {
            var message = await carTask;
            if (!string.IsNullOrWhiteSpace(message))
            {
                SetHint(message);
            }

            return message;
        }

        private async Task SpeakWithAndroidTtsAsync(string reply)
        {
            try
            {
                avatarSpeechDriver.StartNativeSpeechMouth();
                await speechService.SpeakWithAndroidTtsAsync(reply);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Android native TTS failed, falling back to local tone: {ex.Message}");
                SetHint($"系统 TTS 播报失败，已用本地提示音替代。{ex.Message}");
                await avatarSpeechDriver.PlayFallbackToneAsync(reply);
            }
            finally
            {
                avatarSpeechDriver.StopNativeSpeechMouth();
            }
        }

        private void LoadConfigIfNeeded()
        {
            if (config == null)
            {
                config = Resources.Load<AppConfig>("OpenAIConfig");
                if (config == null)
                {
                    config = ScriptableObject.CreateInstance<AppConfig>();
                    Debug.LogWarning("未找到 OpenAIConfig.asset，已使用不含 Key 的默认演示配置。");
                }
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

            if (androidCar == null)
            {
                androidCar = gameObject.GetComponent<AndroidBluetoothCarBridge>();
            }

            if (androidCar == null)
            {
                androidCar = gameObject.AddComponent<AndroidBluetoothCarBridge>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            avatarSpeechDriver = gameObject.GetComponent<AvatarSpeechDriver>();
            if (avatarSpeechDriver == null)
            {
                avatarSpeechDriver = gameObject.AddComponent<AvatarSpeechDriver>();
            }

            avatarSpeechDriver.Bind(avatarDriver, audioSource);

            if (gameObject.GetComponent<ApiConfigPanel>() == null)
            {
                gameObject.AddComponent<ApiConfigPanel>();
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
            avatarSpeechDriver.SetEmotion(AvatarEmotion.sorrow);
            avatarSpeechDriver.PlayGesture(AvatarGesture.nod);
            SetState(AvatarState.idle, "空闲");
            SetSubtitle(ToUserFacingError(message));
            SetHint("请检查麦克风权限、网络和 API 设置后重试。");
        }

        private string ResolveStartupHint()
        {
            var memoryLabel = config != null && config.enableLongTermMemory ? "长期记忆已启用" : "长期记忆未启用";
            if (!CanUseDeepSeek)
            {
                return speechService.CanRecognize
                    ? $"演示模式：安卓语音输入 + 本地模拟回复，{memoryLabel}。"
                    : $"演示模式：未配置 DeepSeek Key，将使用本地模拟回复，{memoryLabel}。";
            }

            return speechService.CanRecognize
                ? $"云端模式：安卓系统语音 + DeepSeek LLM 已接入，{memoryLabel}。"
                : $"DeepSeek 已配置：当前环境无安卓系统语音，将使用本地模拟输入和提示音，{memoryLabel}。";
        }

        private void SetState(AvatarState state, string label)
        {
            avatarSpeechDriver.SetState(state);

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

        private void OnDestroy()
        {
            speechService?.Output.Stop();
            carCommunicationService?.CancelPendingCommand();
        }

        private static string ToUserFacingError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && message.Contains("麦克风权限"))
            {
                return "未获得麦克风权限，请授权后重试。";
            }

            if (!string.IsNullOrWhiteSpace(message) && message.Contains("JSON"))
            {
                return "对话服务返回格式异常，请稍后重试。";
            }

            if (!string.IsNullOrWhiteSpace(message) && (message.Contains("网络") || message.Contains("HTTP")))
            {
                return "网络连接失败，请检查网络后重试。";
            }

            return "连接或语音交互失败，请检查权限、网络和 API 设置。";
        }
    }
}
