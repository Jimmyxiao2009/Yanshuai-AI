# OnDeviceAI → Qwen3.5-0.8B 改造设计文档

> 目标：把现有的 **嵌入推理引擎**（OnEmbedder）改造为可在端侧运行 **Qwen3.5-0.8B**（"Qwen3-Next" 混合线性注意力架构）的 **自回归生成引擎**，为言枢提供完全离线的对话/补全能力。
>
> 作者：Claude · 日期：2026-06-29 · 状态：方案待确认
>
> ⚠️ **重要更正（v2）**：Qwen3.5-0.8B 于 2026-02 发布，**不是标准 Transformer**，而是 **Gated DeltaNet（线性注意力）+ Gated 全注意力 的 3:1 混合架构**（即 “Qwen3-Next”）。本文档已据此重写注意力部分。早期版本里 “标准 GQA+RoPE+SwiGLU Transformer” 的注意力设计**作废**。

---

## 0. 一句话结论（TL;DR）

现在的 OnDeviceAI 是一个 **BERT 式嵌入编码器**（encoder，输出一个语义向量），而 Qwen3.5 是 **解码器（decoder-only，自回归逐 token 生成）**，并且用的是 **混合线性注意力**：每 4 层里 3 层是 **Gated DeltaNet**（递归式线性注意力，无 KV cache），1 层是 **Gated 全注意力**（GQA + RoPE + KV cache）。两者在归一化、位置编码、注意力、FFN、输出头、tokenizer 上几乎**没有可复用的数学层**。

因此这是一次 **新引擎的搭建**，而不是在 `OnEmbedder.cs` 上打补丁。可复用的只有外壳：文件读取（header + tensor 块）的套路、加载权重到 `float[]` 的方式、以及集成进 `App.xaml.cs` 的钩子。

> ⚠️ 还有一个现实问题必须先摆在桌面上：当前 `OnEmbedder.cs` 的 `TransformerForward` **根本没实现自注意力**（第 260 行注释写着 “Simplified self-attention (identity)”），它现在连嵌入都算不准。所以别把它当“能跑的基线”。

> ✅ **一个意外利好**：18/24 层是 DeltaNet 线性注意力，解码时是**固定大小的递归状态**，没有随上下文增长的 KV cache。只有 6 层全注意力保留真 KV cache。对 W10M 这种 RAM 紧张设备反而是优势——长上下文不爆内存。代价是 DeltaNet 实现/验证难度远高于普通注意力（见第 7.A 节，现头号风险）。

---

## 1. 现状盘点

| 项 | 现状 |
|---|---|
| 引擎 | `OnEmbedder/OnEmbedder.cs`，**纯 C# CPU**（C++/D3D 路线已废弃，原因：本机无 C++ 编译环境） |
| 模型类型 | Encoder（BERT），双向注意力，Mean Pooling → 384/768 维向量 |
| 归一化 | LayerNorm（weight + bias） |
| 位置编码 | 学习式绝对位置嵌入（`position_embeddings`） |
| 注意力 | 标称 MHA，**实际未实现**（恒等占位） |
| FFN | `Linear → GeLU → Linear`（2 个矩阵） |
| 输出 | Pooler / Mean Pool，**无 LM head** |
| Tokenizer | WordPiece，空格切分 + 单字 fallback（很糙） |
| 文件格式 | `.embmodel`（magic `EMBE`，64 字节 header + vocab + 每层 12 个张量 + pooler） |
| 导出 | `model_export/export_model.py`，基于 `sentence-transformers`，**仅支持 BERT** |
| 验证 | `test/TestOnEmbedder.csproj`（net8.0 控制台，可在 PC 上跑） |
| 目标平台 | W10M / UWP，骁龙 800/801，Adreno 330，1–3GB RAM |

---

## 2. 目标与可行性评估（先做现实检查）

### 2.1 Qwen3.5-0.8B 是什么

