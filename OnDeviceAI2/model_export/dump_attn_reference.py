"""
P2b 对齐基准：导出单层 全注意力(Qwen3_5Attention) 的输入/权重/cos-sin/参考输出/中间张量。

流程同 dump_reference.py：HF 真模块 → numpy 重写 → 断言一致 → 写 .bin。
用 layer 3（它是 full_attention 层）。
输出：reference_attn_layer3.bin
"""
import os
import sys
import json
import struct
import numpy as np
import torch

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.join(HERE, "qwen3_5_0_8b")
OUT_BIN = os.path.join(HERE, "reference_attn_layer3.bin")
LAYER = 3
SEQ = 8
SEED = 1234

from safetensors import safe_open  # noqa: E402

PREFIX = f"model.language_model.layers.{LAYER}.self_attn."
PARAM_KEYS = ["q_proj.weight", "k_proj.weight", "v_proj.weight", "o_proj.weight",
              "q_norm.weight", "k_norm.weight"]


def load_weights():
    idx = json.load(open(os.path.join(MODEL_DIR, "model.safetensors.index.json"), encoding="utf-8"))
    shard = sorted(set(idx["weight_map"].values()))[0]
    f = safe_open(os.path.join(MODEL_DIR, shard), framework="pt")
    return {k: f.get_tensor(PREFIX + k).float() for k in PARAM_KEYS}


# ── numpy 重写（C# 蓝本）──────────────────────────────────────────────────
def rmsnorm_np(x, w, eps):       # Qwen3_5RMSNorm: 零中心化，乘 (1+weight)，over 最后一维
    var = np.mean(x * x, axis=-1, keepdims=True)
    return (x / np.sqrt(var + eps)) * (1.0 + w)


def apply_rope_np(t, cos, sin, rot):
    # t: [S, heads, hd]；cos/sin: [S, rot]；只转前 rot 维（NeoX 式 rotate_half）
    S, nh, hd = t.shape
    out = t.copy()
    half = rot // 2
    for s in range(S):
        for h in range(nh):
            for d in range(half):
                x1 = t[s, h, d]
                x2 = t[s, h, d + half]
                c = cos[s, d]; sn = sin[s, d]
                out[s, h, d] = x1 * c - x2 * sn
                out[s, h, d + half] = x2 * cos[s, d + half] + x1 * sin[s, d + half]
    return out


def attention_numpy(x, w, cos, sin, cfg, dump):
    S, H = x.shape
    nh, nkv, hd, rot = cfg["nh"], cfg["nkv"], cfg["hd"], cfg["rot"]
    eps = cfg["eps"]
    Wq = w["q_proj.weight"].numpy(); Wk = w["k_proj.weight"].numpy()
    Wv = w["v_proj.weight"].numpy(); Wo = w["o_proj.weight"].numpy()
    qn = w["q_norm.weight"].numpy(); kn = w["k_norm.weight"].numpy()

    qg = (x @ Wq.T).reshape(S, nh, hd * 2)
    query = qg[:, :, :hd]
    gate = qg[:, :, hd:].reshape(S, nh * hd)
    k = (x @ Wk.T).reshape(S, nkv, hd)
    v = (x @ Wv.T).reshape(S, nkv, hd)

    query = rmsnorm_np(query, qn, eps)
    k = rmsnorm_np(k, kn, eps)
    dump["q_normed"] = np.ascontiguousarray(query.reshape(S, nh * hd))

    query = apply_rope_np(query, cos, sin, rot)
    k = apply_rope_np(k, cos, sin, rot)
    dump["q_roped"] = np.ascontiguousarray(query.reshape(S, nh * hd))

    scaling = 1.0 / np.sqrt(hd)
    groups = nh // nkv
    out = np.zeros((S, nh, hd), dtype=np.float64)
    for qh in range(nh):
        kvh = qh // groups
        for ti in range(S):
            sc = np.array([np.dot(query[ti, qh].astype(np.float64),
                                  k[tj, kvh].astype(np.float64)) * scaling for tj in range(ti + 1)])
            sc = sc - sc.max()
            p = np.exp(sc); p /= p.sum()
            for tj in range(ti + 1):
                out[ti, qh] += p[tj] * v[tj, kvh]
    attn = out.reshape(S, nh * hd).astype(np.float32)
    dump["attn_pre_gate"] = attn.copy()
    sig = 1.0 / (1.0 + np.exp(-gate))
    attn = attn * sig
    return attn @ Wo.T


