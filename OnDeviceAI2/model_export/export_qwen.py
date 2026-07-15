"""
将 Qwen3.5-0.8B（Qwen3-Next 混合架构，文本塔）导出为 .llmmodel(v2) 格式。
供 OnDeviceAI 的纯 C# 推理引擎（QwenRunner）使用。

特点：
  - 直接读 safetensors（不实例化模型，低内存），bf16 → fp32 经 torch 转换。
  - 只导出 model.language_model.*（跳过 visual / mtp）。
  - 支持 --quant fp32 (调试对齐用) / int8 (per-output-channel 对称量化)。
  - 张量名/形状严格对齐 docs/qwen3_5-0_8b-arch-reference.md。

用法：
  pip install torch safetensors numpy
  python export_qwen.py --model-dir qwen3_5_0_8b --output qwen3_5_0_8b.llmmodel --quant fp32
  python export_qwen.py --model-dir qwen3_5_0_8b --output qwen3_5_0_8b-int8.llmmodel --quant int8

二进制格式（小端，全部由本文件 + C# 读取端共同约定）：
  Header(128B) | layer_types[L] (1B/层) | Tokenizer 块 | 权重张量流
  权重读取顺序见 write_layer() / export()。
"""
import argparse
import json
import struct
import os
import sys
import numpy as np

# Windows GBK 控制台下避免 emoji/中文编码崩溃
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ── .llmmodel 常量 ────────────────────────────────────────────────────────
MAGIC = 0x4C4C4D32          # "LLM2"
VERSION = 2
ARCH_QWEN35 = 2

QUANT_FP32 = 0
QUANT_INT8 = 1              # per-output-channel 对称
QUANT_INT4 = 2             # 预留

LT_LINEAR = 0               # linear_attention (Gated DeltaNet)
LT_FULL = 1                 # full_attention


# ── 张量读取：safetensors(bf16) → np.float32 ───────────────────────────────
class SafeReader:
    def __init__(self, model_dir):
        from safetensors import safe_open
        # 找到唯一的权重分片
        idx = os.path.join(model_dir, "model.safetensors.index.json")
        shard = None
        if os.path.exists(idx):
            wm = json.load(open(idx, encoding="utf-8"))["weight_map"]
            shards = sorted(set(wm.values()))
            assert len(shards) == 1, f"暂只支持单分片，发现 {shards}"
            shard = shards[0]
        else:
            for f in os.listdir(model_dir):
                if f.endswith(".safetensors"):
                    shard = f
                    break
        assert shard, "找不到 .safetensors 权重文件"
        self.path = os.path.join(model_dir, shard)
        self._f = safe_open(self.path, framework="pt")
        self.keys = set(self._f.keys())

    def get(self, name):
        """返回 np.float32 数组（bf16/f32 统一转 fp32）。"""
        t = self._f.get_tensor(name)
        return t.float().cpu().numpy().astype(np.float32)

    def has(self, name):
        return name in self.keys


# ── 量化 ──────────────────────────────────────────────────────────────────
def write_tensor_fp32(f, arr):
    arr = np.ascontiguousarray(arr, dtype=np.float32)
    f.write(struct.pack("<I", arr.ndim))
    for d in arr.shape:
        f.write(struct.pack("<I", d))
    f.write(struct.pack("<I", QUANT_FP32))
    f.write(arr.tobytes())


def write_tensor_int8(f, arr):
    """对 2D 权重 [out, in] 做 per-output-channel 对称量化。1D/小张量退回 fp32。"""
    arr = np.ascontiguousarray(arr, dtype=np.float32)
    if arr.ndim != 2:
        write_tensor_fp32(f, arr)
        return
    out, _ = arr.shape
    amax = np.max(np.abs(arr), axis=1)                 # [out]
    scale = np.where(amax > 0, amax / 127.0, 1.0).astype(np.float32)
    q = np.round(arr / scale[:, None]).clip(-127, 127).astype(np.int8)
    f.write(struct.pack("<I", arr.ndim))
    for d in arr.shape:
        f.write(struct.pack("<I", d))
    f.write(struct.pack("<I", QUANT_INT8))
    f.write(scale.tobytes())                            # fp32 × out
    f.write(q.tobytes())                                # int8 × out*in


