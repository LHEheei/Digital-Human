#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SrtpAvatar.Tests
{
    public sealed class ConversationInterfaceTests
    {
        private Scene scene;
        private DigitalHumanAppController app;
        private ConversationPanel panel;
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/DesktopAvatarDemo.unity", LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath("Assets/Scenes/DesktopAvatarDemo.unity");
            app = Object.FindAnyObjectByType<DigitalHumanAppController>();
            panel = app.GetComponent<ConversationPanel>();
            yield return null;
            app.SelectDesktopMode(false);
            app.DesktopSimulation.ChunkDelayMilliseconds = 1;
            app.DesktopSecondsPerCharacter = .001f;
            app.DesktopSimulation.Reply = "你好。我们继续聊吧。";
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (app != null) { app.CancelCurrentConversation(); yield return Until(() => !app.IsBusy); }
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
        }
        [UnityTest] public IEnumerator SendStopAndRetryButtonsDriveTheRealConversationFlow()
        {
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Slow;
            app.DesktopSimulation.SlowChunkDelayMilliseconds = 50;
            app.DesktopSimulation.Reply = "你好。让我们慢慢聊聊今天发生的事情，以及接下来想一起完成的计划。";
            app.DesktopSecondsPerCharacter = 1f;
            panel.MessageInput.text = "请给我一些建议";
            yield return null;
            Assert.IsTrue(panel.SendButton.interactable);
            panel.SendButton.onClick.Invoke();
            yield return Until(() => app.CurrentState == AvatarState.speaking);
            yield return null;
            Assert.IsTrue(panel.StopButton.gameObject.activeSelf);
            Assert.IsFalse(panel.MessageInput.interactable);
            panel.StopButton.onClick.Invoke();
            yield return Until(() => !app.IsBusy); yield return null;
            Assert.AreEqual(ConversationTurnStatus.Cancelled, app.History.Latest.Status);
            Assert.IsTrue(panel.RetryButton.gameObject.activeSelf);
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Normal;
            app.DesktopSecondsPerCharacter = .001f;
            panel.RetryButton.onClick.Invoke();
            panel.RetryButton.onClick.Invoke(); // A second click before the next UI frame must be ignored.
            yield return Until(() => !app.IsBusy); yield return null;
            Assert.AreEqual(2, app.History.Turns.Count);
            Assert.AreEqual("请给我一些建议", app.History.Latest.UserText);
            Assert.AreEqual(ConversationTurnStatus.Completed, app.History.Latest.Status);
            Assert.AreEqual(1, app.DesktopDialogue.RecentTurnCount);
            Assert.IsFalse(panel.RetryButton.gameObject.activeSelf);
            Assert.AreEqual("", panel.MessageInput.text);
        }
        [UnityTest] public IEnumerator FailedTurnRemainsVisibleAndRetryCommitsOnlyTheSuccessfulTurn()
        {
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.AuthenticationFailure;
            LogAssert.Expect(LogType.Exception, new Regex("AppException:"));
            panel.MessageInput.text = "继续刚才的话题"; panel.SendButton.onClick.Invoke();
            yield return Until(() => !app.IsBusy); yield return null;
            Assert.AreEqual(ConversationTurnStatus.Failed, app.History.Latest.Status);
            Assert.IsNotEmpty(app.History.Latest.ErrorMessage);
            Assert.IsTrue(panel.RetryButton.gameObject.activeSelf);
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Normal;
            panel.RetryButton.onClick.Invoke(); yield return Until(() => !app.IsBusy); yield return null;
            Assert.AreEqual(ConversationTurnStatus.Failed, app.History.Turns[0].Status);
            Assert.AreEqual(ConversationTurnStatus.Completed, app.History.Latest.Status);
            Assert.AreEqual(1, app.DesktopDialogue.RecentTurnCount);
        }
        [UnityTest] public IEnumerator SentenceCuesStartAtPlaybackBeforeMetadataAndCancelStopsFurtherCues()
        {
            var sentences = new List<string>(); var gestures = new List<AvatarGesture>();
            var firstStartedBeforeDecision = false;
            app.SentencePlaybackStarted += (text, cue) =>
            {
                if (sentences.Count == 0) firstStartedBeforeDecision = app.LastDecision == null;
                sentences.Add(text); gestures.Add(cue.Gesture);
            };
            app.DesktopSimulation.ChunkDelayMilliseconds = 35;
            app.DesktopSimulation.Reply = "你好。首先可以整理一下今天的计划。";
            var operation = app.SubmitDesktopTextAsync("开始回答");
            yield return Until(() => operation.IsCompleted);
            Assert.IsFalse(operation.IsFaulted);
            Assert.IsTrue(firstStartedBeforeDecision);
            CollectionAssert.AreEqual(new[] { "你好。", "首先可以整理一下今天的计划。" }, sentences);
            Assert.AreEqual(AvatarGesture.wave, gestures[0]);
            var cueCount = sentences.Count;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(cueCount, sentences.Count, "Late metadata must not replay gestures.");
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Slow;
            app.DesktopSimulation.SlowChunkDelayMilliseconds = 100;
            app.DesktopSecondsPerCharacter = 1f;
            operation = app.SubmitDesktopTextAsync("停止测试");
            yield return Until(() => sentences.Count > cueCount);
            app.CancelCurrentConversation(); yield return Until(() => !app.IsBusy);
            cueCount = sentences.Count;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(cueCount, sentences.Count);
            Assert.AreEqual(AvatarState.idle, app.CurrentState);
        }
        [UnityTest] public IEnumerator MemoryPanelEditsAndDeletesOneRecordAndHistoriesRemainIsolated()
        {
            app.DesktopSimulation.Memory = "用户喜欢音乐；用户喜欢电影";
            var operation = app.SubmitDesktopTextAsync("请记住我的兴趣");
            yield return Until(() => operation.IsCompleted); yield return null;
            Assert.IsFalse(operation.IsFaulted);
            var original = app.GetMemoryEntries(); Assert.AreEqual(2, original.Count);
            panel.MemoryButton.onClick.Invoke(); yield return null;
            var memory = panel.MemoryPanel; Assert.IsTrue(memory.IsOpen);
            memory.SelectEntry(original[0].Id);
            memory.EditInput.text = "用户喜欢古典音乐";
            memory.SaveButton.onClick.Invoke();
            Assert.AreEqual("用户喜欢古典音乐", app.GetMemoryEntries()[0].Text);
            var path = Path.Combine(app.DesktopMemoryDirectory, "simulation.json");
            Assert.AreEqual("用户喜欢古典音乐", new LongTermMemoryStore(path).GetEntries()[0].Text);
            memory.DeleteButton.onClick.Invoke();
            Assert.AreEqual(1, app.GetMemoryEntries().Count);
            Assert.AreEqual(original[1].Id, app.GetMemoryEntries()[0].Id);
            Assert.IsNull(memory.SelectedId); Assert.IsFalse(memory.SaveButton.interactable);
            memory.CloseButton.onClick.Invoke(); Assert.IsFalse(memory.IsOpen);
            app.SelectDesktopMode(true); Assert.AreEqual(0, app.History.Turns.Count);
            Assert.AreEqual(0, app.GetMemoryEntries().Count);
            app.SelectDesktopMode(false); Assert.AreEqual(1, app.History.Turns.Count);
            Assert.AreEqual(1, app.GetMemoryEntries().Count);
        }

        [UnityTest] public IEnumerator SessionsPanelCreatesSwitchesAndDeletesPersistedConversations()
        {
            var first = app.ActiveSessionId;
            app.DesktopSimulation.Memory = "用户喜欢古典音乐";
            var initial = app.SubmitDesktopTextAsync("第一个会话的话题");
            yield return Until(() => initial.IsCompleted);
            Assert.IsFalse(initial.IsFaulted);
            Assert.AreEqual(1, app.DesktopDialogue.RecentTurnCount);

            panel.MessageInput.text = "旧会话的草稿";
            panel.SessionsButton.onClick.Invoke();
            var sessions = panel.SessionsPanel;
            Assert.IsTrue(sessions.IsOpen);
            sessions.NewButton.onClick.Invoke();
            yield return null;
            var second = app.ActiveSessionId;
            Assert.AreNotEqual(first, second);
            Assert.IsFalse(sessions.IsOpen);
            Assert.IsEmpty(panel.MessageInput.text);
            Assert.IsEmpty(app.History.Turns);
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            Assert.AreEqual(1, app.GetMemoryEntries().Count);

            var next = app.SubmitDesktopTextAsync("第二个会话的话题");
            yield return Until(() => next.IsCompleted);
            Assert.IsFalse(next.IsFaulted);
            panel.SessionsButton.onClick.Invoke();
            yield return Capture("sessions-portrait", 720, 1280);
            yield return Capture("sessions-landscape", 1280, 720);
            FindSessionRow(sessions, first).onClick.Invoke();
            Assert.IsTrue(sessions.OpenButton.interactable);
            sessions.OpenButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(first, app.ActiveSessionId);
            Assert.AreEqual("第一个会话的话题", app.History.Latest.UserText);
            StringAssert.DoesNotContain("第二个会话的话题", app.DesktopDialogue.RecentContext);

            panel.SessionsButton.onClick.Invoke();
            FindSessionRow(sessions, second).onClick.Invoke();
            sessions.DeleteButton.onClick.Invoke();
            Assert.AreEqual(2, app.GetSessions().Count);
            sessions.DeleteButton.onClick.Invoke();
            Assert.AreEqual(1, app.GetSessions().Count);
            var restored = new ConversationSessionStore(Path.Combine(app.DesktopMemoryDirectory, "simulation-conversations.json"));
            Assert.AreEqual(first, restored.ActiveId);
            Assert.AreEqual(1, restored.GetSessions().Count);
            Assert.AreEqual("第一个会话的话题", restored.History.Latest.UserText);
            Assert.AreEqual(1, app.GetMemoryEntries().Count);
        }

        private static Button FindSessionRow(ConversationSessionsPanel panel, string id) =>
            Array.Find(panel.GetComponentsInChildren<Button>(), button => button.name == "Session " + id);

        [UnityTest] public IEnumerator TapSpeechFillsAnEditableDraftAndWaitsForSend()
        {
            var recognition = new TaskCompletionSource<SpeechRecognitionTurn>();
            SetRecognizer((_, token, ready) => { ready(); return recognition.Task; });
            panel.InputModeButton.onClick.Invoke();
            yield return Capture("input-mode-portrait", 720, 1280);
            SelectInputMode("点按说话");
            yield return null;
            Assert.AreEqual(SpeechInputMode.TapToSpeak, app.InputMode);
            Assert.IsTrue(panel.DictationButton.gameObject.activeSelf);
            yield return Capture("tap-to-speak-landscape", 1280, 720);
            panel.DictationButton.onClick.Invoke();
            yield return Until(() => app.ListeningPhase == SpeechListeningPhase.Ready);
            Assert.IsTrue(app.IsDictating);
            Assert.IsFalse(app.CanSubmitText);
            recognition.SetResult(new SpeechRecognitionTurn("语音转成的草稿", null));
            yield return Until(() => panel.MessageInput.text == "语音转成的草稿");
            Assert.IsEmpty(app.History.Turns);
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            panel.MessageInput.text = "修改后发送的草稿";
            panel.SendButton.onClick.Invoke();
            yield return Until(() => !app.IsBusy);
            Assert.AreEqual("修改后发送的草稿", app.History.Latest.UserText);
            Assert.AreEqual(ConversationTurnStatus.Completed, app.History.Latest.Status);
        }

        [UnityTest] public IEnumerator SwitchingFromTapToTextDiscardsLateSpeechAndKeepsTheDraft()
        {
            var recognition = new TaskCompletionSource<SpeechRecognitionTurn>();
            var calls = 0;
            CancellationToken activeCapture = default;
            SetRecognizer((_, token, ready) =>
            {
                calls++;
                activeCapture = token;
                ready();
                return calls == 1 ? recognition.Task : Task.FromResult(new SpeechRecognitionTurn("自动识别发送", null));
            });
            app.SetInputMode(SpeechInputMode.TapToSpeak);
            var config = (AppConfig)typeof(DigitalHumanAppController).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
            config.enableAutoConversation = true;
            config.deepSeekApiKey = "local-test-only";
            panel.MessageInput.text = "保留的文字草稿";
            panel.DictationButton.onClick.Invoke();
            yield return Until(() => app.IsDictating && calls == 1);
            panel.InputModeButton.onClick.Invoke();
            SelectInputMode("仅文字");
            Assert.IsTrue(activeCapture.IsCancellationRequested);
            recognition.SetResult(new SpeechRecognitionTurn("已经取消的识别结果", null));
            yield return Until(() => !app.IsDictating);
            yield return null;
            Assert.AreEqual(SpeechInputMode.TextOnly, app.InputMode);
            Assert.AreEqual(1, calls);
            Assert.IsFalse(panel.DictationButton.gameObject.activeSelf);
            Assert.AreEqual("保留的文字草稿", panel.MessageInput.text);
            Assert.IsEmpty(app.History.Turns);

            panel.InputModeButton.onClick.Invoke();
            SelectInputMode("自动识别");
            yield return Until(() => app.History.Latest?.Status == ConversationTurnStatus.Completed && !app.IsBusy);
            Assert.AreEqual("自动识别发送", app.History.Latest.UserText);
            app.SetInputMode(SpeechInputMode.TextOnly);
        }

        private void SetRecognizer(Func<Action<string>, CancellationToken, Action, Task<SpeechRecognitionTurn>> fake)
        {
            typeof(DigitalHumanAppController).GetField("recognizeSpeech", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, fake);
        }

        private void SelectInputMode(string name)
        {
            var overlay = panel.InputModeButton.transform.parent.parent.Find("Input Mode Overlay");
            var choice = Array.Find(overlay.GetComponentsInChildren<Button>(), button => button.name == name);
            Assert.IsNotNull(choice);
            choice.onClick.Invoke();
        }
        [UnityTest] public IEnumerator LongHistoryScrollsAndPortraitLandscapeAndMemoryLayoutsRender()
        {
            app.DesktopSimulation.Reply = "可以先整理今天的计划，把最重要的一件事放在前面。也给自己留一点休息时间，完成一小步后再继续。";
            app.DesktopSimulation.Memory = "用户喜欢古典音乐；用户希望回复简洁自然";
            for (var i = 0; i < 10; i++)
            {
                var operation = app.SubmitDesktopTextAsync("这是第 " + (i + 1) + " 轮对话，我想安排接下来的学习与休息时间。");
                yield return Until(() => operation.IsCompleted); Assert.IsFalse(operation.IsFaulted);
            }
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.Greater(panel.HistoryScroll.content.rect.height, panel.HistoryScroll.viewport.rect.height);
            panel.HistoryScroll.verticalNormalizedPosition = 1;
            var firstPosition = panel.HistoryScroll.content.anchoredPosition.y;
            panel.HistoryScroll.verticalNormalizedPosition = 0;
            Assert.Greater(panel.HistoryScroll.content.anchoredPosition.y, firstPosition);
            yield return Capture("chat-portrait", 720, 1280);
            yield return Capture("chat-landscape", 1280, 720);
            panel.MemoryButton.onClick.Invoke();
            panel.MemoryPanel.SelectEntry(app.GetMemoryEntries()[0].Id);
            panel.MemoryPanel.EditInput.text = "用户喜欢古典音乐，希望回复简洁、自然。";
            yield return Capture("memory-portrait", 720, 1280);
            yield return Capture("memory-landscape", 1280, 720);
            panel.MemoryPanel.CloseButton.onClick.Invoke();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var config = (AppConfig)typeof(DigitalHumanAppController).GetField("config", flags).GetValue(app);
            config.deepSeekApiKey = "local-test-only"; config.enableAutoConversation = true;
            typeof(DigitalHumanAppController).GetMethod("ReportListeningError", flags).Invoke(app,
                new object[] { new AppException(AppErr.micPerm, "麦克风权限已被禁止，请前往系统设置开启。") });
            yield return null;
            Assert.IsTrue(panel.RetryListeningButton.gameObject.activeSelf);
            yield return Capture("speech-retry-portrait", 720, 1280);
            yield return Capture("speech-retry-landscape", 1280, 720);
            config.enableAutoConversation = false; app.RefreshConversationSettings();
        }
        private IEnumerator Capture(string name, int width, int height)
        {
            var directory = Environment.GetEnvironmentVariable("SRTP_CONVERSATION_PREVIEW");
            if (string.IsNullOrWhiteSpace(directory)) yield break;
            Directory.CreateDirectory(directory);
            Camera camera = null;
            foreach (var candidate in Object.FindObjectsByType<Camera>())
                if (candidate.gameObject.scene == scene) { camera = candidate; break; }
            Assert.IsNotNull(camera);
            var target = new RenderTexture(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var canvases = Object.FindObjectsByType<Canvas>();
            var modes = new RenderMode[canvases.Length]; var cameras = new Camera[canvases.Length];
            var distances = new float[canvases.Length];
            for (var i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode; cameras[i] = canvases[i].worldCamera; distances[i] = canvases[i].planeDistance;
                if (canvases[i].gameObject.scene != scene || !canvases[i].isRootCanvas) continue;
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = camera;
                canvases[i].planeDistance = camera.nearClipPlane + .05f;
            }
            try
            {
                camera.targetTexture = target;
                yield return null;
                panel.RefreshLayout(); Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                RenderOverlay(camera, canvases);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                Assert.Greater(panel.HistoryScroll.viewport.rect.height, 80, name);
                Assert.Greater(panel.MessageInput.GetComponent<RectTransform>().rect.width, 100, name);
            }
            finally
            {
                for (var i = 0; i < canvases.Length; i++)
                { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i]; }
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                Object.Destroy(image); Object.Destroy(target); panel.RefreshLayout();
            }
        }
        private void RenderOverlay(Camera camera, Canvas[] canvases)
        {
            // Composite the UI after the scene, matching ScreenSpaceOverlay even when
            // VRM eye materials use a render queue later than camera-space UI.
            var renderers = Object.FindObjectsByType<Renderer>();
            var rendererEnabled = new bool[renderers.Length];
            var canvasEnabled = new bool[canvases.Length];
            var flags = camera.clearFlags;
            for (var i = 0; i < renderers.Length; i++) rendererEnabled[i] = renderers[i].enabled;
            for (var i = 0; i < canvases.Length; i++) canvasEnabled[i] = canvases[i].enabled;
            try
            {
                for (var i = 0; i < canvases.Length; i++)
                    if (canvases[i].gameObject.scene == scene) canvases[i].enabled = false;
                camera.Render();
                for (var i = 0; i < canvases.Length; i++) canvases[i].enabled = canvasEnabled[i];
                for (var i = 0; i < renderers.Length; i++)
                    if (renderers[i].gameObject.scene == scene) renderers[i].enabled = false;
                camera.clearFlags = CameraClearFlags.Depth;
                Canvas.ForceUpdateCanvases(); camera.Render();
            }
            finally
            {
                camera.clearFlags = flags;
                for (var i = 0; i < renderers.Length; i++) renderers[i].enabled = rendererEnabled[i];
                for (var i = 0; i < canvases.Length; i++) canvases[i].enabled = canvasEnabled[i];
            }
        }
        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 20f;
            while (!condition()) { Assert.Less(Time.realtimeSinceStartup, deadline, "Timed out waiting for UI operation."); yield return null; }
        }
    }
}
#endif
