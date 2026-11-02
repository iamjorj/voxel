"""Export MeanVC2 to three ONNX files Voxel can run without Python.

  asr.onnx    raw 16 kHz audio window -> content frames, 40 ms each (Fast-U2++ encoder)
                120ms model: 3280 samples (19 fbank frames) -> 4 frames; 40ms model: 2000 -> 2
  vc.onnx     content (10 ms frames, chunk + look-ahead) + speaker embedding -> chunk of mel frames
              (DiT, both ODE steps)
  vocos.onnx  mel frames -> waveform (Vocos, ISTFT done with plain matrix math)

Every part is checked against the original PyTorch code before it is written.

Usage: python -I export_meanvc.py <MeanVC2 dir> <out dir> [120ms|40ms]
"""
import json
import math
import os
import sys

import numpy as np
import torch
import torch.nn.functional as F
import torchaudio.compliance.kaldi as kaldi
from torch import nn

ROOT = os.path.abspath(sys.argv[1])
OUT = os.path.abspath(sys.argv[2])
sys.path.insert(0, os.path.join(ROOT, "runtime"))
from run_rt import MODEL_PATHS, VOCODER_PATH, _load_vc_model  # noqa: E402

MODEL = sys.argv[3] if len(sys.argv) > 3 else "120ms"
PATHS = MODEL_PATHS[MODEL]
torch.set_num_threads(4)
_cfg = json.load(open(PATHS["config"]))["model"]
CHUNK, BLOCK = _cfg["chunk_size"], _cfg["block_size"]     # 10 ms mel frames: chunk + look-ahead block
SEQ = CHUNK + BLOCK
ASR_FRAMES, ASR_STRIDE = PATHS["bn_window"], PATHS["bn_stride"]
ASR_SAMPLES = (ASR_FRAMES - 1) * 160 + 400                # 3280 / 2000
ASR_CACHE = PATHS["required_cache_size"]
ASR_OFF0, ASR_OFF_STEP = PATHS["asr_offset_init"], PATHS["asr_offset_step"]


def stream_delay_ms(bn_per_call, first_out, out_per_step):
    """Worst lag (ms) between input and output in the streaming loop, fed in 20 ms pieces,
    not counting run time. Count-only simulation of MeanVcModel.Process."""
    buf = bn = produced = worst = steps = fed = 0
    for _ in range(2000):
        fed += 320
        buf += 320
        before = produced
        while buf >= ASR_SAMPLES:
            buf -= ASR_STRIDE * 160
            bn += bn_per_call
            while bn >= SEQ:
                bn -= CHUNK
                produced += first_out if steps == 0 else out_per_step
                steps += 1
        if produced > before:
            worst = max(worst, fed - before)
    return worst // 16


# ---------------------------------------------------------------------------
# Kaldi fbank as plain matrix ops (same numbers as torchaudio kaldi.fbank)
# ---------------------------------------------------------------------------

class Fbank(nn.Module):
    def __init__(self):
        super().__init__()
        n = torch.arange(512, dtype=torch.float64)
        k = torch.arange(257, dtype=torch.float64)
        ang = 2 * math.pi * n[:, None] * k[None, :] / 512
        self.register_buffer("cos", torch.cos(ang).float())
        self.register_buffer("sin", torch.sin(ang).float())
        self.register_buffer("window", torch.hann_window(400, periodic=False, dtype=torch.float64).pow(0.85).float())
        bins, _ = kaldi.get_mel_banks(80, 512, 16000.0, 20.0, 0.0, 100.0, -500.0, 1.0)
        self.register_buffer("mel", F.pad(bins, (0, 1)).t().contiguous().float())   # [257, 80]
        self.eps = torch.finfo(torch.float32).eps

    def forward(self, wav):                                  # wav [n] already scaled by 32768
        frames = wav.unfold(0, 400, 160)                     # [N, 400]
        frames = frames - frames.mean(dim=1, keepdim=True)
        prev = torch.cat([frames[:, :1], frames[:, :-1]], dim=1)
        frames = (frames - 0.97 * prev) * self.window
        frames = F.pad(frames, (0, 112))                     # -> 512
        power = (frames @ self.cos) ** 2 + (frames @ self.sin) ** 2
        return torch.clamp(power @ self.mel, min=self.eps).log()


class FbankStep(nn.Module):
    """wav [1, 3280] -> fbank [1, 19, 80]. Joined in front of the TorchScript encoder after export
    (a traced module can't call a TorchScript one)."""

    def __init__(self):
        super().__init__()
        self.fbank = Fbank()

    def forward(self, wav):
        return self.fbank(wav[0] * 32768.0).unsqueeze(0)


