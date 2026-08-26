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
        [SerializeField] private Text statusText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Text hintText;

        private SpeechService speechService;
        private DialogueService dialogueService;
        private CarCommunicationService carCommunicationService;
        private AvatarSpeechDriver avatarSpeechDriver;
        private EmotionPerceptionController emotionPerception;
        private EmotionStatusPanel emotionStatusPanel;
        private EmotionEvaluationPanel emotionEvaluationPanel;
        private AppUi ui;
        private CancellationTokenSource conversationCancellation;
        private CancellationTokenSource autoListeningCancellation;
        private bool busy;
        private bool autoListening;
        private bool isApplicationActive = true;
        private bool isDestroyed;
        private int autoListeningGeneration;

        private bool CanUseDeepSeek => config.HasDeepSeekKey;
        private bool CanAutoListen => CanUseDeepSeek && isApplicationActive && speechService.CanRecognize;

        private void Awake()
        {
            LoadConfigIfNeeded();
            EnsureRuntimeComponents();
            speechService = new SpeechService(config, androidSpeech);
            dialogueService = new DialogueService(config);
            carCommunicationService = new CarCommunicationService(config, androidCar);
            ui = new AppUi(statusText, subtitleText, hintText);
        }

        private void Start()
        {
            ShowIdle();
            ScheduleAutoListening();
        }

        private void ScheduleAutoListening()
        {
            if (!CanAutoListen || busy || autoListening || isDestroyed)
            {
                return;
            }

            _ = RunAutomaticListeningAsync(autoListeningGeneration);
        }

        private async Task RunAutomaticListeningAsync(int generation)
        {
            if (!CanAutoListen || busy || autoListening || generation != autoListeningGeneration || isDestroyed)
            {
                return;
            }

            autoListening = true;
            var cancellation = new CancellationTokenSource();
            autoListeningCancellation = cancellation;
            try
            {
                SetState(AvatarState.listening, "倾听");
                SetSubtitle("正在聆听，直接说话即可开始对话");
                SetHint("自动语音交互已开启；直接说话即可。回复时会暂停聆听。");

                emotionPerception?.SuspendAudioCapture();
                string userText;
                try
                {
                    userText = await speechService.ListenForUserTextAsync(SetSubtitle, cancellation.Token);
                }
                finally
                {
                    emotionPerception?.ResumeAudioCapture();
                }

                if (cancellation.IsCancellationRequested || generation != autoListeningGeneration || !AutoConversationPolicy.HasUsableText(userText))
                {
                    return;
                }

                await RunConversationFromCapturedTextAsync(userText);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (generation == autoListeningGeneration && !AutoConversationPolicy.ShouldQuietlyRetry(ex))
                {
                    Debug.LogWarning("Automatic speech recognition failed: " + ex.Message);
                    SetState(AvatarState.idle, "空闲");
                    SetSubtitle("自动语音识别暂不可用，正在重试...");
                    SetHint("请检查系统语音识别服务、网络和麦克风权限；系统会自动重试。");
                }
            }
            finally
            {
                if (ReferenceEquals(autoListeningCancellation, cancellation))
                {
                    autoListeningCancellation = null;
                }

                autoListening = false;
                var shouldRestart = generation == autoListeningGeneration && CanAutoListen && !busy && !isDestroyed;
                if (shouldRestart)
                {
                    SetHint("回复结束，正在恢复聆听...");
                    try
                    {
                        var delay = AutoConversationPolicy.NormalizeRetryDelay(config.autoConversationRetryDelaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    if (generation == autoListeningGeneration && CanAutoListen && !busy && !isDestroyed)
                    {
                        ScheduleAutoListening();
                    }
                }

                cancellation.Dispose();
            }
        }

        private async Task RunConversationFromCapturedTextAsync(string userText)
        {
            if (!AutoConversationPolicy.HasUsableText(userText) || !TryBeginConversation(out var cancellationToken))
            {
                return;
            }

            try
            {
                await RespondToUserTextAsync(userText.Trim(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Conversation canceled.");
            }
            catch (AppException ex)
            {
                Debug.LogException(ex);
                ShowRecoverableError(ex.Error, ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ShowRecoverableError(AppErr.unknown);
            }
            finally
            {
                FinishConversation(cancellationToken);
            }
        }

        private bool TryBeginConversation(out CancellationToken cancellationToken)
        {
            cancellationToken = default;
            if (busy || isDestroyed)
            {
                return false;
            }

            busy = true;
            conversationCancellation?.Cancel();
            conversationCancellation?.Dispose();
            conversationCancellation = new CancellationTokenSource();
            cancellationToken = conversationCancellation.Token;
            return true;
        }

        private async Task RespondToUserTextAsync(string userText, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetState(AvatarState.thinking, "思考");
            SetSubtitle($"你：{userText.Trim()}");
            SetHint("正在理解你的语音...");

            var input = new TalkInput { text = userText };
            input = emotionPerception == null ? input : emotionPerception.ApplyLatest(input);
            var decision = await dialogueService.DecideAsync(input, cancellationToken);
            var finalHint = await PresentReplyAsync(userText, decision, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            SetState(AvatarState.idle, "空闲");
            SetHint(string.IsNullOrWhiteSpace(finalHint) ? ResolveStartupHint() : finalHint);
        }

        private void FinishConversation(CancellationToken cancellationToken)
        {
            busy = false;
            if (conversationCancellation != null && conversationCancellation.Token == cancellationToken)
            {
                conversationCancellation.Dispose();
                conversationCancellation = null;
            }
        }

        private void StopAutoListening()
        {
            autoListeningGeneration++;
            autoListeningCancellation?.Cancel();
            autoListeningCancellation = null;
            speechService?.Input.Cancel();
        }

        private async Task<string> PresentReplyAsync(string userText, DialogueDecision decision, CancellationToken cancellationToken)
        {
            var safeDecision = decision.Normalize();
            cancellationToken.ThrowIfCancellationRequested();

            avatarSpeechDriver.SetEmotion(safeDecision.ParsedEmotion);
            avatarSpeechDriver.PlayGesture(safeDecision.ParsedGesture);
            SetState(AvatarState.speaking, "说话");
            SetSubtitle(AutoConversationPolicy.FormatTurn(userText, safeDecision.reply));
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
            }

            if (config == null)
            {
                throw new InvalidOperationException("缺少必需配置 Assets/Resources/OpenAIConfig.asset。");
            }
        }

        private void EnsureRuntimeComponents()
        {
            androidSpeech = GetOrAddComponent(androidSpeech);
            androidCar = GetOrAddComponent(androidCar);
            audioSource = GetOrAddComponent(audioSource);
            avatarSpeechDriver = GetOrAddComponent(avatarSpeechDriver);
            emotionPerception = GetOrAddComponent(emotionPerception);
            emotionStatusPanel = GetOrAddComponent(emotionStatusPanel);

            avatarSpeechDriver.Bind(avatarDriver, audioSource);
            emotionPerception.Initialize(config);
            emotionStatusPanel.Initialize(emotionPerception);

            if (config.enableEmotionEvaluationPanel)
            {
                emotionEvaluationPanel = GetOrAddComponent(emotionEvaluationPanel);
                emotionEvaluationPanel.Initialize(emotionPerception);
            }

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
            SetSubtitle(CanAutoListen ? "正在聆听，直接说话即可开始对话" : "请先完成 API 和系统语音服务配置");
            SetHint(ResolveStartupHint());
        }

        private void ShowRecoverableError(AppErr error, string message = null)
        {
            avatarSpeechDriver.SetEmotion(AvatarEmotion.sorrow);
            avatarSpeechDriver.PlayGesture(AvatarGesture.nod);
            SetState(AvatarState.idle, "空闲");
            SetSubtitle(string.IsNullOrWhiteSpace(message) ? ToUserFacingError(error) : message);
            SetHint(ResolveErrorHint(error));
        }

        private string ResolveStartupHint()
        {
            var input = speechService.CanRecognize ? "输入：Android ASR" : "输入：系统语音不可用";
            var output = speechService.CanSpeak ? "输出：Android TTS" : "输出：系统 TTS 不可用";
            var interaction = CanAutoListen ? "交互：自动聆听" : "交互：等待语音服务";
            var dialogue = CanUseDeepSeek ? "对话：DeepSeek" : "对话：AI 未配置";
            var memory = config.enableLongTermMemory ? "记忆：启用" : "记忆：关闭";
            var car = config.enableCarCommunication && config.carCommunicationMode != CarCommunicationMode.Disabled ? "小车：启用" : "小车：关闭";
            return $"{input} · {output} · {interaction}\n{dialogue} · {memory} · {car}";
        }

        private void SetState(AvatarState state, string label)
        {
            avatarSpeechDriver.SetState(state);

            ui.SetStatus(label);
        }

        private void SetSubtitle(string message)
        {
            ui.SetSubtitle(message);
        }

        private void SetHint(string message)
        {
            ui.SetHint(message);
        }

        private void OnApplicationPause(bool paused)
        {
            isApplicationActive = !paused;
            if (paused)
            {
                StopAutoListening();
                return;
            }

            ScheduleAutoListening();
        }

        private void OnDestroy()
        {
            isDestroyed = true;
            StopAutoListening();
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
                case AppErr.aiUnavailable: return "AI 未连接，请点击右上角 API 按钮完成配置。";
                case AppErr.badJson: return "对话服务返回格式异常。";
                case AppErr.config: return "API 配置无效，请检查设置。";
                case AppErr.car: return "小车通讯失败。";
                default: return "连接或语音交互失败，请稍后重试。";
            }
        }

        private static string ResolveErrorHint(AppErr error)
        {
            if (error == AppErr.micPerm)
            {
                return "可重新授权；若已禁止，请前往系统设置开启。";
            }

            return error == AppErr.aiUnavailable
                ? "请填写 DeepSeek 的 Base URL、模型名和 Key 后重试。"
                : "请检查网络、设备状态和 API 设置。";
        }
    }
}
