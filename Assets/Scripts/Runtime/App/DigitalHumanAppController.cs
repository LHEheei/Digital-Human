using System;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed partial class DigitalHumanAppController : MonoBehaviour
    {
        private const int VoiceEmotionResponseBudgetMilliseconds = 150;
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
        private AutomaticConversationLoop automaticConversation;
        private bool busy;
        private ConversationTiming conversationTiming;
        private bool isApplicationActive = true;
        private bool isDestroyed;

        public bool IsBusy => busy || manualSubmissionPending;
        public AvatarState CurrentState { get; private set; }
        public string CurrentSubtitle => subtitleText.text;
        public string CurrentHint => hintText.text;
        public AppErr LastConversationError { get; private set; }
        public DialogueDecision LastDecision { get; private set; }
        public string LastTiming { get; private set; } = "";
        public bool LastPlaybackFailed { get; private set; }
        private bool CanPlayReply => speechService.CanSpeak || Application.isEditor;

        private bool CanUseDeepSeek => config.HasDeepSeekKey;
        private bool CanAutoListen => !manualSubmissionPending && !textInputFocused && !memoryPanelOpen && config.enableAutoConversation && isActiveAndEnabled && !isDestroyed && CanUseDeepSeek && isApplicationActive && config.preferAndroidNativeSpeech;

        private void Awake()
        {
            LoadConfigIfNeeded();
#if UNITY_EDITOR
            PrepareDesktopConfig();
#endif
            EnsureRuntimeComponents();
            speechService = new SpeechService(config, androidSpeech);
            androidSpeech.TtsStarted += OnTtsStarted;
#if UNITY_EDITOR
            InitializeDesktopDialogue();
#else
            dialogueService = new DialogueService(config);
#endif
            carCommunicationService = new CarCommunicationService(config, androidCar);
            ui = new AppUi(statusText, subtitleText, hintText);
            automaticConversation = new AutomaticConversationLoop(() => CanAutoListen,
                RunAutomaticListeningTurnAsync,
                () => AutoConversationPolicy.NormalizeRetryDelay(config.autoConversationRetryDelaySeconds),
                ReportListeningError);
            InitializeConversationPanel();
        }

        private void Start()
        {
            ShowIdle();
            ScheduleAutoListening();
        }

        private void ScheduleAutoListening() => automaticConversation?.Refresh();

        public void RefreshConversationSettings()
        {
            ListeningErrorMessage = "";
            StopAutoListening();
            conversationCancellation?.Cancel();
            ScheduleAutoListening();
        }

        private async Task RunAutomaticListeningTurnAsync(CancellationToken cancellationToken)
        {
            BeginListeningPresentation();
            emotionPerception?.SuspendAudioCapture();
            SpeechRecognitionTurn userTurn;
            try
            {
                userTurn = await speechService.ListenForUserTurnAsync(SetSubtitle, cancellationToken, () => OnRecognitionReady(cancellationToken));
            }
            finally
            {
                if (isApplicationActive && !isDestroyed) emotionPerception?.ResumeAudioCapture();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (userTurn != null && AutoConversationPolicy.HasUsableText(userTurn.Text))
                await RunConversationFromCapturedTurnAsync(userTurn, cancellationToken);
        }

        private async Task RunConversationFromCapturedTurnAsync(SpeechRecognitionTurn userTurn, CancellationToken parentCancellation)
        {
            if (userTurn == null || !AutoConversationPolicy.HasUsableText(userTurn.Text) || !TryBeginConversation(parentCancellation, out var cancellationToken))
            {
                return;
            }

            LastConversationError = AppErr.none;
            LastDecision = null;
            LastPlaybackFailed = false;
            LastErrorMessage = "";
            ListeningPhase = SpeechListeningPhase.Inactive;
            ListeningErrorMessage = "";
            BeginTurnRecord(userTurn.Text);
            ResetSentencePlayback();
            conversationTiming = new ConversationTiming();
            try
            {
                if (emotionPerception != null && userTurn.HasAudio)
                {
                    // Voice emotion runs locally and can take longer than ASR on a phone.
                    // Give it a small head start for this reply, but never make the user wait
                    // for a full model inference before their words are shown and answered.
                    var voiceEmotionTask = emotionPerception.AnalyzeSpeechTurnAsync(userTurn.AudioWav, userTurn.Text, cancellationToken);
                    await WaitBrieflyForVoiceEmotionAsync(voiceEmotionTask, cancellationToken);
                }

                conversationTiming.MarkEmotionReady();
                await RespondToUserTextAsync(userTurn.Text, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                LastConversationError = AppErr.canceled;
                LastErrorMessage = "已停止本轮回答。";
                if (!isDestroyed && isApplicationActive && isActiveAndEnabled)
                {
                    SetState(AvatarState.idle, "空闲");
                    SetHint("本轮已取消，可以开始下一轮。");
                }
                Debug.Log("Conversation canceled.");
            }
            catch (AppException ex)
            {
                LastConversationError = ex.Error;
                LastErrorMessage = ex.Message;
                Debug.LogException(ex);
                if (!cancellationToken.IsCancellationRequested && !isDestroyed && isApplicationActive) ShowRecoverableError(ex.Error, ex.Message);
            }
            catch (Exception ex)
            {
                LastConversationError = AppErr.unknown;
                LastErrorMessage = ToUserFacingError(AppErr.unknown);
                Debug.LogException(ex);
                if (!cancellationToken.IsCancellationRequested && !isDestroyed && isApplicationActive) ShowRecoverableError(AppErr.unknown);
            }
            finally
            {
                LastTiming = (Application.isEditor ? "[Desktop preview] " : "") + conversationTiming.ToString();
                if (config.ShowDiagnostics) Debug.Log(LastTiming);
                conversationTiming = null;
                EndSentencePresentation(LastConversationError != AppErr.none);
                FinishTurnRecord();
                FinishConversation(cancellationToken);
            }
        }
        private static async Task WaitBrieflyForVoiceEmotionAsync(Task analysisTask, CancellationToken cancellationToken)
        {
            if (analysisTask == null)
            {
                return;
            }

            var budget = Task.Delay(VoiceEmotionResponseBudgetMilliseconds, cancellationToken);
            if (await Task.WhenAny(analysisTask, budget) == analysisTask)
            {
                await analysisTask;
            }
            else
            {
                _ = ObserveVoiceEmotionAsync(analysisTask);
            }
        }

        private static async Task ObserveVoiceEmotionAsync(Task analysisTask)
        {
            try { await analysisTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnTtsStarted()
        {
            conversationTiming?.MarkFirstAudio();
            StartPendingPresentation();
        }

        private bool TryBeginConversation(CancellationToken parentCancellation, out CancellationToken cancellationToken)
        {
            cancellationToken = default;
            if (busy || isDestroyed || !isActiveAndEnabled || !isApplicationActive)
            {
                return false;
            }

            busy = true;
            conversationCancellation?.Cancel();
            conversationCancellation?.Dispose();
            conversationCancellation = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation);
            cancellationToken = conversationCancellation.Token;
            return true;
        }

        private async Task RespondToUserTextAsync(string userText, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetState(AvatarState.thinking, "思考");
            SetSubtitle($"你：{userText.Trim()}");
            SetHint("正在理解你的话...");
            var input = new TalkInput { text = userText };
            input = emotionPerception == null ? input : emotionPerception.ApplyLatest(input);
            var streamSpeech = config.enableStreamingReplies && !config.enableCarCommunication && CanPlayReply;
            using var replyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var sentences = new ReplySpeechQueue();
            var replyToken = replyCancellation.Token;
            var subtitle = new StringBuilder();
            var speechFailed = false;
            var playback = streamSpeech ? sentences.PlayAsync(async (sentence, token) =>
            {
                if (speechFailed) return;
                try { await PlayPresentedSentenceAsync(sentence, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    speechFailed = true;
                    LastPlaybackFailed = true;
                    Debug.LogWarning("Android TTS failed during streamed reply: " + ex.Message);
                    SetHint("语音暂不可用，请查看文字回复。");
                }

            }, replyToken) : Task.CompletedTask;
            try
            {
                Action<string> emit = streamSpeech ? sentence =>
                {
                    replyToken.ThrowIfCancellationRequested();
                    if (sentences.EnqueuedCount == 0)
                    {
                        conversationTiming?.MarkFirstSentence();

                        SetHint("正在回答...");
                    }
                    subtitle.Append(sentence);
                    UpdateTurnReply(subtitle.ToString());
                    SetSubtitle(AutoConversationPolicy.FormatTurn(userText, subtitle.ToString()));
                    sentences.Enqueue(sentence);
                } : null;
                var decision = await dialogueService.DecideAsync(input, replyToken, emit);
                LastDecision = decision;
                UpdateTurnReply(decision.reply);
                conversationTiming?.MarkModelReady();
                sentences.Complete();
                string finalHint;
                if (sentences.EnqueuedCount > 0)
                {
                    SetSubtitle(AutoConversationPolicy.FormatTurn(userText, decision.reply));
                    var carTask = RunCarCommandAsync(decision, replyToken);
                    await Task.WhenAll(playback, carTask);
                    finalHint = await carTask;
                    if (speechFailed) await PlayToneWithPresentationAsync(decision.reply, replyToken);
                }
                else
                {
                    await playback;
                    conversationTiming?.MarkFirstSentence();
                    finalHint = await PresentReplyAsync(userText, decision, replyToken);
                }
                replyToken.ThrowIfCancellationRequested();
                SetState(AvatarState.idle, "空闲");
                SetHint(string.IsNullOrWhiteSpace(finalHint) ? ResolveStartupHint() : finalHint);
            }
            finally
            {
                replyCancellation.Cancel();
                sentences.Complete();
                try { await playback; }
                catch (OperationCanceledException) when (replyCancellation.IsCancellationRequested) { }
            }
        }

        private void FinishConversation(CancellationToken cancellationToken)
        {
            busy = false;
            if (conversationCancellation != null && conversationCancellation.Token == cancellationToken)
            {
                conversationCancellation.Cancel();
                conversationCancellation.Dispose();
                conversationCancellation = null;
            }
            NotifyConversationChanged();
        }

        private void StopAutoListening()
        {
            automaticConversation?.Stop();
            speechService?.Input.Cancel();
            ListeningPhase = SpeechListeningPhase.Inactive;
            NotifyConversationChanged();
        }

        private async Task<string> PresentReplyAsync(string userText, DialogueDecision decision, CancellationToken cancellationToken)
        {
            var safeDecision = decision.Normalize();
            cancellationToken.ThrowIfCancellationRequested();
            SetSubtitle(AutoConversationPolicy.FormatTurn(userText, safeDecision.reply));
            SetHint("正在回答...");
            var carTask = RunCarCommandAsync(safeDecision, cancellationToken);
            if (CanPlayReply)
            {
                try
                {
                    foreach (var sentence in SentenceMotionPlanner.SplitSentences(safeDecision.reply))
                        await PlayPresentedSentenceAsync(sentence, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LastPlaybackFailed = true;
                    Debug.LogWarning("Reply playback failed: " + ex.Message);
                    SetHint("语音暂不可用，请查看文字回复。");
                    await PlayToneWithPresentationAsync(safeDecision.reply, cancellationToken);
                }
            }
            else await PlayToneWithPresentationAsync(safeDecision.reply, cancellationToken);
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

        private Task PlayReplySentenceAsync(string sentence, CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            return PlayDesktopSpeechAsync(sentence, cancellationToken);
#else
            return speechService.SpeakWithAndroidTtsAsync(sentence, cancellationToken);
#endif
        }

        public void CancelCurrentConversation()
        {
            manualSubmissionCancellation?.Cancel();
            conversationCancellation?.Cancel();
            speechService?.CancelAll();
            avatarSpeechDriver?.StopAll();
            EndSentencePresentation(true);
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

            avatarSpeechDriver.Bind(avatarDriver, audioSource);
            emotionPerception.Initialize(config);
            if (config.ShowDiagnostics)
            {
                emotionStatusPanel = GetOrAddComponent(emotionStatusPanel);
                emotionStatusPanel.Initialize(emotionPerception);
            }

            if (config.ShowDiagnostics && config.enableEmotionEvaluationPanel)
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
#if UNITY_EDITOR
            SetSubtitle("在 SRTP / Dialogue Workbench 输入文字开始对话。");
#else
            SetSubtitle(CanAutoListen ? "正在聆听，直接说话即可开始对话" : "请先完成 API 和系统语音服务配置");
#endif
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
#if UNITY_EDITOR
            return DesktopLiveMode ? "真实模型 · 电脑端预览播放" : "离线模拟 · 电脑端预览播放";
#else
            if (!config.ShowDiagnostics) return CanAutoListen ? "可以直接和我说话。" : "请先完成语音和 API 设置。";
            var input = speechService.CanRecognize ? "输入：Android ASR" : "输入：系统语音不可用";
            var output = speechService.CanSpeak ? "输出：Android TTS" : "输出：系统 TTS 不可用";
            var interaction = CanAutoListen ? "交互：自动聆听" : "交互：等待语音服务";
            var dialogue = CanUseDeepSeek ? "对话：DeepSeek" : "对话：AI 未配置";
            var memory = config.enableLongTermMemory ? "记忆：启用" : "记忆：关闭";
            var car = config.enableCarCommunication && config.carCommunicationMode != CarCommunicationMode.Disabled ? "小车：启用" : "小车：关闭";
            return $"{input} · {output} · {interaction}\n{dialogue} · {memory} · {car}";
#endif
        }

        private void SetState(AvatarState state, string label)
        {
            CurrentState = state;
            avatarSpeechDriver.SetState(state);

            ui.SetStatus(label);
            NotifyConversationChanged();
        }

        private void SetSubtitle(string message)
        {
            ui.SetSubtitle(message);
            NotifyConversationChanged();
        }

        private void SetHint(string message)
        {
            ui.SetHint(message);
            NotifyConversationChanged();
        }

        private void OnApplicationPause(bool paused)
        {
            isApplicationActive = !paused;
            if (paused)
            {
                StopAutoListening();
                CancelCurrentConversation();
                return;
            }

            ScheduleAutoListening();
        }

        private void OnEnable()
        {
            if (automaticConversation != null) ScheduleAutoListening();
        }

        private void OnDisable()
        {
            StopAutoListening();
            CancelCurrentConversation();
            NotifyConversationChanged();
        }

        private void OnDestroy()
        {
            isDestroyed = true;
            manualSubmissionCancellation?.Cancel();
            if (androidSpeech != null) androidSpeech.TtsStarted -= OnTtsStarted;
            automaticConversation?.Dispose();
            StopAutoListening();
            conversationCancellation?.Cancel();
            conversationCancellation?.Dispose();
            conversationCancellation = null;
            speechService?.CancelAll();
            carCommunicationService?.CancelPendingCommand();
            avatarSpeechDriver?.StopAll();
#if UNITY_EDITOR
            Destroy(config);
#endif
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
                case AppErr.memory: return "记忆写入失败，请重试。";
                default: return "连接或语音交互失败，请稍后重试。";
            }
        }

        private static string ResolveErrorHint(AppErr error)
        {
            if (error == AppErr.micPerm)
            {
                return "可重新授权；若已禁止，请前往系统设置开启。";
            }

            if (error == AppErr.memory)
            {
                return "请检查设备剩余存储空间和文件访问状态后重试。";
            }

            return error == AppErr.aiUnavailable
                ? "请填写 DeepSeek 的 Base URL、模型名和 Key 后重试。"
                : "请检查网络、设备状态和 API 设置。";
        }
    }
}
