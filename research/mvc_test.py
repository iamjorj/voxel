"""MeanVC2 test on this laptop: live-style speed + woman/man samples, no training.

Feeds a source clip through MeanVC2's own streaming runner in 160 ms pieces (exactly what
the live microphone mode does), times every piece, and writes the converted audio for two
target voices. Also saves each target's speaker embedding as .npy (the tiny per-voice file
the app would ship).

Usage: python mvc_test.py   (run from the research folder after the one-time download)
"""
import os
import sys
import time

import numpy as np
import soundfile as sf
import torch

HERE = os.path.dirname(os.path.abspath(__file__))
RUNTIME = os.path.join(HERE, "MeanVC2", "runtime")
sys.path.insert(0, RUNTIME)
from run_rt import VCRunner  # noqa: E402
from src.speaker import extract_embedding  # noqa: E402

WAVS = os.path.join(HERE, "LLVC", "test_wavs")
SOURCE = os.path.join(WAVS, "8842-302196-0000.wav")   # the speaker being converted
TARGETS = {
    "woman": os.path.join(WAVS, "1919-142785-0000.wav"),  # measured ~184 Hz
    "man": os.path.join(WAVS, "777-126732-0000.wav"),     # measured ~114 Hz
}
OUT = os.path.join(HERE, "mvc_out")


def load16k(path):
    wav, sr = sf.read(path, dtype="float32", always_2d=True)
    wav = wav.mean(axis=1)
    if sr != 16000:
        import librosa
        wav = librosa.resample(wav, orig_sr=sr, target_sr=16000)
    return wav.astype(np.float32)


def run_live_style(runner, wav):
    """Same 160 ms pieces as the live mic mode; returns audio and per-piece times (ms)."""
    runner._init_cache()
    torch.manual_seed(42)
    parts, times = [], []
    for pos in range(0, len(wav), runner.CHUNK):
        piece = wav[pos:pos + runner.CHUNK]
        t0 = time.perf_counter()
        out = runner.process_chunk(piece)
        times.append((time.perf_counter() - t0) * 1000)
        if out is not None:
            parts.append(out)
    return (np.concatenate(parts) if parts else np.zeros(0, np.float32)), np.array(times)


def main():
    os.makedirs(OUT, exist_ok=True)
    wav = load16k(SOURCE)
    sf.write(os.path.join(OUT, "0-original.wav"), wav, 16000)
    piece_ms = 2560 / 16

    for model in ("40ms", "120ms"):
        print(f"\n=== model {model} ===", flush=True)
        runner = VCRunner(target_wav=TARGETS["woman"], device="cpu", model=model)
        for name, ref in TARGETS.items():
            emb = extract_embedding(runner.spk_model, ref, device="cpu")
            np.save(os.path.join(OUT, f"{name}.npy"), emb.numpy())
            runner.vc_spk_emb = emb
            with torch.no_grad():
                runner.vc_gtm_kv = runner.vc.gtm(emb)
            run_live_style(runner, wav[:16000])          # warm-up
            audio, t = run_live_style(runner, wav)
            sf.write(os.path.join(OUT, f"{model}-{name}.wav"), audio, 16000)
            busy = t.mean() / piece_ms
            print(f"{model} {name}: {t.mean():.1f} ms avg / {np.percentile(t, 95):.1f} ms p95 / {t.max():.1f} ms max "
                  f"per {piece_ms:.0f} ms of audio -> {busy:.0%} of one core "
                  f"({'LIVE OK' if np.percentile(t, 95) < piece_ms else 'TOO SLOW'})", flush=True)
    print(f"\nlisten: {OUT}")


if __name__ == "__main__":
    main()
