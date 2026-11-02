"""Dynamic int8 quantization of an exported LLVC ONNX model, with a quality/speed check.

Usage: python llvc_quantize.py <model.onnx>   -> writes <model>-int8.onnx and copies the .json
"""
import json
import os
import shutil
import sys
import time

import numpy as np
import onnxruntime as ort
import soundfile as sf
from onnxruntime.quantization import QuantType, quantize_dynamic

src = sys.argv[1]
base, _ = os.path.splitext(src)
dst = base + "-int8.onnx"
quantize_dynamic(src, dst, weight_type=QuantType.QInt8)
shutil.copy(base + ".json", base + "-int8.json")
print(f"wrote {dst} ({os.path.getsize(dst) // 1024} KB, from {os.path.getsize(src) // 1024} KB)")

meta = json.load(open(base + ".json"))
L, chunk_len, ctx, sr = meta["L"], meta["chunk_len"], meta["context"], meta["sample_rate"]
here = os.path.dirname(os.path.abspath(__file__))
wav, file_sr = sf.read(os.path.join(here, "LLVC", "test_wavs", "1919-142785-0000.wav"), dtype="float32", always_2d=True)
audio = wav.mean(axis=1)
pad = (-len(audio)) % chunk_len
audio = np.concatenate([audio[L:], np.zeros(L + pad, np.float32)])


def run(path):
    opts = ort.SessionOptions()
    opts.intra_op_num_threads = 2
    s = ort.InferenceSession(path, opts, providers=["CPUExecutionProvider"])
    state = {k: np.zeros(meta[k], np.float32) for k in ("enc_buf", "dec_buf", "out_buf", "pre_ctx")}
    prev = np.zeros(ctx, np.float32)
    outs, times = [], []
    for i in range(0, len(audio), chunk_len):
        c = audio[i:i + chunk_len]
        inp = np.concatenate([prev, c]).reshape(1, 1, -1)
        prev = c[-ctx:]
        t0 = time.perf_counter()
        o, state["enc_buf"], state["dec_buf"], state["out_buf"], state["pre_ctx"] = s.run(None, {"chunk": inp, **state})
        times.append(time.perf_counter() - t0)
        outs.append(o.reshape(-1))
    return np.concatenate(outs), np.mean(times)


ref, t_ref = run(src)
got, t_q = run(dst)
snr = 10 * np.log10(np.sum(ref ** 2) / (np.sum((ref - got) ** 2) + 1e-12))
print(f"fp32 {t_ref*1000:.2f} ms/chunk, int8 {t_q*1000:.2f} ms/chunk ({chunk_len/sr*1000:.0f} ms audio); "
      f"int8 vs fp32 SNR {snr:.1f} dB")
sf.write(base + "-int8-check.wav", got, sr)
