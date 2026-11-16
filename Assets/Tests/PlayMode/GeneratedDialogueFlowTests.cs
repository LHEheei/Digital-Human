#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
    public sealed class GeneratedDialogueFlowTests
    {
        private Scene scene;
        private DigitalHumanAppController app;
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/DesktopAvatarDemo.unity", LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath("Assets/Scenes/DesktopAvatarDemo.unity");
            app = Object.FindAnyObjectByType<DigitalHumanAppController>();
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (app != null) { app.CancelCurrentConversation(); yield return Until(() => !app.IsBusy); }
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
        [UnityTest] public IEnumerator SceneDefaultsToRealModelWithNoSimulatedGreeting()
        {
            Assert.IsTrue(app.DesktopLiveMode);
            Assert.IsEmpty(app.DesktopSimulation.Reply);
            Assert.IsEmpty(app.History.Turns);
            Assert.IsNull(app.LastDecision);
            yield return null;
        }
        [UnityTest] public IEnumerator MissingConfigurationShowsGuidanceAndNeverFallsBackToSimulation()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var config = (AppConfig)typeof(DigitalHumanAppController).GetField("config", flags).GetValue(app);
            var client = new AvailabilityClient();
            var dialogue = new DialogueService(config, client,
                new LongTermMemoryStore(Path.Combine(app.DesktopMemoryDirectory, "unavailable-test.json")));
            typeof(DigitalHumanAppController).GetField("liveDesktopDialogue", flags).SetValue(app, dialogue);
            app.DesktopSimulation.Reply = "这句仅用于检测误入模拟分支。";
            app.SelectDesktopMode(true);
            yield return null;
            Assert.IsTrue(app.NeedsDialogueConfiguration);
            Assert.AreEqual("等待 API 配置", UiText("State"));
            StringAssert.Contains("配置模型", UiText("Chat Dock/Feedback"));
            LogAssert.Expect(LogType.Exception, new Regex("AppException:"));
            yield return Complete(app.SubmitDesktopTextAsync("我今天想讨论一件事"));
            Assert.IsTrue(app.DesktopLiveMode);
            Assert.AreEqual(AppErr.aiUnavailable, app.LastConversationError);
            Assert.IsNull(app.LastDecision);
            Assert.IsEmpty(app.History.Latest.Reply);
            Assert.AreEqual(ConversationTurnStatus.Failed, app.History.Latest.Status);
            Assert.AreEqual(0, client.Calls);
            // Model availability changes refresh the configuration hint without a history rebuild.
            client.IsAvailable = true;
            app.RefreshConversationSettings();
            yield return null;
            Assert.AreEqual("等待输入", UiText("State"));
            StringAssert.DoesNotContain("配置模型", UiText("Chat Dock/Feedback"));
        }
        [UnityTest] public IEnumerator OfflineTestRequiresSuppliedTextAndRetryUsesOnlyThatText()
        {
            app.SelectDesktopMode(false);
            app.DesktopSimulation.ChunkDelayMilliseconds = 1;
            app.DesktopSecondsPerCharacter = .001f;
            LogAssert.Expect(LogType.Exception, new Regex("AppException:"));
            yield return Complete(app.SubmitDesktopTextAsync("检查测试输入"));
            Assert.AreEqual(AppErr.config, app.LastConversationError);
            Assert.IsEmpty(app.History.Latest.Reply);
            Assert.IsNull(app.LastDecision);
            Assert.AreEqual(0, app.DesktopDialogue.RecentTurnCount);
            app.DesktopSimulation.Reply = "这是手动提供的测试内容。";
            yield return Complete(app.RetryLastTurnAsync());
            Assert.AreEqual(AppErr.none, app.LastConversationError);
            Assert.AreEqual(app.DesktopSimulation.Reply, app.History.Latest.Reply);
            Assert.IsFalse(app.DesktopLiveMode);
        }
        [UnityTest] public IEnumerator EmptySpeechFailsWithoutPlayingADefaultSentence()
        {
            var bridge = app.GetComponent<AndroidNativeSpeechBridge>();
            var speech = bridge.SpeakAsync("   ", "zh-CN");
            yield return Until(() => speech.IsCompleted);
            Assert.IsTrue(speech.IsFaulted);
            Assert.AreEqual(AppErr.tts, ((AppException)speech.Exception.InnerException).Error);
        }
        private string UiText(string path) => app.GetComponent<ConversationPanel>().MessageInput.GetComponentInParent<Canvas>()
            .transform.Find("Conversation Interface/" + path).GetComponent<Text>().text;
        private static IEnumerator Complete(Task task)
        {
            yield return Until(() => task.IsCompleted);
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
        }
        private static IEnumerator Until(Func<bool> predicate)
        {
            var end = Time.realtimeSinceStartup + 10;
            while (!predicate()) { Assert.Less(Time.realtimeSinceStartup, end); yield return null; }
        }
        private sealed class AvailabilityClient : IDialogueClient
        {
            public bool IsAvailable { get; set; }
            public int Calls;
            public Task<DialogueDecision> CompleteAsync(TalkInput input, string recent, string longTerm,
                CancellationToken token, Action<string> onReplySentence = null)
            {
                Calls++;
                throw new InvalidOperationException("This test must never request a model response.");
            }
        }
    }
}
#endif
