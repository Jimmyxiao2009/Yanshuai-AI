// 嵌入查找 + Position Embedding Compute Shader
// 输入: token_ids[seq_len], pos_ids[seq_len]
// 输出: output[seq_len, hidden_dim]

cbuffer Constants : register(b0)
{
    uint seq_len;
    uint hidden_dim;
    uint pad0;
    uint pad1;
};

// Token Embedding 表 [vocab_size, hidden_dim]
StructuredBuffer<float> EmbedTable  : register(t0);
// Position Embedding 表 [max_pos, hidden_dim]
StructuredBuffer<float> PosTable    : register(t1);
// Token IDs
StructuredBuffer<uint> TokenIds     : register(t2);
// Position IDs
StructuredBuffer<uint> PosIds       : register(t3);

// LayerNorm 参数
StructuredBuffer<float> LnWeight    : register(t4);
StructuredBuffer<float> LnBias      : register(t5);

RWStructuredBuffer<float> Output    : register(u0);

groupshared float shared_sum;
groupshared float shared_sq_sum;
groupshared float shared_vals[384];  // max hidden_dim

[numthreads(384, 1, 1)]
void CSMain(uint3 gtid : SV_GroupThreadID, uint3 gid : SV_GroupID)
{
    uint seq_idx = gid.x;
    uint hid_idx = gtid.x;

    if (seq_idx >= seq_len || hid_idx >= hidden_dim) return;

    uint token_id = TokenIds[seq_idx];
    uint pos_id   = PosIds[seq_idx];

    // Token embedding + Position embedding
    float tok_val = EmbedTable[token_id * hidden_dim + hid_idx];
    float pos_val = PosTable[pos_id * hidden_dim + hid_idx];
    float val = tok_val + pos_val;

    Output[seq_idx * hidden_dim + hid_idx] = val;

    // LayerNorm: 均值 & 方差
    // 使用 group shared memory 规约
    shared_vals[hid_idx] = val;

    GroupMemoryBarrierWithGroupSync();

    if (hid_idx == 0)
    {
        float sum = 0.0f;
        float sq_sum = 0.0f;
        for (uint i = 0; i < hidden_dim; i++)
        {
            float v = shared_vals[i];
            sum += v;
            sq_sum += v * v;
        }
        float mean = sum / hidden_dim;
        float inv_std = rsqrt(sq_sum / hidden_dim - mean * mean + 1e-12f);
        shared_sum = mean;
        shared_sq_sum = inv_std;
    }

    GroupMemoryBarrierWithGroupSync();

    float m = shared_sum;
    float s = shared_sq_sum;
    float out_val = (val - m) * s * LnWeight[hid_idx] + LnBias[hid_idx];

    Output[seq_idx * hidden_dim + hid_idx] = out_val;
}
