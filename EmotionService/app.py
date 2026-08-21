from __future__ import annotations

import logging
import threading

from fastapi import FastAPI, File, Header, HTTPException, UploadFile
from fastapi.concurrency import run_in_threadpool
from pydantic import BaseModel

from emotion_contract import EmotionResult, fusion
from inference import EmotionInference, InferenceError
from service_metrics import PerceptionMetrics

app = FastAPI(title="SRTP EmotionService", version="1.0.0")
inference = EmotionInference()
logger = logging.getLogger("emotion_service")
_last_face_outcome = ""
metrics = PerceptionMetrics()


class FusionRequest(BaseModel):
    face: dict | None = None
    voice: dict | None = None


@app.on_event("startup")
def warm_models() -> None:
    """Accept face traffic promptly while the slower voice model warms in background."""
    inference.warm_face()
    threading.Thread(target=_warm_voice_model, name="emotion-voice-warmup", daemon=True).start()


def _warm_voice_model() -> None:
    try:
        inference.warm_voice()
        logger.info("Voice emotion model warmup complete.")
    except Exception:
        logger.exception("Voice emotion model warmup failed.")


@app.get("/health")
def health() -> dict:
    models = inference.status()
    return {
        "status": "ok" if models["modelLoading"] == "ready" else "warming",
        "service": "EmotionService",
        "models": models,
        "perception": metrics.snapshot(),
    }


@app.post("/v1/emotions/face")
async def face(file: UploadFile = File(...), x_srtp_emotion_session: str | None = Header(default=None)) -> dict:
    global _last_face_outcome
    if file.content_type not in {"image/jpeg", "image/jpg"}:
        raise HTTPException(415, "Expected image/jpeg.")
    try:
        metrics.mark_request("face")
        result = await run_in_threadpool(inference.face, await file.read(), x_srtp_emotion_session or "")
        outcome = _face_outcome(result)
        metrics.mark_face_outcome(outcome)
        if outcome != _last_face_outcome:
            _last_face_outcome = outcome
            logger.info("Face perception state changed: %s", outcome)
        return result.as_dict()
    except InferenceError as exc:
        metrics.mark_failure("face")
        logger.warning("Face inference failed: %s", exc)
        raise HTTPException(422, str(exc)) from exc


@app.post("/v1/emotions/voice")
async def voice(file: UploadFile = File(...)) -> dict:
    if file.content_type not in {"audio/wav", "audio/x-wav"}:
        raise HTTPException(415, "Expected audio/wav.")
    if not inference.voice_ready:
        raise HTTPException(503, "Voice emotion model is warming; retry shortly.")
    try:
        metrics.mark_request("voice")
        return (await run_in_threadpool(inference.voice, await file.read())).as_dict()
    except InferenceError as exc:
        metrics.mark_failure("voice")
        logger.warning("Voice inference failed: %s", exc)
        raise HTTPException(422, str(exc)) from exc


@app.post("/v1/emotions/fusion")
def fuse(request: FusionRequest) -> dict:
    metrics.mark_request("fusion")
    face_result = EmotionResult(**request.face) if request.face else None
    voice_result = EmotionResult(**request.voice) if request.voice else None
    result = fusion(face_result, voice_result)
    if result.error:
        metrics.mark_failure("fusion")
    return result.as_dict()


def _face_outcome(result: EmotionResult) -> str:
    if result.usable:
        return f"usable:{result.label}"
    return f"unusable:{result.error or 'unknown'}"
