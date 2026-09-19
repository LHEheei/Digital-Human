using System;
using System.IO;
using NUnit.Framework;

namespace SrtpAvatar.Tests
{
    public sealed class ProjectOptimizationTests
    {
        private string directory;
        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "srtp-optimization-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }
        [TearDown] public void TearDown()
        {
            Directory.Delete(directory, true);
        }

        [TestCase("不要清除记忆")]
        [TestCase("清除记忆是什么意思？")]
        [TestCase("你刚才说了清除记忆")]
        [TestCase("我不想重新开始对话")]
        [TestCase("\"忘记我\"")]
        public void MemoryClearRequiresExplicitCommand(string text)
        {
            Assert.IsFalse(ConversationMemory.IsClearRequest(text));
        }
        [TestCase("清除记忆")]
        [TestCase("请帮我清空所有记忆。")]
        public void ExplicitMemoryClearStillWorks(string text)
        {
            Assert.IsTrue(ConversationMemory.IsClearRequest(text));
        }
        [Test]
        public void CorruptMemoryRecoversLastGoodSave()
        {
            var path = Path.Combine(directory, "memory.json");
            var store = new LongTermMemoryStore(path);
            store.Remember("用户喜欢音乐", 20);
            store.Remember("用户喜欢电影", 20);
            File.WriteAllText(path, "{broken");
            var restored = new LongTermMemoryStore(path);
            Assert.AreEqual(1, restored.Count);
            StringAssert.Contains("用户喜欢音乐", restored.BuildContext(20));
        }
        [Test]
        public void ClearDoesNotResurrectBackupMemory()
        {
            var path = Path.Combine(directory, "memory.json");
            var store = new LongTermMemoryStore(path);
            store.Remember("用户喜欢音乐", 20);
            store.Remember("用户喜欢电影", 20);
            store.Clear();
            File.WriteAllText(path, "{broken");
            Assert.AreEqual(0, new LongTermMemoryStore(path).Count);
        }
    }
}