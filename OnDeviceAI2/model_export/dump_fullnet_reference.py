"""
P2c 对齐基准：整网前向参考转储。

两套基准同时写出：
  A. HF fp32 真值：Qwen3_5ForCausalLM 整网 forward（output_hidden_states）
     → 每层 hidden + 末位 logits + 贪心续写 token ids（ground truth）
  B. numpy int8 仿真：按 export_qwen.py 完全相同的 int8 量化→反量化权重，
     纯 numpy 跑整网 → 每层 hidden + 末位 logits
     （C# int8 引擎应与 B 逐层对齐 <1e-3；与 A 的差即纯量化损失）

输出：reference_fullnet.bin (RFT1) + .json meta
用法：venv\\Scripts\\python dump_fullnet_reference.py
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
OUT_BIN = os.path.join(HERE, "reference_fullnet.bin")

PROMPT = "中国的首都是北京，法国的首都是"
GREEDY_TOKENS = 24

LM = "model.language_model"


# ── int8 量化仿真（与 export_qwen.py 位级一致）───────────────────────────
def quant_dequant_int8(arr):
    """2D per-output-channel 对称量化 → 反量化（模拟 C# 端看到的权重）。"""
    if arr.ndim != 2:
        return arr.astype(np.float32)
    amax = np.max(np.abs(arr), axis=1)
    scale = np.where(amax > 0, amax / 127.0, 1.0).astype(np.float32)
    q = np.round(arr / scale[:, None]).clip(-127, 127).astype(np.int8)
    return (q.astype(np.float32) * scale[:, None]).astype(np.float32)


# ── numpy 算子（与 C# QwenMath 语义一致）─────────────────────────────────
def rmsnorm_1p(x, w, eps):
    """(1+weight) RMSNorm over last dim。x: [..., D]"""
    inv = 1.0 / np.sqrt(np.mean(x * x, axis=-1, keepdims=True) + eps)
    return (1.0 + w) * (x * inv)


def silu(x):
    return x * (1.0 / (1.0 + np.exp(-x)))


def sigmoid(x):
    return 1.0 / (1.0 + np.exp(-x))


# ── DeltaNet（复用 dump_reference.py 的验证实现，权重换 int8 仿真）────────
def deltanet_np(x, w, cfg):
    S = x.shape[0]
    nvh = cfg["nvh"]; kdim = cfg["kdim"]; vdim = cfg["vdim"]
    K = cfg["conv"]; eps = cfg["eps"]
    key_dim = cfg["nkh"] * kdim
    val_dim = nvh * vdim

    mixed = x @ w["qkv"].T
    convw = w["conv"][:, 0, :]
    conv_out = np.zeros_like(mixed)
    for t in range(S):
        for j in range(K):
            ti = t - (K - 1) + j
            if ti >= 0:
                conv_out[t] += convw[:, j] * mixed[ti]
    mixed = silu(conv_out)

    q = mixed[:, 0:key_dim].reshape(S, nvh, kdim)
    k = mixed[:, key_dim:2 * key_dim].reshape(S, nvh, kdim)
    v = mixed[:, 2 * key_dim:2 * key_dim + val_dim].reshape(S, nvh, vdim)

    a = x @ w["a"].T
    b = x @ w["b"].T
    beta = sigmoid(b)
    g = -np.exp(w["A_log"]) * np.log1p(np.exp(a + w["dt_bias"]))
    z = (x @ w["z"].T).reshape(S, nvh, vdim)

    scale = 1.0 / np.sqrt(kdim)
    state = np.zeros((nvh, kdim, vdim), dtype=np.float64)
    core = np.zeros((S, nvh, vdim), dtype=np.float32)
    for t in range(S):
        for h in range(nvh):
            qh = q[t, h].astype(np.float64)
            kh = k[t, h].astype(np.float64)
            vh = v[t, h].astype(np.float64)
            qh = qh / np.sqrt(np.sum(qh * qh) + 1e-6) * scale
            kh = kh / np.sqrt(np.sum(kh * kh) + 1e-6)
            St = state[h]
            St *= np.exp(g[t, h])
            kv_mem = (St * kh[:, None]).sum(axis=0)
            delta = (vh - kv_mem) * beta[t, h]
            St += kh[:, None] * delta[None, :]
            core[t, h] = (St * qh[:, None]).sum(axis=0).astype(np.float32)

    normed = np.zeros((S, nvh, vdim), dtype=np.float32)
    for t in range(S):
        for h in range(nvh):
            y = core[t, h]
            yn = y * (1.0 / np.sqrt(np.mean(y * y) + eps))
            yn = w["norm"] * yn
            normed[t, h] = yn * silu(z[t, h])
    return normed.reshape(S, val_dim) @ w["out"].T