- **发布**：2026-02（Qwen3.5 系列），权重在 HuggingFace / ModelScope，Apache-2.0。
- **架构代号**：“Qwen3-Next” —— 混合 **线性注意力（Gated DeltaNet）+ 全注意力（Gated Attention）**，3:1 交错。
- **多模态**：原模型带视觉塔（vision-language）。**我们只用文本塔 `Qwen3_5TextConfig`，视觉部分整个不导出、不实现。**
- **MTP**：含 1 个 multi-token-prediction 头（训练/投机解码用），**基础推理可忽略，不导出**。

### 2.2 关键超参（✅ 全部已从官方 config.json + safetensors 头核实）

> 📌 **完整权威规格见 [`qwen3_5-0_8b-arch-reference.md`](qwen3_5-0_8b-arch-reference.md)**（含每个张量的精确名称/形状/dtype + 前向公式）。原始 `config.json` / `chat_template.jinja` 已存于 `model_export/qwen3_5_0_8b/`。下面是摘要：

```
# —— 主干 ——
hidden_size            = 1024
num_hidden_layers      = 24
layer_types            = [L,L,L,F] × 6   # full_attention_interval=4 → 18 linear + 6 full
intermediate_size      = 3584            (SwiGLU)
vocab_size             = 248320          (嵌入表 ~2.5 亿，tie 复用为 lm_head)
rms_norm_eps           = 1e-6
tie_word_embeddings    = true
hidden_act             = silu

# —— 全注意力层 (6 层: idx 3,7,11,15,19,23) ——
num_attention_heads    = 8 ;  num_key_value_heads = 2  (GQA 4:1)
head_dim               = 256             (解耦)
attention_bias         = false
attn_output_gate       = true   ⚠️ q_proj 输出 4096 = q(2048)+输出门 gate(2048)
q_norm / k_norm        = 有 (QK-Norm, over head_dim=256)
rope_theta             = 1e7
partial_rotary_factor  = 0.25   ⚠️ 仅前 64/256 维参与 RoPE 旋转
mrope_section          = [11,11,10]  (纯文本退化为标准 1D RoPE)

# —— 线性注意力层 (Gated DeltaNet, 18 层) ——
linear_num_key_heads=16, linear_num_value_heads=16, key/value_head_dim=128
linear_conv_kernel_dim = 4       (depthwise 因果卷积, 作用于 qkv 6144 通道)
mamba_ssm_dtype        = float32 ⚠️ 递归状态用 fp32，不量化
# 投影: in_proj_qkv[6144], in_proj_a[16], in_proj_b[16], in_proj_z[2048], out_proj[1024,2048]
# 机制: conv+SiLU → q/k L2归一化 → 衰减 g=exp(-exp(A_log)·softplus(a+dt_bias)) → β=sigmoid(b)
#       → delta rule 递归 S[16,128,128] → gated-RMSNorm(norm[128])·SiLU(z) → out_proj
```

> 引擎仍做成 **config 驱动**（`layer_types` 数组决定每层走 linear/full），换 checkpoint 不改代码。但 0.8B 的上述数字现已是确定值，可直接照着写。

### 2.3 内存预算（最关键的约束）

参数分布（Qwen3.5-0.8B，估算）：

- 词嵌入表：`248320 × 1024 ≈ 2.54 亿`（tie 后既当 embedding 又当 lm_head）—— **单这一项就占大头**
- 24 层主干（注意力 + FFN）≈ **5–6 亿**
- 合计 ≈ **0.8–0.9 亿... 实为 8–9 亿参数**

| 精度 | 权重体积 | W10M 可行性 |
|---|---|---|
| fp32 | ~3.4 GB | ❌ 绝无可能 |
| fp16/bf16 | ~1.7 GB | ⚠️ 官方“2GB RAM”基准约在此档；C# 无原生 fp16 算 |
| **int8** | **~0.85 GB** | ⚠️ 仅高内存机型（2–4GB）+ memory manifest 提权 |
| **int4** | **~0.45 GB** | ✅ 目标精度，但反量化逻辑更复杂 |

