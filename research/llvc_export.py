"""Export the LLVC streaming step to ONNX and verify it against PyTorch.

The ONNX model processes one chunk: input [1,1,2L+chunk] plus the four state buffers,
and returns the converted chunk plus updated buffers. Voxel runs it with ONNX Runtime.

Usage: python llvc_export.py <out.onnx> [chunk_factor]
"""
import json
import os
import sys
import time

import numpy as np
import soundfile as sf
import torch

LLVC_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "LLVC")
sys.path.insert(0, LLVC_DIR)
from infer import load_model, infer_stream  # noqa: E402

CHECKPOINT = os.path.join(LLVC_DIR, "llvc_models", "models", "checkpoints", "llvc", "G_500000.pth")
CONFIG = os.path.join(LLVC_DIR, "experiments", "llvc", "config.json")


class StreamStep(torch.nn.Module):
    """One streaming step with explicit state in/out (no in-place aliasing of inputs)."""

    def __init__(self, net):
        super().__init__()
        self.net = net

    def forward(self, chunk, enc_buf, dec_buf, out_buf, pre_ctx):
        out, enc_buf, dec_buf, out_buf, pre_ctx = self.net(
            chunk, enc_buf.clone(), dec_buf.clone(), out_buf.clone(), pre_ctx.clone(), pad=False)
        return out, enc_buf, dec_buf, out_buf, pre_ctx


def main():
    out_path = sys.argv[1]
    chunk_factor = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    model, sr = load_model(CHECKPOINT, CONFIG)
    model.eval()
    L = model.L
    chunk_len = model.dec_chunk_size * L * chunk_factor
    step = StreamStep(model).eval()

    enc_buf, dec_buf, out_buf = model.init_buffers(1, torch.device("cpu"))
    pre_ctx = model.convnet_pre.init_ctx_buf(1, torch.device("cpu"))
    chunk = torch.zeros(1, 1, 2 * L + chunk_len)
    print(f"L={L} chunk_len={chunk_len} input={tuple(chunk.shape)} enc={tuple(enc_buf.shape)} "
          f"dec={tuple(dec_buf.shape)} out={tuple(out_buf.shape)} pre={tuple(pre_ctx.shape)}")

    with torch.inference_mode():
        torch.onnx.export(
            step, (chunk, enc_buf, dec_buf, out_buf, pre_ctx), out_path,
            input_names=["chunk", "enc_buf", "dec_buf", "out_buf", "pre_ctx"],
            output_names=["out", "enc_buf_out", "dec_buf_out", "out_buf_out", "pre_ctx_out"],
            opset_version=17, dynamo=False)
    print(f"exported {out_path} ({os.path.getsize(out_path) // 1024} KB)")

    meta = {"sample_rate": sr, "L": L, "chunk_len": chunk_len, "context": 2 * L,
            "enc_buf": list(enc_buf.shape), "dec_buf": list(dec_buf.shape),
            "out_buf": list(out_buf.shape), "pre_ctx": list(pre_ctx.shape)}
    with open(os.path.splitext(out_path)[0] + ".json", "w") as f:
        json.dump(meta, f, indent=2)

    # Verify: ONNX Runtime streaming vs PyTorch streaming on a real clip.
    import onnxruntime as ort
    opts = ort.SessionOptions()
    opts.intra_op_num_threads = 4
    sess = ort.InferenceSession(out_path, opts, providers=["CPUExecutionProvider"])

    wav, file_sr = sf.read(os.path.join(LLVC_DIR, "test_wavs", "1919-142785-0000.wav"), dtype="float32", always_2d=True)
    audio = torch.from_numpy(wav.mean(axis=1))
    assert file_sr == sr
    with torch.inference_mode():
        ref, _, _ = infer_stream(model, audio.clone(), chunk_factor, sr)
    ref = ref.squeeze(0).numpy()

    # Same chunking as infer_stream.
    a = audio.clone()
    if len(a) % chunk_len:
        a = torch.nn.functional.pad(a, (0, chunk_len - len(a) % chunk_len))
    a = torch.cat((a[L:], torch.zeros(L)))
    chunks = torch.split(a, chunk_len)
    state = {"enc_buf": enc_buf.numpy(), "dec_buf": dec_buf.numpy(), "out_buf": out_buf.numpy(), "pre_ctx": pre_ctx.numpy()}
    outs, times = [], []
    prev = torch.zeros(2 * L)
    for c in chunks:
        inp = torch.cat([prev, c]).reshape(1, 1, -1).numpy()
        prev = c[-2 * L:]
        t0 = time.perf_counter()
        o, state["enc_buf"], state["dec_buf"], state["out_buf"], state["pre_ctx"] = sess.run(
            None, {"chunk": inp, **state})
        times.append(time.perf_counter() - t0)
        outs.append(o.reshape(-1))
    got = np.concatenate(outs)[: len(audio)]
    diff = np.max(np.abs(got - ref[: len(got)]))
    avg = np.mean(times)
    print(f"max |onnx - torch| = {diff:.2e}; ORT per chunk {avg*1000:.2f} ms for {chunk_len/sr*1000:.1f} ms audio "
          f"-> RTF {chunk_len/sr/avg:.2f} (4 threads)")
    sf.write(os.path.splitext(out_path)[0] + "-check.wav", got, sr)


if __name__ == "__main__":
    main()