# ── 全注意力（与 C# FullAttention 语义一致）───────────────────────────────
def attention_np(x, w, cfg):
    S = x.shape[0]
    nh = cfg["nh"]; nkv = cfg["nkv"]; hd = cfg["hd"]
    rot = cfg["rot"]; theta = cfg["theta"]; eps = cfg["eps"]
    groups = nh // nkv

    qg = (x @ w["q"].T).reshape(S, nh, 2 * hd)
    query = qg[:, :, :hd].copy()             # [S,nh,hd]
    gate = qg[:, :, hd:].copy()
    kbuf = (x @ w["k"].T).reshape(S, nkv, hd)
    vbuf = (x @ w["v"].T).reshape(S, nkv, hd)

    query = rmsnorm_1p(query, w["qn"], eps)
    kbuf = rmsnorm_1p(kbuf, w["kn"], eps)

    half = rot // 2
    inv_freq = 1.0 / (theta ** (2.0 * np.arange(half) / rot))
    pos = np.arange(S)[:, None]
    ang = pos * inv_freq[None, :]            # [S,half]
    cos = np.cos(ang); sin = np.sin(ang)

    def rope(buf):
        x1 = buf[:, :, :half].copy()
        x2 = buf[:, :, half:rot].copy()
        buf[:, :, :half] = x1 * cos[:, None, :] - x2 * sin[:, None, :]
        buf[:, :, half:rot] = x2 * cos[:, None, :] + x1 * sin[:, None, :]
    rope(query); rope(kbuf)

    scaling = 1.0 / np.sqrt(hd)
    out = np.zeros((S, nh, hd), dtype=np.float32)
    for h in range(nh):
        kvh = h // groups
        sc = (query[:, h] @ kbuf[:, kvh].T) * scaling   # [S,S]
        mask = np.triu(np.full((S, S), -np.inf), k=1)
        sc = sc + mask
        sc = sc - sc.max(axis=-1, keepdims=True)
        p = np.exp(sc); p /= p.sum(axis=-1, keepdims=True)
        out[:, h] = p @ vbuf[:, kvh]

    out = out * sigmoid(gate)
    return out.reshape(S, nh * hd) @ w["o"].T


# ── 整网 numpy int8 前向 ──────────────────────────────────────────────────
def fullnet_np(token_ids, R, tc, quant=True):
    qd = quant_dequant_int8 if quant else (lambda a: a.astype(np.float32))
    eps = tc["rms_norm_eps"]
    L = tc["num_hidden_layers"]
    layer_types = tc["layer_types"]

    dn_cfg = dict(nvh=tc["linear_num_value_heads"], nkh=tc["linear_num_key_heads"],
                  kdim=tc["linear_key_head_dim"], vdim=tc["linear_value_head_dim"],
                  conv=tc["linear_conv_kernel_dim"], eps=eps)
    at_cfg = dict(nh=tc["num_attention_heads"], nkv=tc["num_key_value_heads"],
                  hd=tc["head_dim"],
                  rot=int(round(tc["head_dim"] * tc["rope_parameters"]["partial_rotary_factor"])),
                  theta=tc["rope_parameters"]["rope_theta"], eps=eps)

    embed = qd(R.get(f"{LM}.embed_tokens.weight"))     # [vocab,1024] int8 仿真
    x = embed[np.array(token_ids)]                     # [S,1024]
    hiddens = [x.copy()]

    for i in range(L):
        p = f"{LM}.layers.{i}"
        ln1 = R.get(f"{p}.input_layernorm.weight")
        h = rmsnorm_1p(x, ln1, eps)
        if layer_types[i] == "full_attention":
            w = dict(q=qd(R.get(f"{p}.self_attn.q_proj.weight")),
                     k=qd(R.get(f"{p}.self_attn.k_proj.weight")),
                     v=qd(R.get(f"{p}.self_attn.v_proj.weight")),
                     o=qd(R.get(f"{p}.self_attn.o_proj.weight")),
                     qn=R.get(f"{p}.self_attn.q_norm.weight"),
                     kn=R.get(f"{p}.self_attn.k_norm.weight"))
            x = x + attention_np(h.astype(np.float32), w, at_cfg)
        else:
            w = dict(qkv=qd(R.get(f"{p}.linear_attn.in_proj_qkv.weight")),
                     a=qd(R.get(f"{p}.linear_attn.in_proj_a.weight")),
                     b=qd(R.get(f"{p}.linear_attn.in_proj_b.weight")),
                     z=qd(R.get(f"{p}.linear_attn.in_proj_z.weight")),
                     conv=R.get(f"{p}.linear_attn.conv1d.weight"),
                     A_log=R.get(f"{p}.linear_attn.A_log"),
                     dt_bias=R.get(f"{p}.linear_attn.dt_bias"),
                     norm=R.get(f"{p}.linear_attn.norm.weight"),
                     out=qd(R.get(f"{p}.linear_attn.out_proj.weight")))
            x = x + deltanet_np(h.astype(np.float32), w, dn_cfg)

        ln2 = R.get(f"{p}.post_attention_layernorm.weight")
        h = rmsnorm_1p(x, ln2, eps)
        g = silu(h @ qd(R.get(f"{p}.mlp.gate_proj.weight")).T)
        u = h @ qd(R.get(f"{p}.mlp.up_proj.weight")).T
        x = x + (g * u) @ qd(R.get(f"{p}.mlp.down_proj.weight")).T
        hiddens.append(x.copy().astype(np.float32))
        print(f"    np 层 {i:2d} 完成")

    fn = R.get(f"{LM}.norm.weight")
    xn = rmsnorm_1p(x, fn, eps)
    logits_last = xn[-1] @ embed.T                     # tie lm_head
    return hiddens, logits_last.astype(np.float32)