> 结论：**量化是强制项**。第一版做 **int8 per-channel**（实现简单、精度损失小）跑通；若内存放不下，再上 **int4 分组量化**。词嵌入表巨大（2.5 亿），建议**始终 int8 存储**。
>
> 注：DeltaNet 层解码时只保留固定大小递归状态 `S[heads × kdim × vdim]`，**不随上下文增长**；只有 6 个全注意力层有真 KV cache。所以长上下文的**运行时内存**比同规模标准 Transformer 友好得多。

### 2.4 速度预估（务必管理预期）

- 每生成 1 token 的算力 ≈ `2 × 非嵌入参数 ≈ 2 × 6亿 ≈ 1.2 GFLOP`（外加最后一步 lm_head ~0.5 GFLOP，vocab 大）
- 骁龙 800 CPU，**单线程托管 C# 无 SIMD**，实测有效算力乐观 ~0.5–2 GFLOP/s
- → **约 0.8–3 秒/token**，即 **0.3–1.2 token/秒**
- 一条 100 字回复 ≈ **1.5–5 分钟**

| 优化手段 | 预期加速 | 难度 |
|---|---|---|
| 4 核多线程 matmul（Krait 四核） | ~3–4× | 中 |
| int8 整数内积替代 float | ~1.5–2× | 中 |
| `System.Numerics.Vector<T>` SIMD | ~2–4×（UWP ARM 支持有限，需实测） | 中 |
| **DirectCompute GPU（Adreno 330）** | **10–50×** | 高（原 C++/D3D 路线，已废弃） |

> 线性注意力的好处主要体现在**长上下文**（不爆内存、无二次方读放大）；短对话下 matmul（proj/FFN/lm_head）仍是瓶颈，速度量级同上。纯 CPU int8 + 多线程现实目标 **2–4 token/s**，勉强可用于短回复。要流畅最终还得回 GPU compute shader。先把 CPU 版跑通做正确性基线，再谈提速。

---

## 3. 核心架构差异（这张表是改造的地图）

| 维度 | 现状（BERT 嵌入） | 目标（Qwen3.5-0.8B 解码器） | 工作量 |
|---|---|---|---|
| 拓扑 | Encoder，双向 | Decoder-only，**因果（causal）** | 重写 |
| 输出 | 1 个 pooled 向量 | **自回归 token 流** | 新增 |
| 归一化 | LayerNorm(w,b) | **RMSNorm(w only)** | 改写 |
| 位置编码 | 学习式绝对位置 | **RoPE**（仅全注意力层用，θ=1e7）；DeltaNet 层不用 RoPE | 新增 |
| 注意力 | 标称 MHA（未实现） | **混合**：6 层 GQA 全注意力 + 18 层 **Gated DeltaNet 线性注意力** | 全新实现（DeltaNet 是难点） |
| 状态缓存 | 无 | 全注意力层：**KV cache**；DeltaNet 层：**固定大小递归状态 S** | 新增 |
| FFN | Linear→GeLU→Linear | **SwiGLU**: `down(silu(gate(x)) * up(x))`，3 个矩阵 | 改写 |
| 输出头 | Pooler | **final RMSNorm + lm_head**（tie embedding） | 新增 |
| 采样 | 无 | greedy / temperature / top-p / top-k / 重复惩罚 / EOS | 新增 |
| Tokenizer | WordPiece ~3万，空格切分 | **byte-level BPE ~24.8万** + chat template | 新增 |
| 精度 | fp32 | int8/int4 量化 | 新增 |

**可复用**：二进制读 header/tensor 的代码风格、`float[]` 权重承载方式、`App.xaml.cs` 集成钩子、`test/` PC 验证工程。**其余全新写。** DeltaNet 线性注意力没有任何现成可复用部分，是从零实现的最大单点。

---

## 4. 方案选型

