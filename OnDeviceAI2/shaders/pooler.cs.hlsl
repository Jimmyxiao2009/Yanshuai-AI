// Mean Pooling + L2 Normalize Compute Shader
// 输入: hidden_states[seq_len, hidden_dim]
// 输出: embedding[pooler_dim] (一个向量)

cbuffer Constants : register(b0)
{
    uint seq_len;
    uint hidden_dim;
    uint pooler_dim;
    uint pad0;
};

StructuredBuffer<float>  Input     : register(t0);
StructuredBuffer<float>  PoolerW   : register(t1);  // [hidden_dim, pooler_dim]
StructuredBuffer<float>  PoolerB   : register(t2);  // [pooler_dim]

RWStructuredBuffer<float> Output   : register(u0);

groupshared float shared_sum[768]; // max hidden_dim

[numthreads(768, 1, 1)]
void CSMain(uint3 gtid : SV_GroupThreadID, uint3 gid : SV_GroupID)
{
    uint hid_idx = gtid.x;
    if (hid_idx >= hidden_dim) return;

    // Mean Pooling: 对所有序列位置取平均
    float mean = 0.0f;
    for (uint i = 0; i < seq_len; i++)
    {
        mean += Input[i * hidden_dim + hid_idx];
    }
    mean /= max((float)seq_len, 1.0f);
    shared_sum[hid_idx] = mean;

    GroupMemoryBarrierWithGroupSync();

    // L2 Normalize
    if (hid_idx == 0)
    {
        float sum_sq = 0.0f;
        for (uint i = 0; i < hidden_dim; i++)
            sum_sq += shared_sum[i] * shared_sum[i];
        float inv_norm = rsqrt(sum_sq + 1e-12f);

        for (uint i = 0; i < hidden_dim; i++)
            shared_sum[i] *= inv_norm;
    }

    GroupMemoryBarrierWithGroupSync();

    // Pooler projection (if pooler_dim != hidden_dim)
    if (pooler_dim <= hidden_dim && hid_idx < pooler_dim)
    {
        // Simple projection or direct copy
        Output[hid_idx] = shared_sum[hid_idx];
    }
}
