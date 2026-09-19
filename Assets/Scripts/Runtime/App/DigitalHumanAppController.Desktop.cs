#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed partial class DigitalHumanAppController
    {
        private ConversationHistory simulatedHistory;
        private readonly ConversationHistory liveDesktopHistory = new ConversationHistory();
        private DialogueService simulatedDialogue;
        private DialogueService liveDesktopDialogue;
        public SimulatedDialogueClient DesktopSimulation { get; private set; }
        public bool DesktopLiveMode { get; private set; }
        public bool DesktopHasLiveKey => config.HasDeepSeekKey;
        public AudioClip DesktopPreviewClip { get; set; }
        public float DesktopSecondsPerCharacter { get; set; } = 0.045f;
        public bool DesktopSimulatePlaybackFailure { get; set; }
        public DialogueService DesktopDialogue => dialogueService;
        public string DesktopMemoryDirectory { get; private set; }

        private void PrepareDesktopConfig()
        {
            // Runtime clone: entering Play Mode must not change the Android asset or access devices.
            config = Instantiate(config);
            config.enableAutoConversation = false;
            config.enableCarCommunication = false;
            config.enableEmotionPerception = false;
        }

        private void InitializeDesktopDialogue()
        {
            DesktopMemoryDirectory = Path.Combine(Application.temporaryCachePath, "DialogueWorkbench", Guid.NewGuid().ToString("N"));
            simulatedHistory = History;
            DesktopSimulation = new SimulatedDialogueClient();
            simulatedDialogue = new DialogueService(config, DesktopSimulation,
                new LongTermMemoryStore(Path.Combine(DesktopMemoryDirectory, "simulation.json")));
            liveDesktopDialogue = new DialogueService(config, new DeepSeekDialogueClient(config),
                new LongTermMemoryStore(Path.Combine(DesktopMemoryDirectory, "live.json")));
            dialogueService = simulatedDialogue;
            GetComponent<AvatarTestPanel>()?.SetVisible(false);
        }

        public void SelectDesktopMode(bool live)
        {
            if (IsBusy) throw new InvalidOperationException("请先等待本轮完成或取消。");
            DesktopLiveMode = live;
            dialogueService = live ? liveDesktopDialogue : simulatedDialogue;
            History = live ? liveDesktopHistory : simulatedHistory;
            LastConversationError = AppErr.none;
            LastErrorMessage = "";
            LastPlaybackFailed = false;
            LastDecision = null;
            LastTiming = "";
            ShowIdle();
        }

        public Task SubmitDesktopTextAsync(string text)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !isApplicationActive)
                throw new InvalidOperationException("请在运行中的演示场景提交文字。");
            if (IsBusy) throw new InvalidOperationException("本轮尚未完成，请等待或取消。");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请输入对话内容。", nameof(text));
            return SubmitTextAsync(text);
        }

        private async Task PlayDesktopSpeechAsync(string sentence, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DesktopSimulatePlaybackFailure) throw new AppException(AppErr.tts, "模拟播放失败。");
            OnTtsStarted();
            if (DesktopPreviewClip != null)
            {
                await avatarSpeechDriver.PlayAudioClipAsync(DesktopPreviewClip, cancellationToken);
                return;
            }
            avatarSpeechDriver.StartNativeSpeechMouth();
            try
            {
                // Silent preview of speaking rhythm; this is deliberately not labelled as TTS.
                var duration = Mathf.Clamp(sentence.Length * DesktopSecondsPerCharacter, 0.1f, 5f);
                await Task.Delay(TimeSpan.FromSeconds(duration), cancellationToken);
            }
            finally { avatarSpeechDriver.StopNativeSpeechMouth(); }
        }
    }
}
#endif
