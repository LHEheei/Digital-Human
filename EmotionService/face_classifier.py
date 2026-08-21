from __future__ import annotations

from dataclasses import dataclass
from hashlib import sha256
from pathlib import Path
from typing import Mapping

import numpy

from emotion_contract import EmotionScore
from errors import InferenceError


MODEL_NAME = "enet_b0_8_best_afew"
# The official HSE release is a legacy PyTorch module pickle.  The exact digest
# is pinned before it is loaded with weights_only=False; a changed remote file
# is rejected rather than executed.
MODEL_SHA256 = "47c1423f3e6f50e3750bf7b0eda7db947c9ce0c2637e1766bf2187eddc652b17"
MODEL_LABELS = (
    "Anger", "Contempt", "Disgust", "Fear",
    "Happiness", "Neutral", "Sadness", "Surprise",
)
PROJECT_LABELS = ("joy", "angry", "sorrow", "surprised", "neutral")
MODEL_TO_PROJECT = {
    "Anger": "angry",
    "Happiness": "joy",
    "Sadness": "sorrow",
    "Surprise": "surprised",
    # The first release deliberately has no fear/disgust/contempt UI state.
    # Preserve uncertainty by folding those unsupported classes into neutral.
    "Contempt": "neutral",
    "Disgust": "neutral",
    "Fear": "neutral",
    "Neutral": "neutral",
}


@dataclass(frozen=True)
class FaceClassification:
    label: str
    score: float
    margin: float
    scores: Mapping[str, float]

    @staticmethod
    def from_model_scores(raw_scores: Mapping[str, float]) -> "FaceClassification":
        projected = {label: 0.0 for label in PROJECT_LABELS}
        for model_label, score in raw_scores.items():
            target = MODEL_TO_PROJECT.get(model_label, "neutral")
            projected[target] += max(0.0, float(score))

        total = sum(projected.values())
        if total <= 0.0:
            return FaceClassification("neutral", 0.0, 0.0, projected)
        normalized = {label: value / total for label, value in projected.items()}
        ranked = sorted(normalized.items(), key=lambda item: item[1], reverse=True)
        return FaceClassification(ranked[0][0], ranked[0][1], ranked[0][1] - ranked[1][1], normalized)

    def conservative_candidate(self) -> tuple[str, float]:
        # A weak or near-tied classifier output is presented as a neutral
        # apparent-expression cue rather than a confident emotion claim.
        if self.label != "neutral" and (self.score < 0.45 or self.margin < 0.10):
            return "neutral", 0.65
        return self.label, min(0.90, max(0.0, self.score))

    def as_scores(self) -> list[EmotionScore]:
        return [EmotionScore(label, score) for label, score in sorted(self.scores.items())]


class HseFaceEmotionClassifier:
    """CPU facial-expression classifier over an already cropped face image."""

    def __init__(self) -> None:
        self._model = None
        self._transforms = None

    @property
    def loaded(self) -> bool:
        return self._model is not None and self._transforms is not None

    def warmup(self) -> None:
        self._ensure_model()

    def classify(self, face_bgr: numpy.ndarray) -> FaceClassification:
        model, transforms = self._ensure_model()
        try:
            import cv2
            import torch
            from PIL import Image

            rgb = cv2.cvtColor(face_bgr, cv2.COLOR_BGR2RGB)
            tensor = transforms(Image.fromarray(rgb)).unsqueeze(0)
            with torch.inference_mode():
                features = model(tensor)
                logits = numpy.dot(features.cpu().numpy(), self._classifier_weights.T) + self._classifier_bias
            probabilities = _softmax(logits[0])
            raw_scores = {label: float(probabilities[index]) for index, label in enumerate(MODEL_LABELS)}
            return FaceClassification.from_model_scores(raw_scores)
        except InferenceError:
            raise
        except Exception as exc:
            raise InferenceError(f"Face expression classifier unavailable: {type(exc).__name__}") from exc

    def _ensure_model(self):
        if self.loaded:
            return self._model, self._transforms
        try:
            import torch
            from hsemotion.facial_emotions import get_model_path
            from torchvision import transforms

            path = Path(get_model_path(MODEL_NAME))
            if not path.is_file() or _sha256_file(path) != MODEL_SHA256:
                raise InferenceError("Face expression model integrity check failed.")
            # The digest check above pins the single trusted HSE release before
            # loading its legacy module format.  Do not remove that boundary or
            # load unverified model files with weights_only=False.
            model = torch.load(path, map_location="cpu", weights_only=False)
            classifier = model.classifier[0] if isinstance(model.classifier, torch.nn.Sequential) else model.classifier
            self._classifier_weights = classifier.weight.cpu().detach().numpy()
            self._classifier_bias = classifier.bias.cpu().detach().numpy()
            model.classifier = torch.nn.Identity()
            self._model = model.eval()
            self._transforms = transforms.Compose(
                [
                    transforms.Resize((224, 224)),
                    transforms.ToTensor(),
                    transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]),
                ]
            )
            return self._model, self._transforms
        except InferenceError:
            raise
        except Exception as exc:
            raise InferenceError(f"Face expression model unavailable: {type(exc).__name__}") from exc


def _sha256_file(path: Path) -> str:
    digest = sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _softmax(values: numpy.ndarray) -> numpy.ndarray:
    shifted = values - numpy.max(values)
    exponentials = numpy.exp(shifted)
    return exponentials / numpy.sum(exponentials)
