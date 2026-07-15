// Multi-Head Self Attention + FFN Compute Shader
// 输入: hidden_states[seq_len, hidden_dim] (来自上一层/embedding)
// 输出: output[seq_len, hidden_dim]

cbuffer Constants : register(b0)
{
    uint seq_len;
    uint hidden_dim;
    uint num_heads;
    uint head_dim;    // hidden_dim / num_heads
};

// 权重: QKV
StructuredBuffer<float> QKVWeight : register(t0);  // [hidden_dim, hidden_dim*3]
StructuredBuffer<float> QKVBias   : register(t1);  // [hidden_dim*3]
// Output projection
StructuredBuffer<float> OutWeight : register(t2);  // [hidden_dim, hidden_dim]
StructuredBuffer<float> OutBias   : register(t3);  // [hidden_dim]
// LayerNorm
StructuredBuffer<float> AttnLnW   : register(t4);  // [hidden_dim]
StructuredBuffer<float> AttnLnB   : register(t5);  // [hidden_dim]
// FFN
StructuredBuffer<float> FFN1W     : register(t6);  // [hidden_dim, ffn_dim]
StructuredBuffer<float> FFN1B     : register(t7);  // [ffn_dim]
StructuredBuffer<float> FFN2W     : register(t8);  // [ffn_dim, hidden_dim]
StructuredBuffer<float> FFN2B     : register(t9);  // [hidden_dim]
StructuredBuffer<float> FFNLnW    : register(t10); // [hidden_dim]
StructuredBuffer<float> FFNLnB    : register(t11); // [hidden_dim]

// 输入 & 输出
StructuredBuffer<float>  Input   : register(t12);
RWStructuredBuffer<float> Output  : register(u0);

// 临时 Q/K/V 缓冲区（显存）
globallycoherent RWStructuredBuffer<float> QBuffer  : register(u1);
globallycoherent RWStructuredBuffer<float> KBuffer  : register(u2);
globallycoherent RWStructuredBuffer<float> VBuffer  : register(u3);
globallycoherent RWStructuredBuffer<float> AttnOut  : register(u4);
globallycoherent RWStructuredBuffer<float> FFNIn    : register(u5);
globallycoherent RWStructuredBuffer<float> FFNOut   : register(u6);

// 共享内存
groupshared float shared_buf[1024];  // max head_dim * threads

// ── GeLU 近似 ────────────────────────────────────────────────────────────
float gelu(float x)
{
    return 0.5f * x * (1.0f + tanh(0.7978845608f * (x + 0.044715f * x * x * x)));
}

// ── Softmax ──────────────────────────────────────────────────────────────
void softmax(uint seq_idx, uint head_idx, uint dim)
{
    // 找到最大值
    float max_val = -3.4e38f;
    for (uint i = 0; i < seq_len; i++)
    {
        float v = QBuffer[head_idx * seq_len * seq_len + seq_idx * seq_len + i];
        max_val = max(max_val, v);
    }

    // 计算 exp sum
    float sum = 0.0f;
    for (uint i = 0; i < seq_len; i++)
    {
        uint idx = head_idx * seq_len * seq_len + seq_idx * seq_len + i;
        QBuffer[idx] = exp(QBuffer[idx] - max_val);
        sum += QBuffer[idx];
    }

    // 归一化
    float inv_sum = 1.0f / (sum + 1e-10f);
    for (uint i = 0; i < seq_len; i++)
    {
        uint idx = head_idx * seq_len * seq_len + seq_idx * seq_len + i;
        QBuffer[idx] *= inv_sum;
    }
}

// ── GroupNorm / LayerNorm (单序列位置) ─────────────────────────────────
float layer_norm(uint seq_idx, uint hid_idx, float val,
                 StructuredBuffer<float> weights,
                 StructuredBuffer<float> biases,
                 StructuredBuffer<float> input)
{
    // 计算当前序列位置的均值方差
    float sum = 0.0f;
    float sq = 0.0f;
    uint base = seq_idx * hidden_dim;
    for (uint i = 0; i < hidden_dim; i++)
    {
        float v = input[base + i];
        sum += v;
        sq += v * v;
    }
    float mean = sum / hidden_dim;
    float var = sq / hidden_dim - mean * mean;
    float inv_std = rsqrt(var + 1e-12f);
    return (val - mean) * inv_std * weights[hid_idx] + biases[hid_idx];
}

// ── MatMul (手写) ────────────────────────────────────────────────────────
float dot_product(uint row, uint col, uint m, uint k, uint n,
                  StructuredBuffer<float> a, StructuredBuffer<float> b)
{
    float sum = 0.0f;
    for (uint i = 0; i < k; i++)
        sum += a[row * k + i] * b[i * n + col];
    return sum;
}

