using System;
using NUnit.Framework;

namespace SrtpAvatar.Tests
{
    public sealed class InteractionPolishTests
    {
        [TestCase("麦克风权限请求超时，请重试。")]
        [TestCase("没有麦克风权限。")]
        [TestCase("speech timeout")]
        public void PermissionFailuresRequireActionRegardlessOfWording(string message)
        {
            var error = new AppException(AppErr.micPerm, message);
            Assert.IsFalse(AutoConversationPolicy.ShouldQuietlyRetry(error));
            Assert.IsTrue(AutoConversationPolicy.RequiresListeningAction(new InvalidOperationException("wrapped", error)));
        }
        [TestCase("系统语音识别超时。")]
        [TestCase("网络连接超时。")]
        public void ServiceTimeoutIsVisibleAndNotANormalNoSpeechResult(string message)
        { Assert.IsFalse(AutoConversationPolicy.ShouldQuietlyRetry(new AppException(AppErr.speech, message))); }
        [Test] public void ListeningRetryDelayBacksOffAndHasACeiling()
        {
            var expected = new[] { 1.2f, 1.2f, 2.4f, 4.8f, 9.6f, 19.2f, 30f, 30f };
            for (var failures = 0; failures < expected.Length; failures++)
                Assert.AreEqual(expected[failures], AutoConversationPolicy.RetryDelay(1.2f, failures), .001f);
            Assert.AreEqual(30f, AutoConversationPolicy.RetryDelay(1.2f, 100));
            Assert.AreEqual(60f, AutoConversationPolicy.RetryDelay(60f, 8), "Respect an explicitly slower base delay.");
        }
        [TestCase("你好像有些累了。")]
        [TestCase("你好好休息。")]
        [TestCase("再见面时我们可以继续聊。")]
        [TestCase("不要说你好。")]
        [TestCase("“你好”是一种问候。")]
        [TestCase("她说：\"你好\"。")]
        [TestCase("「再见」是书名。")]
        [TestCase("没问题才怪。")]
        [TestCase("我理解不了这件事。")]
        [TestCase("不要建议我休息。")]
        [TestCase("我们不需要慢慢来。")]
        [TestCase("“慢慢来”是这段话的标题。")]
        public void SimilarNegatedAndQuotedPhrasesDoNotTriggerLocalGestures(string text)
        {
            var cue = new SentenceMotionPlanner().Plan(text, 0, 0);
            Assert.AreEqual(AvatarGesture.none, cue.Gesture);
            Assert.AreEqual(AvatarEmotion.neutral, cue.Emotion);
        }
        [TestCase("你好。")]
        [TestCase("您好！今天想聊什么？")]
        [TestCase("你好呀，我们开始吧。")]
        [TestCase("嗨！")]
        [TestCase("再见，明天聊。")]
        public void RealGreetingsStillWave(string text)
        {
            var cue = new SentenceMotionPlanner().Plan(text, 0, 0);
            Assert.AreEqual(AvatarGesture.wave, cue.Gesture); Assert.AreEqual(AvatarEmotion.joy, cue.Emotion);
        }
        [Test] public void RejectedGreetingDoesNotConsumeGestureCooldown()
        {
            var planner = new SentenceMotionPlanner();
            Assert.AreEqual(AvatarGesture.none, planner.Plan("你好像累了。", 0, 0).Gesture);
            Assert.AreEqual(AvatarGesture.wave, planner.Plan("你好！", 0, 1).Gesture);
            Assert.AreEqual(AvatarGesture.none, planner.Plan("你好！", 0, 2).Gesture);
        }
        [Test] public void AffirmationAndEmpathyRemainAvailableOutsideQuotedOrNegatedClauses()
        {
            Assert.AreEqual(AvatarGesture.nod, new SentenceMotionPlanner().Plan("明白了。", 0, 0).Gesture);
            Assert.AreEqual(AvatarGesture.nod, new SentenceMotionPlanner().Plan("不用担心，慢慢来。", 0, 0).Gesture);
            Assert.AreEqual(AvatarGesture.explain, new SentenceMotionPlanner().Plan("你提到“你好”，可以先说明使用场景。", 0, 0).Gesture);
        }
        [Test] public void QuotedPhrasesStaySuppressedAcrossPlaybackSentences()
        {
            var planner = new SentenceMotionPlanner();
            var sentences = SentenceMotionPlanner.SplitSentences("他写道：“先看看。建议先停一下。”所以可以稍后继续。");

            Assert.AreEqual(AvatarGesture.none, planner.Plan(sentences[0], 0, 0).Gesture);
            Assert.AreEqual(AvatarGesture.none, planner.Plan(sentences[1], 1, 1).Gesture);
            Assert.AreEqual(AvatarGesture.explain, planner.Plan(sentences[2], 2, 2).Gesture);
        }
        [Test] public void UnclosedQuoteDoesNotAffectTheNextReply()
        {
            var planner = new SentenceMotionPlanner();
            Assert.AreEqual(AvatarGesture.none, planner.Plan("他说：“慢慢来。", 0, 0).Gesture);
            Assert.AreEqual(AvatarGesture.wave, planner.Plan("你好！", 0, 1).Gesture);
        }
        [Test] public void HistoryRevisionsChangeOnlyWhenVisibleDataChanges()
        {
            var history = new ConversationHistory(); var a = history.Begin("第一轮"); var b = history.Begin("第二轮");
            var revision = history.Revision;
            history.UpdateReply(a, "回复");
            Assert.AreEqual(revision + 1, history.Revision); Assert.AreEqual(1, a.Revision); Assert.AreEqual(0, b.Revision);
            history.UpdateReply(a, "回复"); Assert.AreEqual(revision + 1, history.Revision);
            history.Finish(a, AppErr.none, "", true); Assert.AreEqual(revision + 2, history.Revision);
            history.Finish(a, AppErr.none, "", true); Assert.AreEqual(revision + 2, history.Revision);
        }
    }
}
