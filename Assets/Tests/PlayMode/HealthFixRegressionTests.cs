#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class HealthFixRegressionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene scene;
        private DigitalHumanAppController app;
        private ConversationPanel panel;
        private VrmAvatarBridge avatar;
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/DesktopAvatarDemo.unity", LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath("Assets/Scenes/DesktopAvatarDemo.unity");
            app = Object.FindAnyObjectByType<DigitalHumanAppController>();
            panel = app.GetComponent<ConversationPanel>();
            avatar = Object.FindAnyObjectByType<VrmAvatarBridge>();
            yield return null;
            app.DesktopSimulation.ChunkDelayMilliseconds = 1;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (app != null) { app.CancelCurrentConversation(); yield return Until(() => !app.IsBusy); }
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
        }

        [UnityTest] public IEnumerator DestroyCancelsEveryNativeRequestDespiteSynchronousContinuation()
        {
            var owner = new GameObject("Pending native request regression");
            var bridge = owner.AddComponent<AndroidNativeEmotionBridge>();
            var type = typeof(AndroidNativeEmotionBridge);
            var pending = (Dictionary<string, TaskCompletionSource<UserEmo>>)type.GetField("pendingRequests", Private).GetValue(bridge);
            var awaitMethod = type.GetMethod("AwaitNativeRequestAsync", Private);
            var completions = new List<TaskCompletionSource<UserEmo>>();
            var tasks = new List<Task>();
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    var completion = new TaskCompletionSource<UserEmo>();
                    var id = "pending-" + i;
                    completions.Add(completion); pending.Add(id, completion);
                    tasks.Add((Task)awaitMethod.Invoke(bridge, new object[] { id, completion, CancellationToken.None }));
                }
                Assert.DoesNotThrow(() => type.GetMethod("OnDestroy", Private).Invoke(bridge, null));
                yield return Until(() => tasks.TrueForAll(task => task.IsCompleted));
                Assert.IsEmpty(pending);
                Assert.IsTrue(tasks.TrueForAll(task => task.IsCanceled));
                bridge.OnNativeFaceError("pending-0\nlate callback");
                bridge.OnNativeVoiceError("pending-1\nlate callback");
                Assert.IsEmpty(pending);
            }
            finally
            {
                pending.Clear();
                foreach (var completion in completions) completion.TrySetCanceled();
                Object.Destroy(owner);
            }
        }

        [UnityTest] public IEnumerator ListeningWaitsForReadyAndIgnoresLateReadyAfterCancellation()
        {
            Invoke("BeginListeningPresentation");
            yield return null;
            Assert.AreEqual("正在准备语音服务", UiText("State"));
            Assert.AreEqual(SpeechListeningPhase.Preparing, app.ListeningPhase);
            Invoke("OnRecognitionReady", CancellationToken.None);
            yield return null;
            Assert.AreEqual("正在聆听", UiText("State"));
            Invoke("StopAutoListening");
            Invoke("OnRecognitionReady", CancellationToken.None);
            yield return null;
            Assert.AreEqual("等待输入", UiText("State"));
            Invoke("BeginListeningPresentation");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); Invoke("OnRecognitionReady", cancellation.Token);
                Assert.AreEqual(SpeechListeningPhase.Preparing, app.ListeningPhase);
            }
        }

        [UnityTest] public IEnumerator PermissionAndServiceErrorsStayVisibleUntilRecognitionRecovers()
        {
            var messages = new[] { "未获得麦克风权限，可重新授权后再试。", "麦克风权限已被禁止，请前往系统设置开启。", "当前设备没有可用的系统语音识别服务。" };
            foreach (var message in messages)
            {
                Invoke("ReportListeningError", new AppException(message.Contains("权限") ? AppErr.micPerm : AppErr.speech, message));
                yield return null;
                Assert.AreEqual("语音暂不可用", UiText("State"));
                Assert.AreEqual(message, UiText("Chat Dock/Feedback"));
                Invoke("BeginListeningPresentation");
                yield return null;
                Assert.AreEqual(message, UiText("Chat Dock/Feedback"), "Retry must not hide the unresolved problem.");
                Invoke("OnRecognitionReady", CancellationToken.None);
                yield return null;
                Assert.AreEqual("", UiText("Chat Dock/Feedback"));
                Assert.AreEqual("正在聆听", UiText("State"));
            }
        }

        [UnityTest] public IEnumerator UnavailableRecognitionServiceReportsThroughAutomaticLoop()
        {
            var config = (AppConfig)typeof(DigitalHumanAppController).GetField("config", Private).GetValue(app);
            config.deepSeekApiKey = "local-test-only";
            config.preferAndroidNativeSpeech = true;
            config.enableAutoConversation = true;
            app.RefreshConversationSettings();
            yield return Until(() => app.ListeningPhase == SpeechListeningPhase.Failed);
            yield return null;
            Assert.AreEqual("语音暂不可用", UiText("State"));
            StringAssert.Contains("没有可用的系统语音识别服务", UiText("Chat Dock/Feedback"));
            Assert.IsEmpty(app.History.Turns, "Unavailable ASR must not create a model conversation.");
            config.enableAutoConversation = false;
            app.RefreshConversationSettings();
        }

        [UnityTest] public IEnumerator ListeningErrorRemainsVisibleAfterPreviousPlaybackFailure()
        {
            app.DesktopSimulatePlaybackFailure = true;
            app.DesktopSimulation.Reply = "好。";
            var operation = app.SubmitDesktopTextAsync("上一轮播放失败");
            yield return Until(() => operation.IsCompleted);
            Assert.IsFalse(operation.IsFaulted);
            Assert.IsTrue(app.LastPlaybackFailed);
            const string message = "麦克风权限已被禁止，请前往系统设置开启。";
            Invoke("ReportListeningError", new AppException(AppErr.micPerm, message));
            yield return null;
            Assert.AreEqual(message, UiText("Chat Dock/Feedback"));
            Invoke("BeginListeningPresentation"); Invoke("OnRecognitionReady", CancellationToken.None);
            yield return null;
            Assert.AreEqual("语音暂不可用，文字回复已保留。", UiText("Chat Dock/Feedback"));
        }

        [UnityTest] public IEnumerator NormalNoSpeechEndsReadyStateWithoutShowingAnError()
        {
            Invoke("BeginListeningPresentation"); Invoke("OnRecognitionReady", CancellationToken.None);
            Invoke("ReportListeningError", new AppException(AppErr.speech, "没有听到说话"));
            yield return null;
            Assert.AreEqual("等待输入", UiText("State"));
            Assert.AreEqual("", UiText("Chat Dock/Feedback"));
        }

        [UnityTest] public IEnumerator SwitchingHistoriesAfterCapacityTrimPreservesVisibleOrder()
        {
            app.SelectDesktopMode(true);
            SeedHistory("live", 3);
            yield return null;
            app.SelectDesktopMode(false);
            SeedHistory("simulation", 41);
            yield return null;
            AssertRows("simulation", 2, 41);
            app.SelectDesktopMode(true);
            yield return null;
            AssertRows("live", 1, 3);
            app.SelectDesktopMode(false);
            yield return null;
            AssertRows("simulation", 2, 41);
        }

        [UnityTest] public IEnumerator ShortGreetingCompletesItsWaveAcrossTheNextSentenceAndIdle()
        {
            app.DesktopSimulation.Reply = "你好。接着聊聊。";
            Assert.AreEqual(.045f, app.DesktopSecondsPerCharacter);
            var hand = avatar.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
            var restingHeight = hand.position.y;
            CaptureMotion("greeting-before");
            var operation = app.SubmitDesktopTextAsync("打个招呼");
            yield return Until(() => operation.IsCompleted);
            Assert.IsFalse(operation.IsFaulted);
            Assert.AreEqual(AvatarState.idle, app.CurrentState);
            Assert.AreEqual(AvatarGesture.wave, ActiveGesture(), "Natural sentence end must not cancel the gesture.");
            var elapsedAtReplyEnd = (float)typeof(VrmAvatarBridge).GetField("gestureElapsed", Private).GetValue(avatar);
            Assert.Less(elapsedAtReplyEnd, 1f, "The reply should finish before the wave's main phase.");
            yield return new WaitForSeconds(.65f);
            Assert.AreEqual(AvatarGesture.wave, ActiveGesture());
            Assert.Greater(hand.position.y - restingHeight, .15f, "Check actual hand motion, not only the emitted gesture enum.");
            CaptureMotion("greeting-wave-after-speech");
            yield return new WaitForSeconds(2.3f);
            Assert.AreEqual(AvatarGesture.none, ActiveGesture());
            Assert.Less(Mathf.Abs(hand.position.y - restingHeight), .07f);
            CaptureMotion("greeting-returned");
        }

        [UnityTest] public IEnumerator PauseStopsTheRemainingGestureAfterShortSpeechCompletes()
        {
            app.DesktopSimulation.Reply = "你好。";
            var operation = app.SubmitDesktopTextAsync("简短问候");
            yield return Until(() => operation.IsCompleted);
            Assert.AreEqual(AvatarGesture.wave, ActiveGesture());
            app.SendMessage("OnApplicationPause", true);
            Assert.AreEqual(AvatarGesture.none, ActiveGesture());
            app.SendMessage("OnApplicationPause", false);
            yield return null;
            Assert.IsFalse(app.IsBusy);
        }

        private void SeedHistory(string prefix, int count)
        {
            for (var i = 1; i <= count; i++)
            {
                var turn = app.History.Begin(prefix + "-" + i);
                app.History.UpdateReply(turn, "回复 " + i);
                app.History.Finish(turn, AppErr.none, "", true);
            }
            Invoke("SetHint", "history updated");
        }
        private void AssertRows(string prefix, int first, int last)
        {
            var actual = new List<string>();
            foreach (Transform child in panel.HistoryScroll.content)
            {
                if (!child.gameObject.activeSelf || !child.name.StartsWith("Turn ")) continue;
                actual.Add(child.Find("User").GetComponent<Text>().text);
            }
            var expected = new List<string>();
            for (var i = first; i <= last; i++) expected.Add("你  ·  " + prefix + "-" + i);
            CollectionAssert.AreEqual(expected, actual);
        }
        private string UiText(string path) => panel.HistoryScroll.GetComponentInParent<Canvas>().transform
            .Find("Conversation Interface/" + path).GetComponent<Text>().text;
        private void Invoke(string method, params object[] args) => typeof(DigitalHumanAppController).GetMethod(method, Private).Invoke(app, args);
        private AvatarGesture ActiveGesture() => (AvatarGesture)typeof(VrmAvatarBridge).GetField("activeGesture", Private).GetValue(avatar);
        private static IEnumerator Until(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + 15;
            while (!condition()) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
        }
        private void CaptureMotion(string name)
        {
            var directory = Environment.GetEnvironmentVariable("SRTP_HEALTH_PREVIEW");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            var camera = Camera.main;
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            var orthographic = camera.orthographic; var size = camera.orthographicSize;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = new RenderTexture(640, 900, 24); var image = new Texture2D(640, 900, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = new Vector3(0, .45f, -3.7f); camera.transform.LookAt(new Vector3(0, .45f, 0));
                camera.orthographic = true; camera.orthographicSize = .9f;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 640, 900), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.orthographic = orthographic; camera.orthographicSize = size;
                Object.Destroy(image); Object.Destroy(target);
            }
        }
    }
}
#endif
