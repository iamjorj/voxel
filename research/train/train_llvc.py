"""Train an LLVC voice from QuickVC-made pairs. Single process, resumable, CPU or GPU.

A cleaned-up version of Koe AI's train.py: same model, same losses (multi-period
discriminator + feature matching + multi-resolution mel), minus the fairseq/HuBERT
perceptual loss so nothing exotic has to be installed. Checkpoints land in <dir>/checkpoints
and training resumes from the newest one automatically.

Usage: python train_llvc.py --dir <run dir containing config.json> [--steps 200000]
"""
import argparse
import glob
import json
import os
import random
import sys
import time

import soundfile as sf
import torch
import torch.nn.functional as F

HERE = os.path.dirname(os.path.abspath(__file__))
LLVC_DIR = os.path.join(os.path.dirname(HERE), "LLVC")
sys.path.insert(0, LLVC_DIR)
from dataset import LLVCDataset  # noqa: E402
from discriminators import MultiPeriodDiscriminator, discriminator_loss, feature_loss, generator_loss  # noqa: E402
from model import Net  # noqa: E402
import utils  # noqa: E402


def latest(dirpath, pattern):
    files = glob.glob(os.path.join(dirpath, pattern))
    if not files:
        return None
    return max(files, key=lambda p: int(os.path.basename(p).split("_")[1].split(".")[0]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    ap.add_argument("--steps", type=int, default=200000)
    ap.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    args = ap.parse_args()

    config = json.load(open(os.path.join(args.dir, "config.json")))
    device = torch.device(args.device)
    torch.manual_seed(config["seed"])
    random.seed(config["seed"])
    ckpt_dir = os.path.join(args.dir, "checkpoints")
    samples_dir = os.path.join(args.dir, "samples")
    os.makedirs(ckpt_dir, exist_ok=True)
    os.makedirs(samples_dir, exist_ok=True)

    data = LLVCDataset(**config["data"], dset="train")
    print(f"{len(data)} training pairs")
    loader = torch.utils.data.DataLoader(data, batch_size=config["batch_size"], shuffle=True, drop_last=True,
                                         num_workers=2 if device.type == "cuda" else 0)

    net_g = Net(**config["model_params"]).to(device)
    net_d = MultiPeriodDiscriminator(periods=config["periods"]).to(device)
    print(f"generator {utils.model_size(net_g):.2f} M params")
    opt = config["optim"]
    optim_g = torch.optim.AdamW(net_g.parameters(), opt["lr"], betas=opt["betas"], eps=opt["eps"], weight_decay=opt["weight_decay"])
    optim_d = torch.optim.AdamW(net_d.parameters(), opt["lr"], betas=opt["betas"], eps=opt["eps"], weight_decay=opt["weight_decay"])

    step, epoch = 0, 0
    g_path, d_path = latest(ckpt_dir, "G_*.pth"), latest(ckpt_dir, "D_*.pth")
    if g_path and d_path:
        net_g, optim_g, _, epoch, step = utils.load_checkpoint(g_path, net_g, optim_g)
        net_d, optim_d, _, _, _ = utils.load_checkpoint(d_path, net_d, optim_d)
        print(f"resumed from step {step}")
    sched_g = torch.optim.lr_scheduler.ExponentialLR(optim_g, gamma=config["lr_sched"]["lr_decay"], last_epoch=epoch - 1)
    sched_d = torch.optim.lr_scheduler.ExponentialLR(optim_d, gamma=config["lr_sched"]["lr_decay"], last_epoch=epoch - 1)

    use_amp = config.get("fp16_run", False) and device.type == "cuda"
    scaler = torch.amp.GradScaler(enabled=use_amp)
    test_wavs = [(os.path.basename(p), utils.load_wav_to_torch(p, config["data"]["sr"]))
                 for p in sorted(glob.glob(os.path.join(config["test_dir"], "*.wav")))[:2]]
    seg = config["segment_size"]
    t0 = time.time()

    while step < args.steps:
        net_g.train(); net_d.train()
        for og, gt in loader:
            if step >= args.steps:
                break
            og, gt = og.to(device), gt.to(device)

            with torch.autocast(device.type, enabled=use_amp):
                output = net_g(og)
            # Random slice for the discriminator (cheaper), full length for mel loss.
            if seg < output.shape[-1]:
                s = random.randint(0, output.shape[-1] - seg - 1)
                gt_s, out_s = gt[:, :, s:s + seg], output.detach()[:, :, s:s + seg]
            else:
                gt_s, out_s = gt, output.detach()

            with torch.autocast(device.type, enabled=use_amp):
                d_r, d_g, _, _ = net_d(out_s, gt_s)
            loss_d, _, _ = discriminator_loss(d_r, d_g)
            optim_d.zero_grad(set_to_none=True)
            scaler.scale(loss_d).backward()
            scaler.unscale_(optim_d)
            torch.nn.utils.clip_grad_norm_(net_d.parameters(), config["grad_clip_threshold"])
            scaler.step(optim_d)

            with torch.autocast(device.type, enabled=use_amp):
                d_r, d_g, fmap_r, fmap_g = net_d(gt, output)
            loss_mel = utils.aux_mel_loss(output.float(), gt, config) * config["aux_mel"]["c"]
            loss_fm = feature_loss(fmap_r, fmap_g) * config["feature_loss_c"]
            loss_gen, _ = generator_loss(d_g)
            loss_g = loss_gen * config["disc_loss_c"] + loss_fm + loss_mel
            optim_g.zero_grad(set_to_none=True)
            scaler.scale(loss_g).backward()
            scaler.unscale_(optim_g)
            torch.nn.utils.clip_grad_norm_(net_g.parameters(), config["grad_clip_threshold"])
            scaler.step(optim_g)
            scaler.update()
            step += 1

            if step % config["log_interval"] == 0 or step == 1:
                rate = step / max(time.time() - t0, 1e-6)
                print(f"step {step}  mel {loss_mel.item():.2f}  fm {loss_fm.item():.2f}  gen {loss_gen.item():.2f}  "
                      f"disc {loss_d.item():.2f}  ({rate:.2f} steps/s)", flush=True)
            if step % config["checkpoint_interval"] == 0:
                utils.save_state(net_g, optim_g, optim_g.param_groups[0]["lr"], epoch, step, os.path.join(ckpt_dir, f"G_{step}.pth"))
                utils.save_state(net_d, optim_d, optim_d.param_groups[0]["lr"], epoch, step, os.path.join(ckpt_dir, f"D_{step}.pth"))
                # Keep only the two newest checkpoints: they're ~200 MB each with optimizer state.
                for pat in ("G_*.pth", "D_*.pth"):
                    old = sorted(glob.glob(os.path.join(ckpt_dir, pat)), key=lambda p: int(os.path.basename(p).split("_")[1].split(".")[0]))
                    for p in old[:-2]:
                        os.remove(p)
                net_g.eval()
                with torch.no_grad():
                    for name, wav in test_wavs:
                        out = net_g(wav.view(1, 1, -1).to(device)).reshape(-1).cpu().clamp(-1, 1)
                        sf.write(os.path.join(samples_dir, f"{step}_{name}"), out.numpy(), config["data"]["sr"])
                net_g.train()
                print(f"saved checkpoint at step {step}", flush=True)
        epoch += 1
        sched_g.step(); sched_d.step()

    utils.save_state(net_g, optim_g, optim_g.param_groups[0]["lr"], epoch, step, os.path.join(ckpt_dir, f"G_{step}.pth"))
    utils.save_state(net_d, optim_d, optim_d.param_groups[0]["lr"], epoch, step, os.path.join(ckpt_dir, f"D_{step}.pth"))
    print(f"done at step {step}")


if __name__ == "__main__":
    main()
