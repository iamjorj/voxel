"""Build LLVC training pairs with QuickVC as the teacher.

For every source clip: cut it into fixed-length pieces, convert each piece into the target
voice (given by one reference clip), and write <id>_original.wav / <id>_converted.wav at
16 kHz into train/ val/ dev/ folders, the layout LLVC's dataset.py expects.

Usage: python make_pairs.py --src <folder of wav/flac> --ref <reference.wav> --out <dataset dir>
                            [--hours 5] [--piece 65536] [--device cuda]
"""
import argparse
import glob
import os
import random
import sys

import numpy as np
import soundfile as sf
import torch
import torchaudio.functional as AF
from tqdm import tqdm

HERE = os.path.dirname(os.path.abspath(__file__))
LLVC_DIR = os.path.join(os.path.dirname(HERE), "LLVC")
sys.path.insert(0, LLVC_DIR)
from mel_processing import mel_spectrogram_torch  # noqa: E402
from minimal_quickvc.models import SynthesizerTrn  # noqa: E402
from minimal_quickvc.utils import load_checkpoint  # noqa: E402

SR = 16000


def load_wav(path):
    audio, sr = sf.read(path, dtype="float32", always_2d=True)
    audio = torch.from_numpy(audio.mean(axis=1))
    if sr != SR:
        audio = AF.resample(audio, sr, SR)
    return audio


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--ref", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--hours", type=float, default=5.0, help="how much source audio to use")
    ap.add_argument("--piece", type=int, default=65536, help="samples per training piece (LLVC wav_len)")
    ap.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    ap.add_argument("--quickvc", default=os.path.join(LLVC_DIR, "llvc_models", "models", "quickvc", "quickvc_100.pth"))
    ap.add_argument("--seed", type=int, default=1234)
    args = ap.parse_args()

    random.seed(args.seed)
    device = torch.device(args.device)
    hubert = torch.hub.load("bshall/hubert:main", "hubert_soft", trust_repo=True).eval().to(device)
    model = SynthesizerTrn().eval()
    load_checkpoint(args.quickvc, model, None)
    model.to(device)
    mel = mel_spectrogram_torch(load_wav(args.ref).unsqueeze(0), SR, 1280, 80, 320, 1280, 0, None).to(device)

    files = sorted(glob.glob(os.path.join(args.src, "**", "*.wav"), recursive=True)
                   + glob.glob(os.path.join(args.src, "**", "*.flac"), recursive=True))
    random.shuffle(files)
    budget = int(args.hours * 3600 * SR)
    for split in ("train", "val", "dev"):
        os.makedirs(os.path.join(args.out, split), exist_ok=True)

    written = 0
    used = 0
    pbar = tqdm(total=budget, unit="samples", unit_scale=True)
    with torch.inference_mode():
        for f in files:
            if used >= budget:
                break
            audio = load_wav(f)
            # Drop the last partial piece; pad nothing, so every pair is exactly `piece` long.
            n = len(audio) // args.piece
            for k in range(n):
                piece = audio[k * args.piece:(k + 1) * args.piece]
                units = hubert.units(piece.view(1, 1, -1).to(device)).transpose(2, 1)
                converted = model.infer(units, mel=mel).reshape(-1).cpu()
                converted = converted[:len(piece)]
                if len(converted) < len(piece):
                    converted = torch.cat([converted, torch.zeros(len(piece) - len(converted))])
                # 1% val, 1% dev, rest train
                r = random.random()
                split = "val" if r < 0.01 else "dev" if r < 0.02 else "train"
                stem = os.path.join(args.out, split, f"{written:06d}")
                sf.write(stem + "_original.wav", (piece.numpy() * 32767).astype(np.int16), SR)
                sf.write(stem + "_converted.wav", (converted.clamp(-1, 1).numpy() * 32767).astype(np.int16), SR)
                written += 1
                used += len(piece)
                pbar.update(len(piece))
                if used >= budget:
                    break
    pbar.close()
    print(f"wrote {written} pairs ({used / SR / 3600:.2f} h) to {args.out}")


if __name__ == "__main__":
    main()
