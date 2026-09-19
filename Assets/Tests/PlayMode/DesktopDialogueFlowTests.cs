#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SrtpAvatar.Tests
{
    public sealed class DesktopDialogueFlowTests
    {
        private Scene scene;
        private DigitalHumanAppController app;
        private AudioClip preview;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/DesktopAvatarDemo.unity", LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath("Assets/Scenes/DesktopAvatarDemo.unity");
            app = Object.FindAnyObjectByType<DigitalHumanAppController>();
            Assert.IsNotNull(app);
            yield return null;
            app.DesktopSimulation.ChunkDelayMilliseconds = 1;
            app.DesktopSecondsPerCharacter = .001f;
            app.DesktopSimulation.Reply = "你好。我们继续聊吧。";
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (app != null)
            {
                app.CancelCurrentConversation();
                yield return Until(() => !app.IsBusy);
            }
            if (preview != null) Object.Destroy(preview);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FirstSentenceAppearsBeforeCompletionThenMetadataAndMemoryCommit()
        {
            app.DesktopSimulation.ChunkDelayMilliseconds = 35;
            app.DesktopSimulation.Reply = "你好。让我们慢慢聊聊今天发生的事情，以及接下来想一起完成的计划。";
            app.DesktopSimulation.Emotion = "joy";
            app.DesktopSimulation.Gesture = "explain";
            app.DesktopSimulation.Memory = "用户喜欢音乐";
            var turn = app.SubmitDesktopTextAsync("请记住我喜欢音乐");
            yield return Until(() => app.CurrentSubtitle.Contains("你好。"));
            Assert.IsTrue(app.IsBusy);
            Assert.IsFalse(turn.IsCompleted);
            // Text can arrive while TTS is preparing; speaking begins at playback.
            yield return Until(() => app.CurrentState == AvatarState.speaking);
            Assert.IsNull(app.LastDecision);
            Assert.AreEqual(0, app.DesktopDialogue.LongTermMemoryCount);
            CapturePreviewIfRequested();
            yield return Complete(turn);
            Assert.AreEqual(AppErr.none, app.LastConversationError);
            Assert.AreEqual("joy", app.LastDecision.emotion);
            Assert.AreEqual("explain", app.LastDecision.gesture);
            Assert.AreEqual(1, app.DesktopDialogue.RecentTurnCount);
            Assert.AreEqual(1, app.DesktopDialogue.LongTermMemoryCount);
            Assert.AreEqual(AvatarState.idle, app.CurrentState);
            StringAssert.Contains(app.LastDecision.reply, app.CurrentSubtitle);
            StringAssert.Contains("Desktop preview", app.LastTiming);
        }

        [UnityTest]
        public IEnumerator MultipleTurnsCarryContextAndClearScopesStayIsolatedFromLiveMode()
        {
            app.DesktopSimulation.Memory = "用户喜欢音乐";
            yield return Complete(app.SubmitDesktopTextAsync("第一轮：我喜欢音乐"));
            app.DesktopSimulation.Memory = "";
            yield return Complete(app.SubmitDesktopTextAsync("第二轮：你还记得吗"));
            StringAssert.Contains("第一轮", app.DesktopSimulation.LastRecentContext);
            StringAssert.Contains("用户喜欢音乐", app.DesktopSimulation.LastLongTermContext);
            yield return Complete(app.SubmitDesktopTextAsync("不要清除记忆"));
            Assert.AreEqual(3, app.DesktopDialogue.RecentTurnCount);
            Assert.AreEqual(1, app.DesktopDialogue.LongTermMemoryCount);
            yield return Complete(app.SubmitDesktopTextAsync("忘记刚才"));
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            Assert.AreEqual(1, app.DesktopDialogue.LongTermMemoryCount);
            app.SelectDesktopMode(true);
            Assert.AreEqual(0, app.DesktopDialogue.LongTermMemoryCount);
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            app.SelectDesktopMode(false);
            Assert.AreEqual(1, app.DesktopDialogue.LongTermMemoryCount);
            yield return Complete(app.SubmitDesktopTextAsync("清除记忆"));
            Assert.AreEqual(0, app.DesktopDialogue.LongTermMemoryCount);
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            for (var i = 0; i < 8; i++) yield return Complete(app.SubmitDesktopTextAsync("连续测试第" + i + "轮"));
            Assert.AreEqual(6, app.DesktopDialogue.RecentTurnCount);
            StringAssert.Contains("连续测试第6轮", app.DesktopSimulation.LastRecentContext);
        }

        [UnityTest]
        public IEnumerator CancelAfterFirstSentenceStopsPlaybackAndNextTurnSucceeds()
        {
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Slow;
            app.DesktopSimulation.SlowChunkDelayMilliseconds = 80;
            app.DesktopSimulation.Reply = "你好。接下来这是一段较长的文字，用来验证取消以后不会继续显示或者提交本轮记忆。";
            app.DesktopSimulation.Memory = "用户喜欢音乐";
            app.DesktopSecondsPerCharacter = 1f;
            var turn = app.SubmitDesktopTextAsync("开始慢速回复");
            yield return Until(() => app.CurrentState == AvatarState.speaking);
            Assert.Throws<InvalidOperationException>(() => app.SubmitDesktopTextAsync("重复提交"));
            app.CancelCurrentConversation();
            yield return Complete(turn);
            Assert.AreEqual(AppErr.canceled, app.LastConversationError);
            Assert.AreEqual(AvatarState.idle, app.CurrentState);
            Assert.IsFalse(app.GetComponent<AudioSource>().isPlaying);
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            Assert.AreEqual(0, app.DesktopDialogue.LongTermMemoryCount);
            var subtitle = app.CurrentSubtitle;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.AreEqual(subtitle, app.CurrentSubtitle);
            ResetSimulation();
            yield return Complete(app.SubmitDesktopTextAsync("重新开始正常对话"));
            Assert.AreEqual(AppErr.none, app.LastConversationError);
            Assert.AreEqual(1, app.DesktopDialogue.RecentTurnCount);
        }

        [UnityTest]
        public IEnumerator AuthenticationTimeoutBrokenStreamAndInvalidJsonRecoverWithoutCommittingFailedTurns()
        {
            var scenarios = new[] { SimulatedDialogueScenario.AuthenticationFailure, SimulatedDialogueScenario.Timeout,
                SimulatedDialogueScenario.Interrupted, SimulatedDialogueScenario.InvalidJson };
            var errors = new[] { AppErr.auth, AppErr.network, AppErr.network, AppErr.badJson };
            for (var i = 0; i < scenarios.Length; i++)
            {
                app.DesktopSimulation.Scenario = scenarios[i];
                app.DesktopSimulation.Memory = "用户喜欢音乐";
                var previous = app.DesktopDialogue.RecentTurnCount;
                LogAssert.Expect(LogType.Exception, new Regex("AppException:"));
                yield return Complete(app.SubmitDesktopTextAsync("测试异常回复"));
                Assert.AreEqual(errors[i], app.LastConversationError, scenarios[i].ToString());
                Assert.IsNull(app.LastDecision);
                Assert.AreEqual(previous, app.DesktopDialogue.RecentTurnCount);
                Assert.AreEqual(0, app.DesktopDialogue.LongTermMemoryCount);
                Assert.AreEqual(AvatarState.idle, app.CurrentState);
                ResetSimulation();
                yield return Complete(app.SubmitDesktopTextAsync("故障后的正常回复"));
                Assert.AreEqual(AppErr.none, app.LastConversationError);
                Assert.AreEqual(previous + 1, app.DesktopDialogue.RecentTurnCount);
            }
        }

        [UnityTest]
        public IEnumerator PauseAndDisableCancelPendingTurnsWithoutBlockingResume()
        {
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Slow;
            app.DesktopSimulation.SlowChunkDelayMilliseconds = 150;
            var paused = app.SubmitDesktopTextAsync("暂停之前");
            app.SendMessage("OnApplicationPause", true);
            app.SendMessage("OnApplicationPause", false);
            yield return Complete(paused);
            Assert.AreEqual(AppErr.canceled, app.LastConversationError);
            Assert.IsFalse(app.IsBusy);
            var disabled = app.SubmitDesktopTextAsync("停用之前");
            app.enabled = false;
            yield return Complete(disabled);
            app.enabled = true;
            yield return null;
            ResetSimulation();
            yield return Complete(app.SubmitDesktopTextAsync("恢复之后"));
            Assert.AreEqual(AppErr.none, app.LastConversationError);
            Assert.AreEqual(1, app.DesktopDialogue.RecentTurnCount);
        }

        [UnityTest]
        public IEnumerator ReferenceAudioIsReleasedOnCancelButAssetRemainsAvailable()
        {
            preview = AudioFallback.CreatePromptTone(3f);
            app.DesktopPreviewClip = preview;
            var turn = app.SubmitDesktopTextAsync("播放参考音频");
            yield return Until(() => app.GetComponent<AudioSource>().isPlaying);
            app.CancelCurrentConversation();
            yield return Complete(turn);
            Assert.IsFalse(app.GetComponent<AudioSource>().isPlaying);
            Assert.IsNull(app.GetComponent<AudioSource>().clip);
            Assert.IsTrue(preview != null, "Caller-owned reference audio must not be destroyed.");
            app.DesktopPreviewClip = null;
            yield return Complete(app.SubmitDesktopTextAsync("恢复静音预览"));
            Assert.AreEqual(AppErr.none, app.LastConversationError);
        }

        [UnityTest]
        public IEnumerator PlaybackFailureKeepsFullReplyAndAllowsNextTurn()
        {
            app.DesktopSimulatePlaybackFailure = true;
            yield return Complete(app.SubmitDesktopTextAsync("模拟播放错误"));
            Assert.AreEqual(AppErr.none, app.LastConversationError);
            Assert.IsTrue(app.LastPlaybackFailed);
            StringAssert.Contains(app.DesktopSimulation.Reply, app.CurrentSubtitle);
            Assert.AreEqual(AvatarState.idle, app.CurrentState);
            app.DesktopSimulatePlaybackFailure = false;
            yield return Complete(app.SubmitDesktopTextAsync("再次对话"));
            Assert.IsFalse(app.LastPlaybackFailed);
            Assert.AreEqual(2, app.DesktopDialogue.RecentTurnCount);
        }

        [UnityTest]
        public IEnumerator UnloadingSceneCancelsTheTurnAndReleasesOwnership()
        {
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Slow;
            app.DesktopSimulation.SlowChunkDelayMilliseconds = 100;
            var turn = app.SubmitDesktopTextAsync("退出场景前的未完成回复");
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return Complete(turn);
            Assert.IsTrue(app == null);
        }

        [UnityTest]
        public IEnumerator WorkbenchWindowOpensDuringPlayAndSurvivesControllerTeardown()
        {
            Type windowType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                windowType = windowType ?? assembly.GetType("SrtpAvatarEditor.DialogueWorkbenchWindow");
            Assert.IsNotNull(windowType);
            var window = EditorWindow.GetWindow(windowType);
            try
            {
                window.Show();
                window.Repaint();
                yield return new WaitForSecondsRealtime(.3f);
                yield return Complete(app.SubmitDesktopTextAsync("窗口打开时的完整对话"));
                window.Repaint();
                yield return SceneManager.UnloadSceneAsync(scene);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.IsTrue(window != null);
            }
            finally { window.Close(); }
        }

        private void CapturePreviewIfRequested()
        {
            var directory = Environment.GetEnvironmentVariable("SRTP_DESKTOP_PREVIEW");
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            Camera camera = null;
            foreach (var candidate in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == scene) { camera = candidate; break; }
            Assert.IsNotNull(camera);
            var target = new RenderTexture(720, 1280, 24);
            var image = new Texture2D(720, 1280, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvases.Length];
            var cameras = new Camera[canvases.Length];
            for (var i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;
                cameras[i] = canvases[i].worldCamera;
                if (canvases[i].gameObject.scene == scene && canvases[i].isRootCanvas)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                }
            }
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(directory, "desktop-dialogue-game.png"), image.EncodeToPNG());
            }
            finally
            {
                for (var i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = modes[i];
                    canvases[i].worldCamera = cameras[i];
                }
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.Destroy(image);
                Object.Destroy(target);
            }
        }

        private void ResetSimulation()
        {
            app.DesktopSimulation.Scenario = SimulatedDialogueScenario.Normal;
            app.DesktopSimulation.Reply = "你好。我们继续聊吧。";
            app.DesktopSimulation.Memory = "";
            app.DesktopSecondsPerCharacter = .001f;
        }
        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 15f;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Timed out waiting for the dialogue state.");
                yield return null;
            }
        }
        private static IEnumerator Complete(Task task)
        {
            yield return Until(() => task.IsCompleted);
            if (task.IsFaulted) throw task.Exception;
            Assert.IsFalse(task.IsCanceled);
        }
    }
}
#endif