def write_bin(path, tensors):
    with open(path, "wb") as f:
        f.write(b"RFT1")
        f.write(struct.pack("<I", len(tensors)))
        for name, arr in tensors.items():
            arr = np.ascontiguousarray(arr, dtype=np.float32)
            nb = name.encode("utf-8")
            f.write(struct.pack("<I", len(nb))); f.write(nb)
            f.write(struct.pack("<I", arr.ndim))
            for d in arr.shape:
                f.write(struct.pack("<I", d))
            f.write(arr.tobytes())
    print(f"写出 {path}  ({os.path.getsize(path)/1024:.1f} KB, {len(tensors)} tensors)")


def main():
    from transformers import AutoConfig
    from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5Attention, Qwen3_5TextRotaryEmbedding

    tc = AutoConfig.from_pretrained(MODEL_DIR).get_text_config()
    tc._attn_implementation = "eager"
    rot = int(tc.head_dim * tc.rope_parameters.get("partial_rotary_factor", 1.0))
    cfg = dict(hidden=tc.hidden_size, nh=tc.num_attention_heads, nkv=tc.num_key_value_heads,
               hd=tc.head_dim, rot=rot, eps=tc.rms_norm_eps, theta=tc.rope_parameters["rope_theta"])
    print("cfg:", cfg)

    w = load_weights()
    mod = Qwen3_5Attention(tc, layer_idx=LAYER).to(torch.float32).eval()
    mod.load_state_dict({k: w[k].clone() for k in PARAM_KEYS}, strict=True)

    torch.manual_seed(SEED)
    x = torch.randn(1, SEQ, cfg["hidden"], dtype=torch.float32)

    rope = Qwen3_5TextRotaryEmbedding(tc)
    pos = torch.arange(SEQ)[None, :]
    cos, sin = rope(x, pos)                     # [1,S,rot]
    cos_np = cos[0].numpy(); sin_np = sin[0].numpy()

    # 因果 mask（eager）
    mask = torch.full((SEQ, SEQ), float("-inf"))
    mask = torch.triu(mask, diagonal=1)[None, None]   # [1,1,S,S]

    with torch.no_grad():
        y_hf, _ = mod(x, position_embeddings=(cos, sin), attention_mask=mask, past_key_values=None)
    y_hf = y_hf[0].numpy()

    dump = {}
    y_np = attention_numpy(x[0].numpy(), w, cos_np, sin_np, cfg, dump)

    diff = float(np.max(np.abs(y_np - y_hf)))
    rel = diff / (np.max(np.abs(y_hf)) + 1e-9)
    print(f"[numpy vs HF] max|Δ| = {diff:.3e}   rel = {rel:.3e}")
    print("✅ numpy == HF" if diff < 1e-3 else "⚠️ 不一致！")

    out = {"input": x[0].numpy(), "cos": cos_np, "sin": sin_np, "ref_output": y_hf, "y_numpy": y_np}
    for k in PARAM_KEYS:
        out["w." + k] = w[k].numpy()
    for k, vv in dump.items():
        out["mid." + k] = vv
    write_bin(OUT_BIN, out)
    json.dump(dict(cfg=cfg, seq=SEQ, seed=SEED, layer=LAYER, max_abs_diff=diff),
              open(OUT_BIN + ".json", "w", encoding="utf-8"), ensure_ascii=False, indent=2)


if __name__ == "__main__":
    main()