def merge(first, second, io_map, path):
    """Feed `first`'s outputs into `second`'s inputs and save one ONNX file."""
    import onnx
    from onnx import compose
    a, b = onnx.load(first), onnx.load(second)
    if a.opset_import[0].version != b.opset_import[0].version:
        from onnx import version_converter
        a = version_converter.convert_version(a, b.opset_import[0].version)
    a.ir_version = b.ir_version = max(a.ir_version, b.ir_version)
    keep = [i.name for i in a.graph.input]
    a = compose.add_prefix(a, "pre/")                     # both exports reuse names like /Constant_output_0
    m = compose.merge_models(a, b, io_map=[("pre/" + o, i) for o, i in io_map])
    for name in keep:                                     # restore the public input names
        for node in m.graph.node:
            node.input[:] = [name if x == "pre/" + name else x for x in node.input]
        for inp in m.graph.input:
            if inp.name == "pre/" + name:
                inp.name = name
    onnx.checker.check_model(m)
    onnx.save(m, path)
    os.remove(first)
    os.remove(second)


# ---------------------------------------------------------------------------
# DiT step with fixed-size caches (static shapes, both ODE steps in one call)
# ---------------------------------------------------------------------------

class VcStep(nn.Module):
    """noise [1,16,80], cond [1,16,256], spk [1,256], steps [1] (chunks done so far), caches
    -> mel [1,80,12], new caches.

    Caches hold un-rotated keys/values with the newest frame last, padded at the front to a
    fixed length. Rotary positions only matter relative to each other, so positions are
    counted from the start of the padded cache instead of a growing stream offset (the
    original resets its offset and cache every 40 s; this never needs to).
    """

    def __init__(self, dit):
        super().__init__()
        self.dit = dit
        self.lens = []
        for b in dit.transformer_blocks:
            p = b.attn.processor
            assert p.chunk_size == CHUNK and p.block_size == BLOCK and p.pe_attn_head is None
            lmax = (p.t_p + 1) * CHUNK
            total = lmax + SEQ
            idx = torch.arange(total)
            ci = idx // CHUNK
            qi, kj = ci[:, None], ci[None, :]
            allowed = (qi == kj) | ((kj >= qi - p.t_p) & (kj < qi)) | \
                      ((kj == qi + 1) & ((idx % CHUNK)[None, :] < BLOCK * p.t_f))
            self.register_buffer(f"allow{len(self.lens)}", allowed[-SEQ:].clone())
            freqs, _ = dit.rotary_embed.forward(torch.arange(total))
            self.register_buffer(f"freqs{len(self.lens)}", freqs[0].clone())     # [total, 64]
            self.lens.append(lmax)

    @staticmethod
    def rope(x, freqs):
        x2 = x.reshape(*x.shape[:-1], -1, 2)
        rot = torch.stack((-x2[..., 1], x2[..., 0]), dim=-1).reshape(x.shape)
        return x * freqs.cos() + rot * freqs.sin()

    def block(self, i, blk, h, t_emb, kc, vc, steps):
        attn = blk.attn
        lmax = self.lens[i]
        norm, gate_msa, shift_mlp, scale_mlp, gate_mlp = blk.attn_norm(h, emb=t_emb)
        q = attn.to_q(norm).view(1, SEQ, attn.heads, -1).transpose(1, 2)
        k = attn.to_k(norm).view(1, SEQ, attn.heads, -1).transpose(1, 2)
        v = attn.to_v(norm).view(1, SEQ, attn.heads, -1).transpose(1, 2)
        q, k = attn.q_norm(q), attn.k_norm(k)
        key = torch.cat([kc, k], dim=2)
        val = torch.cat([vc, v], dim=2)
        new_k, new_v = key[:, :, CHUNK:CHUNK + lmax], val[:, :, CHUNK:CHUNK + lmax]

        freqs = getattr(self, f"freqs{i}")
        q = self.rope(q, freqs[-SEQ:])
        key = self.rope(key, freqs)
        valid_from = lmax - torch.clamp(steps * CHUNK, max=lmax)               # [1]
        pos = torch.arange(lmax + SEQ)
        allowed = getattr(self, f"allow{i}") & (pos[None, :] >= valid_from)
        bias = torch.where(allowed, 0.0, -1e9)
        w = torch.softmax(q @ key.transpose(-2, -1) / math.sqrt(q.shape[-1]) + bias, dim=-1)
        out = (w @ val).transpose(1, 2).reshape(1, SEQ, -1)
        out = attn.to_out[0](out)

        h = h + gate_msa.unsqueeze(1) * out
        norm = blk.ff_norm(h) * (1 + scale_mlp[:, None]) + shift_mlp[:, None]
        h = h + gate_mlp.unsqueeze(1) * blk.ff(norm)
        return h, new_k, new_v

    def forward(self, noise, cond, spk, steps, k0, v0, k1, v1, k2, v2, k3, v3):
        d = self.dit
        caches = [(k0, v0), (k1, v1), (k2, v2), (k3, v3)]
        k_mem, v_mem = d.gtm(spk)
        timbre = d.temporal_timbre(cond, k_mem, v_mem)
        spks = spk.unsqueeze(1).expand(-1, SEQ, -1)
        x = noise
        new = None
        for t_val, r_val in ((1.0, 0.5), (0.5, 0.0)):
            t_emb = d.t_time_embed(torch.full((1,), t_val)) + d.r_time_embed(torch.full((1,), r_val))
            h = d.input_embed(x, timbre, spks)
            new = []
            for i, blk in enumerate(d.transformer_blocks):
                h, nk, nv = self.block(i, blk, h, t_emb, caches[i][0], caches[i][1], steps)
                new += [nk, nv]
            u = d.proj_out(d.norm_out(h, t_emb))
            x = x - 0.5 * u
        return ((x[:, :CHUNK, :].transpose(1, 2) + 1) / 2, *new)     # mel in the vocoder's 0..1 range


