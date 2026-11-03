"""Find new MeanVC voices in GLOBE (CC0 Common Voice clips with age + gender labels).

Picks speakers matching an age/gender filter, measures their average pitch, joins a few of
their clips into one reference, and saves the speaker embedding (.npy, the same 256 floats a
.spk file holds) plus the reference clip so it can be listened to.

Usage: python -I find_voices.py "<globe parquet glob>" <out dir>
"""
import glob
import io
import os
import sys

import librosa
import numpy as np
import pyarrow.parquet as pq
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "MeanVC2", "runtime"))
from run_rt import SPEAKER_MODEL_PATH, WAVLM_CONFIG_PATH  # noqa: E402
from src.speaker import extract_embedding, init_speaker_model  # noqa: E402

GLOBE, OUT = sys.argv[1:3]
# (who to look at, sort key): oldest men in any order, young women highest pitch first
GROUPS = {
    "grandpa": (lambda r: r["gender"] == "male" and r["age"] in ("sixties", "seventies", "eighties", "nineties"),
                lambda hz: 0),
    "egirl": (lambda r: r["gender"] == "female" and r["age"] in ("teens", "twenties"), lambda hz: -hz),
}
REF_SECONDS = 12
KEEP = 16


def speakers():
    rows = []
    for f in sorted(glob.glob(GLOBE)):
        t = pq.read_table(f, columns=["speaker_id", "age", "gender", "duration", "audio"])
        for r in t.to_pylist():
            rows.append(r)
    by = {}
    for r in rows:
        by.setdefault(r["speaker_id"], []).append(r)
    return by


def load(r):
    wav, sr = sf.read(io.BytesIO(r["audio"]["bytes"]), dtype="float32", always_2d=True)
    wav = wav.mean(axis=1)
    if sr != 16000:
        wav = librosa.resample(wav, orig_sr=sr, target_sr=16000)
    wav, _ = librosa.effects.trim(wav, top_db=30)
    return wav


def main():
    os.makedirs(OUT, exist_ok=True)
    by = speakers()
    spk_model = init_speaker_model(SPEAKER_MODEL_PATH, "cpu", wavlm_config=WAVLM_CONFIG_PATH)
    for group, (keep, rank) in GROUPS.items():
        found = []
        for sid, clips in by.items():
            if not keep(clips[0]):
                continue
            wav = np.concatenate([load(c) for c in clips[:6]])[: REF_SECONDS * 16000]
            if len(wav) < 4 * 16000:
                continue
            f0, voiced, _ = librosa.pyin(wav, fmin=60, fmax=500, sr=16000, frame_length=1024)
            f0 = f0[voiced]
            if len(f0) < 50:
                continue
            found.append((rank(np.median(f0)), sid, clips, wav, f0))
        print(f"\n{group}: {len(found)} speakers, keeping {KEEP}", flush=True)
        for _, sid, clips, wav, f0 in sorted(found, key=lambda x: x[0])[:KEEP]:
            hz = float(np.median(f0))
            shake = float(np.median(np.abs(np.diff(np.log2(f0)))) * 1200)  # cents between frames
            name = f"{group}-{sid[:8]}-{hz:.0f}hz"
            path = os.path.join(OUT, name + ".wav")
            sf.write(path, wav, 16000)
            emb = extract_embedding(spk_model, path, device="cpu")
            np.save(os.path.join(OUT, name + ".npy"), emb.numpy())
            print(f"  {name}: {len(wav) / 16000:.0f}s ref, pitch {hz:.0f} Hz, shake {shake:.0f} cents, "
                  f"age {clips[0]['age']}, {len(clips)} clips", flush=True)


if __name__ == "__main__":
    main()
