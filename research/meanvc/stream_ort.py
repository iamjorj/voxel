"""Streaming MeanVC2 with only the exported ONNX files: the exact loop Voxel's C# engine runs.

Converts a clip in live-sized pieces, compares it with the original PyTorch output and
prints how long each step takes.

Usage: python -I stream_ort.py <onnx dir> <speaker.npy> <in.wav> <out.wav> [reference.wav]
"""
import json
import os
import sys
import time

import numpy as np
import onnxruntime as rt
import soundfile as sf
import torch

D, SPK, IN, OUT = sys.argv[1:5]
REF = sys.argv[5] if len(sys.argv) > 5 else None
meta = json.load(open(os.path.join(D, "meanvc.json")))


def sess(name):
    o = rt.SessionOptions()
    o.intra_op_num_threads = 1
    o.graph_optimization_level = rt.GraphOptimizationLevel.ORT_ENABLE_ALL
    return rt.InferenceSession(os.path.join(D, name), o, providers=["CPUExecutionProvider"])


class Stream:
    def __init__(self, spk):
        self.asr, self.vc, self.voc = sess("asr.onnx"), sess("vc.onnx"), sess("vocos.onnx")
        self.spk = spk.reshape(1, 256).astype(np.float32)
        self.chunk, self.block = meta["chunk"], meta["block"]
        self.seq = self.chunk + self.block
        self.buf = np.zeros(0, np.float32)
        self.off = meta["asr_offset_init"]
        self.att = np.zeros((6, 4, meta["asr_cache"], 128), np.float32)
        self.cnn = np.zeros((6, 1, 256, 8), np.float32)
        self.prev_bn = None
        self.bn = np.zeros((0, 256), np.float32)
        self.steps = 0
        self.kv = {f"{a}{i}": np.zeros((1, 2, L, 64), np.float32) for i, L in enumerate(meta["cache_lens"]) for a in "kv"}
        self.noise_cache = None
        self.mel_cache = None
        self.last = None
        self.t = {"asr": [], "vc": [], "voc": []}
        self.fade_in = np.linspace(0, 1, meta["hop"], dtype=np.float32)

    def feed(self, x):
        self.buf = np.concatenate([self.buf, x])
        out = []
        while len(self.buf) >= meta["asr_samples"]:
            self._content(self.buf[:meta["asr_samples"]])
            self.buf = self.buf[meta["asr_hop"]:]
            while len(self.bn) >= self.seq:
                out.append(self._convert(self.bn[:self.seq]))
                self.bn = self.bn[self.chunk:]
        return np.concatenate(out) if out else np.zeros(0, np.float32)

    def _content(self, wav):
        if self.off >= 4000:
            self.off = max(meta["asr_cache"], self.off - 4000)
        t0 = time.perf_counter()
        bn, self.att, self.cnn = self.asr.run(None, {"wav": wav[None], "offset": np.array(self.off, np.int64),
                                                     "rcs": np.array(meta["asr_cache"], np.int64),
                                                     "att": self.att, "cnn": self.cnn})
        self.t["asr"].append(time.perf_counter() - t0)
        self.off += meta["asr_offset_step"]
        bn = bn[0]
        self.got = len(bn)
        if self.prev_bn is not None:
            bn = np.concatenate([self.prev_bn, bn])
        self.prev_bn = bn[-1:]
        # linear, align_corners, four 10 ms frames per 40 ms frame; the first point belongs to the previous run
        out_n = 4 * self.got
        src = np.arange(1, out_n + 1) * (len(bn) - 1) / out_n
        i0 = np.minimum(np.floor(src).astype(int), len(bn) - 2)
        w = (src - i0)[:, None].astype(np.float32)
        self.bn = np.concatenate([self.bn, bn[i0] * (1 - w) + bn[i0 + 1] * w])

    def _convert(self, cond):
        noise = torch.randn(1, self.seq, 80).numpy()
        if self.noise_cache is not None:
            noise[:, :self.block] = self.noise_cache
        self.noise_cache = noise[:, -self.block:].copy()
        t0 = time.perf_counter()
        out = self.vc.run(None, {"noise": noise, "cond": cond[None], "spk": self.spk,
                                 "steps": np.array([self.steps], np.int64), **self.kv})
        self.t["vc"].append(time.perf_counter() - t0)
        self.steps += 1
        mel = out[0]
        self.kv = {f"{a}{i}": out[1 + 2 * i + j] for i in range(4) for j, a in enumerate("kv")}

        if self.mel_cache is not None:
            mel = np.concatenate([self.mel_cache, mel], axis=2)
        self.mel_cache = mel[:, :, -meta["vocoder_overlap"]:]
        t0 = time.perf_counter()
        wav = self.voc.run(None, {"mel": mel})[0][0]
        self.t["voc"].append(time.perf_counter() - t0)
        h = meta["hop"]
        if self.last is not None:
            new = np.concatenate([self.last * self.fade_in[::-1] + wav[:h] * self.fade_in, wav[h:-h]])
        else:
            new = wav[:-h]
        self.last = wav[-h:]
        return new


def main():
    wav, sr = sf.read(IN, dtype="float32")
    assert sr == 16000
    torch.manual_seed(42)
    s = Stream(np.load(SPK))
    t0 = time.perf_counter()
    parts = [s.feed(wav[p:p + 2560]) for p in range(0, len(wav), 2560)]
    total = time.perf_counter() - t0
    out = np.concatenate(parts)
    sf.write(OUT, out, 16000)
    secs = len(wav) / 16000
    for k, v in s.t.items():
        v = np.array(v) * 1000
        print(f"{k:4s}: {v.mean():6.1f} ms avg  {np.percentile(v, 95):6.1f} p95  x{len(v)}")
    print(f"total {total:.2f}s for {secs:.1f}s audio -> {total / secs:.0%} of one core")
    if REF:
        ref, _ = sf.read(REF, dtype="float32")
        n = min(len(ref), len(out))
        err = np.abs(ref[:n] - out[:n])
        print(f"vs original: max diff {err.max():.4f}, mean {err.mean():.5f} (signal peak {np.abs(ref).max():.2f}), "
              f"lengths {len(out)} / {len(ref)}")


if __name__ == "__main__":
    main()
