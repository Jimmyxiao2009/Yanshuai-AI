"""
OnEmbedder 推理性能对比脚本
对比 标量逐元素 vs NumPy向量化 的 单次/十次推理耗时

用法: python bench_embedder.py [.embmodel路径]
"""

import struct, sys, os, time, math
import numpy as np

MODEL_MAGIC = 0x454D4245  # "EBME"


def read_header(data, offset):
    fields = struct.unpack_from("<16I", data, offset)
    return offset + 64, {
        "magic": fields[0], "version": fields[1], "hidden_dim": fields[2],
        "num_layers": fields[3], "num_heads": fields[4], "vocab_size": fields[5],
        "max_seq_len": fields[6], "pooler_dim": fields[7], "ffn_dim": fields[8],
        "tokenizer_type": fields[9], "need_merge": fields[10], "activation": fields[11],
        "model_type": fields[12], "embedding_type": fields[13], "head_size": fields[14],
        "reserved": fields[15],
    }


def read_vocab(data, offset):
    vocab_size = struct.unpack_from("<I", data, offset)[0]; offset += 4
    vocab = {}
    for _ in range(vocab_size):
        tlen = struct.unpack_from("<I", data, offset)[0]; offset += 4
        token = data[offset:offset + tlen].decode("utf-8"); offset += tlen
        tid = struct.unpack_from("<I", data, offset)[0]; offset += 4
        vocab[token] = tid
    return offset, vocab


def skip_merges(data, offset):
    merges = struct.unpack_from("<I", data, offset)[0]; offset += 4
    for _ in range(merges):
        mlen = struct.unpack_from("<I", data, offset)[0]; offset += 4
        offset += mlen
    return offset


def read_tensor(data, offset):
    dims = struct.unpack_from("<I", data, offset)[0]; offset += 4
    total = 1
    for _ in range(dims):
        d = struct.unpack_from("<I", data, offset)[0]; offset += 4
        total *= d
    offset += dims * 4  # strides
    arr = np.frombuffer(data, dtype=np.float32, count=total, offset=offset).copy()
    return offset + total * 4, arr


def load_model(path):
    print(f"Loading: {path}")
    with open(path, "rb") as f:
        data = f.read()
    offset = 0
    offset, hdr = read_header(data, offset)
    assert hdr["magic"] == MODEL_MAGIC, f"Bad magic: {hdr['magic']:#x}"
    print(f"  Model: {hdr['hidden_dim']}d, {hdr['num_layers']} layers, "
          f"ffn={hdr['ffn_dim']}, vocab={hdr['vocab_size']}, "
          f"max_seq={hdr['max_seq_len']}")

    offset, vocab = read_vocab(data, offset)
    offset = skip_merges(data, offset)

    # Embeddings
    offset, embed_w = read_tensor(data, offset)
    offset, pos_w   = read_tensor(data, offset)
    offset, emb_ln_w = read_tensor(data, offset)
    offset, emb_ln_b = read_tensor(data, offset)

    # Transformer layers
    layers = []
    for l in range(hdr["num_layers"]):
        ly = {}
        for name in ["attn_ln_w", "attn_ln_b", "qkv_w", "qkv_b",
                      "out_w", "out_b",
                      "ffn_ln_w", "ffn_ln_b", "ffn1_w", "ffn1_b",
                      "ffn2_w", "ffn2_b"]:
            offset, ly[name] = read_tensor(data, offset)
        layers.append(ly)

    # Pooler
    has_pooler = struct.unpack_from("<I", data, offset)[0]; offset += 4
    pooler_w = pooler_b = None
    if has_pooler:
        offset, pooler_w = read_tensor(data, offset)
        offset, pooler_b = read_tensor(data, offset)

    print(f"  Total tensors loaded: {len(layers)} layers")
    return hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b, layers, pooler_w, pooler_b


def tokenize(text, vocab, max_len=512):
    ids = [101]
    for word in text.split():
        if word in vocab:
            ids.append(vocab[word])
        else:
            for ch in word:
                ids.append(vocab.get(ch, 100))
    ids.append(102)
    if len(ids) > max_len:
        ids = ids[:max_len - 1] + [102]
    return ids


def gelu(x):
    return 0.5 * x * (1.0 + np.tanh(np.sqrt(2 / np.pi) * (x + 0.044715 * x**3)))


