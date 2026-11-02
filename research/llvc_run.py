"""Run LLVC streaming conversion on wav files without torchaudio's codec backend.

Usage: python llvc_run.py <in_dir_or_file> <out_dir> [chunk_factor]
Prints RTF (>1 = faster than real time) and end-to-end latency per file.
"""
import os
import sys
import time

import numpy as np
import soundfile as sf
import torch
import torchaudio.functional as AF

LLVC_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "LLVC")
sys.path.insert(0, LLVC_DIR)
from infer import load_model, infer_stream  # noqa: E402

CHECKPOINT = os.path.join(LLVC_DIR, "llvc_models", "models", "checkpoints", "llvc", "G_500000.pth")
CONFIG = os.path.join(LLVC_DIR, "experiments", "llvc", "config.json")


def load_wav(path, target_sr):
    audio, sr = sf.read(path, dtype="float32", always_2d=True)
    audio = torch.from_numpy(audio.mean(axis=1))
    if sr != target_sr:
        audio = AF.resample(audio, sr, target_sr)
    return audio


def main():
    src, out_dir = sys.argv[1], sys.argv[2]
    chunk_factor = int(sys.argv[3]) if len(sys.argv) > 3 else 1
    os.makedirs(out_dir, exist_ok=True)
    model, sr = load_model(CHECKPOINT, CONFIG)
    model.eval()
    files = [src] if os.path.isfile(src) else [os.path.join(src, f) for f in sorted(os.listdir(src)) if f.lower().endswith(".wav")]
    print(f"model sample rate {sr} Hz, chunk factor {chunk_factor}, torch threads {torch.get_num_threads()}")
    for f in files:
        audio = load_wav(f, sr)
        t0 = time.time()
        out, rtf, latency = infer_stream(model, audio, chunk_factor, sr)
        wall = time.time() - t0
        out_path = os.path.join(out_dir, os.path.basename(f))
        sf.write(out_path, out.squeeze(0).numpy(), sr)
        print(f"{os.path.basename(f)}: {len(audio)/sr:.1f}s audio in {wall:.2f}s  RTF {rtf:.2f}  latency {latency:.0f} ms")


if __name__ == "__main__":
    main()
