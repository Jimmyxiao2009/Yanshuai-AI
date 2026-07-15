# Qwen3.5-0.8B 架构参考（Ground Truth）

> 全部数据抓自 HuggingFace `Qwen/Qwen3.5-0.8B` 官方仓库（config.json + safetensors 头 + tokenizer + chat_template），2026-06。
> 这是 **P1 导出脚本和 P2 推理引擎的权威依据**，所有形状/dtype 均为真实值。
> 原始文件已存：`model_export/qwen3_5_0_8b/{config.json, chat_template.jinja}`。

---

## 1. 顶层结构

```
Qwen3_5ForConditionalGeneration
├── model.visual.*              视觉塔（12 blocks）         ← 我们【不实现/不导出】
├── model.language_model.*      文本解码器（24 层）          ← 目标
└── mtp.*                       多 token 预测头（1 层）       ← 我们【不实现/不导出】
```

- `model_type = qwen3_5` / `text_config.model_type = qwen3_5_text`
- 权重 dtype = **BF16**（少数 norm/A_log 为 F32），导出时转 fp32 再量化 int8
- `tie_word_embeddings = true` → lm_head 复用 `embed_tokens.weight`
- 总张量数 488（含视觉/MTP）；文本塔需要的见下

## 2. 文本塔超参（全部已确认）

| 字段 | 值 |
|---|---|
| hidden_size | 1024 |
| num_hidden_layers | 24 |
| **layer_types** | `[L,L,L,F] × 6`（L=linear_attention, F=full_attention），`full_attention_interval=4` |
| intermediate_size (MLP) | 3584 |
| hidden_act | silu（SwiGLU） |
| rms_norm_eps | 1e-6 |
| vocab_size | 248320 |
| **full 注意力** num_attention_heads | 8 |
| **full 注意力** num_key_value_heads | 2（GQA 4:1） |
| **full 注意力** head_dim | 256（解耦，≠1024/8） |
| **full 注意力** attention_bias | false（q/k/v/o 均无 bias） |
| **full 注意力** attn_output_gate | **true**（输出门，见 §4） |
| **full 注意力** q_norm/k_norm | 有（QK-Norm，RMSNorm over head_dim=256） |
| **DeltaNet** linear_num_key_heads | 16 |
| **DeltaNet** linear_num_value_heads | 16 |
| **DeltaNet** linear_key_head_dim | 128 |
| **DeltaNet** linear_value_head_dim | 128 |
| **DeltaNet** linear_conv_kernel_dim | 4 |
| **DeltaNet** mamba_ssm_dtype | **float32**（递归状态用 fp32，别量化） |
| RoPE rope_theta | 1e7 |
| RoPE **partial_rotary_factor** | **0.25** ⚠️ 只有 256×0.25=**64** 维参与旋转，其余 192 维不旋 |
| RoPE mrope | `mrope_interleaved=true`, `mrope_section=[11,11,10]`（多模态3D；**纯文本退化为标准1D RoPE**，三段位置 id 取同值即可） |
| max_position_embeddings | 262144（端侧自定上限即可） |

## 3. Gated DeltaNet 层（18 层，权重名 + 形状已确认）

每层张量（`model.language_model.layers.{i}.`）：

| 张量 | 形状 | dtype | 含义 |
|---|---|---|---|
| `input_layernorm.weight` | [1024] | BF16 | 注意力前 RMSNorm |
| `linear_attn.in_proj_qkv.weight` | **[6144,1024]** | BF16 | → q/k/v 各 2048（16 head × 128） |
| `linear_attn.in_proj_a.weight` | [16,1024] | BF16 | → a（每 value head 1 个标量，算 dt/衰减） |
| `linear_attn.in_proj_b.weight` | [16,1024] | BF16 | → b（每 head 更新门 β 的 logit） |
| `linear_attn.in_proj_z.weight` | [2048,1024] | BF16 | → z（输出门，16×128） |
| `linear_attn.conv1d.weight` | **[6144,1,4]** | BF16 | depthwise 因果卷积，作用于 qkv(6144)，kernel=4，**无 bias** |
| `linear_attn.A_log` | [16] | **F32** | 衰减基参（每 value head） |
| `linear_attn.dt_bias` | [16] | BF16 | dt 偏置（每 head） |
| `linear_attn.norm.weight` | [128] | **F32** | 输出 gated-RMSNorm（over value_head_dim=128） |
| `linear_attn.out_proj.weight` | [1024,2048] | BF16 | 2048→1024 |
| `post_attention_layernorm.weight` | [1024] | BF16 | FFN 前 RMSNorm |
| `mlp.gate_proj.weight` | [3584,1024] | BF16 | SwiGLU gate |
| `mlp.up_proj.weight` | [3584,1024] | BF16 | SwiGLU up |
| `mlp.down_proj.weight` | [1024,3584] | BF16 | SwiGLU down |

