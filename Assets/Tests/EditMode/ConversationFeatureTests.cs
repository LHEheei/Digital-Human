using System;
using System.IO;
using NUnit.Framework;

namespace SrtpAvatar.Tests
{
    public sealed class ConversationFeatureTests
    {
        private string directory, path;
        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "srtp-features-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); path = Path.Combine(directory, "memory.json");
        }
        [TearDown] public void TearDown() { Directory.Delete(directory, true); }

        [Test] public void LegacyRecordsGainStableIdsAndPreserveCreationDateOnEdit()
        {
            File.WriteAllText(path, "{\"items\":[{\"text\":\"用户喜欢音乐\",\"createdAt\":\"2025-01-01\"},{\"text\":\"用户喜欢电影\"}]}");
            var store = new LongTermMemoryStore(path);
            var before = store.GetEntries();
            Assert.AreNotEqual(before[0].Id, before[1].Id);
            Assert.IsNotEmpty(before[0].Id);
            store.UpdateEntry(before[0].Id, "用户喜欢古典音乐");
            var restored = new LongTermMemoryStore(path).GetEntries();
            Assert.AreEqual(before[0].Id, restored[0].Id);
            Assert.AreEqual("2025-01-01", restored[0].CreatedAt);
            Assert.AreEqual("用户喜欢古典音乐", restored[0].Text);
            Assert.AreEqual("用户喜欢音乐", before[0].Text, "Snapshots must remain immutable.");
            StringAssert.Contains("\"version\": 2", File.ReadAllText(path));
        }
        [Test] public void EditedAndDeletedMemoriesCannotReturnFromRecoveryBackup()
        {
            var store = new LongTermMemoryStore(path);
            store.Remember("用户喜欢音乐；用户喜欢电影", 20);
            var ids = store.GetEntries();
            store.UpdateEntry(ids[0].Id, "用户喜欢古典音乐");
            store.DeleteEntry(ids[1].Id);
            File.WriteAllText(path, "{broken");
            var recovered = new LongTermMemoryStore(path);
            Assert.AreEqual(1, recovered.Count);
            Assert.AreEqual("用户喜欢古典音乐", recovered.GetEntries()[0].Text);
            Assert.Throws<ArgumentException>(() => recovered.DeleteEntry(ids[1].Id));
        }
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("用户密码是测试内容")]
        [TestCase("用户喜欢电影")]
        public void InvalidEditsLeaveBothMemoryAndDiskUnchanged(string text)
        {
            var store = new LongTermMemoryStore(path);
            store.Remember("用户喜欢音乐；用户喜欢电影", 20);
            var disk = File.ReadAllText(path);
            Assert.Throws<ArgumentException>(() => store.UpdateEntry(store.GetEntries()[0].Id, text));
            Assert.AreEqual(disk, File.ReadAllText(path));
            Assert.AreEqual("用户喜欢音乐", store.GetEntries()[0].Text);
        }
        [Test] public void LengthLimitAndFailedWritesDoNotPublishAnUnsavedEdit()
        {
            var store = new LongTermMemoryStore(path);
            store.Remember("用户喜欢音乐", 20);
            var id = store.GetEntries()[0].Id;
            Assert.Throws<ArgumentException>(() => store.UpdateEntry(id, new string('乐', 81)));
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<AppException>(() => store.UpdateEntry(id, "用户喜欢古典音乐"));
            Assert.AreEqual("用户喜欢音乐", store.GetEntries()[0].Text);
            Assert.AreEqual("用户喜欢音乐", new LongTermMemoryStore(path).GetEntries()[0].Text);
            store.UpdateEntry(id, "用户喜欢古典音乐");
            Assert.AreEqual("用户喜欢古典音乐", new LongTermMemoryStore(path).GetEntries()[0].Text);
        }
        [Test] public void MotionCooldownAppliesAcrossSentencesAndTurns()
        {
            var planner = new SentenceMotionPlanner();
            Assert.AreEqual(AvatarGesture.wave, planner.Plan("你好。", 0, 0).Gesture);
            Assert.AreEqual(AvatarGesture.none, planner.Plan("首先可以这样。", 1, 1).Gesture);
            Assert.AreEqual(AvatarGesture.explain, planner.Plan("例如这样。", 2, 3).Gesture);
            Assert.AreEqual(AvatarGesture.none, planner.Plan("因为这样。", 3, 6).Gesture);
            Assert.AreEqual(AvatarGesture.none, planner.Plan("你好。", 0, 9).Gesture);
            Assert.AreEqual(AvatarGesture.wave, planner.Plan("你好。", 0, 12).Gesture);
        }
        [Test] public void EmpathyOverridesConflictingEmotionAndThinkingIsNotReplayedWhileSpeaking()
        {
            var planner = new SentenceMotionPlanner();
            var cue = planner.Plan("这段时间很不容易，我们慢慢来。", 0, 0,
                new DialogueDecision { emotion = "angry", gesture = "wave" });
            Assert.AreEqual(AvatarEmotion.concerned, cue.Emotion);
            Assert.AreEqual(AvatarGesture.nod, cue.Gesture);
            Assert.AreEqual(AvatarGesture.none, planner.Plan("我们继续。", 0, 20,
                new DialogueDecision { emotion = "neutral", gesture = "think" }).Gesture);
            Assert.AreEqual(AvatarGesture.none, planner.Plan("你好。", 1, 30).Gesture);
        }
        [Test] public void SentenceSplitRetainsOrderPunctuationAndTail()
        {
            CollectionAssert.AreEqual(new[] { "你好。", "首先这样！", "还有一个建议" },
                SentenceMotionPlanner.SplitSentences("你好。首先这样！还有一个建议"));
        }
        [Test] public void RetryKeepsPartialReplyAndNeverReexecutesCommittedTurn()
        {
            var history = new ConversationHistory(2);
            var first = history.Begin("失败的提问"); history.UpdateReply(first, "已经显示的半句");
            history.Finish(first, AppErr.network, "网络中断", false);
            Assert.IsTrue(history.CanRetry); Assert.AreEqual("已经显示的半句", first.Reply);
            var second = history.Begin(first.UserText); history.Finish(second, AppErr.canceled, "", true);
            Assert.IsFalse(history.CanRetry);
            var third = history.Begin("第三轮"); history.Finish(third, AppErr.none, "", true);
            Assert.AreEqual(2, history.Turns.Count); Assert.AreSame(second, history.Turns[0]);
            Assert.AreEqual(ConversationTurnStatus.Completed, third.Status);
        }

        [Test] public void LocalSessionsRestoreActiveHistoryAndIndependentContext()
        {
            var store = new ConversationSessionStore(path);
            var first = store.ActiveId;
            AddSessionReply(store, "第一段聊天", "第一段回复");
            store.Create();
            var second = store.ActiveId;
            Assert.IsEmpty(store.History.Turns);
            Assert.AreEqual(0, store.Memory.Count);
            AddSessionReply(store, "第二段聊天", "第二段回复");
            var incomplete = store.History.Begin("网络失败");
            store.History.UpdateReply(incomplete, "半句回复");
            store.History.Finish(incomplete, AppErr.network, "网络中断", false);
            store.History.Begin("尚未结束");
            store.SaveActive();

            var restored = new ConversationSessionStore(path);
            Assert.AreEqual(second, restored.ActiveId);
            Assert.AreEqual(2, restored.History.Turns.Count);
            Assert.IsTrue(restored.History.CanRetry);
            Assert.AreEqual("半句回复", restored.History.Latest.Reply);
            Assert.AreEqual("网络中断", restored.History.Latest.ErrorMessage);
            StringAssert.Contains("第二段聊天", restored.Memory.BuildContext(6));
            StringAssert.DoesNotContain("第一段聊天", restored.Memory.BuildContext(6));
            restored.Open(first);
            Assert.AreEqual("第一段回复", restored.History.Latest.Reply);
            Assert.AreEqual(first, new ConversationSessionStore(path).ActiveId);
            StringAssert.DoesNotContain("第二段聊天", restored.Memory.BuildContext(6));
        }

        [Test] public void DeletingSessionsRemovesTheirContentsAndReplacesAnEmptyList()
        {
            var store = new ConversationSessionStore(path);
            var first = store.ActiveId;
            AddSessionReply(store, "要删除的记录", "旧回复");
            store.Create();
            var second = store.ActiveId;
            store.Delete(first);
            Assert.AreEqual(second, store.ActiveId);
            Assert.AreEqual(1, store.GetSessions().Count);
            StringAssert.DoesNotContain("要删除的记录", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".bak"));
            store.Delete(second);
            var restored = new ConversationSessionStore(path);
            Assert.AreNotEqual(second, restored.ActiveId);
            Assert.AreEqual(1, restored.GetSessions().Count);
            Assert.IsEmpty(restored.History.Turns);
        }

        [Test] public void ClearingAllContextsKeepsVisibleHistoryAcrossSessions()
        {
            var store = new ConversationSessionStore(path);
            var first = store.ActiveId;
            AddSessionReply(store, "旧会话", "保留的第一段回复");
            store.Create();
            AddSessionReply(store, "当前会话", "保留的第二段回复");
            store.ClearAllContexts();
            Assert.AreEqual(0, store.Memory.Count);

            var restored = new ConversationSessionStore(path);
            Assert.AreEqual(0, restored.Memory.Count);
            Assert.AreEqual("保留的第二段回复", restored.History.Latest.Reply);
            restored.Open(first);
            Assert.AreEqual(0, restored.Memory.Count);
            Assert.AreEqual("保留的第一段回复", restored.History.Latest.Reply);
        }

        [Test] public void FailedSessionWriteRetainsTheActiveSessionForRetry()
        {
            var store = new ConversationSessionStore(path);
            var id = store.ActiveId;
            AddSessionReply(store, "原会话", "原回复");
            store.SaveActive();
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.AreEqual(AppErr.sessionStorage, Assert.Throws<AppException>(store.Create).Error);
            Assert.AreEqual(id, store.ActiveId);
            Assert.AreEqual("原回复", store.History.Latest.Reply);
            Assert.AreEqual(1, new ConversationSessionStore(path).GetSessions().Count);
            store.Create();
            Assert.AreEqual(2, new ConversationSessionStore(path).GetSessions().Count);
        }

        [Test] public void UnreadableSessionArchiveIsPreservedWhileTemporarySessionsRemainUsable()
        {
            File.WriteAllText(path, "{broken");
            var store = new ConversationSessionStore(path);
            Assert.IsNotEmpty(store.LoadError);
            store.Create();
            AddSessionReply(store, "临时记录", "临时回复");
            store.SaveActive();
            Assert.AreEqual("临时回复", store.History.Latest.Reply);
            Assert.AreEqual("{broken", File.ReadAllText(path));
        }

        private static void AddSessionReply(ConversationSessionStore store, string userText, string reply)
        {
            var turn = store.History.Begin(userText);
            store.History.UpdateReply(turn, reply);
            store.History.Finish(turn, AppErr.none, "", true);
            store.Memory.Add(userText, new DialogueDecision { reply = reply }, 6);
        }
    }
}
