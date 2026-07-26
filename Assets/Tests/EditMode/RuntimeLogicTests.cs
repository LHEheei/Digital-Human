using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace SrtpAvatar.Tests
{
    public sealed class RuntimeLogicTests
    {
        private string memoryFilePath;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(memoryFilePath) && File.Exists(memoryFilePath))
            {
                File.Delete(memoryFilePath);
            }
        }

        [Test]
        public void DialogueDecisionNormalize_UsesSafeDefaults()
        {
            var decision = new DialogueDecision
            {
                reply = " ",
                emotion = "invalid",
                gesture = "dance",
                carIntent = " forward ",
                memory = " note "
            }.Normalize();

            Assert.AreEqual("我理解了。", decision.reply);
            Assert.AreEqual("neutral", decision.emotion);
            Assert.AreEqual("none", decision.gesture);
            Assert.AreEqual("forward", decision.carIntent);
            Assert.AreEqual("note", decision.memory);
        }

        [TestCase("{\"reply\":\"你好\",\"emotion\":\"joy\"}")]
        [TestCase("```json\n{\"reply\":\"你好\"}\n```")]
        public void DialogueParser_AcceptsJsonAndMarkdownFence(string raw)
        {
            Assert.IsTrue(DialogueParser.TryFromPossiblyWrappedJson(raw, out var decision));
            Assert.AreEqual("你好", decision.reply);
        }

        [TestCase("{\"emotion\":\"joy\"}")]
        [TestCase("not-json")]
        [TestCase("")]
        public void DialogueParser_RejectsMissingReplyOrInvalidJson(string raw)
        {
            Assert.IsFalse(DialogueParser.TryFromPossiblyWrappedJson(raw, out _));
        }

        [Test]
        public void MemoryFilter_RemovesSensitiveAndOverlongData()
        {
            Assert.IsTrue(MemoryFilter.ShouldRemember("用户喜欢科幻电影"));
            Assert.IsFalse(MemoryFilter.ShouldRemember("api key: example-secret"));
            Assert.IsFalse(MemoryFilter.ShouldRemember("token: secret-value"));
            Assert.IsFalse(MemoryFilter.ShouldRemember("联系电话 13800138000"));
            Assert.IsFalse(MemoryFilter.ShouldRemember("邮箱 a@example.com"));
            Assert.IsFalse(MemoryFilter.ShouldRemember(new string('a', 257)));
        }

        [Test]
        public void LongTermMemoryStore_PersistsDeduplicatesTrimsAndRecovers()
        {
            memoryFilePath = Path.Combine(Path.GetTempPath(), "srtp-memory-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new LongTermMemoryStore(memoryFilePath);
            Assert.IsTrue(store.Remember("第一条\n第二条\n第三条", 2));
            Assert.AreEqual(2, store.Count);
            Assert.IsTrue(store.Remember("第三条", 2));
            Assert.AreEqual(2, store.Count);
            StringAssert.Contains("updatedAt", File.ReadAllText(memoryFilePath));

            File.WriteAllText(memoryFilePath, "{broken");
            var recovered = new LongTermMemoryStore(memoryFilePath);
            Assert.AreEqual(0, recovered.Count);
        }

        [TestCase("https://api.deepseek.com", true, "https://api.deepseek.com")]
        [TestCase("https://example.test/v1/", true, "https://example.test/v1")]
        [TestCase("http://api.deepseek.com", false, "https://api.deepseek.com")]
        [TestCase("not a url", false, "https://api.deepseek.com")]
        [TestCase("", true, "https://api.deepseek.com")]
        public void BaseUrlValidation_OnlyAcceptsSafeHttps(string source, bool valid, string expected)
        {
            var result = RuntimeSecrets.TryNormalizeDeepSeekBaseUrl(source, out var normalized, out _, out _);
            Assert.AreEqual(valid, result);
            Assert.AreEqual(expected, normalized);
        }

        [Test]
        public void LongTermMemoryStore_RefreshesDuplicateBeforeTrimming()
        {
            memoryFilePath = Path.Combine(Path.GetTempPath(), "srtp-memory-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new LongTermMemoryStore(memoryFilePath);
            Assert.IsTrue(store.Remember("A\nB\nC", 3));
            Assert.IsTrue(store.Remember("A", 2));

            var context = store.BuildContext(2);
            StringAssert.Contains("- C", context);
            StringAssert.Contains("- A", context);
            StringAssert.DoesNotContain("- B", context);
        }

        [Test]
        public void UserEmo_NormalizesLabelScoreAndSource()
        {
            var emotion = new UserEmo { label = "fun", score = 4f, source = "" }.Normalize();
            Assert.AreEqual("neutral", emotion.label);
            Assert.AreEqual(1f, emotion.score);
            Assert.AreEqual("unknown", emotion.source);
            Assert.IsFalse(emotion.IsUsable);
        }

        [Test]
        public async Task DemoEmoService_ReturnsStableDataAndRecognizableFailures()
        {
            var service = new DemoEmoService();
            var face = await service.GetFaceAsync(new byte[] { 1 }, default);
            var emptyVoice = await service.GetVoiceAsync(new byte[0], default);

            Assert.AreEqual("joy", face.label);
            Assert.AreEqual("face", face.source);
            Assert.IsTrue(face.IsUsable);
            Assert.AreEqual("voice", emptyVoice.source);
            Assert.IsFalse(string.IsNullOrWhiteSpace(emptyVoice.error));
        }

        [Test]
        public void UserEmo_IsUsableRequiresPositiveScoreAndValidResult()
        {
            Assert.IsFalse(new UserEmo { score = 0f, source = "face" }.IsUsable);
            Assert.IsTrue(new UserEmo { score = .01f, source = "face" }.IsUsable);
            Assert.IsFalse(new UserEmo { score = .5f, source = "invalid" }.IsUsable);
            Assert.IsFalse(new UserEmo { score = .5f, source = "voice", error = "failed" }.IsUsable);
        }

        [Test]
        public void TalkInput_OnlyAddsValidEmotionContext()
        {
            var input = new TalkInput
            {
                faceEmo = new UserEmo { label = "joy", score = .8f, source = "face" },
                voiceEmo = UserEmo.Failure("voice", "empty audio")
            };

            var context = input.BuildEmotionContext();
            StringAssert.Contains("joy", context);
            StringAssert.DoesNotContain("empty audio", context);
        }

        [Test]
        public void CarCommand_UsesWhitelistFixedTokenAndStopPriority()
        {
            var config = ScriptableObject.CreateInstance<AppConfig>();
            config.carCommandForward = "F";
            config.carCommandStop = "S";
            try
            {
                Assert.IsTrue(CarCommand.TryFromDecision(new DialogueDecision { carIntent = "forward" }, config, out var forward));
                Assert.AreEqual("forward", forward.Intent);
                Assert.AreEqual("F", forward.Payload);
                Assert.IsFalse(CarCommand.TryFromDecision(new DialogueDecision { carIntent = "launch" }, config, out _));

                var service = new CarCommunicationService(config, null);
                Assert.AreEqual(CarExecutionOrder.immediate, service.GetExecutionOrder(new DialogueDecision { carIntent = "stop" }));
                Assert.AreEqual(CarExecutionOrder.parallel, service.GetExecutionOrder(new DialogueDecision { carIntent = "launch" }));
                Assert.AreEqual(CarExecutionOrder.none, service.GetExecutionOrder(new DialogueDecision { carIntent = "none" }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AndroidCallbackParser_OnlyAcceptsCurrentIdAndDelimiter()
        {
            Assert.IsTrue(AndroidNativeSpeechBridge.TryReadCallback("current\nhello", "current", out var payload));
            Assert.AreEqual("hello", payload);
            Assert.IsFalse(AndroidNativeSpeechBridge.TryReadCallback("old\nhello", "current", out _));
            Assert.IsFalse(AndroidNativeSpeechBridge.TryReadCallback("current", "current", out _));
            Assert.IsFalse(AndroidNativeSpeechBridge.TryReadCallback("", "current", out _));
        }
    }
}
