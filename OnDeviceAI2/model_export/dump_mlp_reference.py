"""P2 对齐：SwiGLU MLP 单层（layer 0）。down(silu(gate(x))*up(x))。输出 reference_mlp_layer0.bin"""
import os, sys, json, struct
import numpy as np, torch
try: sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception: pass

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.join(HERE, "qwen3_5_0_8b")
OUT_BIN = os.path.join(HERE, "reference_mlp_layer0.bin")
LAYER, SEQ, SEED = 0, 8, 1234
from safetensors import safe_open

PREFIX = f"model.language_model.layers.{LAYER}.mlp."
KEYS = ["gate_proj.weight", "up_proj.weight", "down_proj.weight"]


def main():
    idx = json.load(open(os.path.join(MODEL_DIR, "model.safetensors.index.json"), encoding="utf-8"))
    shard = sorted(set(idx["weight_map"].values()))[0]
    f = safe_open(os.path.join(MODEL_DIR, shard), framework="pt")
    w = {k: f.get_tensor(PREFIX + k).float().numpy() for k in KEYS}

    torch.manual_seed(SEED)
    H = w["gate_proj.weight"].shape[1]
    x = torch.randn(SEQ, H, dtype=torch.float32).numpy()

    # numpy SwiGLU
    g = x @ w["gate_proj.weight"].T
    u = x @ w["up_proj.weight"].T
    silu = g * (1.0 / (1.0 + np.exp(-g)))
    y_np = (silu * u) @ w["down_proj.weight"].T

    # torch 参照
    xt = torch.tensor(x)
    gt = xt @ torch.tensor(w["gate_proj.weight"]).T
    ut = xt @ torch.tensor(w["up_proj.weight"]).T
    y_hf = (torch.nn.functional.silu(gt) * ut) @ torch.tensor(w["down_proj.weight"]).T
    y_hf = y_hf.numpy()
    diff = float(np.max(np.abs(y_np - y_hf)))
    print(f"[numpy vs torch] max|Δ|={diff:.3e}", "✅" if diff < 1e-4 else "⚠️")

    out = {"input": x, "ref_output": y_hf}
    for k in KEYS: out["w." + k] = w[k]
    with open(OUT_BIN, "wb") as fo:
        fo.write(b"RFT1"); fo.write(struct.pack("<I", len(out)))
        for name, arr in out.items():
            arr = np.ascontiguousarray(arr, dtype=np.float32)
            nb = name.encode("utf-8")
            fo.write(struct.pack("<I", len(nb))); fo.write(nb)
            fo.write(struct.pack("<I", arr.ndim))
            for d in arr.shape: fo.write(struct.pack("<I", d))
            fo.write(arr.tobytes())
    print(f"写出 {OUT_BIN} ({os.path.getsize(OUT_BIN)/1024:.0f} KB)")


if __name__ == "__main__":
    main()
