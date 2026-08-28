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
        public void SpeechRecognitionTurn_PreservesRecognizedTextAndCapturedAudio()
        {
            var turn = new SpeechRecognitionTurn("  你好  ", new byte[] { 1, 2, 3 });

            Assert.AreEqual("你好", turn.Text);
            Assert.IsTrue(turn.HasAudio);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, turn.AudioWav);
        }
        [Test]
        public void CoreServices_RejectMissingConfiguration()
        {
            Assert.Throws<ArgumentNullException>(() => new DialogueService(null));
            Assert.Throws<ArgumentNullException>(() => new SpeechService(null, null));
            Assert.Throws<ArgumentNullException>(() => new CarCommunicationService(null, null));
            Assert.Throws<ArgumentNullException>(() => new HttpEmoService(null));
        }

        [TestCase("http://127.0.0.1:8000", false)]
        [TestCase("https://example.test/api/", false)]
        [TestCase("", true)]
        [TestCase("not a url", true)]
        public void HttpEmoService_ValidatesBaseUrlAtConstruction(string source, bool invalid)
        {
            var config = ScriptableObject.CreateInstance<AppConfig>();
            try
            {
                config.emotionServiceBaseUrl = source;
                if (invalid)
                {
                    Assert.Throws<ArgumentException>(() => new HttpEmoService(config));
                }
                else
                {
                    Assert.DoesNotThrow(() => new HttpEmoService(config));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
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
        public void LongTermMemoryStore_BuildContextDoesNotDiscardStoredItems()
        {
            memoryFilePath = Path.Combine(Path.GetTempPath(), "srtp-memory-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new LongTermMemoryStore(memoryFilePath);
            Assert.IsTrue(store.Remember("A\nB\nC", 3));

            var context = store.BuildContext(1);

            Assert.AreEqual(3, store.Count);
            StringAssert.Contains("- C", context);
            StringAssert.DoesNotContain("- A", context);
        }

        [Test]
        public void ConversationMemory_BuildContextDoesNotDiscardStoredTurns()
        {
            var memory = new ConversationMemory();
            var decision = new DialogueDecision { reply = "reply" };
            memory.Add("first", decision, 3);
            memory.Add("second", decision, 3);
            memory.Add("third", decision, 3);

            var context = memory.BuildContext(1);

            Assert.AreEqual(3, memory.Count);
            StringAssert.Contains("third", context);
            StringAssert.DoesNotContain("first", context);
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
        public void UserEmo_NormalizesOnlySupportedClassifierScores()
        {
            var emotion = new UserEmo
            {
                scores = new[]
                {
                    new EmotionScore { label = "joy", score = 2f },
                    new EmotionScore { label = "fear", score = .8f },
                    new EmotionScore { label = "joy", score = .6f },
                    new EmotionScore { label = "neutral", score = -.2f }
                }
            }.Normalize();

            Assert.AreEqual(2, emotion.scores.Length);
            Assert.AreEqual("joy", emotion.scores[0].label);
            Assert.AreEqual(1f, emotion.scores[0].score);
            Assert.AreEqual("neutral", emotion.scores[1].label);
            Assert.AreEqual(0f, emotion.scores[1].score);
        }

        [Test]
        public void HttpEmoService_ParsesFaceClassifierDistribution()
        {
            const string json = "{\"label\":\"joy\",\"score\":0.8,\"source\":\"face\",\"scores\":[{\"label\":\"joy\",\"score\":0.7},{\"label\":\"fear\",\"score\":0.2}]}";

            Assert.IsTrue(HttpEmoService.TryParseResult(json, "face", out var result));
            Assert.AreEqual(1, result.scores.Length);
            Assert.AreEqual("joy", result.scores[0].label);
            Assert.AreEqual(.7f, result.scores[0].score);
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
        public void HttpEmoService_ExtractsSafeValidationErrorDetail()
        {
            Assert.IsTrue(HttpEmoService.TryParseErrorDetail("{\"detail\":\"No clear speech detected.\"}", out var detail));
            Assert.AreEqual("No clear speech detected.", detail);
            Assert.IsFalse(HttpEmoService.TryParseErrorDetail("{broken", out _));
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
        public void UserEmo_FreshnessRejectsExpiredResult()
        {
            var emotion = new UserEmo { label = "joy", score = .8f, source = "face", capturedAtUnixMs = 1000 };
            Assert.IsTrue(emotion.IsFresh(3000, 2000));
            Assert.IsFalse(emotion.IsFresh(3001, 2000));
            Assert.IsNull(EmotionSamplingPolicy.FreshOrNull(emotion, 3001, 2000));
            Assert.AreSame(emotion, EmotionSamplingPolicy.FreshOrNull(emotion, 3000, 2000));
        }

        [Test]
        public void EmotionResponsePolicy_AppliesOnlyConfidentFusionResult()
        {
            var decision = new DialogueDecision { emotion = "neutral", gesture = "none" };
            var applied = EmotionResponsePolicy.Apply(decision, new UserEmo { label = "sorrow", score = .7f, source = "fusion" });
            Assert.AreEqual("sorrow", applied.emotion);
            Assert.AreEqual("nod", applied.gesture);
            Assert.AreEqual("好的，我在。", applied.reply);

            var lowConfidence = EmotionResponsePolicy.Apply(new DialogueDecision { emotion = "joy" }, new UserEmo { label = "angry", score = .4f, source = "fusion" });
            Assert.AreEqual("joy", lowConfidence.emotion);
        }

        [Test]
        public void EmotionResponsePolicy_RejectsMissingDecision()
        {
            Assert.Throws<ArgumentNullException>(() => EmotionResponsePolicy.Apply(null, null));
        }
        [Test]
        public void EmotionResponsePolicy_PreservesGentleEmotionReflection()
        {
            var decision = new DialogueDecision { reply = "听起来你可能有点累。", gesture = "none" };
            var applied = EmotionResponsePolicy.Apply(decision, new UserEmo { label = "neutral", score = .9f, source = "fusion" });

            Assert.AreEqual("听起来你可能有点累。", applied.reply);
        }

        [Test]
        public void EmotionResponsePolicy_RemovesTechnicalEmotionDisclosure()
        {
            var decision = new DialogueDecision { reply = "我通过摄像头识别出你很难过。", gesture = "none" };
            var applied = EmotionResponsePolicy.Apply(decision, new UserEmo { label = "sorrow", score = .9f, source = "fusion" });

            Assert.AreEqual("我可能理解得不完全准确，但愿意听你多说一点。", applied.reply);
        }

        [Test]
        public void Pcm16Wav_ExtractsCircularAudioAndWritesWaveHeader()
        {
            var samples = new[] { 0f, .1f, .2f, .3f, .4f, .5f };
            var extracted = Pcm16Wav.ExtractLatestMono(samples, 1, 2, 4);
            CollectionAssert.AreEqual(new[] { .4f, .5f, 0f, .1f }, extracted);

            var wav = Pcm16Wav.EncodeMono(extracted, 16000);
            Assert.AreEqual(52, wav.Length);
            Assert.AreEqual((byte)'R', wav[0]);
            Assert.AreEqual((byte)'W', wav[8]);
        }

        [Test]
        public void CameraFrameTransform_RotatesPortraitCameraFramesUpright()
        {
            var source = new[]
            {
                new Color32(1, 0, 0, 255), new Color32(2, 0, 0, 255),
                new Color32(3, 0, 0, 255), new Color32(4, 0, 0, 255),
                new Color32(5, 0, 0, 255), new Color32(6, 0, 0, 255)
            };

            var rotated = CameraFrameTransform.OrientAndScale(source, 2, 3, 90, false, 10, out var width, out var height);

            Assert.AreEqual(3, width);
            Assert.AreEqual(2, height);
            CollectionAssert.AreEqual(new byte[] { 5, 3, 1, 6, 4, 2 }, System.Array.ConvertAll(rotated, pixel => pixel.r));
        }

        [Test]
        public void CameraFrameTransform_AppliesVerticalMirrorBeforeEncoding()
        {
            var source = new[]
            {
                new Color32(1, 0, 0, 255), new Color32(2, 0, 0, 255),
                new Color32(3, 0, 0, 255), new Color32(4, 0, 0, 255)
            };

            var mirrored = CameraFrameTransform.OrientAndScale(source, 2, 2, 0, true, 10, out _, out _);

            CollectionAssert.AreEqual(new byte[] { 3, 4, 1, 2 }, System.Array.ConvertAll(mirrored, pixel => pixel.r));
        }

        [Test]
        public void CameraPreviewTransform_AppliesDeviceOrientationWithoutChangingCaptureData()
        {
            Assert.AreEqual(-90f, CameraPreviewTransform.GetZRotation(90));
            Assert.AreEqual(new Vector2(135f, 180f), CameraPreviewTransform.GetDisplayBounds(new Vector2(180f, 135f), 90));

            var mirroredUv = CameraPreviewTransform.GetUvRect(true);
            Assert.AreEqual(1f, mirroredUv.y);
            Assert.AreEqual(-1f, mirroredUv.height);
        }

        [Test]
        public void EmotionSamplingPolicy_UsesClampedIntervals()
        {
            Assert.IsFalse(EmotionSamplingPolicy.IsDue(0.05f, 0f, 0f));
            Assert.IsTrue(EmotionSamplingPolicy.IsDue(0.1f, 0f, 0f));
        }

        [Test]
        public void EmotionSamplingPolicy_OnlyTreatsRecentShortFaceLossAsTransient()
        {
            var face = new UserEmo { label = "joy", score = .8f, source = "face", capturedAtUnixMs = 1000 };

            Assert.IsTrue(EmotionSamplingPolicy.IsTransientFaceLoss(face, 1, 3000));
            Assert.IsFalse(EmotionSamplingPolicy.IsTransientFaceLoss(face, EmotionSamplingPolicy.ConsecutiveFaceMissesBeforeUnavailable, 3000));
            Assert.IsFalse(EmotionSamplingPolicy.IsTransientFaceLoss(face, 1, 4001));
        }

        [Test]
        public void LocalEmotionFusion_UsesRecentOnDeviceFaceResultWithoutServer()
        {
            var now = 10000L;
            var fusion = LocalEmotionFusion.Fuse(
                new UserEmo { label = "joy", score = .82f, source = "face", capturedAtUnixMs = now },
                null,
                now);

            Assert.AreEqual("joy", fusion.label);
            Assert.AreEqual("fusion", fusion.source);
            Assert.AreEqual(.82f, fusion.score);
        }

        [Test]
        public void LocalEmotionFusion_FallsBackToLowConfidenceNeutralForExpiredInputs()
        {
            var fusion = LocalEmotionFusion.Fuse(
                new UserEmo { label = "angry", score = .9f, source = "face", capturedAtUnixMs = 1 },
                null,
                10000);

            Assert.AreEqual("neutral", fusion.label);
            Assert.AreEqual(.45f, fusion.score);
        }

        [Test]
        public void FaceEmotionTemporalFilter_PassesStrongNonNeutralSignalImmediately()
        {
            var filter = new FaceEmotionTemporalFilter();

            var stable = filter.Observe(FaceSample("joy", .78f, .12f, 1));

            Assert.AreEqual("joy", stable.label);
            Assert.Greater(stable.score, .70f);
            Assert.AreEqual(1, stable.capturedAtUnixMs);
        }

        [Test]
        public void FaceEmotionTemporalFilter_StrongNewSignalIsNotHiddenByPreviousLabel()
        {
            var filter = new FaceEmotionTemporalFilter();
            Assert.AreEqual("neutral", filter.Observe(FaceSample("neutral", .85f, .85f, 1)).label);

            var changed = filter.Observe(FaceSample("angry", .78f, .10f, 2));

            Assert.AreEqual("angry", changed.label);
            Assert.Greater(changed.score, .70f);
            Assert.AreEqual(2, changed.capturedAtUnixMs);
        }

        [Test]
        public void FaceEmotionTemporalFilter_ConvertsNearTieToConservativeNeutral()
        {
            var filter = new FaceEmotionTemporalFilter();
            UserEmo result = null;
            for (var index = 0; index < 5; index++)
            {
                result = filter.Observe(new UserEmo
                {
                    label = "angry",
                    score = .42f,
                    source = "face",
                    capturedAtUnixMs = index + 1,
                    scores = new[]
                    {
                        new EmotionScore { label = "angry", score = .42f },
                        new EmotionScore { label = "sorrow", score = .38f },
                        new EmotionScore { label = "neutral", score = .20f }
                    }
                });
            }

            Assert.NotNull(result);
            Assert.AreEqual("neutral", result.label);
            Assert.AreEqual(.65f, result.score);
        }

        [Test]
        public void EmotionEvaluationSession_CollectsOnlyAfterLabelSelectionAndStopsAtTarget()
        {
            var session = new EmotionEvaluationSession(2);
            var raw = FaceSample("joy", .8f, .1f, 1000);
            var stabilized = FaceSample("joy", .75f, .15f, 1000);

            Assert.IsFalse(session.Observe(raw, stabilized));
            Assert.IsTrue(session.Start("joy"));
            Assert.IsTrue(session.Observe(raw, stabilized));
            Assert.IsTrue(session.IsCollecting);
            Assert.IsTrue(session.Observe(raw, stabilized));
            Assert.IsFalse(session.IsCollecting);
            Assert.AreEqual(2, session.Count("joy"));
            Assert.IsFalse(session.Observe(raw, stabilized));
        }

        [Test]
        public void EmotionEvaluationSession_RecordsRawDistributionAndFinalDecisionInCsv()
        {
            var session = new EmotionEvaluationSession(1);
            var raw = FaceSample("joy", .8f, .1f, 1000);
            var stabilized = FaceSample("neutral", .65f, .65f, 1000);

            Assert.IsTrue(session.Start("sorrow"));
            Assert.IsTrue(session.Observe(raw, stabilized));
            var csv = session.ToCsv();

            StringAssert.Contains("intended_label,raw_label,raw_score,joy_score", csv);
            StringAssert.Contains("sorrow,joy,0.800000,0.800000", csv);
            StringAssert.Contains("neutral,0.650000,1000", csv);
            StringAssert.DoesNotContain("image", csv.ToLowerInvariant());
            StringAssert.DoesNotContain("audio", csv.ToLowerInvariant());
        }

        [Test]
        public void EmotionEvaluationSession_RejectsUnknownLabelAndClearRemovesRecords()
        {
            var session = new EmotionEvaluationSession(1);
            Assert.IsFalse(session.Start("happy"));
            Assert.IsTrue(session.Start("neutral"));
            Assert.IsTrue(session.Observe(FaceSample("neutral", .9f, .9f, 1), FaceSample("neutral", .9f, .9f, 1)));

            session.Clear();

            Assert.AreEqual(0, session.Records.Count);
            Assert.IsFalse(session.IsCollecting);
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

        [Test]
        public void AutoConversationPolicy_TreatsSilenceAsNormalAndKeepsOtherErrorsVisible()
        {
            Assert.IsTrue(AutoConversationPolicy.HasUsableText("你好"));
            Assert.IsFalse(AutoConversationPolicy.HasUsableText("  "));
            Assert.IsTrue(AutoConversationPolicy.ShouldQuietlyRetry("没有听到说话。"));
            Assert.IsTrue(AutoConversationPolicy.ShouldQuietlyRetry("离线语音模型正在准备，请稍后重试。"));
            Assert.IsTrue(AutoConversationPolicy.ShouldQuietlyRetry(new AppException(AppErr.speech, "识别失败", new InvalidOperationException("没有识别到语音内容。"))));
            Assert.IsFalse(AutoConversationPolicy.ShouldQuietlyRetry("系统语音识别网络失败。"));
            Assert.AreEqual(1.2f, AutoConversationPolicy.NormalizeRetryDelay(0f));
            Assert.AreEqual(.25f, AutoConversationPolicy.NormalizeRetryDelay(.1f));
        }

        [Test]
        public void AutoConversationPolicy_FormatsImmediateQuestionAndReplyWithoutConfirmation()
        {
            Assert.AreEqual("你：今天怎么样？\n\n她：我在这里，想听你说。", AutoConversationPolicy.FormatTurn(" 今天怎么样？ ", " 我在这里，想听你说。 "));
            Assert.AreEqual("你：（未识别到语音）\n\n她：（暂无回复）", AutoConversationPolicy.FormatTurn(" ", " "));
        }

        [Test]
        public void VoiceEmotionCalibrationPolicy_DowngradesUncorroboratedSorrowToNeutral()
        {
            var raw = new UserEmo
            {
                label = "sorrow",
                score = .91f,
                source = "voice",
                capturedAtUnixMs = 1000L,
                scores = new[]
                {
                    new EmotionScore { label = "sorrow", score = .91f },
                    new EmotionScore { label = "neutral", score = .04f }
                }
            };

            var calibrated = VoiceEmotionCalibrationPolicy.Calibrate(raw, "你好", null, 1200L);

            Assert.AreEqual("neutral", calibrated.label);
            Assert.GreaterOrEqual(calibrated.score, .55f);
        }

        [Test]
        public void VoiceEmotionCalibrationPolicy_KeepsSorrowWithSemanticOrFaceEvidence()
        {
            var raw = new UserEmo { label = "sorrow", score = .8f, source = "voice", capturedAtUnixMs = 1000L };
            Assert.AreEqual("sorrow", VoiceEmotionCalibrationPolicy.Calibrate(raw, "我今天很难过", null, 1200L).label);

            var face = new UserEmo { label = "sorrow", score = .7f, source = "face", capturedAtUnixMs = 1100L };
            Assert.AreEqual("sorrow", VoiceEmotionCalibrationPolicy.Calibrate(raw, "你好", face, 1200L).label);
        }

        private static UserEmo FaceSample(string label, float labelScore, float neutralScore, long capturedAt)
        {
            return new UserEmo
            {
                label = label,
                score = labelScore,
                source = "face",
                capturedAtUnixMs = capturedAt,
                scores = new[]
                {
                    new EmotionScore { label = label, score = labelScore },
                    new EmotionScore { label = "neutral", score = neutralScore },
                    new EmotionScore { label = "angry", score = label == "angry" ? labelScore : .02f },
                    new EmotionScore { label = "sorrow", score = .04f },
                    new EmotionScore { label = "surprised", score = .04f }
                }
            };
        }
    }
}