| 方案 | 说明 | 取舍 |
|---|---|---|
| **A. 纯 C# CPU + int8/int4**（推荐先做） | 延续现有“无 C++ 环境”的纯托管路线，新写 `QwenRunner.cs` | ✅ 可移植、可在 PC 上单测；❌ 慢 |
| B. DirectCompute GPU（HLSL CS） | 原始设计意图，Adreno 330 上跑 matmul | ✅ 快 10–50×；❌ C++/D3D 已废弃，C# 侧走 SharpDX/互操作成本高、风险大 |
| C. 外部运行时（ONNX Runtime / llama.cpp / GGUF） | 直接套现成推理库 | ❌ 这些都不支持 W10M ARM32 UWP，基本不可行，排除 |

**推荐路径**：先用 **方案 A** 做出**正确性正确**的可运行版本（PC 上对齐 HuggingFace 输出），再视速度需求把热点（matmul/attention）逐步下放到 **方案 B** 的 compute shader。两者共用同一套 `.llmmodel` 格式与 tokenizer。

---

## 5. 新文件格式 `.llmmodel` (v2) 设计

不要复用 `.embmodel`（字段语义对不上）。新建格式，magic 改为 `LLM2`(`0x4C4C4D32`)。所有多字节小端。

### 5.1 Header（128 字节，留足扩展位）

| 偏移 | 字段 | 说明 |
|---|---|---|
| 0 | magic = `0x4C4C4D32` | "LLM2" |
| 4 | version = 2 | |
| 8 | arch | 2 = Qwen3.5 / Qwen3-Next 混合 |
| 12 | hidden_size | 1024 |
| 16 | num_layers | 24 |
| 20 | num_q_heads | 8（全注意力层） |
| 24 | num_kv_heads | 2（全注意力层 GQA） |
| 28 | head_dim | 256（全注意力，**独立字段**，不要用 hidden/heads 推） |
| 32 | intermediate_size | 3584 |
| 36 | vocab_size | 248320 |
| 40 | max_seq_len | 例如 4096（端侧不必上 262k） |
| 44 | rope_theta (float) | 10000000.0 |
| 48 | rms_norm_eps (float) | 1e-6 |
| 52 | tie_embeddings | 1 |
| 56 | linear_conv_kernel | 4（DeltaNet 短卷积核） |
| 60 | linear_num_v_heads | DeltaNet value head 数（待核实） |
| 64 | linear_num_k_heads | DeltaNet key head 数（待核实） |
| 68 | linear_k_head_dim | DeltaNet key head 维（待核实） |
| 72 | linear_v_head_dim | DeltaNet value head 维（待核实） |
| 76 | quant_type | 0=fp32, 1=int8-per-channel, 2=int4-group |
| 80 | quant_group_size | int4 分组大小（如 64/128），int8 时填 0 |
| 84 | bos_token_id | |
| 88 | eos_token_id | 可能多个 → 见 5.4 |
| 92 | pad_token_id | |
| 96 | **layer_types_offset** | 紧跟 header 的 `num_layers` 字节数组：0=linear, 1=full |
| 100..127 | reserved | 预留 |

紧接 header 之后写 **`layer_types[num_layers]`**（每层 1 字节，0=DeltaNet 线性层 / 1=全注意力层），引擎据此决定每层走哪条路径。

### 5.2 量化张量块格式

```
[ dims:u32 ][ shape[dims]:u32 ]
[ quant_type:u32 ]   // 冗余存一份，便于逐张量混合精度
若 fp32:   [ data: float32 × N ]
若 int8:   [ scale: float32 × out_features ][ data: int8 × N ]   // per-output-channel 对称量化
若 int4:   [ scale: float32 × (N/group) ][ data: 每2个int4打包成1字节 ]
```

### 5.3 权重布局（按读取顺序）

