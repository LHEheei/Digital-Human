import io
from types import SimpleNamespace
import unittest
import wave

import numpy

from audio_quality import AudioSignalQuality, extract_active_pcm16_wav, has_speech_like_signal, measure_pcm16_wav
from emotion_contract import EmotionResult, EmotionScore, failure, fusion, map_voice_label
from face_expression import ExpressionCandidate, FaceExpressionClassifier, FaceExpressionSession, FaceExpressionSessions
from face_crop import crop_relative_face
from face_quality import assess_face_crop
from face_classifier import FaceClassification
from face_landmarker import BlendshapeFaceLandmarker
from inference import EmotionInference, InferenceError
from service_metrics import PerceptionMetrics


class EmotionContractTests(unittest.TestCase):

    def test_status_distinguishes_warming_and_ready_models(self):
        inference = EmotionInference()
        self.assertEqual("warming", inference.status()["modelLoading"])
        inference._face_landmarker._landmarker = object()
        inference._face_landmarker._face_detector = object()
        inference._face_classifier._model = object()
        inference._face_classifier._transforms = object()
        inference._voice_model = object()
        self.assertEqual("ready", inference.status()["modelLoading"])

    def test_perception_metrics_are_counts_only_and_do_not_include_media(self):
        metrics = PerceptionMetrics()
        metrics.mark_request("face")
        metrics.mark_request("face")
        metrics.mark_failure("face")
        metrics.mark_face_outcome("usable:joy")

        self.assertEqual(2, metrics.snapshot()["requests"]["face"])
        self.assertEqual(1, metrics.snapshot()["failures"]["face"])
        self.assertEqual("usable:joy", metrics.snapshot()["lastFaceOutcome"])

    def test_face_classifier_projects_unsupported_labels_to_neutral_and_rejects_ties(self):
        projected = FaceClassification.from_model_scores(
            {"Fear": 0.30, "Neutral": 0.10, "Happiness": 0.60}
        )
        self.assertEqual("joy", projected.label)
        self.assertAlmostEqual(0.60, projected.score)

        tied = FaceClassification.from_model_scores({"Anger": 0.46, "Happiness": 0.44, "Neutral": 0.10})
        self.assertEqual(("neutral", 0.65), tied.conservative_candidate())
        self.assertEqual(["angry", "joy", "neutral", "sorrow", "surprised"], [item.label for item in tied.as_scores()])

    def test_emotion_result_normalizes_a_safe_classifier_distribution(self):
        result = EmotionResult(
            "joy",
            0.8,
            "face",
            scores=[
                EmotionScore("joy", 1.5),
                {"label": "fear", "score": 0.9},
                {"label": "neutral", "score": -1.0},
            ],
        ).normalized()

        self.assertEqual(["joy", "neutral"], [item.label for item in result.scores])
        self.assertEqual(1.0, result.scores[0].score)
        self.assertEqual(0.0, result.scores[1].score)
        self.assertEqual(
            [{"label": "joy", "score": 1.0}, {"label": "neutral", "score": 0.0}],
            result.as_dict()["scores"],
        )

    def test_face_expression_uses_passive_baseline_without_blocking_output(self):
        session = FaceExpressionSession()
        neutral = self._blendshapes()
        for _ in range(7):
            result = session.observe(neutral)
            self.assertTrue(result.usable)
            self.assertFalse(result.error)
        self.assertTrue(session.observe(neutral).usable)
        self.assertTrue(session.is_calibrated)

        joy = self._blendshapes(mouthSmileLeft=0.4, mouthSmileRight=0.42, jawOpen=0.16)
        self.assertEqual("neutral", session.observe(joy).label)
        self.assertEqual("neutral", session.observe(joy).label)
        self.assertEqual("neutral", session.observe(joy).label)
        stable = session.observe(joy)
        self.assertEqual("joy", stable.label)
        self.assertGreater(stable.score, 0.5)

    def test_face_expression_change_during_passive_baseline_never_reports_a_calibration_error(self):
        session = FaceExpressionSession()
        neutral = self._blendshapes()
        for _ in range(7):
            session.observe(neutral)

        unstable = session.observe(self._blendshapes(mouthSmileLeft=0.4, mouthSmileRight=0.4))
        self.assertTrue(unstable.usable)
        self.assertFalse(unstable.error)
        self.assertFalse(session.is_calibrated)

        for _ in range(4):
            result = session.observe(
                neutral,
                ExpressionCandidate("joy", 0.8),
            )
        self.assertEqual("joy", result.label)

    def test_face_session_resets_stale_baseline_after_sustained_face_loss(self):
        sessions = FaceExpressionSessions()
        baseline = self._blendshapes()
        for _ in range(8):
            sessions.observe("device-a", baseline)

        for _ in range(5):
            sessions.observe_no_face("device-a")

        result = sessions.observe("device-a", baseline)
        self.assertTrue(result.usable)
        self.assertFalse(result.error)

    def test_face_classifier_neutralizes_ambiguous_or_weak_cues(self):
        baseline = self._blendshapes()
        ambiguous = self._blendshapes(
            browDownLeft=0.11,
            browDownRight=0.11,
            mouthPressLeft=0.11,
            mouthPressRight=0.11,
            mouthSmileLeft=0.11,
            mouthSmileRight=0.11,
        )
        self.assertEqual("neutral", FaceExpressionClassifier.classify(ambiguous, baseline).label)

    def test_negative_expression_needs_paired_cues_and_avoids_false_angry(self):
        baseline = self._blendshapes()
        brow_only = self._blendshapes(browDownLeft=0.5, browDownRight=0.5)
        paired = self._blendshapes(browDownLeft=0.5, browDownRight=0.5, mouthPressLeft=0.4, mouthPressRight=0.4)

        self.assertEqual("neutral", FaceExpressionClassifier.classify(brow_only, baseline).label)
        self.assertEqual("angry", FaceExpressionClassifier.classify(paired, baseline).label)

    def test_face_sessions_keep_calibration_per_device_and_do_not_share_baseline(self):
        sessions = FaceExpressionSessions()
        baseline = self._blendshapes()
        for _ in range(8):
            sessions.observe("device-a", baseline)
        result_a = sessions.observe("device-a", self._blendshapes(mouthSmileLeft=0.4, mouthSmileRight=0.4))
        result_b = sessions.observe("device-b", baseline)

        self.assertFalse(result_a.error)
        self.assertTrue(result_b.usable)
        self.assertFalse(result_b.error)

    def test_trained_face_candidate_is_available_on_the_first_frame(self):
        session = FaceExpressionSession()

        result = session.observe(
            self._blendshapes(mouthSmileLeft=0.4, mouthSmileRight=0.4),
            ExpressionCandidate("joy", 0.84),
        )

        self.assertTrue(result.usable)
        self.assertFalse(result.error)
        # The temporal filter may conservatively report neutral at first, but
        # the request is no longer rejected for a user-calibration step.
        self.assertEqual("neutral", result.label)

    def test_audio_noise_gate_rejects_silence_and_accepts_speech_like_signal(self):
        self.assertFalse(has_speech_like_signal(AudioSignalQuality(rms=0.0, active_frame_ratio=0.0)))
        self.assertFalse(has_speech_like_signal(AudioSignalQuality(rms=0.01, active_frame_ratio=0.01)))
        self.assertTrue(has_speech_like_signal(AudioSignalQuality(rms=0.01, active_frame_ratio=0.1, longest_active_seconds=0.8)))

    def test_audio_energy_vad_extracts_the_longest_active_phrase(self):
        samples = numpy.zeros(16_000 * 4, dtype="<i2")
        samples[16_000 : 16_000 + 16_000] = 4_000
        source = self._make_wav_from_samples(samples)
        quality = measure_pcm16_wav(source)
        active = extract_active_pcm16_wav(source, quality)

        self.assertTrue(has_speech_like_signal(quality))
        self.assertGreaterEqual(quality.longest_active_seconds, 0.95)
        with wave.open(io.BytesIO(active), "rb") as reader:
            self.assertGreater(reader.getnframes(), 16_000)
            self.assertLess(reader.getnframes(), 16_000 * 2)

    def test_face_crop_expands_relative_detection_without_leaving_image(self):
        image = numpy.arange(100, dtype=numpy.uint8).reshape(10, 10)
        crop = crop_relative_face(image, (0.4, 0.4, 0.2, 0.2), padding_ratio=0.0)

        self.assertEqual((2, 2), crop.shape)
        self.assertEqual(44, crop[0, 0])
        self.assertEqual(55, crop[1, 1])

    def test_face_quality_rejects_tiny_dark_or_blurry_crops(self):
        tiny = numpy.full((32, 32, 3), 120, dtype=numpy.uint8)
        dark = numpy.zeros((120, 120, 3), dtype=numpy.uint8)
        blurry = numpy.full((120, 120, 3), 120, dtype=numpy.uint8)
        usable = numpy.zeros((120, 120, 3), dtype=numpy.uint8)
        usable[::2, ::2] = 255

        self.assertIn("too small", assess_face_crop(tiny).error)
        self.assertIn("too dark", assess_face_crop(dark).error)
        self.assertIn("too blurry", assess_face_crop(blurry).error)
        self.assertTrue(assess_face_crop(usable).usable)

    def test_auxiliary_face_crop_defers_multiple_candidates_to_landmarker(self):
        detector = BlendshapeFaceLandmarker()
        detector._face_detector = self._FakeFaceDetector(2)

        image = numpy.zeros((80, 80, 3), dtype=numpy.uint8)
        self.assertIsNone(detector._detect_single_face_crop(image))

    class _FakeFaceDetector:
        def __init__(self, count: int):
            self._count = count

        def process(self, _image):
            box = SimpleNamespace(xmin=0.25, ymin=0.25, width=0.5, height=0.5)
            detection = SimpleNamespace(location_data=SimpleNamespace(relative_bounding_box=box))
            return SimpleNamespace(detections=[detection] * self._count)
    def test_label_mapping_accepts_only_project_categories(self):
        self.assertEqual("surprised", map_voice_label("surprise"))
        self.assertEqual("joy", map_voice_label("\u5f00\u5fc3/happy"))
        self.assertEqual("sorrow", map_voice_label("\u96be\u8fc7/sad"))
        self.assertEqual("neutral", map_voice_label("fear"))

    def test_fusion_uses_agreement_and_neutralizes_conflicts(self):
        now = 10_000
        face = EmotionResult("joy", 0.7, "face", "", 9_000)
        voice = EmotionResult("angry", 0.8, "voice", "", 9_000)
        result = fusion(face, voice, now)
        self.assertEqual("neutral", result.label)
        self.assertEqual("fusion", result.source)
        self.assertEqual("", result.error)
        self.assertLess(result.score, 0.5)

        agreed = fusion(face, EmotionResult("joy", 0.8, "voice", "", 9_000), now)
        self.assertEqual("joy", agreed.label)
        self.assertGreater(agreed.score, 0.7)

    def test_fusion_caps_a_single_source(self):
        result = fusion(EmotionResult("joy", 1.0, "face", "", 9_000), None, 10_000)
        self.assertEqual("joy", result.label)
        self.assertEqual(0.75, result.score)

    def test_fusion_rejects_stale_or_low_confidence_results(self):
        stale = EmotionResult("joy", 0.9, "face", "", 1)
        low = EmotionResult("angry", 0.4, "voice", "", 10_000)
        self.assertTrue(fusion(stale, None, 10_000).error)
        self.assertTrue(fusion(None, low, 10_000).error)

    def test_failure_has_normalized_source_and_error(self):
        result = failure("face", "No face")
        self.assertEqual("face", result.source)
        self.assertEqual("neutral", result.label)
        self.assertEqual("No face", result.error)

    def test_wav_validation_accepts_required_format_and_rejects_short_audio(self):
        valid = self._make_wav(16_000)
        EmotionInference._validate_wav(valid)
        with self.assertRaisesRegex(InferenceError, "No clear speech"):
            EmotionInference().voice(valid)

        with self.assertRaises(InferenceError):
            EmotionInference._validate_wav(self._make_wav(15_999))

    def test_voice_prediction_uses_highest_score_label(self):
        label, score = EmotionInference._extract_voice_prediction(
            [{"labels": ["\u751f\u6c14/angry", "\u5f00\u5fc3/happy"], "scores": [0.2, 0.8]}]
        )
        self.assertEqual("\u5f00\u5fc3/happy", label)
        self.assertEqual(0.8, score)

    @staticmethod
    def _make_wav(frame_count: int) -> bytes:
        return EmotionContractTests._make_wav_from_samples(numpy.zeros(frame_count, dtype="<i2"))

    @staticmethod
    def _make_wav_from_samples(samples: numpy.ndarray) -> bytes:
        stream = io.BytesIO()
        with wave.open(stream, "wb") as writer:
            writer.setnchannels(1)
            writer.setsampwidth(2)
            writer.setframerate(16_000)
            writer.writeframes(samples.astype("<i2", copy=False).tobytes())
        return stream.getvalue()

    @staticmethod
    def _blendshapes(**overrides: float) -> dict[str, float]:
        values = {
            "mouthSmileLeft": 0.05,
            "mouthSmileRight": 0.05,
            "browDownLeft": 0.05,
            "browDownRight": 0.05,
            "mouthFrownLeft": 0.05,
            "mouthFrownRight": 0.05,
            "mouthPressLeft": 0.05,
            "mouthPressRight": 0.05,
            "eyeWideLeft": 0.05,
            "eyeWideRight": 0.05,
            "browOuterUpLeft": 0.05,
            "browOuterUpRight": 0.05,
            "browInnerUp": 0.05,
            "jawOpen": 0.05,
        }
        values.update(overrides)
        return values
