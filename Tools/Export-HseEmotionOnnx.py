"""Export the pinned HSEmotion AFEW model to a deterministic Android ONNX asset."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

import numpy as np
import torch


MODEL_NAME = "enet_b0_8_best_afew"
MODEL_SHA256 = "47c1423f3e6f50e3750bf7b0eda7db947c9ce0c2637e1766bf2187eddc652b17"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    from hsemotion.facial_emotions import get_model_path

    source = Path(get_model_path(MODEL_NAME))
    if sha256_file(source) != MODEL_SHA256:
        raise RuntimeError("Pinned HSEmotion model digest does not match.")

    model = torch.load(source, map_location="cpu", weights_only=False).eval()
    dummy = torch.zeros((1, 3, 224, 224), dtype=torch.float32)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        model,
        dummy,
        args.output,
        input_names=["input"],
        output_names=["logits"],
        opset_version=17,
        do_constant_folding=True,
        dynamo=False,
    )

    import onnx
    import onnxruntime as ort

    exported = onnx.load(args.output)
    onnx.checker.check_model(exported)

    sample = torch.rand((1, 3, 224, 224), dtype=torch.float32)
    with torch.inference_mode():
        expected = model(sample).cpu().numpy()
    session = ort.InferenceSession(str(args.output), providers=["CPUExecutionProvider"])
    actual = session.run(["logits"], {"input": sample.cpu().numpy()})[0]
    max_error = float(np.max(np.abs(expected - actual)))
    if max_error > 1e-4:
        raise RuntimeError(f"ONNX parity check failed: max error {max_error:.8f}")

    print(f"Exported {args.output}")
    print(f"Size: {args.output.stat().st_size} bytes")
    print(f"SHA-256: {sha256_file(args.output)}")
    print(f"PyTorch/ONNX max error: {max_error:.8f}")


if __name__ == "__main__":
    main()