```
Header (128B)
layer_types[num_layers]    (每层 1 字节)
Tokenizer 块（见第 9 节）
token_embedding            [vocab, hidden]   (int8)

for layer in 0..num_layers:
    input_layernorm.weight   [hidden]   (fp32, RMSNorm 只有 weight)

    if layer_types[layer] == full:        # ── 全注意力层（6 个）──
        q_proj.weight    [q_heads*head_dim, hidden]
        k_proj.weight    [kv_heads*head_dim, hidden]
        v_proj.weight    [kv_heads*head_dim, hidden]
        o_proj.weight    [hidden, q_heads*head_dim]
        # Qwen3.5 全注意力带 gating（Gated Attention）→ 可能多一个 gate 投影，按 config 核实
    else:                                  # ── DeltaNet 线性层（18 个）──
        in_proj_qkvz / 各自的 q/k/v 投影     [linear_heads*hdim, hidden]
        conv1d.weight    [conv_dim, kernel=4]   短因果卷积（depthwise）
        A_log            [linear_v_heads]       衰减参数
        dt_bias          [linear_v_heads]       时间步偏置
        beta_proj/gate   更新门 β 的投影
        out_gate_proj    输出门（SiLU）的投影
        norm.weight      [v_head_dim]           输出前的 (Gated)RMSNorm
        out_proj.weight  [hidden, linear_v_heads*v_head_dim]

    post_attention_layernorm.weight [hidden]   (fp32)
    gate_proj.weight   [intermediate, hidden]
    up_proj.weight     [intermediate, hidden]
    down_proj.weight   [hidden, intermediate]

final_norm.weight      [hidden]   (fp32)
若 tie_embeddings==0: lm_head.weight [vocab, hidden]
```

> ⚠️ DeltaNet 层的精确权重名/形状以 HF `modeling_qwen3_5.py` 的 `Qwen3_5LinearAttention`（或 `GatedDeltaNet`）模块为准——**P1 阶段先把 state_dict 的 key 全 dump 出来对齐**，再定本布局。上面是基于通用 Gated-DeltaNet 的占位，需用真实权重校正。
> 全注意力层是否带 bias / 额外 gate 投影，也按 config 核实后再定。

---

## 6. 导出管线 `export_qwen.py`

新写脚本（与 `export_model.py` 并列），用 `transformers`（需较新版本，含 `Qwen3_5` 支持）加载 **文本模型**，遍历 `state_dict` 写入 `.llmmodel`。

要点：
1. **P1 第一步**：`model = AutoModelForCausalLM.from_pretrained(name, torch_dtype=float32)`；先 `print(model.config)` 和 `for k,v in model.state_dict().items(): print(k, v.shape)` 把**真实超参和权重 key/形状全 dump 出来**，用来校正第 2.2 / 5.3 表的“待核实”项。**这一步是整个项目的地基，必须先做。**
2. 从 `model.config` 填 header，并把 `config.layer_types`（或等价的 full-attention interval）导成 `layer_types[num_layers]` 字节数组。
3. **只导出文本塔**，跳过 `visual.*` / vision 相关权重和 MTP 头（`mtp.*`）。
4. **量化**：对每个 2D 权重做 per-output-channel int8 对称量化：
   `scale = max(abs(W), axis=1) / 127; q = round(W / scale).clip(-127,127).astype(int8)`。
   norm 类 1D 权重、`A_log`/`dt_bias`/conv 等小张量保持 fp32。
5. **Tokenizer**：导出 `tokenizer.json`（HF fast tokenizer 的完整 BPE：vocab + merges + 特殊 token + pre-tokenizer 正则）。端侧直接吃这份 JSON，别自己重切（见第 9 节）。
6. 写一个 `--verify` 模式：导出后用 Python 端跑一遍，**保存每一层的中间 hidden 张量**和最终 logits 的参考样本（输入 token → 各层输出 + top-5 token），供 C# 端**逐层对齐**。DeltaNet 容易错，只比最终 logits 不够。
7. `--quant {int8,int4,fp32}` 可选；fp32 仅用于 PC 调试对齐。

---

## 7. 推理引擎模块拆解 `QwenRunner.cs`

纯 C#（方案 A）。建议拆成清晰的小函数，便于后续逐个搬到 GPU。

