"""
P2a 对齐基准：导出单层 Gated DeltaNet 的输入 / 权重 / 参考输出 / 中间张量。

流程：
  1. 用 transformers 实例化 Qwen3_5GatedDeltaNet(layer_idx=0)，载入真实权重（safetensors）。
  2. 喂一个固定随机输入（seed 固定），跑 HF 真模块 → 参考输出。
  3. 用纯 numpy 的「逐 token 递归」重写一遍同样的前向，断言与 HF 输出一致(<1e-4)。
     —— 这份 numpy 实现就是 C# DeltaNet 的逐行蓝本。
  4. 把 输入 + 该层权重 + HF 参考输出 + numpy 中间张量 全部写进一个 .bin，供 C# 测试读取比对。

输出文件：reference_deltanet_layer0.bin（格式见 write_bin）
用法：python dump_reference.py
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
OUT_BIN = os.path.join(HERE, "reference_deltanet_layer0.bin")
LAYER = 0
SEQ = 8
SEED = 1234

# ── 读取该层 linear_attn 权重（bf16 → fp32 numpy）──────────────────────────
from safetensors import safe_open  # noqa: E402

PREFIX = f"model.language_model.layers.{LAYER}.linear_attn."
PARAM_KEYS = ["in_proj_qkv.weight", "in_proj_a.weight", "in_proj_b.weight",
              "in_proj_z.weight", "conv1d.weight", "A_log", "dt_bias",
              "norm.weight", "out_proj.weight"]


def load_weights():
    idx = json.load(open(os.path.join(MODEL_DIR, "model.safetensors.index.json"), encoding="utf-8"))
    shard = sorted(set(idx["weight_map"].values()))[0]
    f = safe_open(os.path.join(MODEL_DIR, shard), framework="pt")
    w = {}
    for k in PARAM_KEYS:
        w[k] = f.get_tensor(PREFIX + k).float()
    return w


# ── 纯 numpy 的逐 token 递归 DeltaNet（C# 蓝本）────────────────────────────
def rmsnorm_gated_np(y, weight, z, eps):
    # y,z: [v_dim]; RMSNorm over v_dim, 再乘 silu(z)
    var = np.mean(y * y)
    yn = y * (1.0 / np.sqrt(var + eps))
    yn = weight * yn
    return yn * (z * (1.0 / (1.0 + np.exp(-z))))   # silu(z)=z*sigmoid(z)


def deltanet_numpy(x, w, cfg, dump):
    """x: [S, hidden] fp32。返回 output [S, hidden]，并把中间张量塞进 dump。"""
    S, H = x.shape
    nvh, nkh = cfg["nvh"], cfg["nkh"]
    kdim, vdim = cfg["kdim"], cfg["vdim"]
    K = cfg["conv"]
    eps = cfg["eps"]
    key_dim = nkh * kdim          # 2048
    val_dim = nvh * vdim          # 2048

    Wqkv = w["in_proj_qkv.weight"].numpy()   # [6144,1024]
    Wa = w["in_proj_a.weight"].numpy()       # [16,1024]
    Wb = w["in_proj_b.weight"].numpy()       # [16,1024]
    Wz = w["in_proj_z.weight"].numpy()       # [2048,1024]
    conv = w["conv1d.weight"].numpy()        # [6144,1,4]
    A_log = w["A_log"].numpy()               # [16]
    dt_bias = w["dt_bias"].numpy()           # [16]
    norm_w = w["norm.weight"].numpy()        # [128]
    Wout = w["out_proj.weight"].numpy()      # [1024,2048]

    # 1) 投影 in_proj_qkv → [S,6144]
    mixed = x @ Wqkv.T                        # [S,6144]
    # 2) depthwise 因果卷积(kernel=4, 左pad 3, 无bias) + SiLU
    convw = conv[:, 0, :]                     # [6144,4]
    conv_out = np.zeros_like(mixed)
    for t in range(S):
        for j in range(K):                    # out[t] = sum_j w[:,j]*x[t-(K-1)+j]
            ti = t - (K - 1) + j
            if ti >= 0:
                conv_out[t] += convw[:, j] * mixed[ti]
    mixed = conv_out * (1.0 / (1.0 + np.exp(-conv_out)))   # SiLU
    dump["mixed_conv"] = mixed.copy()

    # 3) split q,k,v
    q = mixed[:, 0:key_dim].reshape(S, nkh, kdim)
    k = mixed[:, key_dim:2 * key_dim].reshape(S, nkh, kdim)
    v = mixed[:, 2 * key_dim:2 * key_dim + val_dim].reshape(S, nvh, vdim)

    # 4) 门控参数
    a = x @ Wa.T                              # [S,16]
    b = x @ Wb.T                              # [S,16]
    beta = 1.0 / (1.0 + np.exp(-b))           # sigmoid
    softplus = np.log1p(np.exp(a + dt_bias))  # softplus(a+dt_bias)
    g = -np.exp(A_log) * softplus             # [S,16]  (负值)
    z = (x @ Wz.T).reshape(S, nvh, vdim)
    dump["g"] = g.copy(); dump["beta"] = beta.copy()

    # 5) 逐 token 递归（每 head 独立；S_state[k_dim,v_dim]）
    assert nvh == nkh, "0.8B: vh==kh, 无 GQA 复制"
    scale = 1.0 / np.sqrt(kdim)
    state = np.zeros((nvh, kdim, vdim), dtype=np.float64)   # fp64 累积更稳，最后转回
    core = np.zeros((S, nvh, vdim), dtype=np.float32)
    for t in range(S):
        for h in range(nvh):
            qh = q[t, h].astype(np.float64)
            kh = k[t, h].astype(np.float64)
            vh = v[t, h].astype(np.float64)
            # l2norm(eps=1e-6) over dim
            qh = qh / np.sqrt(np.sum(qh * qh) + 1e-6)
            kh = kh / np.sqrt(np.sum(kh * kh) + 1e-6)
            qh = qh * scale
            St = state[h]
            St *= np.exp(g[t, h])                     # 衰减
            kv_mem = (St * kh[:, None]).sum(axis=0)   # sum over k → [v]
            delta = (vh - kv_mem) * beta[t, h]
            St += kh[:, None] * delta[None, :]        # outer
            core[t, h] = (St * qh[:, None]).sum(axis=0).astype(np.float32)
    dump["core"] = core.copy()

    # 6) gated RMSNorm（over v_dim=128）+ out_proj
    out = np.zeros((S, cfg["hidden"]), dtype=np.float32)
    normed = np.zeros((S, nvh, vdim), dtype=np.float32)
    for t in range(S):
        for h in range(nvh):
            normed[t, h] = rmsnorm_gated_np(core[t, h], norm_w, z[t, h], eps)
    flat = normed.reshape(S, nvh * vdim)             # [S,2048]
    out = flat @ Wout.T                              # [S,1024]
    dump["normed"] = flat.copy()
    return out


# ── 二进制写出 ────────────────────────────────────────────────────────────
def write_bin(path, tensors: dict):
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
    from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5GatedDeltaNet

    tc = AutoConfig.from_pretrained(MODEL_DIR).get_text_config()
    cfg = dict(hidden=tc.hidden_size, nvh=tc.linear_num_value_heads, nkh=tc.linear_num_key_heads,
               kdim=tc.linear_key_head_dim, vdim=tc.linear_value_head_dim,
               conv=tc.linear_conv_kernel_dim, eps=tc.rms_norm_eps)
    print("cfg:", cfg)

    w = load_weights()

    # 实例化 HF 模块并载入权重
    mod = Qwen3_5GatedDeltaNet(tc, layer_idx=LAYER).to(torch.float32).eval()
    sd = {k: w[k].clone() for k in PARAM_KEYS}
    missing, unexpected = mod.load_state_dict(sd, strict=False)
    assert not unexpected, f"unexpected: {unexpected}"
    assert not missing, f"missing: {missing}"

    # 固定输入
    torch.manual_seed(SEED)
    x = torch.randn(1, SEQ, cfg["hidden"], dtype=torch.float32)

    with torch.no_grad():
        y_hf = mod(x, cache_params=None, attention_mask=None)   # [1,S,1024]
    y_hf = y_hf[0].numpy()

    # numpy 逐 token 递归参考
    dump = {}
    y_np = deltanet_numpy(x[0].numpy(), w, cfg, dump)

    diff = np.max(np.abs(y_np - y_hf))
    rel = diff / (np.max(np.abs(y_hf)) + 1e-9)
    print(f"[numpy vs HF] max|Δ| = {diff:.3e}   rel = {rel:.3e}")
    if diff > 1e-3:
        print("⚠️  numpy 参考与 HF 不一致，先别用于对齐！")
    else:
        print("✅ numpy 递归实现 == HF chunk 实现（前向已验证）")

    # 写出：输入 + 权重 + HF 参考输出 + 中间张量
    out = {"input": x[0].numpy(), "ref_output": y_hf, "y_numpy": y_np}
    for k in PARAM_KEYS:
        out["w." + k] = w[k].numpy()
    for k, vv in dump.items():
        out["mid." + k] = vv
    write_bin(OUT_BIN, out)

    # 同时存一份 meta，方便 C# 端核对维度
    meta = dict(cfg=cfg, seq=SEQ, seed=SEED, layer=LAYER,
                max_abs_diff_numpy_vs_hf=float(diff))
    json.dump(meta, open(OUT_BIN + ".json", "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("meta:", meta)


if __name__ == "__main__":
    main()
