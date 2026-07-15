# .embmodel 二进制格式规范

## 概述
专为 W10M DirectCompute 推理设计的嵌入模型权重格式。
所有多字节值采用 **小端序（little-endian）**。

## 文件结构

```
┌──────────────────────────────────────────────┐
│ Header (64 bytes)                             │
├──────────────────────────────────────────────┤
│ Tokenizer: Vocab 表                           │
├──────────────────────────────────────────────┤
│ Tokenizer: Merge 规则（可选）                  │
├──────────────────────────────────────────────┤
│ Token Embedding 表                            │
├──────────────────────────────────────────────┤
│ Transformer 层权重（每层重复）                  │
│   ├── LayerNorm (attention)                  │
│   ├── QKV 权重矩阵                            │
│   ├── Output 权重矩阵                         │
│   ├── LayerNorm (ffn)                        │
│   ├── FFN1 权重矩阵                           │
│   └── FFN2 权重矩阵                           │
├──────────────────────────────────────────────┤
│ Pooler 权重                                   │
└──────────────────────────────────────────────┘
```

## Header 格式

| 偏移 | 大小 | 字段 | 说明 |
|------|------|------|------|
| 0 | 4 | magic | 固定 `0x454D4245` ("EMBE") |
| 4 | 4 | version | 格式版本号，当前 = 1 |
| 8 | 4 | hidden_dim | Transformer hidden 维度 (eg. 384) |
| 12 | 4 | num_layers | Transformer 层数 (eg. 6) |
| 16 | 4 | num_heads | Attention head 数 (eg. 12) |
| 20 | 4 | vocab_size | 词表大小 (eg. 30522) |
| 24 | 4 | max_seq_len | 最大序列长度 (eg. 128) |
| 28 | 4 | pooler_dim | Pooler 输出维度 (eg. 384) |
| 32 | 4 | ffn_dim | FFN 中间维度 (eg. 1536) |
| 36 | 4 | tokenizer_type | 0=WordPiece, 1=BPE |
| 40 | 4 | need_merge | 是否有 merge 规则 (0/1) |
| 44 | 4 | activation | 0=ReLU, 1=GeLU, 2=SwiGLU |
| 48 | 4 | model_type | 0=encoder, 1=decoder |
| 52 | 4 | embedding_type | 0=absolute, 1=relative |
| 56 | 4 | head_size | head_dim = hidden / num_heads |
| 60 | 4 | reserved | 保留字段 |

## Tokenizer Vocab

| 大小 | 说明 |
|------|------|
| 4 | vocab_size (uint32) |
| vocab_size × | 每个词条: |
| 4 | token_len (uint32) |
| token_len bytes | UTF-8 编码的 token 文本 |
| 4 | token_id (uint32，索引) |

## 权重张量格式

每个张量块：

| 大小 | 说明 |
|------|------|
| 4 | dims (uint32，维度数，通常 2) |
| 4 × dims | 各维度大小 (uint32) |
| 4 × dims | strides (uint32，步长) |
| `乘积`×4 | float32 权重数据 |

## 层结构

每层按以下顺序存储：

```
1. attention_ln_weight  [hidden_dim] float32
2. attention_ln_bias    [hidden_dim] float32
3. qkv_weight           [hidden_dim × hidden_dim×3] float32
4. qkv_bias             [hidden_dim×3] float32
5. output_weight        [hidden_dim × hidden_dim] float32
6. output_bias          [hidden_dim] float32
7. ffn_ln_weight        [hidden_dim] float32
8. ffn_ln_bias          [hidden_dim] float32
9. ffn1_weight          [hidden_dim × ffn_dim] float32
10. ffn1_bias           [ffn_dim] float32
11. ffn2_weight         [ffn_dim × hidden_dim] float32
12. ffn2_bias           [hidden_dim] float32
```

## Pooler

| 大小 | 说明 |
|------|------|
| 4 | has_pooler (uint32, 0/1) |
| 如果有: | |
| weight | [hidden_dim × pooler_dim] float32 |
| bias | [pooler_dim] float32 |