```
QwenRunner
├── LoadModel(path)            // 解析 header + 量化张量 → 内存
├── Tokenizer (见 §9)
│
├── Forward(int[] tokens, int posStart, State st) → float[] logits
│   ├── Embed: token_id → hidden[hidden_size]（从 int8 表反量化）
│   ├── for each layer:
│   │   ├── h = RMSNorm(x, input_layernorm.w)
│   │   ├── if layer_types[l] == full:  attn_out = FullAttention(h, st.kv[l], pos)   // §7.B
│   │   │   else:                        attn_out = DeltaNet(h, st.recur[l])          // §7.A
│   │   ├── x = x + attn_out                              // 残差
│   │   ├── h = RMSNorm(x, post_attention_layernorm.w)
│   │   └── SwiGLU FFN:  g=silu(Wgate·h); u=Wup·h; x = x + Wdown·(g ⊙ u)   // 残差
│   ├── x = RMSNorm(x, final_norm.w)
│   └── logits = lm_head·x   (tie → 复用 embedding 表)
│
├── State: { kv[6 个全注意力层的 KV-Cache], recur[18 个 DeltaNet 层的递归状态 S] }
├── Sampler(logits) → next_token        // §8
└── Generate(prompt, opts) → IEnumerable<string>   // 流式 yield，逐 token 回吐
```

### 7.A Gated DeltaNet 线性注意力（18 层，**最大难点 / 头号风险**）

参考实现：HF `modeling_qwen3_5.py` 的线性注意力模块（ground truth）+ `rasbt/LLMs-from-scratch ch04/08_deltanet`（干净教学版）。

每层维护一个**固定大小递归状态** `S[h, k_dim, v_dim]`（与上下文长度无关）。**逐 token** 递归（与自回归解码天然契合）：

```
# 投影 + 短因果卷积（depthwise conv1d, kernel=4，保留最近 4 步）
q,k,v = proj(x);  q,k,v = causal_conv1d(q,k,v)        # SiLU 激活
q,k   = L2_normalize(q), L2_normalize(k)              # 数值稳定关键
α_t   = exp(-exp(A_log) * softplus(W_α·x + dt_bias))  # 衰减门(每 head 标量)
β_t   = sigmoid(W_β·x)                                 # 更新门

# —— delta rule 递归（每个 head 独立）——
S      = S * α_t                       # 1. 衰减遗忘
kv_mem = (S * k_t).sum(over k_dim)     # 2. 当前 key 召回的旧记忆
delta  = (v_t - kv_mem) * β_t          # 3. 预测误差 × 更新门
S      = S + outer(k_t, delta)         # 4. 写回状态
y_t    = (S * q_t).sum(over k_dim)     # 5. 用 query 读出

y_t = RMSNorm(y_t) * SiLU(W_outgate·x) # 输出门（Gated）
out = W_out · y_t
```

> 实现陷阱：① q/k 必须 L2 归一化；② α 是 per-head 标量衰减，别和 β 搞混；③ 短卷积是 **causal**（只看过去），decode 时要维护最近 3 个 token 的卷积缓冲；④ 先在 PC 上拿单层和 HF 输出**逐元素对齐**，再接整网——DeltaNet 错了往往输出乱码却无报错。

### 7.B 全注意力（6 层，GQA + RoPE + Gated）

```
q = Wq·h, k = Wk·h, v = Wv·h            // int8 matmul + dequant
RoPE(q, k, pos)                          // θ=1e7，预计算 cos/sin 表 [max_seq, head_dim/2]
append k,v → KV-Cache[layer]
GQA: 每个 Q head 对应 kv_head = qh / (q_heads/kv_heads) = qh / 4   // 不复制 KV
scores = q·kᵀ / sqrt(head_dim) + causal_mask
attn   = softmax(scores) · v
# Qwen3.5 "Gated Attention"：输出再过一个 sigmoid/SiLU 门，按 config 核实
attn_out = Wo·(attn ⊙ gate)
```

### 关键实现注意点

