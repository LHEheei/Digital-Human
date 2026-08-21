from __future__ import annotations

import io
import wave
from dataclasses import dataclass

import numpy


@dataclass(frozen=True)
class AudioSignalQuality:
    """Non-content audio quality values used to avoid classifying silence as emotion."""

    rms: float
    active_frame_ratio: float
    longest_active_seconds: float = 0.0
    speech_duration_seconds: float = 0.0
    speech_start_frame: int = -1
    speech_end_frame: int = -1


def measure_pcm16_wav(wav_bytes: bytes) -> AudioSignalQuality:
    """Measure signal energy only; no audio samples are retained or logged."""
    with wave.open(io.BytesIO(wav_bytes), "rb") as reader:
        samples = numpy.frombuffer(reader.readframes(reader.getnframes()), dtype="<i2").astype(numpy.float32) / 32768.0

    if samples.size == 0:
        return AudioSignalQuality(0.0, 0.0)

    rms = float(numpy.sqrt(numpy.mean(numpy.square(samples))))
    frame_size = 320  # 20 ms at the required 16 kHz sample rate.
    frame_count = samples.size // frame_size
    if frame_count == 0:
        return AudioSignalQuality(rms, 0.0)

    frames = samples[: frame_count * frame_size].reshape(frame_count, frame_size)
    frame_rms = numpy.sqrt(numpy.mean(numpy.square(frames), axis=1))
    # A small adaptive component prevents a fixed gate from treating a quiet
    # room and a noisy room identically.  This is deliberately an energy VAD,
    # not a claim that every active frame contains intelligible speech.
    noise_floor = float(numpy.percentile(frame_rms, 20))
    active = frame_rms >= max(0.006, noise_floor * 3.0)
    active_frame_ratio = float(numpy.mean(active))
    start, end = _longest_active_region(active)
    duration = max(0, end - start) * frame_size / 16_000.0 if start >= 0 else 0.0
    return AudioSignalQuality(rms, active_frame_ratio, duration, duration, start, end)


def has_speech_like_signal(quality: AudioSignalQuality) -> bool:
    """A conservative noise gate, not a replacement for speech recognition or VAD."""
    return (
        quality.rms >= 0.003
        and quality.active_frame_ratio >= 0.03
        and quality.longest_active_seconds >= 0.60
    )


def extract_active_pcm16_wav(wav_bytes: bytes, quality: AudioSignalQuality) -> bytes:
    """Return the longest active PCM16 region with a small temporal margin.

    The returned bytes live only for the immediate inference request.  This
    removes surrounding silence from a rolling microphone window without
    writing or logging its content.
    """
    if quality.speech_start_frame < 0 or quality.speech_end_frame <= quality.speech_start_frame:
        return wav_bytes

    frame_size = 320
    padding_frames = 5  # 100 ms on each side keeps natural word boundaries.
    with wave.open(io.BytesIO(wav_bytes), "rb") as reader:
        start = max(0, quality.speech_start_frame - padding_frames) * frame_size
        end = min(reader.getnframes(), (quality.speech_end_frame + padding_frames) * frame_size)
        reader.setpos(start)
        frames = reader.readframes(max(0, end - start))
        output = io.BytesIO()
        with wave.open(output, "wb") as writer:
            writer.setnchannels(reader.getnchannels())
            writer.setsampwidth(reader.getsampwidth())
            writer.setframerate(reader.getframerate())
            writer.writeframes(frames)
        return output.getvalue()


def _longest_active_region(active: numpy.ndarray) -> tuple[int, int]:
    """Find the longest active span, allowing a short gap inside a phrase."""
    best_start = best_end = -1
    current_start = -1
    last_active = -1
    for index, is_active in enumerate(active):
        if is_active:
            if current_start < 0:
                current_start = index
            last_active = index
        elif current_start >= 0 and index - last_active > 3:
            end = last_active + 1
            if end - current_start > best_end - best_start:
                best_start, best_end = current_start, end
            current_start = -1
            last_active = -1

    if current_start >= 0:
        end = last_active + 1
        if end - current_start > best_end - best_start:
            best_start, best_end = current_start, end
    return best_start, best_end