# ── safetensors 读取 ─────────────────────────────────────────────────────
class SafeReader:
    def __init__(self, model_dir):
        from safetensors import safe_open
        idx = json.load(open(os.path.join(model_dir, "model.safetensors.index.json"), encoding="utf-8"))
        shard = sorted(set(idx["weight_map"].values()))[0]
        self._f = safe_open(os.path.join(model_dir, shard), framework="pt")

    def get(self, name):
        return self._f.get_tensor(name).float().cpu().numpy().astype(np.float32)


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
    print(f"写出 {path}  ({os.path.getsize(path)/1024/1024:.1f} MB, {len(tensors)} tensors)")


def main():
    from transformers import AutoTokenizer, AutoConfig

    tok = AutoTokenizer.from_pretrained(MODEL_DIR)
    ids = tok.encode(PROMPT, add_special_tokens=False)
    print(f"prompt: {PROMPT!r}")
    print(f"token ids ({len(ids)}): {ids}")

    cfg = AutoConfig.from_pretrained(MODEL_DIR)
    tc_obj = cfg.get_text_config()
    tc = dict(
        hidden_size=tc_obj.hidden_size,
        num_hidden_layers=tc_obj.num_hidden_layers,
        layer_types=list(tc_obj.layer_types),
        rms_norm_eps=tc_obj.rms_norm_eps,
        num_attention_heads=tc_obj.num_attention_heads,
        num_key_value_heads=tc_obj.num_key_value_heads,
        head_dim=tc_obj.head_dim,
        linear_num_value_heads=tc_obj.linear_num_value_heads,
        linear_num_key_heads=tc_obj.linear_num_key_heads,
        linear_key_head_dim=tc_obj.linear_key_head_dim,
        linear_value_head_dim=tc_obj.linear_value_head_dim,
        linear_conv_kernel_dim=tc_obj.linear_conv_kernel_dim,
        rope_parameters=dict(
            rope_theta=tc_obj.rope_parameters["rope_theta"],
            partial_rotary_factor=tc_obj.rope_parameters.get("partial_rotary_factor", 1.0),
        ),
    )

    out = {"token_ids": np.array(ids, dtype=np.float32)}

    # ── A. HF fp32 真值 ───────────────────────────────────────────────────
    hf_greedy = None
    try:
        from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5ForCausalLM
        print("加载 HF fp32 整模型（可能需要几分钟 + 数 GB 内存）…")
        model = Qwen3_5ForCausalLM.from_pretrained(
            MODEL_DIR, torch_dtype=torch.float32).eval()
        with torch.no_grad():
            input_ids = torch.tensor([ids])
            r = model(input_ids=input_ids, output_hidden_states=True)
            hs = r.hidden_states           # tuple len L+1, [1,S,H]
            for i, h in enumerate(hs):
                out[f"hf.h{i}"] = h[0].numpy()
            out["hf.logits_last"] = r.logits[0, -1].numpy()
            print(f"  HF 前向完成：{len(hs)} 个 hidden，logits[{r.logits.shape}]")

            g = model.generate(input_ids, max_new_tokens=GREEDY_TOKENS,
                               do_sample=False, use_cache=True)
            hf_greedy = g[0, len(ids):].tolist()
            out["hf.greedy_ids"] = np.array(hf_greedy, dtype=np.float32)
            print(f"  HF 贪心续写: {hf_greedy}")
            print(f"  文本: {PROMPT}{tok.decode(hf_greedy)!r}")
        del model
    except Exception as e:
        print(f"⚠️ HF 整模型路线失败（继续 numpy 路线）: {type(e).__name__}: {e}")

    # ── B. numpy int8 仿真 ────────────────────────────────────────────────
    print("numpy int8 仿真整网前向…")
    R = SafeReader(MODEL_DIR)
    hiddens, logits_last = fullnet_np(ids, R, tc, quant=True)
    for i, h in enumerate(hiddens):
        out[f"npq.h{i}"] = h
    out["npq.logits_last"] = logits_last
    top5 = np.argsort(logits_last)[::-1][:5]
    print(f"  npq top5: {top5.tolist()}  → {[tok.decode([t]) for t in top5]}")

    if "hf.logits_last" in out:
        a = out["hf.logits_last"]; b = logits_last
        cos = float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)))
        print(f"  [npq vs HF] logits 余弦相似度 = {cos:.6f}   "
              f"argmax {'一致' if int(np.argmax(a)) == int(np.argmax(b)) else '不一致'}")

    write_bin(OUT_BIN, out)
    meta = dict(prompt=PROMPT, token_ids=ids, greedy=hf_greedy,
                n_layers=tc["num_hidden_layers"])
    json.dump(meta, open(OUT_BIN + ".json", "w", encoding="utf-8"),
              ensure_ascii=False, indent=2)


if __name__ == "__main__":
    main()