- **两种缓存分开管**：全注意力层 prefill 时一次算完 prompt 的 K/V；DeltaNet 层 prefill 时把递归状态 S 顺序滚到 prompt 末尾。decode 时各自增量更新。
- **RoPE 只用于全注意力层**；DeltaNet 层不加 RoPE（位置信息靠递归 + 卷积隐式编码）。
- **int8 matmul**：`y_i = scale_i * Σ_j (q_w[i,j] * x_j)`，内层用 `int` 累加再乘 scale，或直接 float 累加（先求正确，再优化）。
- **数值对齐**：先做 fp32 路径，和 §6 的 Python 参考**逐层中间张量**对比（不只看最终 logits），误差 < 1e-3，再切 int8。**不要一上来就 int8 调 bug。**
- **C# 语言版本**：UWP 工程默认 C# 7.3，`Span<T>`/`stackalloc` 用法受限；`test/` 是 net8.0 可放飞，但最终要落到 UWP 工程，写法要保守（或在 csproj 显式抬 `<LangVersion>`）。

---

## 8. 采样器（生成质量）

最少实现：
- `temperature`（0 → 退化为 greedy/argmax）
- `top_k`、`top_p (nucleus)`
- `repetition_penalty`（对已出现 token 的 logit 打折，缓解小模型复读）
- EOS 停止（Qwen 可能有多个停止 id，如 `<|im_end|>`）
- `max_new_tokens` 上限

## 9. Tokenizer（byte-level BPE）

⚠️ 现有 WordPiece 切分**完全不能用**于 Qwen。Qwen 用 GPT-2 式 **byte-level BPE**（vocab **~24.8 万** + merges + 一套 pre-tokenize 正则）。

方案：
- 导出时直接带上 HF `tokenizer.json`。
- C# 端实现：UTF-8 → byte-level 映射 → 按 merges 优先级合并 → token id。这是个**独立的、需要单独测试**的模块（拿 Python `tokenizer.encode` 的输出逐句对齐）。
- **Chat template**：Qwen 对话要套
  `<|im_start|>system\n...<|im_end|>\n<|im_start|>user\n...<|im_end|>\n<|im_start|>assistant\n`。
  这层在 prompt 拼接处理，别忘了，否则模型不会“对话”。
- **思考模式**：Qwen3.5 支持 thinking / non-thinking 双模式（`<think>...</think>`）。端侧弱设备建议**默认关思考**（省 token、快出结果），通过 chat template 开关控制。

## 10. 集成进言枢

- 仿照 `OnEmbedder/Integration.cs`：`App` 里加 `static QwenRunner Llm`，`OnLaunched` 里异步 `LoadModel`（**几百 MB，必须后台加载 + 进度提示**，别卡启动）。
- 暴露 `IAsyncEnumerable<string> GenerateAsync(prompt, opts)` 给聊天页做流式上屏。
- `.llmmodel` 几百 MB：**不要打进 appx 包**（包体爆炸 + 商店限制）。改为首次运行从本地/网络下载到 `ApplicationData.Current.LocalFolder`。
- UWP 内存：在 `Package.appxmanifest` 声明高内存需求；低内存机型要有“模型不可用”的优雅降级（回退到云端 API）。

---

## 11. 分阶段里程碑

| 阶段 | 产出 | 验收标准 |
|---|---|---|
| **P0 选型确认** | 定下量化精度、内存策略、思考模式开关 | 你拍板（模型已定 Qwen3.5-0.8B） |
| **P1 dump + 格式** | 抓真实 `config.json`/`state_dict` 校正超参 → `export_qwen.py` + `.llmmodel`(fp32) + 逐层参考张量 | 文件能生成，各层参考输出落盘 |
| **P2a DeltaNet 单层** ✅ | `DeltaNet.cs` fp32 | 已对齐 HF，max\|Δ\|≈3e-7 |
| **P2b 全注意力单层** ✅ | `FullAttention.cs` fp32 | 已对齐 HF，max\|Δ\|≈3e-7 |
| **P2c 整网** ✅ (2026-07-12) | `QwenModel.cs`+`QwenRunner.cs`，int8 .llmmodel 直载 | 24 层 vs numpy-int8 仿真 ~1e-6；logits vs HF fp32 余弦 0.9997、argmax 一致；**贪心 24/24 token 与 HF fp32 完全一致**（"法国的首都是→巴黎，美国的首都是华盛顿…"）；增量解码（KV cache+递归状态+conv 缓冲）已验证；PC 4.7 tok/s |
| **P3 Tokenizer** | byte-level BPE + chat template | encode/decode 与 HF 逐句一致 |
| **P4 采样 + 流式** | temperature/top-p/rep-penalty + 流式 yield | 多轮对话像样 |
| **P5 int8 量化** | 引擎走 int8 路径 | 质量不明显劣化，内存降到 ~0.85GB |
| **P6 上 UWP** | 移植到 UWP 工程 + App 集成 + 后台加载 | 真机/模拟器能对话 |
| **P7 提速** | 多线程 matmul（→ 可选 GPU compute shader） | 达到可接受 token/s |