def make_writers(quant):
    """返回 (write_weight, write_raw): 矩阵权重按 quant 走，norm/小张量恒 fp32。"""
    if quant == "int8":
        return write_tensor_int8, write_tensor_fp32
    return write_tensor_fp32, write_tensor_fp32


# ── Header ────────────────────────────────────────────────────────────────
def write_header(f, cfg, quant_type, layer_types, tok_offset_placeholder=0):
    tc = cfg["text_config"]
    rope = tc["rope_parameters"]
    buf = bytearray(128)
    def put(off, val, fmt="<I"):
        struct.pack_into(fmt, buf, off, val)
    put(0,  MAGIC)
    put(4,  VERSION)
    put(8,  ARCH_QWEN35)
    put(12, tc["hidden_size"])
    put(16, tc["num_hidden_layers"])
    put(20, tc["num_attention_heads"])
    put(24, tc["num_key_value_heads"])
    put(28, tc["head_dim"])
    put(32, tc["intermediate_size"])
    put(36, tc["vocab_size"])
    put(40, MAX_SEQ_LEN)                               # 端侧自定上限
    put(44, float(rope["rope_theta"]), "<f")
    put(48, float(tc["rms_norm_eps"]), "<f")
    put(52, 1 if tc["tie_word_embeddings"] else 0)
    put(56, tc["linear_conv_kernel_dim"])
    put(60, tc["linear_num_value_heads"])
    put(64, tc["linear_num_key_heads"])
    put(68, tc["linear_value_head_dim"])               # k/v head_dim 同为 128
    put(72, tc["linear_key_head_dim"])
    put(76, quant_type)
    put(80, 0)                                         # quant_group_size (int4 用)
    put(84, 0)                                         # bos (Qwen 无显式 bos，用 0 占位)
    put(88, int(tc["eos_token_id"]))                   # 248044 <|endoftext|>
    put(92, IM_END_ID)                                 # 248046 <|im_end|> (第二停止符)
    # partial_rotary: 旋转维数 = head_dim * factor（256*0.25=64）
    put(96, int(round(tc["head_dim"] * rope.get("partial_rotary_factor", 1.0))))
    put(100, 1 if tc.get("text_config", tc).get("attn_output_gate", False) or tc.get("attn_output_gate", False) else 0)
    # reserved 104..127
    f.write(buf)


# 端侧默认上下文上限（DeltaNet 层不受此限，全注意力 KV cache 受此限）
MAX_SEQ_LEN = 4096
IM_END_ID = 248046


# ── Tokenizer 块（嵌入 vocab + merges，自包含）────────────────────────────
def write_tokenizer(f, model_dir):
    vocab = json.load(open(os.path.join(model_dir, "vocab.json"), encoding="utf-8"))
    id2tok = {idx: tok for tok, idx in vocab.items()}
    # 合并 tokenizer_config.json 的 added_tokens_decoder（特殊 token，id ≥ base vocab）
    tcfg = json.load(open(os.path.join(model_dir, "tokenizer_config.json"), encoding="utf-8"))
    for sid, meta in tcfg.get("added_tokens_decoder", {}).items():
        id2tok[int(sid)] = meta["content"]
    items = sorted(id2tok.items(), key=lambda kv: kv[0])   # (id, token) 按 id
    items = [(tok, idx) for idx, tok in items]
    f.write(struct.pack("<I", len(items)))
    for tok, idx in items:
        b = tok.encode("utf-8")
        f.write(struct.pack("<I", len(b)))
        f.write(b)
        f.write(struct.pack("<I", idx))
    # merges.txt: 每行 "a b"（首行可能是版本注释，跳过以 # 开头的）
    merges = []
    with open(os.path.join(model_dir, "merges.txt"), encoding="utf-8") as mf:
        for line in mf:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            merges.append(line)
    f.write(struct.pack("<I", len(merges)))
    for m in merges:
        b = m.encode("utf-8")
        f.write(struct.pack("<I", len(b)))
        f.write(b)
    return len(items), len(merges)


# ── 主导出 ────────────────────────────────────────────────────────────────
LM = "model.language_model"