# ═══════════════════ Scalar 版（模拟原始 OnEmbedder） ═══════════════════

def layer_norm_scalar(x, weight, bias, eps=1e-12):
    mean = sum(x) / len(x)
    var = sum((v - mean) ** 2 for v in x) / len(x)
    inv_std = 1.0 / math.sqrt(var + eps)
    return [(v - mean) * inv_std * w + b for v, w, b in zip(x, weight, bias)]


def ffn_forward_scalar(x, w1, b1, w2, b2, hidden_dim, ffn_dim):
    # GeLU(x @ W1^T + b1) @ W2^T + b2
    h = [0.0] * ffn_dim
    for j in range(ffn_dim):
        s = b1[j]
        for k in range(hidden_dim):
            s += x[k] * w1[j * hidden_dim + k]
        h[j] = gelu_float(s)
    out = [0.0] * hidden_dim
    for j in range(hidden_dim):
        s = b2[j]
        for k in range(ffn_dim):
            s += h[k] * w2[j * ffn_dim + k]
        out[j] = s
    return out


def gelu_float(x):
    return 0.5 * x * (1.0 + math.tanh(math.sqrt(2 / math.pi) * (x + 0.044715 * x**3)))


def encode_scalar(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                  layers, pooler_w, pooler_b, text):
    H = hdr["hidden_dim"]
    ids = tokenize(text, vocab, int(hdr["max_seq_len"]))
    seq_len = len(ids)

    # Embedding lookup + position + LayerNorm
    hidden = []
    for i, tid in enumerate(ids):
        emb_start = tid * H
        pos_start = i * H
        pos = [0.0] * H
        for j in range(H):
            tok_v = embed_w[emb_start + j] if tid < hdr["vocab_size"] else 0.0
            pos_v = pos_w[pos_start + j] if pos_start + j < len(pos_w) else 0.0
            pos[j] = tok_v + pos_v
        pos = layer_norm_scalar(pos, emb_ln_w, emb_ln_b)
        hidden.extend(pos)

    # Transformer layers
    for l in range(hdr["num_layers"]):
        ly = layers[l]
        F = hdr["ffn_dim"]
        next_hidden = [0.0] * (seq_len * H)
        for i in range(seq_len):
            off = i * H
            x = hidden[off:off + H]
            # Pre-LN
            ln = layer_norm_scalar(x, ly["attn_ln_w"], ly["attn_ln_b"])
            # FFN
            ffn_out = ffn_forward_scalar(ln, ly["ffn1_w"], ly["ffn1_b"],
                                         ly["ffn2_w"], ly["ffn2_b"], H, F)
            for j in range(H):
                next_hidden[off + j] = x[j] + ffn_out[j]
        hidden = next_hidden

    # Mean pool
    result = [0.0] * H
    for j in range(H):
        s = sum(hidden[i * H + j] for i in range(seq_len))
        result[j] = s / seq_len

    # L2 normalize
    norm = math.sqrt(sum(v * v for v in result)) + 1e-12
    return [v / norm for v in result]


# ═══════════════════ NumPy 向量化版（模拟 SIMD） ═══════════════════

def encode_numpy(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                 layers, pooler_w, pooler_b, text):
    H = hdr["hidden_dim"]
    ids = tokenize(text, vocab, int(hdr["max_seq_len"]))
    seq_len = len(ids)

    # Embedding + position (vectorized gather)
    tid_arr = np.array(ids, dtype=np.int64)
    mask = (tid_arr >= 0) & (tid_arr < hdr["vocab_size"])
    safe_ids = np.where(mask, tid_arr, 0)
    embed = embed_w.reshape(-1, H)[safe_ids]  # [seq_len, H]
    embed[~mask] = 0.0

    pos_ids = np.arange(seq_len, dtype=np.int64)
    pos_vals = pos_w.reshape(-1, H)[pos_ids[:seq_len]]  # [seq_len, H]
    x = embed + pos_vals  # [seq_len, H]

    # LayerNorm (vectorized)
    mean = x.mean(axis=-1, keepdims=True)
    var = ((x - mean) ** 2).mean(axis=-1, keepdims=True)
    inv_std = 1.0 / np.sqrt(var + 1e-12)
    x = (x - mean) * inv_std * emb_ln_w + emb_ln_b

    # Transformer layers
    for l in range(hdr["num_layers"]):
        ly = layers[l]
        F = hdr["ffn_dim"]

        # Pre-LN
        mean = x.mean(axis=-1, keepdims=True)
        var = ((x - mean) ** 2).mean(axis=-1, keepdims=True)
        inv_std = 1.0 / np.sqrt(var + 1e-12)
        ln = (x - mean) * inv_std * ly["attn_ln_w"] + ly["attn_ln_b"]

        # FFN: GeLU(ln @ W1^T + b1) @ W2^T + b2
        w1 = ly["ffn1_w"].reshape(F, H).T  # [H, F]
        w2 = ly["ffn2_w"].reshape(H, F).T  # [F, H]
        h = gelu(ln @ w1 + ly["ffn1_b"])    # [seq_len, F]
        x = x + (h @ w2 + ly["ffn2_b"])     # residual

    # Mean pool + L2 normalize
    pooled = x.mean(axis=0)  # [H]
    result = pooled / (np.linalg.norm(pooled) + 1e-12)
    return result.astype(np.float64).tolist()