> 建议把 **P1–P4 全部在 PC（net8 控制台）上做完**，正确性锁死后再碰 UWP/真机——W10M 上调 bug 的成本是 PC 的 10 倍。**P2a（DeltaNet 单层对齐）是整个项目的成败分水岭，先打这块硬骨头。**

---

## 12. 风险与开放问题

1. **DeltaNet 正确性（头号风险）**：Gated DeltaNet 的 delta-rule 递归 + 衰减/更新门 + 短卷积 + L2 归一化，细节多、错了往往“静默乱码”。**这是项目成败的关键单点**——必须 P2a 单层逐元素对齐 HF，别贪快直接拼整网。好在有参考实现（HF `modeling_qwen3_5.py` + rasbt deltanet）。
2. **内存**：int8 ~0.85GB，低端 W10M（1GB）几乎放不下 → 可能被迫上 int4，或限定只在高内存机型启用。
3. **速度**：纯 CPU 可能慢到 <1 token/s，体验差。需要尽早做一个 P2c 后的真机速度实测，决定是否必须上 GPU。
4. **GPU 路线**：原 C++/D3D 已废弃（无 C++ 环境）。若要 compute shader，C# 侧得走 D3D11 互操作（SharpDX 在 UWP ARM 上的可用性需验证）。注意 **DeltaNet 的递归在 GPU 上不像普通 matmul 那么好并行**，搬 GPU 时全注意力/FFN 好搬、DeltaNet 难搬。
5. **transformers 版本依赖**：导出需要含 `Qwen3_5` 架构支持的较新 transformers，PC 上先确认能正常 `from_pretrained` 加载。
6. **Tokenizer 正确性**：byte-level BPE + 正则 pre-tokenize 容易出细节 bug，必须逐句对齐。
7. **待核实超参**：DeltaNet 的 head 数/维度、全注意力是否带 Gated 门、tie_embeddings 等，**全部以 P1 dump 出的真实 config 为准**，本文档相关数字是占位。

---

## 13. 需要你确认的事（P0）

已确认：模型 = **Qwen3.5-0.8B**，量化 = **int8**，架构规格已全部抓全（见架构参考文档）。剩下几个小确认：

1. ~~精度~~ → **int8 已定** ✅
2. **速度底线**：能接受第一版 **<1 token/s** 的纯 CPU 版作为正确性基线吗？还是必须先保证速度（那要直接投入 GPU 路线，风险大）？
3. **思考模式**：端侧默认**关闭** thinking（chat 模板注入空 `<think></think>` 块即可），对吧？
4. **模型分发**：~0.75GB(int8) 模型走**首次下载**到 LocalFolder，不打进 appx 包，对吧？
5. **优先级/顺序**：先在 **PC net8 控制台**把引擎（尤其 DeltaNet 单层）跑通对齐，再上 UWP——这个顺序 OK 吗？
6. **权重获取**：代理可用，我已能从 HF 抓 config/tokenizer。**真正下权重（~1.7GB bf16 safetensors）要不要我现在直接拉到 `model_export/qwen3_5_0_8b/`**？还是你本地已有、给我个路径？

---

*P0 基本清零。确认 #2/#3/#5 后即可开 P1：写 `export_qwen.py`（照架构参考的张量表）→ 导出 int8 `.llmmodel` + 逐层参考张量 → P2a DeltaNet 单层对齐。*
