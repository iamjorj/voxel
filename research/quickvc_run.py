"""QuickVC test: convert a clip into the voice of ANY reference clip, no training.

Usage: python quickvc_run.py <input.wav> <reference.wav> <out.wav> [window_ms]
Loads HuBERT-soft from torch.hub (cached after the first run) and the QuickVC checkpoint
from the LLVC model repo. Prints speed for the whole clip and for chunked (live-style) use.
"""
import os
import sys
import time

import numpy as np
import soundfile as sf
import torch
import torchaudio.functional as AF

HERE = os.path.dirname(os.path.abspath(__file__))
LLVC_DIR = os.path.join(HERE, "LLVC")
sys.path.insert(0, LLVC_DIR)
from mel_processing import mel_spectrogram_torch  # noqa: E402
from minimal_quickvc.models import SynthesizerTrn  # noqa: E402
from minimal_quickvc.utils import load_checkpoint  # noqa: E402

SR = 16000
CHECKPOINT = os.path.join(LLVC_DIR, "llvc_models", "models", "quickvc", "quickvc_100.pth")


def load_wav(path):
    audio, sr = sf.read(path, dtype="float32", always_2d=True)
    audio = torch.from_numpy(audio.mean(axis=1))
    if sr != SR:
        audio = AF.resample(audio, sr, SR)
    return audio


def reference_mel(audio):
    # QuickVC's speaker encoder reads an 80-bin mel at 16 kHz (n_fft/win 1280, hop 320).
    return mel_spectrogram_torch(audio.unsqueeze(0), SR, 1280, 80, 320, 1280, 0, None)


def main():
    src, ref, out = sys.argv[1], sys.argv[2], sys.argv[3]
    window_ms = int(sys.argv[4]) if len(sys.argv) > 4 else 200
    torch.set_num_threads(4)

    hubert = torch.hub.load("bshall/hubert:main", "hubert_soft", trust_repo=True).eval()
    model = SynthesizerTrn().eval()
    load_checkpoint(CHECKPOINT, model, None)

    audio = load_wav(src)
    mel = reference_mel(load_wav(ref))

    # Whole clip at once (best quality, offline).
    with torch.inference_mode():
        t0 = time.perf_counter()
        units = hubert.units(audio.view(1, 1, -1)).transpose(2, 1)
        wav = model.infer(units, mel=mel).reshape(-1)
        whole = time.perf_counter() - t0
    sf.write(out, wav.numpy(), SR)
    print(f"whole clip: {len(audio)/SR:.1f} s audio in {whole:.2f} s -> {len(audio)/SR/whole:.2f}x real time")

    # Live-style: fixed windows with 64 ms of extra past context, crossfaded.
    win = int(SR * window_ms / 1000)
    extra = 1024
    times = []
    with torch.inference_mode():
        for start in range(extra, len(audio) - win, win):
            piece = audio[start - extra:start + win]
            t0 = time.perf_counter()
            u = hubert.units(piece.view(1, 1, -1)).transpose(2, 1)
            model.infer(u, mel=mel)
            times.append(time.perf_counter() - t0)
    avg = np.mean(times)
    print(f"chunked {window_ms} ms windows: {avg*1000:.0f} ms per window -> {window_ms/1000/avg:.2f}x real time, "
          f"latency about {window_ms + avg*1000:.0f} ms")


if __name__ == "__main__":
    main()