**前向（逐 token decode，递归状态 `S[head=16, k_dim=128, v_dim=128]` fp32）— 已逐行核对 transformers 5.9 `Qwen3_5GatedDeltaNet` + `torch_recurrent_gated_delta_rule`**：
```
mixed = in_proj_qkv(x)                      # [6144]
mixed = silu(causal_conv1d_update(mixed))   # depthwise 因果卷积 kernel=4 + SiLU；decode 维护 conv_state(最近3步)
q,k,v = split(mixed, [2048,2048,2048])      # 顺序就是 q,k,v；各 reshape [16,128]
# 0.8B: num_v_heads==num_k_heads==16 → 不做 repeat_interleave（无 GQA 复制）
a = in_proj_a(x)                            # [16]
b = in_proj_b(x)                            # [16]
beta = sigmoid(b)                                          # [16] 每 head 标量
g    = -exp(A_log) * softplus(a + dt_bias)                # [16] 注意是负值；用 exp(g) 作衰减
# —— recurrent delta rule（每 head 独立；先做 l2norm + scale）——
q = l2norm(q, eps=1e-6); k = l2norm(k, eps=1e-6)          # 逐 head L2 归一化
q = q * (1/sqrt(128))                                      # ★ query 缩放 1/√k_dim，易漏
for head h:
    S_h    = S_h * exp(g[h])                # 衰减 [128,128]
    kv_mem = (S_h * k_h[:,None]).sum(axis=0)   # 沿 k 轴求和 → [128] 旧记忆
    delta  = (v_h - kv_mem) * beta[h]       # [128]
    S_h    = S_h + k_h[:,None] * delta[None,:] # outer(k,delta) → [128,128]
    y_h    = (S_h * q_h[:,None]).sum(axis=0)   # 沿 k 轴求和 → [128]
# —— gated RMSNorm 输出门（norm over v_dim=128）——
z = in_proj_z(x).reshape(16,128)            # 输出门输入
for head h:                                  # Qwen3_5RMSNormGated:
    yn = y_h * rsqrt(mean(y_h^2) + 1e-6)     #   先 RMSNorm（无 gate 参与方差）
    yn = norm.weight * yn                     #   norm.weight[128]
    y_h = yn * silu(z_h)                      #   再乘 silu(门)
out = out_proj(concat(y_h))                 # [2048]→[1024]
```
> **prefill（seq_len>1）** HF 走 `torch_chunk_gated_delta_rule`（分块并行，数学等价于上面的逐步递归）。C# 端 prefill 也可直接用逐 token 递归滚完 prompt，结果一致、实现更简单。P2a 用 `torch_recurrent_gated_delta_rule` 的单层输出做对齐基准。

## 4. 全注意力层（6 层：索引 3,7,11,15,19,23，已确认）

每层张量：

| 张量 | 形状 | 含义 |
|---|---|---|
| `input_layernorm.weight` | [1024] | RMSNorm |
| `self_attn.q_proj.weight` | **[4096,1024]** | → q(2048) + **输出门 gate(2048)**（attn_output_gate） |
| `self_attn.k_proj.weight` | [512,1024] | 2 kv head × 256 |
| `self_attn.v_proj.weight` | [512,1024] | 2 kv head × 256 |
| `self_attn.o_proj.weight` | [1024,2048] | 8×256 → 1024 |
| `self_attn.q_norm.weight` | [256] | QK-Norm（RMSNorm over head_dim） |
| `self_attn.k_norm.weight` | [256] | QK-Norm |
| `post_attention_layernorm.weight` | [1024] | RMSNorm |
| `mlp.{gate,up,down}_proj.weight` | 同 §3 | SwiGLU |