# ---------------------------------------------------------------------------
# Vocos with ISTFT as matrix math + overlap-add (exportable)
# ---------------------------------------------------------------------------

class ConvNeXt(nn.Module):
    def __init__(self, dim, hidden):
        super().__init__()
        self.dwconv = nn.Conv1d(dim, dim, 7, padding=3, groups=dim)
        self.norm = nn.LayerNorm(dim, eps=1e-6)
        self.pwconv1, self.pwconv2 = nn.Linear(dim, hidden), nn.Linear(hidden, dim)
        self.gamma = nn.Parameter(torch.zeros(dim))

    def forward(self, x):
        y = self.norm(self.dwconv(x).transpose(1, 2))
        y = self.gamma * self.pwconv2(F.gelu(self.pwconv1(y)))
        return x + y.transpose(1, 2)


class VocosBackbone(nn.Module):
    """Plain-PyTorch copy of the TorchScript Vocos backbone (whose Optional arg breaks export)."""

    def __init__(self, voc):
        super().__init__()
        sd = {k: v.detach().clone() for k, v in voc.backbone.state_dict().items()}
        dim, n_mels, _ = sd["embed.weight"].shape
        depth = len({k.split(".")[1] for k in sd if k.startswith("convnext.")})
        self.embed = nn.Conv1d(n_mels, dim, 7, padding=3)
        self.norm = nn.LayerNorm(dim, eps=1e-6)
        self.convnext = nn.ModuleList([ConvNeXt(dim, sd["convnext.0.pwconv1.weight"].shape[0]) for _ in range(depth)])
        self.final_layer_norm = nn.LayerNorm(dim, eps=1e-6)
        self.load_state_dict(sd)

    def forward(self, mel):
        x = self.norm(self.embed(mel).transpose(1, 2)).transpose(1, 2)
        for blk in self.convnext:
            x = blk(x)
        return self.final_layer_norm(x.transpose(1, 2))


