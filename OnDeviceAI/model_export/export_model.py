"""
将 sentence-transformers 模型导出为 .embmodel 格式。
供 W10M DirectCompute 推理使用。

用法：
    pip install -r requirements.txt
    python export_model.py --model BAAI/bge-small-zh-v1.5 --output bge.embmodel
    python export_model.py --model all-MiniLM-L6-v2 --output mini.embmodel
"""
import struct
import json
import numpy as np
import argparse
from pathlib import Path
from sentence_transformers import SentenceTransformer
import torch

# ── 张量写入 ──────────────────────────────────────────────────────────────

def write_tensor(f, arr: np.ndarray):
    """写入一个 float32 张量块"""
    arr = arr.astype(np.float32)
    dims = len(arr.shape)
    f.write(struct.pack('<I', dims))
    for d in arr.shape:
        f.write(struct.pack('<I', d))
    # strides
    stride = arr.strides
    for s in stride:
        f.write(struct.pack('<I', s // 4))
    f.write(arr.tobytes())


def write_header(f, config: dict):
    """写入 64 字节文件头"""
    magic = 0x454D4245  # "EMBE"
    f.write(struct.pack('<IIIIIIII', magic, 1,  # magic, version
                        config['hidden_dim'], config['num_layers'],
                        config['num_heads'], config['vocab_size'],
                        config['max_seq_len'], config['pooler_dim']))
    f.write(struct.pack('<IIIIIIII', config['ffn_dim'], config['tokenizer_type'],
                        config['need_merge'], config['activation'],
                        config['model_type'], config['embedding_type'],
                        config['head_size'], 0))  # reserved


# ── Tokenizer 导出 ────────────────────────────────────────────────────────

def extract_wordpiece_vocab(tokenizer):
    """从 HuggingFace tokenizer 提取 WordPiece 词表"""
    vocab = tokenizer.get_vocab()
    # 按 id 排序
    sorted_items = sorted(vocab.items(), key=lambda x: x[1])
    return [(token, idx) for token, idx in sorted_items]


def extract_bpe_vocab(tokenizer):
    """从 HuggingFace tokenizer 提取 BPE 词表"""
    vocab = tokenizer.get_vocab()
    sorted_items = sorted(vocab.items(), key=lambda x: x[1])
    return [(token, idx) for token, idx in sorted_items]


def extract_merge_rules(tokenizer):
    """提取 BPE merge 规则"""
    try:
        return tokenizer.backend_tokenizer.model.merges
    except:
        return []


def write_tokenizer(f, tokenizer):
    """写入 tokenizer 数据"""
    # 判断类型
    try:
        vocab_items = extract_wordpiece_vocab(tokenizer)
        ttype = 0  # WordPiece
    except:
        vocab_items = extract_bpe_vocab(tokenizer)
        ttype = 1  # BPE

    # 写入 vocab
    f.write(struct.pack('<I', len(vocab_items)))
    for token, idx in vocab_items:
        token_bytes = token.encode('utf-8')
        f.write(struct.pack('<I', len(token_bytes)))
        f.write(token_bytes)
        f.write(struct.pack('<I', idx))

    # 写入 merge 规则（BPE 需要）
    merges = extract_merge_rules(tokenizer) if ttype == 1 else []
    f.write(struct.pack('<I', len(merges)))
    for merge in merges:
        merge_bytes = merge.encode('utf-8')
        f.write(struct.pack('<I', len(merge_bytes)))
        f.write(merge_bytes)

    return ttype, len(merges) > 0


# ── 权重提取 ──────────────────────────────────────────────────────────────

def extract_bert_weights(model):
    """从 sentence-transformers BERT 模型中提取所有权重"""
    # 自动模块（sentence-transformers 包装）
    auto_model = model[0].auto_model
    cfg = auto_model.config

    config = {
        'hidden_dim': cfg.hidden_size,
        'num_layers': cfg.num_hidden_layers,
        'num_heads': cfg.num_attention_heads,
        'vocab_size': cfg.vocab_size,
        'max_seq_len': 128,
        'pooler_dim': cfg.hidden_size,
        'ffn_dim': cfg.intermediate_size,
        'activation': 1,  # GeLU
        'tokenizer_type': 0,  # placeholder, updated after tokenizer export
        'need_merge': 0,
        'head_size': cfg.hidden_size // cfg.num_attention_heads,
        'model_type': 0,
        'embedding_type': 0,
    }

    layers_config = []

    for layer_idx in range(cfg.num_hidden_layers):
        layer = auto_model.encoder.layer[layer_idx]

        layer_data = {
            'attention_ln_weight': layer.attention.output.LayerNorm.weight.detach().numpy(),
            'attention_ln_bias':   layer.attention.output.LayerNorm.bias.detach().numpy(),
            'qkv_weight':          layer.attention.self.query.weight.detach().numpy(),
            'qkv_bias':            layer.attention.self.query.bias.detach().numpy(),
            'output_weight':       layer.attention.output.dense.weight.detach().numpy(),
            'output_bias':         layer.attention.output.dense.bias.detach().numpy(),
            'ffn_ln_weight':       layer.output.LayerNorm.weight.detach().numpy(),
            'ffn_ln_bias':         layer.output.LayerNorm.bias.detach().numpy(),
            'ffn1_weight':         layer.intermediate.dense.weight.detach().numpy(),
            'ffn1_bias':           layer.intermediate.dense.bias.detach().numpy(),
            'ffn2_weight':         layer.output.dense.weight.detach().numpy(),
            'ffn2_bias':           layer.output.dense.bias.detach().numpy(),
        }
        layers_config.append(layer_data)

    # Embedding 表
    embed_weight = auto_model.embeddings.word_embeddings.weight.detach().numpy()
    # Position embedding
    pos_embed_weight = auto_model.embeddings.position_embeddings.weight.detach().numpy()
    # Embedding LayerNorm
    embed_ln_weight = auto_model.embeddings.LayerNorm.weight.detach().numpy()
    embed_ln_bias   = auto_model.embeddings.LayerNorm.bias.detach().numpy()

    # Pooler
    pooler_weight = auto_model.pooler.dense.weight.detach().numpy() if hasattr(auto_model, 'pooler') else None
    pooler_bias   = auto_model.pooler.dense.bias.detach().numpy() if hasattr(auto_model, 'pooler') else None

    return config, embed_weight, pos_embed_weight, embed_ln_weight, embed_ln_bias, layers_config, pooler_weight, pooler_bias


# ── 主流程 ─────────────────────────────────────────────────────────────────

def export_model(model_name: str, output_path: str, max_seq_len: int = 128):
    """将 sentence-transformers 嵌入模型导出为 .embmodel"""

    print(f"加载模型: {model_name}")
    model = SentenceTransformer(model_name)
    tokenizer = model.tokenizer

    print("提取权重...")
    config, embed_w, pos_w, ln_w, ln_b, layers, pooler_w, pooler_b = extract_bert_weights(model)

    config['max_seq_len'] = max_seq_len
    config['emb_vocab_size'] = embed_w.shape[0]

    print(f"  隐藏维度: {config['hidden_dim']}")
    print(f"  层数:     {config['num_layers']}")
    print(f"  注意力头: {config['num_heads']}")
    print(f"  词表大小: {config['vocab_size']}")

    print(f"写入: {output_path}")
    with open(output_path, 'wb') as f:
        # Header
        write_header(f, config)

        # Tokenizer vocab
        ttype, need_merge = write_tokenizer(f, tokenizer)
        config['tokenizer_type'] = ttype
        config['need_merge'] = 1 if need_merge else 0

        # 回到 header 重新写入正确值
        f.seek(0)
        write_header(f, config)
        f.seek(0, 2)  # 回到文件尾

        # Embedding 权重
        print("  写入 Embedding 权重...")
        write_tensor(f, embed_w)          # [vocab, hidden]
        write_tensor(f, pos_w)            # [max_pos, hidden]
        write_tensor(f, ln_w)             # [hidden]
        write_tensor(f, ln_b)             # [hidden]

        # Transformer 层
        print(f"  写入 {len(layers)} 层 Transformer 权重...")
        for i, layer in enumerate(layers):
            print(f"    层 {i+1}/{len(layers)}")
            write_tensor(f, layer['attention_ln_weight'])
            write_tensor(f, layer['attention_ln_bias'])
            write_tensor(f, layer['qkv_weight'])
            write_tensor(f, layer['qkv_bias'])
            write_tensor(f, layer['output_weight'])
            write_tensor(f, layer['output_bias'])
            write_tensor(f, layer['ffn_ln_weight'])
            write_tensor(f, layer['ffn_ln_bias'])
            write_tensor(f, layer['ffn1_weight'])
            write_tensor(f, layer['ffn1_bias'])
            write_tensor(f, layer['ffn2_weight'])
            write_tensor(f, layer['ffn2_bias'])

        # Pooler
        print("  写入 Pooler...")
        has_pooler = 1 if pooler_w is not None else 0
        f.write(struct.pack('<I', has_pooler))
        if has_pooler:
            write_tensor(f, pooler_w)
            write_tensor(f, pooler_b)

    # 统计文件大小
    size_mb = Path(output_path).stat().st_size / 1024 / 1024
    print(f"完成！{output_path} ({size_mb:.1f} MB)")


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='导出嵌入模型为 .embmodel 格式')
    parser.add_argument('--model', type=str, default='BAAI/bge-small-zh-v1.5',
                        help='模型名称或路径')
    parser.add_argument('--output', type=str, default='model.embmodel',
                        help='输出文件路径')
    parser.add_argument('--max-seq-len', type=int, default=128,
                        help='最大序列长度')
    args = parser.parse_args()
    export_model(args.model, args.output, args.max_seq_len)
