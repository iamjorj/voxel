"""Export a trained LLVC checkpoint to the ONNX + JSON pair Voxel loads.

Usage: python export_onnx.py --checkpoint G_123.pth --config config.json --out <name>.onnx [--chunk-factor 4]
"""
import argparse
import json
import os
import sys

import torch

HERE = os.path.dirname(os.path.abspath(__file__))
LLVC_DIR = os.path.join(os.path.dirname(HERE), "LLVC")
sys.path.insert(0, LLVC_DIR)
from model import Net  # noqa: E402


class StreamStep(torch.nn.Module):
    def __init__(self, net):
        super().__init__()
        self.net = net

    def forward(self, chunk, enc_buf, dec_buf, out_buf, pre_ctx):
        return self.net(chunk, enc_buf.clone(), dec_buf.clone(), out_buf.clone(), pre_ctx.clone(), pad=False)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--checkpoint", required=True)
    ap.add_argument("--config", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--chunk-factor", type=int, default=4)
    args = ap.parse_args()

    config = json.load(open(args.config))
    model = Net(**config["model_params"])
    model.load_state_dict(torch.load(args.checkpoint, map_location="cpu")["model"])
    model.eval()
    L = model.L
    chunk_len = model.dec_chunk_size * L * args.chunk_factor
    enc_buf, dec_buf, out_buf = model.init_buffers(1, torch.device("cpu"))
    pre_ctx = model.convnet_pre.init_ctx_buf(1, torch.device("cpu"))
    chunk = torch.zeros(1, 1, 2 * L + chunk_len)

    with torch.inference_mode():
        torch.onnx.export(StreamStep(model).eval(), (chunk, enc_buf, dec_buf, out_buf, pre_ctx), args.out,
                          input_names=["chunk", "enc_buf", "dec_buf", "out_buf", "pre_ctx"],
                          output_names=["out", "enc_buf_out", "dec_buf_out", "out_buf_out", "pre_ctx_out"],
                          opset_version=17, dynamo=False)
    meta = {"sample_rate": config["data"]["sr"], "L": L, "chunk_len": chunk_len, "context": 2 * L,
            "enc_buf": list(enc_buf.shape), "dec_buf": list(dec_buf.shape),
            "out_buf": list(out_buf.shape), "pre_ctx": list(pre_ctx.shape)}
    json.dump(meta, open(os.path.splitext(args.out)[0] + ".json", "w"), indent=2)
    print(f"exported {args.out} ({os.path.getsize(args.out) // 1024} KB), chunk {chunk_len} samples")


if __name__ == "__main__":
    main()
