"""Assemble the self-contained Colab notebook for training one Voxel AI voice.

The notebook embeds make_pairs.py, train_llvc.py, export_onnx.py and the config template
(so it needs no access to our repo), clones Koe AI's LLVC code, downloads the QuickVC teacher,
fetches a free speech corpus, and saves checkpoints to Google Drive so a session can resume.

Usage: python build_notebook.py  -> writes Voxel_Train_Voice.ipynb next to this file
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))


def code(src):
    return {"cell_type": "code", "metadata": {}, "execution_count": None, "outputs": [], "source": src}


def md(src):
    return {"cell_type": "markdown", "metadata": {}, "source": src}


def embed(name):
    body = open(os.path.join(HERE, name), encoding="utf-8").read()
    return code(f"%%writefile /content/work/train/{name}\n{body}")


cells = [
    md("# Voxel — train one AI voice\n\n"
       "Runtime → Change runtime type → **T4 GPU**, then Runtime → **Run all**. "
       "Fill in the three settings below first. Everything is saved to your Google Drive under `Voxel/`, "
       "so if Colab disconnects you just run it again and it continues."),
    code("#@title Settings\n"
         "VOICE_NAME = \"woman-1\"  #@param {type:\"string\"}\n"
         "REFERENCE = \"F:0\"  #@param {type:\"string\"}\n"
         "HOURS_OF_SPEECH = 20  #@param {type:\"number\"}\n"
         "TRAIN_STEPS = 100000  #@param {type:\"integer\"}\n"
         "# REFERENCE = whose voice to learn:\n"
         "#   \"F:0\" / \"M:3\"  -> the 1st female / 4th male reader of the free corpus (change the number for another person)\n"
         "#   \"1088\"         -> a LibriSpeech speaker id\n"
         "#   \"https://...\"  -> a direct link to a 10-30 s clean wav/mp3 of any voice"),
    code("from google.colab import drive\n"
         "drive.mount('/content/drive')\n"
         "import os\n"
         "ROOT = '/content/drive/MyDrive/Voxel'\n"
         "RUN = f'{ROOT}/runs/{VOICE_NAME}'\n"
         "os.makedirs(f'{ROOT}/references', exist_ok=True)\n"
         "os.makedirs(RUN, exist_ok=True)\n"
         "print('run folder:', RUN)"),
    code("%%bash\n"
         "set -e\n"
         "mkdir -p /content/work && cd /content/work\n"
         "[ -d LLVC ] || git clone --depth 1 https://github.com/KoeAI/LLVC.git\n"
         "sed -i 's/from scipy.signal import kaiser/from scipy.signal.windows import kaiser/' LLVC/minimal_quickvc/pqmf.py\n"
         "pip -q install soundfile auraloss librosa huggingface_hub onnx onnxscript speechbrain\n"
         "mkdir -p train\n"
         "echo ok"),
    embed("make_pairs.py"),
    embed("train_llvc.py"),
    embed("export_onnx.py"),
    embed("config_template.json"),
    code("# QuickVC teacher + HuBERT (cached in the session)\n"
         "from huggingface_hub import hf_hub_download\n"
         "hf_hub_download('KoeAI/llvc', 'models/quickvc/quickvc_100.pth', local_dir='/content/work/LLVC/llvc_models')\n"
         "import torch\n"
         "torch.hub.load('bshall/hubert:main', 'hubert_soft', trust_repo=True)\n"
         "print('teacher ready')"),
    code("%%bash\n"
         "# Free speech corpus to learn from (LibriSpeech, CC-BY 4.0). ~6 GB, cached on Drive.\n"
         "# Verifies the archive size and the number of extracted files, so an interrupted session can't leave it half done.\n"
         "set -e\n"
         "D=/content/drive/MyDrive/Voxel/corpus; mkdir -p $D; T=$D/train-clean-100.tar.gz\n"
         "size() { stat -c %s \"$1\" 2>/dev/null || echo 0; }\n"
         "if [ \"$(size $T)\" -lt 6000000000 ]; then echo 'downloading corpus (6.3 GB, once)'; rm -f $T; wget -q --show-progress -O $T https://www.openslr.org/resources/12/train-clean-100.tar.gz; fi\n"
         "echo \"archive: $(size $T) bytes\"\n"
         "mkdir -p /content/corpus && cd /content/corpus\n"
         "N=$(find LibriSpeech/train-clean-100 -name '*.flac' 2>/dev/null | wc -l)\n"
         "if [ \"$N\" -lt 28000 ]; then echo \"extracting ($N of 28539 files present)\"; rm -rf LibriSpeech; tar -xzf $T; fi\n"
         "echo \"corpus ready: $(find LibriSpeech/train-clean-100 -name '*.flac' | wc -l) files\""),
    code("# Reference clip of the target voice (about 30 s at 16 kHz)\n"
         "import glob, os, re, urllib.request\n"
         "import numpy as np, soundfile as sf, torch, torchaudio.functional as AF\n"
         "ref = f'{ROOT}/references/{VOICE_NAME}.wav'\n"
         "CORPUS = '/content/corpus/LibriSpeech/train-clean-100'\n"
         "\n"
         "def to16k(path):\n"
         "    try:\n"
         "        a, sr = sf.read(path, dtype='float32', always_2d=True); a = a.mean(axis=1)\n"
         "    except Exception:\n"
         "        import librosa\n"
         "        a, sr = librosa.load(path, sr=None, mono=True)\n"
         "    a = torch.from_numpy(a)\n"
         "    return AF.resample(a, sr, 16000) if sr != 16000 else a\n"
         "\n"
         "def speakers_by_sex(sex):\n"
         "    ids = []\n"
         "    for line in open('/content/corpus/LibriSpeech/SPEAKERS.TXT'):\n"
         "        if line.startswith(';'): continue\n"
         "        parts = [p.strip() for p in line.split('|')]\n"
         "        if len(parts) >= 3 and parts[1] == sex and parts[2] == 'train-clean-100': ids.append(parts[0])\n"
         "    return ids\n"
         "\n"
         "if REFERENCE.startswith('http'):\n"
         "    urllib.request.urlretrieve(REFERENCE, '/content/ref_download'); audio = to16k('/content/ref_download')\n"
         "else:\n"
         "    if ':' in REFERENCE:\n"
         "        sex, idx = REFERENCE.split(':'); spk = speakers_by_sex(sex.upper())[int(idx)]\n"
         "    else:\n"
         "        spk = REFERENCE\n"
         "    files = sorted(glob.glob(f'{CORPUS}/{spk}/*/*.flac'))\n"
         "    assert files, f'no audio found for speaker {spk}: re-run the corpus cell above'\n"
         "    pieces, total = [], 0\n"
         "    for f in files:\n"
         "        a = to16k(f); pieces.append(a); total += len(a)\n"
         "        if total >= 30 * 16000: break\n"
         "    audio = torch.cat(pieces)\n"
         "    print('reference speaker id:', spk)\n"
         "sf.write(ref, audio.numpy(), 16000)\n"
         "print('reference saved:', ref, f'({len(audio)/16000:.0f} s)')\n"
         "from IPython.display import Audio, display\n"
         "display(Audio(ref))  # listen: this is the voice being learned"),
    code("# Make training pairs (QuickVC converts the corpus into the target voice).\n"
         "# Pairs live on Colab's local disk for speed and are mirrored to Drive so a new session can restore them.\n"
         "import os, subprocess\n"
         "data = '/content/data'\n"
         "drive_data = f'{RUN}/data'\n"
         "if os.path.exists(f'{data}/train'):\n"
         "    print('pairs already on local disk')\n"
         "elif os.path.exists(f'{drive_data}/train'):\n"
         "    print('restoring pairs from Drive to local disk (a few minutes)...')\n"
         "    subprocess.run(['rsync', '-a', '--info=progress2', f'{drive_data}/', f'{data}/'], check=True)\n"
         "else:\n"
         "    !cd /content/work/train && python make_pairs.py --src /content/corpus/LibriSpeech --ref \"{ref}\" --out \"{data}\" --hours {HOURS_OF_SPEECH} --device cuda\n"
         "    print('mirroring pairs to Drive...')\n"
         "    subprocess.run(['rsync', '-a', f'{data}/', f'{drive_data}/'], check=True)\n"
         "print('pairs ready:', sum(len(os.listdir(f'{data}/{s}')) // 2 for s in ('train', 'val', 'dev')))"),
    code("# Training config\n"
         "import json\n"
         "cfg = json.load(open('/content/work/train/config_template.json'))\n"
         "cfg['data']['dir'] = data\n"
         "cfg['test_dir'] = '/content/work/LLVC/test_wavs'\n"
         "json.dump(cfg, open(f'{RUN}/config.json', 'w'), indent=2)\n"
         "print('config written')"),
    code("# Train (resumes automatically from the newest checkpoint in Drive)\n"
         "!cd /content/work/train && python train_llvc.py --dir \"{RUN}\" --steps {TRAIN_STEPS} --device cuda"),
    code("# Export the voice file Voxel uses\n"
         "import glob, os\n"
         "ck = max(glob.glob(f'{RUN}/checkpoints/G_*.pth'), key=lambda p: int(os.path.basename(p).split('_')[1].split('.')[0]))\n"
         "out = f'{ROOT}/voices/{VOICE_NAME}.onnx'\n"
         "os.makedirs(os.path.dirname(out), exist_ok=True)\n"
         "!cd /content/work/train && python export_onnx.py --checkpoint \"{ck}\" --config \"{RUN}/config.json\" --out \"{out}\" --chunk-factor 4\n"
         "print('DONE — download these two files from Drive/Voxel/voices and send them to me:')\n"
         "print(out); print(out.replace('.onnx', '.json'))"),
]

nb = {
    "cells": cells,
    "metadata": {"accelerator": "GPU", "colab": {"provenance": []},
                 "kernelspec": {"display_name": "Python 3", "name": "python3"}, "language_info": {"name": "python"}},
    "nbformat": 4, "nbformat_minor": 0,
}
out = os.path.join(HERE, "Voxel_Train_Voice.ipynb")
json.dump(nb, open(out, "w", encoding="utf-8"), indent=1)
print("wrote", out)
