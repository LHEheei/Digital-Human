using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SrtpAvatar.Tests
{
    public sealed class GeneratedDialogueTests
    {
        private AppConfig config;
        private string directory;
        [SetUp] public void SetUp()
        {
            config = ScriptableObject.CreateInstance<AppConfig>();
            directory = Path.Combine(Path.GetTempPath(), "srtp-generated-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(config);
            Directory.Delete(directory, true);
        }
        private DialogueService Service(IDialogueClient client) => new DialogueService(config, client,
            new LongTermMemoryStore(Path.Combine(directory, "memory.json")));

        [TestCase(null)] [TestCase("")] [TestCase("  \n")]
        public void EmptyReplyNeverBecomesAConversationOrMemory(string reply)
        {
            var decision = new DialogueDecision { reply = reply }.Normalize();
            Assert.IsEmpty(decision.reply);
            var memory = new ConversationMemory();
            memory.Add("用户输入", decision, 10);
            Assert.AreEqual(0, memory.Count);
            Assert.IsEmpty(memory.BuildContext(10));
            Assert.IsEmpty(new DialogueDecision().reply);
        }
        [TestCase(false)] [TestCase(true)]
        public async Task GeneratedTextIsPreservedWithEmotionAndAcrossTurns(bool streaming)
        {
            var client = new StubClient();
            var service = Service(client);
            var first = "我通过摄像头看到画面了。请描述你想讨论的细节。";
            var second = "你提到了窗边的植物，可以从光照变化谈起。";
            foreach (var reply in new[] { first, second })
            {
                client.Decision = new DialogueDecision { reply = reply, emotion = "fun" };
                var sentences = new List<string>();
                var result = await service.DecideAsync(new TalkInput { text = "继续讨论细节",
                    fusionEmo = new UserEmo { label = "sorrow", score = .9f, source = "fusion" } },
                    default, streaming ? sentences.Add : (Action<string>)null);
                Assert.AreEqual(reply, result.reply);
                Assert.AreEqual("neutral", result.emotion);
                if (streaming) Assert.AreEqual(reply, string.Concat(sentences));
            }
            Assert.AreEqual(2, service.RecentTurnCount);
            StringAssert.Contains(first, client.LastContext);
            StringAssert.Contains(second, service.RecentContext);
        }
        [TestCase(null)] [TestCase("")] [TestCase("   ")]
        public void InvalidModelReplyFailsWithoutCommittingMemory(string reply)
        {
            var client = new StubClient { Decision = reply == null ? null :
                new DialogueDecision { reply = reply, memory = "用户喜欢音乐" } };
            var service = Service(client);
            var error = Assert.ThrowsAsync<AppException>(async () => await service.DecideAsync("继续说"));
            Assert.AreEqual(AppErr.badJson, error.Error);
            Assert.AreEqual(0, service.RecentTurnCount);
            Assert.AreEqual(0, service.LongTermMemoryCount);
        }
        [Test] public void UnavailableModelDoesNotInvokeClientOrInventReply()
        {
            var client = new StubClient { IsAvailable = false };
            var service = Service(client);
            var error = Assert.ThrowsAsync<AppException>(async () => await service.DecideAsync("你好"));
            Assert.AreEqual(AppErr.aiUnavailable, error.Error);
            Assert.AreEqual(0, client.Calls);
            Assert.AreEqual(0, service.RecentTurnCount);
        }
        [Test] public async Task ExplicitMemoryOperationKeepsItsConfirmation()
        {
            var client = new StubClient { Decision = new DialogueDecision { reply = "刚刚生成的回复。", memory = "用户喜欢音乐" } };
            var service = Service(client);
            await service.DecideAsync("我喜欢音乐");
            var result = await service.DecideAsync("清除记忆");
            Assert.AreEqual("记忆已经清除，我们可以重新开始。", result.reply);
            Assert.AreEqual(1, client.Calls);
            Assert.AreEqual(0, service.RecentTurnCount);
            Assert.AreEqual(0, service.LongTermMemoryCount);
        }
        [Test] public void EmptyInputIsRejectedBeforeResolvingKeysOrSendingRequests()
        {
            var client = new DeepSeekDialogueClient(config);
            Assert.ThrowsAsync<ArgumentException>(async () => await client.CompleteAsync(new TalkInput(), "", "", default));
        }
        [Test] public async Task OfflineTestHasNoBuiltInDialogue()
        {
            var client = new SimulatedDialogueClient { ChunkDelayMilliseconds = 1 };
            Assert.IsEmpty(client.Reply);
            AppException error = null;
            try { await client.CompleteAsync(new TalkInput { text = "测试" }, "", "", default); }
            catch (AppException ex) { error = ex; }
            Assert.IsNotNull(error);
            Assert.AreEqual(AppErr.config, error.Error);
        }
        [Test] public async Task OfflineTestUsesOnlyTheExplicitFixture()
        {
            var client = new SimulatedDialogueClient { ChunkDelayMilliseconds = 1, Reply = "这段内容由测试提供。第二句检验流式拼接。" };
            var sentences = new List<string>();
            var reply = await client.CompleteAsync(new TalkInput { text = "测试" }, "", "", default, sentences.Add);
            Assert.AreEqual(client.Reply, reply.reply);
            Assert.AreEqual(client.Reply, string.Concat(sentences));
        }
        private sealed class StubClient : IDialogueClient
        {
            public bool IsAvailable { get; set; } = true;
            public DialogueDecision Decision;
            public int Calls;
            public string LastContext;
            public Task<DialogueDecision> CompleteAsync(TalkInput input, string recentContext, string longTermContext,
                CancellationToken token, Action<string> onReplySentence = null)
            {
                Calls++;
                LastContext = recentContext;
                if (!string.IsNullOrWhiteSpace(Decision?.reply)) onReplySentence?.Invoke(Decision.reply);
                return Task.FromResult(Decision);
            }
        }
    }
}
