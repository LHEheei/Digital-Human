using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace SrtpAvatar.Tests
{
    public sealed class ConversationOptimizationTests
    {
        [TestCase("请忘记刚才。", MemoryClearScope.Recent)]
        [TestCase("重新开始对话", MemoryClearScope.Recent)]
        [TestCase("忘记我", MemoryClearScope.All)]
        [TestCase("清除记忆？", MemoryClearScope.None)]
        public void ClearScopeMatchesExplicitIntent(string input, MemoryClearScope expected)
        {
            Assert.AreEqual(expected, ConversationMemory.GetClearScope(input));
        }

        [Test]
        public async Task ResumeWaitsForOldTurnCleanupAndStartsOnlyOnce()
        {
            var enabled = true;
            var foreground = true;
            var started = new TaskCompletionSource<bool>();
            var canceled = new TaskCompletionSource<bool>();
            var cleaned = new TaskCompletionSource<bool>();
            var resumed = new TaskCompletionSource<bool>();
            var calls = 0;
            var active = 0;
            var peakActive = 0;
            using var loop = new AutomaticConversationLoop(() => enabled && foreground, async token =>
            {
                peakActive = Math.Max(peakActive, ++active);
                var call = ++calls;
                if (call == 1) started.TrySetResult(true); else resumed.TrySetResult(true);
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException)
                {
                    if (call == 1) { canceled.TrySetResult(true); await cleaned.Task; }
                    throw;
                }
                finally { active--; }
            }, () => 0.01f, ex => Assert.Fail(ex.ToString()));
            loop.Refresh();
            await TaskTimeout.WithTimeout(started.Task, 3f, "first turn did not start");
            foreground = false;
            loop.Refresh();
            await TaskTimeout.WithTimeout(canceled.Task, 3f, "first turn was not canceled");
            foreground = true;
            loop.Refresh();
            loop.Refresh();
            Assert.AreEqual(1, calls, "A resumed turn must wait for canceled ASR/TTS cleanup.");
            cleaned.SetResult(true);
            await TaskTimeout.WithTimeout(resumed.Task, 3f, "listening did not resume");
            Assert.AreEqual(2, calls);
            Assert.AreEqual(1, peakActive);
            enabled = false;
            loop.Refresh();
            await loop.Completion;
            loop.Refresh();
            Assert.AreEqual(2, calls, "Disabling automatic conversation must prevent a restart.");
        }

        [Test]
        public async Task SentenceQueuePreservesOrderAndCancelsPendingPlayback()
        {
            using var queue = new ReplySpeechQueue();
            using var cancellation = new CancellationTokenSource();
            var spoken = new List<string>();
            var blocked = new TaskCompletionSource<bool>();
            var playback = queue.PlayAsync(async (text, token) =>
            {
                spoken.Add(text);
                if (text == "第二句") { blocked.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); }
            }, cancellation.Token);
            queue.Enqueue("第一句"); queue.Enqueue("第二句"); queue.Enqueue("第三句"); queue.Complete();
            await TaskTimeout.WithTimeout(blocked.Task, 3f, "queue did not reach second sentence");
            cancellation.Cancel();
            try { await playback; Assert.Fail("Playback should cancel."); }
            catch (OperationCanceledException) { }
            CollectionAssert.AreEqual(new[] { "第一句", "第二句" }, spoken);
        }

        [Test]
        public void StreamHandlesUtf8BoundariesAndJsonEscapesWithoutRepeatingSentences()
        {
            var sentences = new List<string>();
            var decoder = new DialogueStreamDecoder(sentences.Add);
            FeedBytewise(decoder, Event("{\"reply\":\"你好。"));
            CollectionAssert.AreEqual(new[] { "你好。" }, sentences, "Speak a complete sentence before the rest of the object arrives.");
            FeedBytewise(decoder, Event("我说\\\"可以\\\"，\\u4f60好！\",\"emotion\":\"joy\",\"gesture\":\"nod\"}"));
            FeedBytewise(decoder, "data: [DONE]\r\n\r\n");
            decoder.Finish();
            CollectionAssert.AreEqual(new[] { "你好。", "我说\"可以\"，你好！" }, sentences);
            Assert.IsTrue(DialogueParser.TryFromPossiblyWrappedJson(decoder.Content, out var decision));
            Assert.AreEqual(decision.reply, decoder.ReplyText);
        }

        [Test]
        public void NestedReplyFieldIsNotSpokenAndIncompleteStreamIsRejected()
        {
            var spoken = new List<string>();
            var decoder = new DialogueStreamDecoder(spoken.Add);
            FeedBytewise(decoder, Event("{\"metadata\":{\"reply\":\"错误。\"},\"reply\":\"正确。"));
            CollectionAssert.AreEqual(new[] { "正确。" }, spoken);
            Assert.Throws<FormatException>(() => decoder.Finish());
        }

        [Test]
        public void StreamAcceptsCompleteNonStreamingProviderResponse()
        {
            var spoken = new List<string>();
            var decoder = new DialogueStreamDecoder(spoken.Add);
            FeedBytewise(decoder, "{\"choices\":[{\"message\":{\"content\":\"{\\\"reply\\\":\\\"你好！\\\"}\"},\"finish_reason\":\"stop\"}]}");
            decoder.Finish();
            CollectionAssert.AreEqual(new[] { "你好！" }, spoken);
        }

        [Test]
        public void StreamRejectsTokenLimitTruncationEvenWithDoneMarker()
        {
            var decoder = new DialogueStreamDecoder(_ => { });
            FeedBytewise(decoder, Event("{\"reply\":\"你好！\"}"));
            FeedBytewise(decoder, "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n");
            Assert.Throws<FormatException>(() => decoder.Finish());
        }

        [Test]
        public void AudioEnvelopeWaitsForPlaybackAndFollowsEnginePosition()
        {
            var timeline = new TtsMouthTimeline();
            timeline.Append(0, .02f, new[] { 0f, 1f, 0f, .5f });
            Assert.IsFalse(timeline.TrySample(10, out _));
            timeline.Start(10);
            Assert.IsTrue(timeline.TrySample(10, out var silence)); Assert.AreEqual(0f, silence);
            timeline.TrySample(10.02, out var peak); Assert.That(peak, Is.EqualTo(1f).Within(.001f));
            timeline.Align(11, .06f);
            timeline.TrySample(11, out var aligned); Assert.That(aligned, Is.EqualTo(.5f).Within(.001f));
            timeline.Reset();
            Assert.IsFalse(timeline.TrySample(12, out _));
        }

        [TestCase("angry", "angry", "neutral")]
        [TestCase("sorrow", "fun", "neutral")]
        [TestCase("joy", "surprised", "surprised")]
        public void AvatarRespondsWithoutAutomaticallyMirroringUser(string user, string reply, string expected)
        {
            var decision = EmotionResponsePolicy.Apply(new DialogueDecision { emotion = reply, gesture = "explain" },
                new UserEmo { label = user, source = "fusion", score = .9f });
            Assert.AreEqual(expected, decision.emotion);
            Assert.AreEqual("explain", decision.gesture);
        }

        [Test]
        public void CameraBufferReusePreservesPixelsAndReducesManagedAllocation()
        {
            var pixels = new Color32[640 * 480];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(i % 251), 42, 7, 255);
            var expected = CameraFrameTransform.OrientAndScale(pixels, 640, 480, 90, true, 640, out _, out _);
            Color32[] reused = null;
            CameraFrameTransform.OrientAndScale(pixels, 640, 480, 90, true, 640, ref reused, out _, out _);
            CollectionAssert.AreEqual(expected, reused);
            var reference = reused;
            var retained = new Color32[10][];
            var start = GC.GetTotalMemory(true);
            for (var i = 0; i < 10; i++) CameraFrameTransform.OrientAndScale(pixels, 640, 480, 90, true, 640, ref reused, out _, out _);
            var reusedBytes = GC.GetTotalMemory(true) - start;
            start = GC.GetTotalMemory(true);
            for (var i = 0; i < 10; i++) retained[i] = CameraFrameTransform.OrientAndScale(pixels, 640, 480, 90, true, 640, out _, out _);
            var allocatingBytes = GC.GetTotalMemory(true) - start;
            GC.KeepAlive(retained);
            Assert.AreSame(reference, reused);
            Assert.Greater(allocatingBytes, 10L * 640 * 480 * 4 * 0.9);
            Assert.Less(reusedBytes, allocatingBytes / 10);
            TestContext.WriteLine($"10 camera transforms at 640x480, retained managed heap delta: allocating={allocatingBytes} bytes, reused={reusedBytes} bytes");
        }

        [Test]
        public void CameraEncodingDoesNotNeedGpuUpload()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var decoded = new Texture2D(2, 2);
            try
            {
                texture.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(255, 0, 0, 255), new Color32(255, 0, 0, 255), new Color32(255, 0, 0, 255) });
                Assert.IsTrue(decoded.LoadImage(texture.EncodeToJPG(95)));
                Assert.Greater(decoded.GetPixel(0, 0).r, .9f);
                Assert.Less(decoded.GetPixel(0, 0).g, .1f);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(decoded); }
        }

        private static string Event(string delta) => "data: {\"choices\":[{\"delta\":{\"content\":" + JsonUtility.ToJson(new TextValue { value = delta }).Substring(9).TrimEnd('}') + "}}]}\r\n\r\n";
        private static void FeedBytewise(DialogueStreamDecoder decoder, string text)
        {
            foreach (var value in Encoding.UTF8.GetBytes(text)) decoder.Append(new[] { value }, 1);
        }
        [Serializable] private sealed class TextValue { public string value; }
    }
}