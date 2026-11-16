#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
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
    public sealed class ConversationRefreshTests
    {
        private Scene scene;
        private DigitalHumanAppController app;
        private ConversationPanel panel;
        private static readonly MethodInfo Tick = typeof(ConversationPanel).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/DesktopAvatarDemo.unity", LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath("Assets/Scenes/DesktopAvatarDemo.unity");
            app = Object.FindAnyObjectByType<DigitalHumanAppController>();
            panel = app.GetComponent<ConversationPanel>();
            yield return null;
            app.SelectDesktopMode(false);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (app != null) app.CancelCurrentConversation();
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
        [UnityTest] public IEnumerator TypingDoesNotRebuildFortyTurnHistory()
        {
            for (var i = 0; i < 40; i++)
            {
                var turn = app.History.Begin("第 " + i + " 轮提问：安排今天的学习和休息时间。");
                app.History.UpdateReply(turn, "先整理计划，把最重要的一件事放在前面。完成一小步后休息，再继续下一项。保持适合自己的节奏。");
                app.History.Finish(turn, AppErr.none, "", true);
            }
            app.SendMessage("SetHint", "history ready");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var probe = panel.HistoryScroll.content.gameObject.AddComponent<HistoryLayoutProbe>();
            for (var i = 0; i < 10; i++) { panel.MessageInput.text = "预热 " + i; Tick.Invoke(panel, null); }
            probe.Passes = 0;
            const int count = 100;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < count; i++) { panel.MessageInput.text = "继续输入 " + i; Tick.Invoke(panel, null); }
            watch.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var directory = Environment.GetEnvironmentVariable("SRTP_REFRESH_METRICS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                var data = new RefreshMetrics { edits = count, layoutPasses = probe.Passes, milliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocated };
                File.WriteAllText(Path.Combine(directory, "typing-metrics.json"), JsonUtility.ToJson(data, true));
            }
            Assert.IsTrue(panel.SendButton.interactable);
            Assert.AreEqual(0, probe.Passes, "Typing should refresh the composer without laying out unchanged history.");
        }
        [UnityTest] public IEnumerator StateOnlyChangesDoNotRebuildHistoryOrMoveScroll()
        {
            SeedHistory(); yield return null; Canvas.ForceUpdateCanvases();
            panel.HistoryScroll.verticalNormalizedPosition = .65f;
            var position = panel.HistoryScroll.content.anchoredPosition;
            var probe = panel.HistoryScroll.content.gameObject.AddComponent<HistoryLayoutProbe>();
            for (var i = 0; i < 20; i++)
            {
                app.SendMessage("SetHint", "state update " + i);
                Tick.Invoke(panel, null);
            }
            Assert.AreEqual(0, probe.Passes);
            Assert.Less(Vector2.Distance(position, panel.HistoryScroll.content.anchoredPosition), .1f);
        }
        [UnityTest] public IEnumerator ReplyUpdatesOnlyItsRowAndKeepsTheReadersPosition()
        {
            SeedHistory(); yield return null; Canvas.ForceUpdateCanvases();
            panel.HistoryScroll.verticalNormalizedPosition = .65f;
            var position = panel.HistoryScroll.content.anchoredPosition;
            var first = panel.HistoryScroll.content.Find("Turn 1/Reply").GetComponent<Text>();
            var latest = panel.HistoryScroll.content.Find("Turn 40/Reply").GetComponent<Text>();
            var firstDirty = 0; var latestDirty = 0;
            first.RegisterDirtyLayoutCallback(() => firstDirty++);
            latest.RegisterDirtyLayoutCallback(() => latestDirty++);
            app.History.UpdateReply(app.History.Latest, "这是最新一条更新后的较长回复。只修改这一条，其他消息继续保持原状。" );
            Tick.Invoke(panel, null);
            Assert.AreEqual(0, firstDirty); Assert.Greater(latestDirty, 0);
            StringAssert.Contains("更新后的", latest.text);
            Assert.Less(Vector2.Distance(position, panel.HistoryScroll.content.anchoredPosition), .1f);
            app.History.Finish(app.History.Latest, AppErr.network, "网络中断", false);
            Tick.Invoke(panel, null);
            Assert.IsTrue(panel.HistoryScroll.content.Find("Turn 40/Turn Status").gameObject.activeSelf);
            StringAssert.Contains("网络中断", panel.HistoryScroll.content.Find("Turn 40/Turn Status").GetComponent<Text>().text);
            var probe = panel.HistoryScroll.content.gameObject.AddComponent<HistoryLayoutProbe>();
            app.History.UpdateReply(app.History.Latest, app.History.Latest.Reply);
            app.SendMessage("SetHint", "unchanged reply"); Tick.Invoke(panel, null);
            Assert.AreEqual(0, probe.Passes);
        }
        [UnityTest] public IEnumerator PermissionPauseSurvivesFocusAndTypedConversationUntilManualRetry()
        {
            var config = EnableListening();
            const string message = "麦克风权限请求超时，请重试。";
            Invoke("ReportListeningError", new AppException(AppErr.micPerm, message));
            app.SetTextInputFocused(true); app.SetTextInputFocused(false);
            app.SendMessage("OnApplicationPause", true); app.SendMessage("OnApplicationPause", false);
            yield return new WaitForSecondsRealtime(.35f);
            Assert.AreEqual(message, app.ListeningErrorMessage);
            Assert.IsTrue((bool)Field("listeningNeedsManualRetry"));
            Assert.IsTrue(panel.RetryListeningButton.gameObject.activeSelf);
            app.DesktopSimulation.ChunkDelayMilliseconds = 1;
            app.DesktopSecondsPerCharacter = .001f;
            app.DesktopSimulation.Reply = "好。";
            var conversation = app.SubmitDesktopTextAsync("继续文字对话");
            while (!conversation.IsCompleted) yield return null;
            Assert.IsFalse(conversation.IsFaulted);
            yield return null;
            Assert.AreEqual(message, app.ListeningErrorMessage, "A typed turn must not erase unresolved permission recovery.");
            Assert.IsTrue(panel.RetryListeningButton.gameObject.activeSelf);
            panel.RetryListeningButton.onClick.Invoke(); panel.RetryListeningButton.onClick.Invoke();
            Assert.AreEqual(SpeechListeningPhase.Preparing, app.ListeningPhase);
            var deadline = Time.realtimeSinceStartup + 5;
            while (app.ListeningPhase != SpeechListeningPhase.Failed)
            { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            Assert.IsFalse((bool)Field("listeningNeedsManualRetry"));
            StringAssert.Contains("没有可用的系统语音识别服务", app.ListeningErrorMessage);
            Assert.AreEqual(1, app.History.Turns.Count);
            config.enableAutoConversation = false; app.RefreshConversationSettings();
        }
        [UnityTest] public IEnumerator ServiceFailuresBackOffAndNormalSilenceRestoresBaseDelay()
        {
            EnableListening();
            var property = typeof(DigitalHumanAppController).GetProperty("ListeningRetryDelay", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var i = 1; i <= 8; i++)
            {
                Invoke("ReportListeningError", new AppException(AppErr.speech, "系统语音识别超时。"));
                Assert.AreEqual(AutoConversationPolicy.RetryDelay(.25f, i), (float)property.GetValue(app), .001f);
                Assert.IsFalse((bool)Field("listeningNeedsManualRetry"));
            }
            Invoke("BeginListeningPresentation"); Invoke("OnRecognitionReady", CancellationToken.None);
            Invoke("ReportListeningError", new AppException(AppErr.speech, "没有听到说话。"));
            Assert.AreEqual(.25f, (float)property.GetValue(app), .001f);
            Assert.AreEqual("", app.ListeningErrorMessage);
            yield return null;
            Assert.IsFalse(panel.RetryListeningButton.gameObject.activeSelf);
        }
        [UnityTest] public IEnumerator NativePermissionCallbackKeepsItsErrorTypeAndIgnoresStaleSessions()
        {
            var owner = new GameObject("Permission callback regression");
            var bridge = owner.AddComponent<AndroidNativeSpeechBridge>();
            var completion = new TaskCompletionSource<SpeechRecognitionTurn>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(AndroidNativeSpeechBridge).GetField("recognitionId", flags).SetValue(bridge, "active");
            typeof(AndroidNativeSpeechBridge).GetField("recognitionCompletion", flags).SetValue(bridge, completion);
            bridge.OnAndroidSpeechPermissionError("stale\n没有麦克风权限。");
            Assert.IsFalse(completion.Task.IsCompleted);
            bridge.OnAndroidSpeechPermissionError("active\n没有麦克风权限。");
            Assert.IsTrue(completion.Task.IsFaulted);
            var error = completion.Task.Exception.InnerException as AppException;
            Assert.IsNotNull(error); Assert.AreEqual(AppErr.micPerm, error.Error);
            Assert.IsNull(typeof(AndroidNativeSpeechBridge).GetField("recognitionId", flags).GetValue(bridge));
            Object.Destroy(owner);
            yield return null;
        }
        [UnityTest] public IEnumerator NativeSpeechTimingStaysWithItsRecognitionSessionAndUtterance()
        {
            var owner = new GameObject("Speech timing callback regression");
            try
            {
                var bridge = owner.AddComponent<AndroidNativeSpeechBridge>();
                var completion = new TaskCompletionSource<SpeechRecognitionTurn>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(AndroidNativeSpeechBridge).GetField("recognitionId", flags).SetValue(bridge, "current");
                typeof(AndroidNativeSpeechBridge).GetField("recognitionCompletion", flags).SetValue(bridge, completion);
                bridge.OnAndroidSpeechResult("previous\nvosk\t900\t1200\n旧内容");
                Assert.IsFalse(completion.Task.IsCompleted);
                bridge.OnAndroidSpeechAudio("current\nAQID");
                bridge.OnAndroidSpeechResult("current\nvosk\t1300\t1800\n第一句\n第二句");
                Assert.IsTrue(completion.Task.IsCompleted);
                var turn = completion.Task.Result;
                Assert.AreEqual("第一句\n第二句", turn.Text);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, turn.AudioWav);
                Assert.AreEqual(SpeechRecognitionBackend.Vosk, turn.Backend);
                Assert.AreEqual(1300L, turn.SpeechEndedAtMs);
                Assert.AreEqual(1800L, turn.RecognitionCompletedAtMs);

                typeof(AndroidNativeSpeechBridge).GetField("utteranceId", flags).SetValue(bridge, "utterance");
                long? ttsStartedAt = null;
                bridge.TtsStarted += timestamp => ttsStartedAt = timestamp;
                bridge.OnAndroidTtsStarted("stale\n1900");
                Assert.IsNull(ttsStartedAt);
                bridge.OnAndroidTtsStarted("utterance\n2100");
                Assert.AreEqual(2100L, ttsStartedAt);
            }
            finally { Object.Destroy(owner); }
            yield return null;
        }
        private AppConfig EnableListening()
        {
            var config = (AppConfig)Field("config");
            config.deepSeekApiKey = "local-test-only"; config.enableAutoConversation = true;
            config.preferAndroidNativeSpeech = true; config.autoConversationRetryDelaySeconds = .25f;
            return config;
        }
        private object Field(string name) => typeof(DigitalHumanAppController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
        private void Invoke(string name, params object[] args) => typeof(DigitalHumanAppController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, args);
        private void SeedHistory()
        {
            for (var i = 0; i < 40; i++)
            {
                var turn = app.History.Begin("第 " + i + " 轮历史");
                app.History.UpdateReply(turn, "这是一条用于确认长列表滚动位置的回复。阅读旧消息时，新回复不应该将画面强行滚到底部。" );
                app.History.Finish(turn, AppErr.none, "", true);
            }
            app.SendMessage("SetHint", "history ready");
        }
        [Serializable] private sealed class RefreshMetrics { public int edits, layoutPasses; public double milliseconds; public long allocatedBytes; }
    }
    public sealed class HistoryLayoutProbe : MonoBehaviour, ILayoutController
    {
        public int Passes;
        public void SetLayoutHorizontal() => Passes++;
        public void SetLayoutVertical() => Passes++;
    }
}
#endif