# ═══════════════════ 测试 ═══════════════════

def cosine_sim(a, b):
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(y * y for y in b))
    return dot / (na * nb) if na * nb > 0 else 0


def main():
    model_path = sys.argv[1] if len(sys.argv) > 1 else "../Assets/bge.embmodel"
    if not os.path.exists(model_path):
        print(f"Model not found: {model_path}")
        sys.exit(1)

    hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b, layers, pw, pb = load_model(model_path)

    test_texts = [
        "你好，请问今天天气怎么样？",
        "The quick brown fox jumps over the lazy dog",
        "角色扮演是一种交互式叙事形式，用户与AI共同构建故事世界",
        "介绍一下你的家乡",
        "What is the capital of France?",
    ]

    print("\n" + "=" * 60)
    print("单次推理测试 (正确性)")
    print("=" * 60)

    for txt in test_texts:
        v1 = encode_scalar(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                           layers, pw, pb, txt)
        v2 = encode_numpy(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                          layers, pw, pb, txt)
        sim = cosine_sim(v1, v2)
        status = "✓" if sim > 0.99 else "✗ MISMATCH"
        print(f"  [{status}] sim={sim:.6f}  \"{txt[:40]}...\"")

    # Benchmark
    print("\n" + "=" * 60)
    print("性能对比 (取 10 次运行的中位数)")
    print("=" * 60)

    bench_text = "你好，这是一条用于性能测试的文本，包含足够多的词汇来触发完整的tokenize和推理流程" * 2

    # Warmup
    for _ in range(3):
        encode_scalar(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                      layers, pw, pb, bench_text)
        encode_numpy(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                     layers, pw, pb, bench_text)

    # Scalar: 10 runs
    scalar_times = []
    for _ in range(10):
        t0 = time.perf_counter()
        encode_scalar(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                      layers, pw, pb, bench_text)
        scalar_times.append(time.perf_counter() - t0)

    # NumPy: 10 runs
    numpy_times = []
    for _ in range(10):
        t0 = time.perf_counter()
        encode_numpy(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                     layers, pw, pb, bench_text)
        numpy_times.append(time.perf_counter() - t0)

    s1 = sorted(scalar_times)[len(scalar_times) // 2]
    s10 = sum(scalar_times)
    n1 = sorted(numpy_times)[len(numpy_times) // 2]
    n10 = sum(numpy_times)

    print(f"  {'':>12} {'Scalar':>10} {'NumPy':>10} {'Speedup':>10}")
    print(f"  {'单次 (中位)':>12} {s1*1000:>8.1f}ms {n1*1000:>8.1f}ms {s1/n1:>9.1f}x")
    print(f"  {'10次 (总计)':>12} {s10*1000:>8.1f}ms {n10*1000:>8.1f}ms {s10/n10:>9.1f}x")
    print(f"  {'平均':>12} {s10/10*1000:>8.1f}ms {n10/10*1000:>8.1f}ms {s10/n10:>9.1f}x")

    if cosine_sim(
        encode_scalar(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                      layers, pw, pb, bench_text),
        encode_numpy(hdr, vocab, embed_w, pos_w, emb_ln_w, emb_ln_b,
                     layers, pw, pb, bench_text)
    ) > 0.99:
        print("\n  标量/向量化 输出一致 ✓")


if __name__ == "__main__":
    main()