[numthreads(384, 1, 1)]
void CSMain(uint3 gtid : SV_GroupThreadID, uint3 gid : SV_GroupID)
{
    uint seq_idx = gid.x;
    uint hid_idx = gtid.x;

    if (seq_idx >= seq_len || hid_idx >= hidden_dim) return;

    uint base = seq_idx * hidden_dim;

    // ── Pre LayerNorm ────────────────────────────────────────────────
    float pre_ln = layer_norm(seq_idx, hid_idx, Input[base + hid_idx],
                              AttnLnW, AttnLnB, Input);
    Input[base + hid_idx] = pre_ln;
    GroupMemoryBarrierWithGroupSync();

    // ── QKV Projection ───────────────────────────────────────────────
    float q_val = dot_product(seq_idx, hid_idx, seq_len, hidden_dim, hidden_dim,
                              Input, QKVWeight) + QKVBias[hid_idx];
    float k_val = dot_product(seq_idx, hidden_dim + hid_idx, seq_len, hidden_dim, hidden_dim,
                              Input, QKVWeight) + QKVBias[hidden_dim + hid_idx];
    float v_val = dot_product(seq_idx, 2 * hidden_dim + hid_idx, seq_len, hidden_dim, hidden_dim,
                              Input, QKVWeight) + QKVBias[2 * hidden_dim + hid_idx];

    QBuffer[seq_idx * hidden_dim + hid_idx] = q_val;
    KBuffer[seq_idx * hidden_dim + hid_idx] = k_val;
    VBuffer[seq_idx * hidden_dim + hid_idx] = v_val;
    
    GroupMemoryBarrierWithGroupSync();

    // ── Scaled Dot-Product Attention ──────────────────────────────────
    // 每个头独立计算
    uint head_idx = hid_idx / head_dim;
    uint head_hid = hid_idx % head_dim;

    float q = QBuffer[base + hid_idx];
    float scale = rsqrt((float)head_dim);

    // 计算 attention score: Q · K^T (分批)
    for (uint pos = 0; pos < seq_len; pos++)
    {
        float k = KBuffer[pos * hidden_dim + hid_idx];
        QBuffer[head_idx * seq_len * seq_len + seq_idx * seq_len + pos] += q * k * scale;
    }

    GroupMemoryBarrierWithGroupSync();

    // Softmax（每个头 + 每个序列位置一次）
    if (hid_idx % head_dim == 0)
        softmax(seq_idx, head_idx, head_dim);

    GroupMemoryBarrierWithGroupSync();

    // ── Attention 加权求和 ───────────────────────────────────────────
    float attn_out = 0.0f;
    for (uint pos = 0; pos < seq_len; pos++)
    {
        float attn_w = QBuffer[head_idx * seq_len * seq_len + seq_idx * seq_len + pos];
        attn_out += attn_w * VBuffer[pos * hidden_dim + hid_idx];
    }
    AttnOut[base + hid_idx] = attn_out;

    GroupMemoryBarrierWithGroupSync();

    // ── Output Projection + Residual ──────────────────────────────────
    float proj = dot_product(seq_idx, hid_idx, seq_len, hidden_dim, hidden_dim,
                             AttnOut, OutWeight) + OutBias[hid_idx];
    float attn_res = Input[base + hid_idx] + proj;  // residual
    FFNIn[base + hid_idx] = attn_res;

    GroupMemoryBarrierWithGroupSync();

    // ── Post Attention LayerNorm ─────────────────────────────────────
    float post_ln = layer_norm(seq_idx, hid_idx, FFNIn[base + hid_idx],
                               FFNLnW, FFNLnB, FFNIn);

    // ── FFN1: GeLU(Wx + b) ───────────────────────────────────────────
    float ffn1 = dot_product(seq_idx, hid_idx, seq_len, hidden_dim, 1536, // ffn_dim
                             Input, FFN1W) + FFN1B[hid_idx];
    // hid_idx 这里可能超出 ffn_dim，需要限制
    if (hid_idx < 1536)
        FFNOut[seq_idx * 1536 + hid_idx] = gelu(ffn1);

    GroupMemoryBarrierWithGroupSync();

    // ── FFN2: Down Projection ────────────────────────────────────────
    if (hid_idx < hidden_dim)
    {
        float ffn2 = 0.0f;
        for (uint i = 0; i < 1536; i++)
            ffn2 += FFNOut[seq_idx * 1536 + i] * FFN2W[i * hidden_dim + hid_idx];
        ffn2 += FFN2B[hid_idx];

        // Final residual
        Output[base + hid_idx] = post_ln + ffn2;
    }
}
