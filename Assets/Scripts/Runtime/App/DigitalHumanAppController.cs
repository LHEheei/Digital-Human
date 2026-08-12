using System;
using System.Threading;
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
        private AppUi ui;
        private CancellationTokenSource conversationCancellation;
        private bool busy;
        private bool isDestroyed;

        private bool CanUseDeepSeek => config != null && config.HasDeepSeekKey;

        private void Awake()
        {
            LoadConfigIfNeeded();
            EnsureRuntimeComponents();
            speechService = new SpeechService(config, androidSpeech);
            dialogueService = new DialogueService(config);
            carCommunicationService = new CarCommunicationService(config, androidCar);
            ui = new AppUi(microphoneButton, statusText, subtitleText, hintText);
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
            if (busy || isDestroyed)
            {
                return;
            }

            busy = true;
            SetButtonInteractable(false);
            conversationCancellation?.Cancel();
            conversationCancellation?.Dispose();
            conversationCancellation = new CancellationTokenSource();
            var cancellationToken = conversationCancellation.Token;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetState(AvatarState.listening, "倾听");
                SetSubtitle("请说话...");

                var userText = await speechService.ListenForUserTextAsync(SetSubtitle, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                SetState(AvatarState.thinking, "思考");
                SetSubtitle("正在理解你的语音...");

                var decision = await dialogueService.DecideAsync(new TalkInput { text = userText }, cancellationToken);
                var finalHint = await PresentReplyAsync(decision, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                SetState(AvatarState.idle, "空闲");
                SetHint(string.IsNullOrWhiteSpace(finalHint) ? ResolveStartupHint() : finalHint);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Conversation canceled.");
            }
            catch (AppException ex)
            {
                Debug.LogException(ex);
                ShowRecoverableError(ex.Error);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowRecoverableError(AppErr.unknown);
            }
            finally
            {
                if (!isDestroyed)
                {
                    SetButtonInteractable(true);
                }

                busy = false;
                if (conversationCancellation != null && conversationCancellation.Token == cancellationToken)
                {
                    conversationCancellation.Dispose();
                    conversationCancellation = null;
                }
            }
        }

        private async Task<string> PresentReplyAsync(DialogueDecision decision, CancellationToken cancellationToken)
        {
            var safeDecision = decision == null ? new DialogueDecision() : decision.Normalize();
            cancellationToken.ThrowIfCancellationRequested();

            avatarSpeechDriver.SetEmotion(safeDecision.ParsedEmotion);
            avatarSpeechDriver.PlayGesture(safeDecision.ParsedGesture);
            SetState(AvatarState.speaking, "说话");
            SetSubtitle(safeDecision.reply);
            SetHint($"表情：{safeDecision.emotion} · 动作：{safeDecision.gesture}");
            var carTask = RunCarCommandAsync(safeDecision, cancellationToken);

            if (speechService.CanSpeak)
            {
                await SpeakWithAndroidTtsAsync(safeDecision.reply, cancellationToken);
                return await carTask;
            }

            await avatarSpeechDriver.PlayFallbackToneAsync(safeDecision.reply, cancellationToken);
            return await carTask;
        }

        private async Task<string> RunCarCommandAsync(DialogueDecision decision, CancellationToken cancellationToken)
        {
            var order = carCommunicationService.GetExecutionOrder(decision);
            if (order == CarExecutionOrder.none)
            {
                return "";
            }

            SetHint(order == CarExecutionOrder.immediate ? "小车：正在紧急停止" : "小车：正在执行指令");
            try
            {
                var result = await carCommunicationService.SendAsync(decision, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.ShouldDisplay)
                {
                    SetHint(result.Message);
                }

                return result.ShouldDisplay ? result.Message : "";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AppException ex)
            {
                Debug.LogWarning($"Car communication failed: {ex.Message}");
                SetHint("小车指令执行失败。");
                return "小车指令执行失败。";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Car communication failed: {ex.Message}");
                SetHint("小车指令执行失败。");
                return "小车指令执行失败。";
            }
        }

        private async Task SpeakWithAndroidTtsAsync(string reply, CancellationToken cancellationToken)
        {
            try
            {
                avatarSpeechDriver.StartNativeSpeechMouth();
                await speechService.SpeakWithAndroidTtsAsync(reply, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Android native TTS failed, falling back to local tone: {ex.Message}");
                SetHint("系统 TTS 不可用，已改用本地提示音。");
                await avatarSpeechDriver.PlayFallbackToneAsync(reply, cancellationToken);
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
            androidSpeech = GetOrAddComponent(androidSpeech);
            androidCar = GetOrAddComponent(androidCar);
            audioSource = GetOrAddComponent(audioSource);
            avatarSpeechDriver = GetOrAddComponent(avatarSpeechDriver);

            avatarSpeechDriver.Bind(avatarDriver, audioSource);

            if (gameObject.GetComponent<ApiConfigPanel>() == null)
            {
                gameObject.AddComponent<ApiConfigPanel>();
            }
        }

        private T GetOrAddComponent<T>(T component) where T : Component
        {
            return component != null ? component : gameObject.GetComponent<T>() ?? gameObject.AddComponent<T>();
        }

        private void ShowIdle()
        {
            SetState(AvatarState.idle, "空闲");
            SetSubtitle("点击麦克风开始对话");
            SetHint(ResolveStartupHint());
        }

        private void ShowRecoverableError(AppErr error)
        {
            avatarSpeechDriver.SetEmotion(AvatarEmotion.sorrow);
            avatarSpeechDriver.PlayGesture(AvatarGesture.nod);
            SetState(AvatarState.idle, "空闲");
            SetSubtitle(ToUserFacingError(error));
            SetHint(ResolveErrorHint(error));
        }

        private string ResolveStartupHint()
        {
            var input = speechService.CanRecognize ? "输入：Android ASR" : "输入：本地模拟";
            var output = speechService.CanSpeak ? "输出：Android TTS" : "输出：本地提示音";
            var dialogue = CanUseDeepSeek ? "对话：DeepSeek" : "对话：本地演示";
            var memory = config != null && config.enableLongTermMemory ? "记忆：启用" : "记忆：关闭";
            var car = config != null && config.enableCarCommunication && config.carCommunicationMode != CarCommunicationMode.Disabled ? "小车：启用" : "小车：关闭";
            return $"{input} · {output}\n{dialogue} · {memory} · {car}";
        }

        private void SetState(AvatarState state, string label)
        {
            avatarSpeechDriver.SetState(state);

            ui?.SetStatus(label);
        }

        private void SetSubtitle(string message)
        {
            ui?.SetSubtitle(message);
        }

        private void SetHint(string message)
        {
            ui?.SetHint(message);
        }

        private void SetButtonInteractable(bool interactable)
        {
            ui?.SetMicrophoneInteractable(interactable);
        }

        private void OnDestroy()
        {
            isDestroyed = true;
            conversationCancellation?.Cancel();
            conversationCancellation?.Dispose();
            conversationCancellation = null;
            speechService?.CancelAll();
            carCommunicationService?.CancelPendingCommand();
            avatarSpeechDriver?.StopAll();
        }

        private static string ToUserFacingError(AppErr error)
        {
            switch (error)
            {
                case AppErr.micPerm: return "麦克风权限未开启。";
                case AppErr.speech: return "语音识别失败，请重试。";
                case AppErr.tts: return "语音播报失败，已切换提示音。";
                case AppErr.network: return "网络连接失败，请稍后重试。";
                case AppErr.auth: return "API 认证失败，请检查设置。";
                case AppErr.badJson: return "对话服务返回格式异常。";
                case AppErr.config: return "API 配置无效，请检查设置。";
                case AppErr.car: return "小车通讯失败。";
                default: return "连接或语音交互失败，请稍后重试。";
            }
        }

        private static string ResolveErrorHint(AppErr error)
        {
            return error == AppErr.micPerm ? "可重新授权；若已禁止，请前往系统设置开启。" : "请检查网络、设备状态和 API 设置。";
        }
    }
}