def write_layer(f, R, i, ltype, w_mat, w_raw):
    p = f"{LM}.layers.{i}"
    w_raw(f, R.get(f"{p}.input_layernorm.weight"))

    if ltype == LT_FULL:
        w_mat(f, R.get(f"{p}.self_attn.q_proj.weight"))   # [4096,1024] q+gate
        w_mat(f, R.get(f"{p}.self_attn.k_proj.weight"))   # [512,1024]
        w_mat(f, R.get(f"{p}.self_attn.v_proj.weight"))   # [512,1024]
        w_mat(f, R.get(f"{p}.self_attn.o_proj.weight"))   # [1024,2048]
        w_raw(f, R.get(f"{p}.self_attn.q_norm.weight"))   # [256]
        w_raw(f, R.get(f"{p}.self_attn.k_norm.weight"))   # [256]
    else:  # LT_LINEAR (Gated DeltaNet)
        w_mat(f, R.get(f"{p}.linear_attn.in_proj_qkv.weight"))  # [6144,1024]
        w_mat(f, R.get(f"{p}.linear_attn.in_proj_a.weight"))    # [16,1024]
        w_mat(f, R.get(f"{p}.linear_attn.in_proj_b.weight"))    # [16,1024]
        w_mat(f, R.get(f"{p}.linear_attn.in_proj_z.weight"))    # [2048,1024]
        w_raw(f, R.get(f"{p}.linear_attn.conv1d.weight"))       # [6144,1,4]
        w_raw(f, R.get(f"{p}.linear_attn.A_log"))               # [16]
        w_raw(f, R.get(f"{p}.linear_attn.dt_bias"))             # [16]
        w_raw(f, R.get(f"{p}.linear_attn.norm.weight"))         # [128]
        w_mat(f, R.get(f"{p}.linear_attn.out_proj.weight"))     # [1024,2048]

    w_raw(f, R.get(f"{p}.post_attention_layernorm.weight"))
    w_mat(f, R.get(f"{p}.mlp.gate_proj.weight"))   # [3584,1024]
    w_mat(f, R.get(f"{p}.mlp.up_proj.weight"))     # [3584,1024]
    w_mat(f, R.get(f"{p}.mlp.down_proj.weight"))   # [1024,3584]


def export(model_dir, output, quant):
    cfg = json.load(open(os.path.join(model_dir, "config.json"), encoding="utf-8"))
    tc = cfg["text_config"]
    layer_types = [LT_FULL if t == "full_attention" else LT_LINEAR for t in tc["layer_types"]]
    quant_type = {"fp32": QUANT_FP32, "int8": QUANT_INT8}[quant]
    w_mat, w_raw = make_writers(quant)

    print(f"加载权重: {model_dir}")
    R = SafeReader(model_dir)
    print(f"  分片: {os.path.basename(R.path)}  张量数: {len(R.keys)}")
    print(f"  层数: {tc['num_hidden_layers']}  ("
          f"{layer_types.count(LT_LINEAR)} linear + {layer_types.count(LT_FULL)} full)")
    print(f"  量化: {quant}")

    with open(output, "wb") as f:
        write_header(f, cfg, quant_type, layer_types)
        f.write(bytes(layer_types))                     # layer_types[L]

        nvoc, nmrg = write_tokenizer(f, model_dir)
        print(f"  tokenizer: {nvoc} vocab + {nmrg} merges")

        print("  写入 embed_tokens ...")
        w_mat(f, R.get(f"{LM}.embed_tokens.weight"))    # [vocab,1024]

        for i, lt in enumerate(layer_types):
            tag = "full" if lt == LT_FULL else "lin "
            print(f"    层 {i:2d}/{len(layer_types)} [{tag}]")
            write_layer(f, R, i, lt, w_mat, w_raw)

        print("  写入 final norm ...")
        w_raw(f, R.get(f"{LM}.norm.weight"))            # [1024]

        if not tc["tie_word_embeddings"]:
            w_mat(f, R.get("lm_head.weight"))           # 本模型 tie=true，不会走这

    sz = os.path.getsize(output) / 1024 / 1024
    print(f"完成 ✅  {output}  ({sz:.1f} MB)")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="导出 Qwen3.5-0.8B 文本塔为 .llmmodel(v2)")
    ap.add_argument("--model-dir", default="qwen3_5_0_8b", help="HF 权重目录")
    ap.add_argument("--output", default="qwen3_5_0_8b.llmmodel")
    ap.add_argument("--quant", choices=["fp32", "int8"], default="fp32")
    ap.add_argument("--max-seq-len", type=int, default=MAX_SEQ_LEN)
    args = ap.parse_args()
    MAX_SEQ_LEN = args.max_seq_len
    export(args.model_dir, args.output, args.quant)
