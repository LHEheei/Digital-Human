using System;
using SrtpAvatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SrtpAvatarEditor
{
    public sealed class DialogueWorkbenchWindow : EditorWindow
    {
        private static readonly string[] Modes = { "离线模拟", "真实模型" };
        private static readonly string[] Scenarios = { "正常回复", "慢速分包", "首句后断流", "认证失败", "请求超时", "非法 JSON" };
        private static readonly string[] Emotions = { "neutral", "joy", "angry", "sorrow", "fun", "surprised" };
        private static readonly string[] Gestures = { "none", "nod", "wave", "think", "explain" };
        [SerializeField] private string input = "你好，今天我们聊些什么？";
        private DigitalHumanAppController controller;
        private Vector2 scroll;
        private string message = "";
        private bool showMemory;

        [MenuItem("SRTP/Dialogue Workbench")]
        public static void Open()
        {
            var window = GetWindow<DialogueWorkbenchWindow>("对话调试");
            window.minSize = new Vector2(420, 580);
            window.Show();
        }

        private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;
        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        private void OnPlayModeChanged(PlayModeStateChange state) { controller = null; message = ""; Repaint(); }
        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying && controller == null)
                controller = FindAnyObjectByType<DigitalHumanAppController>();
            Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("数字人对话调试", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("文字输入 · 流式回复 · 人物反馈 · 独立调试记忆", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("打开演示场景并进入 Play Mode 后，可在这里直接对话。离线模拟无需 API Key。", MessageType.Info);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("打开演示场景并运行", GUILayout.Height(32)) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene("Assets/Scenes/DesktopAvatarDemo.unity");
                        EditorApplication.isPlaying = true;
                    }
                }
                EditorGUILayout.EndScrollView();
                return;
            }
            if (controller == null)
            {
                EditorGUILayout.HelpBox("正在连接运行中的会话控制器。请使用 DesktopAvatarDemo 场景。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            using (new EditorGUI.DisabledScope(controller.IsBusy))
            {
                var selected = GUILayout.Toolbar(controller.DesktopLiveMode ? 1 : 0, Modes);
                if ((selected == 1) != controller.DesktopLiveMode) controller.SelectDesktopMode(selected == 1);
                if (controller.DesktopLiveMode)
                    EditorGUILayout.HelpBox(controller.DesktopHasLiveKey
                        ? "发送后会使用当前 API 设置调用真实模型。人物播放仍是电脑端预览。"
                        : "尚未配置 Key。请使用 Game 窗口右上角 API 面板保存设置。", MessageType.Info);
                else DrawSimulationSettings();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("播放预览", EditorStyles.boldLabel);
                controller.DesktopPreviewClip = (AudioClip)EditorGUILayout.ObjectField("参考音频（可选）", controller.DesktopPreviewClip, typeof(AudioClip), false);
                EditorGUILayout.LabelField(controller.DesktopPreviewClip == null
                    ? "默认静音预览口型节奏，不合成语音。"
                    : "每句播放这段参考音频，以实际音量驱动口型；音频内容不会跟随回复生成。", EditorStyles.wordWrappedMiniLabel);
                controller.DesktopSimulatePlaybackFailure = EditorGUILayout.Toggle("模拟播放失败", controller.DesktopSimulatePlaybackFailure);
                EditorGUILayout.LabelField("输入内容", EditorStyles.boldLabel);
                input = EditorGUILayout.TextArea(input, GUILayout.MinHeight(64));
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(controller.IsBusy || string.IsNullOrWhiteSpace(input) ||
                (controller.DesktopLiveMode && !controller.DesktopHasLiveKey)))
            {
                if (GUILayout.Button("发送", GUILayout.Height(30))) Submit();
            }
            using (new EditorGUI.DisabledScope(!controller.IsBusy))
            {
                if (GUILayout.Button("取消本轮", GUILayout.Height(30))) controller.CancelCurrentConversation();
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(controller.IsBusy ? "本轮进行中" : "等待输入", EditorStyles.boldLabel);
            var subtitleHeight = Mathf.Max(72, EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(controller.CurrentSubtitle), position.width - 40));
            EditorGUILayout.SelectableLabel(controller.CurrentSubtitle, EditorStyles.wordWrappedLabel, GUILayout.Height(subtitleHeight));
            EditorGUILayout.LabelField(controller.CurrentHint, EditorStyles.wordWrappedMiniLabel);
            if (controller.LastConversationError != AppErr.none)
                EditorGUILayout.LabelField("本轮结果", controller.LastConversationError.ToString());
            if (controller.LastDecision != null)
                EditorGUILayout.LabelField("表情 / 动作", controller.LastDecision.emotion + " / " + controller.LastDecision.gesture);
            if (controller.LastPlaybackFailed) EditorGUILayout.HelpBox("播放失败已回退到文字和提示音。", MessageType.Info);
            EditorGUILayout.LabelField("本地预览耗时（不代表手机 TTS）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(controller.LastTiming, EditorStyles.wordWrappedMiniLabel);
            showMemory = EditorGUILayout.Foldout(showMemory,
                $"当前模式记忆：{controller.DesktopDialogue.RecentTurnCount} 轮 / {controller.DesktopDialogue.LongTermMemoryCount} 条", true);
            if (showMemory)
            {
                EditorGUILayout.HelpBox("模拟和真实模式分别保存记忆；重新进入 Play Mode 创建新会话。可发送“忘记刚才”或“清除记忆”验证范围。", MessageType.Info);
                EditorGUILayout.LabelField(controller.DesktopDialogue.RecentContext, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(controller.DesktopDialogue.LongTermContext, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSimulationSettings()
        {
            var simulation = controller.DesktopSimulation;
            simulation.Scenario = (SimulatedDialogueScenario)EditorGUILayout.Popup("测试情景", (int)simulation.Scenario, Scenarios);
            EditorGUILayout.LabelField("固定回复");
            simulation.Reply = EditorGUILayout.TextArea(simulation.Reply, GUILayout.MinHeight(50));
            simulation.Emotion = Emotions[EditorGUILayout.Popup("表情", Math.Max(0, Array.IndexOf(Emotions, simulation.Emotion)), Emotions)];
            simulation.Gesture = Gestures[EditorGUILayout.Popup("动作", Math.Max(0, Array.IndexOf(Gestures, simulation.Gesture)), Gestures)];
            simulation.Memory = EditorGUILayout.TextField("本轮记忆（可选）", simulation.Memory);
        }

        private async void Submit()
        {
            message = "";
            try { await controller.SubmitDesktopTextAsync(input); }
            catch (Exception ex) { message = ex.Message; }
            if (this != null) Repaint();
        }
    }
}