**前向**：
```
qg = q_proj(x)                             # [4096]
q, gate = split(qg, [2048,2048])           # q→8×256, gate→[2048]
q = rmsnorm_perhead(q, q_norm)             # QK-Norm
k = rmsnorm_perhead(k_proj(x), k_norm)     # k→2×256
v = v_proj(x)                              # 2×256
rope(q, k, pos, rotary_dim=64)             # 仅前 64 维旋转(partial 0.25)；纯文本走标准 RoPE
append (k,v) → KV-Cache
GQA: q_head h 用 kv_head = h // 4
attn = softmax(q·kᵀ / sqrt(256) + causal)·v    # [8×256=2048]
attn = attn * sigmoid(gate)                # ★ attn_output_gate（输出门）
out  = o_proj(attn)                        # [1024]
```

## 5. 全局张量

| 张量 | 形状 | 含义 |
|---|---|---|
| `model.language_model.embed_tokens.weight` | [248320,1024] | token 嵌入；tie → 同时作 lm_head |
| `model.language_model.norm.weight` | [1024] | final RMSNorm |

整体：`x = embed[id]` → 24 层（按 layer_types 走 §3/§4）→ final norm → `logits = x @ embed_tokensᵀ`。

## 6. 参数量与内存（int8 目标）

| 部分 | 参数 |
|---|---|
| 嵌入表（tie） | 248320×1024 ≈ 254M |
| 18 个 DeltaNet 层 | ≈ 21.6M × 18 ≈ 389M |
| 6 个全注意力层 | ≈ 18.4M × 6 ≈ 110M |
| **文本塔合计** | **≈ 753M（0.75B）** |

int8 权重 ≈ **~0.75 GB**（+运行时）。递归状态/激活用 fp32。

## 7. Tokenizer & Chat

- byte-level BPE，`vocab.json` + `merges.txt`（也在 `tokenizer.json`），vocab 248320。
- 特殊 token：`<|endoftext|>`=248044, `<|im_start|>`=248045, `<|im_end|>`=248046；视觉/工具 token 248047–248059。
- **停止符**：生成时遇 `<|im_end|>`(248046) 或 `<|endoftext|>`(248044) 停。
- **Chat 模板（无工具简化版）**：
  ```
  <|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n
  ```
  之后接：
  - **非思考模式（端侧默认）**：注入空思考块 `<think>\n\n</think>\n\n` 然后正文
  - **思考模式**：`<think>\n` 让模型自己产出思考
- 工具调用用 `<tool_call><function=…><parameter=…>` XML 格式（端侧一期可不做）。

## 8. 实现易漏点清单（务必逐条核对）

0. ⚠️⚠️ **两种 RMSNorm 语义不同**（实测踩坑）：
   - `Qwen3_5RMSNorm`（q_norm/k_norm/input_layernorm/post_attention_layernorm/final norm）= **`(1 + weight)`** 零中心化（类 Gemma）。
   - `Qwen3_5RMSNormGated`（DeltaNet 内部 norm）= 普通 **`weight`**，且乘 `silu(gate)`。
   - 用错会导致全注意力输出整体偏 ~0.4（rel 0.8），但 DeltaNet 不受影响（它用 gated 那支）。
1. ⚠️ **partial RoPE**：只旋转 head_dim 的前 64 维（0.25×256），后 192 维原样传递。
2. ⚠️ **attn_output_gate**：全注意力 q_proj 输出是 4096=q+gate，注意力结果要 `* sigmoid(gate)`。
3. ⚠️ **DeltaNet 递归状态 fp32**（`mamba_ssm_dtype=float32`），即使权重 int8。
4. ⚠️ **conv1d 是 depthwise 因果卷积**，作用在 q/k/v 拼接后的 6144 通道，kernel=4，decode 时维护最近 3 步缓冲。
5. ⚠️ **q/k L2 归一化**（DeltaNet）与 **q/k RMSNorm**（全注意力 QK-Norm）是两回事，别混。
6. ⚠️ **M-RoPE**：纯文本下三段位置 id 取同值，等价标准 1D RoPE；做多模态才需要分段。
7. 跳过 `model.visual.*` 和 `mtp.*`。