class VocosHead(nn.Module):
    """mel [1,80,T] (0..1) -> wav [1, (T-1)*160]; Vocos with its ISTFT done as matrix math."""

    def __init__(self, voc):
        super().__init__()
        w = voc.head.out.weight
        self.out = nn.Linear(w.shape[1], w.shape[0])
        self.out.load_state_dict({"weight": w.detach().clone(), "bias": voc.head.out.bias.detach().clone()})
        self.backbone = VocosBackbone(voc)
        n_fft, hop = voc.head.istft.n_fft, voc.head.istft.hop_length
        assert voc.head.istft.padding == "center" and n_fft == 640 and hop == 160
        k = torch.arange(n_fft // 2 + 1, dtype=torch.float64)
        n = torch.arange(n_fft, dtype=torch.float64)
        ang = 2 * math.pi * k[:, None] * n[None, :] / n_fft
        w = torch.full((n_fft // 2 + 1, 1), 2.0, dtype=torch.float64)
        w[0] = w[-1] = 1.0
        window = voc.head.istft.window.double()
        self.register_buffer("cr", (w * torch.cos(ang) / n_fft * window).float())     # [321, 640]
        im = -w * torch.sin(ang) / n_fft * window
        im[0] = im[-1] = 0.0
        self.register_buffer("ci", im.float())
        self.register_buffer("ola", torch.eye(n_fft).unsqueeze(1))                  # [640, 1, 640]
        self.register_buffer("wsq", (window ** 2).float().view(1, n_fft, 1))
        self.hop, self.n_fft = hop, n_fft

    def forward(self, mel):
        x = self.out(self.backbone(mel)).transpose(1, 2)                             # [1, 642, T]
        mag, p = torch.chunk(x, 2, dim=1)
        mag = torch.clamp(torch.exp(mag), max=100.0)
        re, im = (mag * torch.cos(p)).transpose(1, 2), (mag * torch.sin(p)).transpose(1, 2)
        frames = (re @ self.cr + im @ self.ci).transpose(1, 2)         # [1, 640, T] windowed
        y = F.conv_transpose1d(frames, self.ola, stride=self.hop)
        env = F.conv_transpose1d(self.wsq.expand_as(frames), self.ola, stride=self.hop)
        pad = self.n_fft // 2
        return (y / env)[:, 0, pad:-pad]


# ---------------------------------------------------------------------------

def ort(path):
    import onnxruntime as rt
    o = rt.SessionOptions()
    o.intra_op_num_threads = 2
    return rt.InferenceSession(path, o, providers=["CPUExecutionProvider"])


def check(name, a, b, tol):
    err = float(np.max(np.abs(np.asarray(a) - np.asarray(b))))
    ref = float(np.max(np.abs(np.asarray(b)))) + 1e-9
    print(f"  {name}: max diff {err:.2e} (signal max {ref:.2f})", flush=True)
    assert err <= tol * ref, f"{name} mismatch"


def main():
    os.makedirs(OUT, exist_ok=True)
    paths = PATHS
    rng = np.random.default_rng(0)

    # ---- ASR ----
    print("ASR", flush=True)
    asr = torch.jit.load(paths["asr_ckpt"]).eval()
    step = FbankStep().eval()
    wav = torch.from_numpy(rng.standard_normal(16000 * 3).astype(np.float32) * 0.1)
    fb_ref = kaldi.fbank(wav.unsqueeze(0) * 32768, frame_length=25, frame_shift=10, snip_edges=True,
                         num_mel_bins=80, energy_floor=0.0, dither=0.0, sample_frequency=16000)
    with torch.no_grad():
        check("fbank vs kaldi", step.fbank(wav * 32768), fb_ref, 1e-3)

    def asr_inputs():
        return (torch.tensor(ASR_OFF0, dtype=torch.int64), torch.zeros(6, 4, ASR_CACHE, 128), torch.zeros(6, 1, 256, 8))

    asr_path = os.path.join(OUT, "asr.onnx")
    with torch.no_grad():
        off, att, cnn = asr_inputs()
        fb_path, enc_path = asr_path + ".fbank", asr_path + ".enc"
        torch.onnx.export(step, (wav[:ASR_SAMPLES].unsqueeze(0),), fb_path,
                          input_names=["wav"], output_names=["fb"], opset_version=17, dynamo=False)
        torch.onnx.export(asr, (fb_ref[:ASR_FRAMES].unsqueeze(0), off, torch.tensor(ASR_CACHE, dtype=torch.int64),
                                att, cnn), enc_path,
                          input_names=["xs", "offset", "rcs", "att", "cnn"], output_names=["bn", "att_out", "cnn_out"],
                          opset_version=17, dynamo=False)
        merge(fb_path, enc_path, [("fb", "xs")], asr_path)
    sess = ort(asr_path)
    off, att, cnn = asr_inputs()
    o_off, o_att, o_cnn = ASR_OFF0, att.numpy(), cnn.numpy()
    with torch.no_grad():
        for c in range(6):     # several calls so the caches and offset move
            seg = wav[c * ASR_STRIDE * 160:c * ASR_STRIDE * 160 + ASR_SAMPLES].unsqueeze(0)
            ref, att, cnn = asr(fb_ref[c * ASR_STRIDE:c * ASR_STRIDE + ASR_FRAMES].unsqueeze(0), off,
                                torch.tensor(ASR_CACHE, dtype=torch.int64), att, cnn)
            got, o_att, o_cnn = sess.run(None, {"wav": seg.numpy(), "offset": np.array(o_off, np.int64),
                                                "rcs": np.array(ASR_CACHE, np.int64), "att": o_att, "cnn": o_cnn})
            off = off + ASR_OFF_STEP
            o_off += ASR_OFF_STEP
        check(f"asr out (call 6, shape {tuple(got.shape)})", got, ref, 2e-3)

    # ---- VC ----
    print("VC", flush=True)
    dit = _load_vc_model(paths["config"], paths["ckpt"], "cpu")
    vc = VcStep(dit).eval()
    spk = torch.from_numpy(np.load(os.path.join(os.path.dirname(OUT), "..", "mvc_out", "woman.npy"))).float().reshape(1, 256)
    gtm = dit.gtm(spk)
    caches = [torch.zeros(1, 2, L, 64) for L in vc.lens for _ in range(2)]
    vc_path = os.path.join(OUT, "vc.onnx")
    cond0 = torch.randn(1, SEQ, 256)
    with torch.no_grad():
        torch.onnx.export(vc, (torch.randn(1, SEQ, 80), cond0, spk, torch.zeros(1, dtype=torch.int64), *caches),
                          vc_path,
                          input_names=["noise", "cond", "spk", "steps"] + [f"{a}{i}" for i in range(4) for a in "kv"],
                          output_names=["mel"] + [f"{a}{i}_out" for i in range(4) for a in "kv"],
                          opset_version=17, dynamo=False)
    sess = ort(vc_path)
    kv_ref, offset = None, 0
    feeds = {f"{a}{i}": c.numpy() for (i, a), c in zip([(i, a) for i in range(4) for a in "kv"], caches)}
    with torch.no_grad():
        for s in range(6):
            noise, cond = torch.randn(1, SEQ, 80), torch.randn(1, SEQ, 256)
            x = noise.clone()
            pre = kv_ref
            for i in range(2):
                t_val = 1.0 - i * 0.5
                u, tmp = dit(x, torch.full((1,), t_val), torch.full((1,), max(0.0, t_val - 0.5)), cache=None,
                             cond=cond, spks=spk, offset=offset, is_inference=True, kv_cache=pre, gtm_kv=gtm)
                x = x - 0.5 * u
            kv_ref = tmp
            offset += CHUNK
            ref = (x[:, :CHUNK].transpose(1, 2) + 1) / 2
            out = sess.run(None, {"noise": noise.numpy(), "cond": cond.numpy(), "spk": spk.numpy(),
                                  "steps": np.array([s], np.int64), **feeds})
            feeds = {f"{a}{i}": out[1 + 2 * i + j] for i in range(4) for j, a in enumerate("kv")}
            check(f"vc mel step {s}", out[0], ref, 2e-3)

    # ---- Vocos ----
    print("Vocos", flush=True)
    voc = torch.jit.load(VOCODER_PATH).eval()
    head = VocosHead(voc).eval()
    voc_path = os.path.join(OUT, "vocos.onnx")
    mel = (torch.tanh(torch.randn(1, 80, 14)) + 1) / 2
    with torch.no_grad():
        torch.onnx.export(head, (mel,), voc_path, input_names=["mel"], output_names=["wav"],
                          dynamic_axes={"mel": {2: "t"}, "wav": {1: "n"}}, opset_version=17, dynamo=False)
        sess = ort(voc_path)
        for T in (12, 14):
            m = (torch.tanh(torch.randn(1, 80, T)) + 1) / 2
            ref = voc.decode(m)
            check(f"vocos T={T}", sess.run(None, {"mel": m.numpy()})[0], ref, 2e-3)

    bn_per_call = got.shape[1] * 4      # each 40 ms content frame becomes four 10 ms frames
    hop = 160
    delay = stream_delay_ms(bn_per_call, (CHUNK - 2) * hop, CHUNK * hop)
    print(f"stream delay before run time: {delay} ms")
    meta = {"engine": "meanvc", "model": MODEL, "sample_rate": 16000, "asr_samples": ASR_SAMPLES,
            "asr_hop": ASR_STRIDE * 160, "asr_cache": ASR_CACHE, "asr_offset_init": ASR_OFF0,
            "asr_offset_step": ASR_OFF_STEP, "delay_ms": delay,
            "chunk": CHUNK, "block": BLOCK, "cache_lens": vc.lens, "heads": 2, "head_dim": 64,
            "vocoder_overlap": 2, "hop": 160}
    with open(os.path.join(OUT, "meanvc.json"), "w") as f:
        json.dump(meta, f, indent=2)
    for n in ("asr.onnx", "vc.onnx", "vocos.onnx"):
        print(f"{n}: {os.path.getsize(os.path.join(OUT, n)) / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
