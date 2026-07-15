// DeltaNet.cs — Qwen3.5 Gated DeltaNet 线性注意力层（逐 token 递归）
//
// 严格对齐 transformers 5.9 modeling_qwen3_5.py:
//   Qwen3_5GatedDeltaNet.forward + torch_recurrent_gated_delta_rule + Qwen3_5RMSNormGated
// 数值蓝本：model_export/dump_reference.py 的 deltanet_numpy（已验证 vs HF max|Δ|=2.4e-7）。
//
// 维度（Qwen3.5-0.8B）：hidden=1024, heads=16(k==v), kdim=vdim=128, conv=4。
// 递归状态 S[head, kdim, vdim] 用 float 累积（架构本身就是 fp32，
// mamba_ssm_dtype=float32；早期用 double 只是为了跟 dump_reference.py 的
// numpy fp64 参考逐位对齐，量化误差远大于 float/double 累加的差异，改回 float
// 既更贴近真实模型、也是未来上 NEON 的前提——ARMv7 NEON 不支持双精度 SIMD）。
//
// 所有 S 相关的临时缓冲区都是可复用的实例字段（首次用到时按需扩容，解码期 S=1
// 之后就不再重新分配），避免每个 token 都在热路径里 new 一堆数组造成 GC 压力。
using System;

namespace Yanshuai.Qwen
{
    /// <summary>DeltaNet 层维度（config 驱动）。</summary>
    public sealed class DeltaNetDims
    {
        public int Hidden;     // 1024
        public int NumKHeads;  // 16
        public int NumVHeads;  // 16
        public int KHeadDim;   // 128
        public int VHeadDim;   // 128
        public int ConvK;      // 4
        public float Eps;      // 1e-6
        public int KeyDim => NumKHeads * KHeadDim;   // 2048
        public int ValDim => NumVHeads * VHeadDim;   // 2048
        public int ConvDim => KeyDim * 2 + ValDim;   // 6144
    }

    /// <summary>逐阶段中间张量，用于与参考逐位比对（可为 null）。</summary>
    public sealed class DeltaNetDebug
    {
        public float[] MixedConv; // [S, ConvDim]  conv+silu 后
        public float[] G;         // [S, NumVHeads]
        public float[] Beta;      // [S, NumVHeads]
        public float[] Core;      // [S, NumVHeads*VHeadDim]  递归输出(pre-norm)
        public float[] Normed;    // [S, ValDim]  gated-RMSNorm 后(pre out_proj)
    }

    public sealed class DeltaNet
    {
        readonly DeltaNetDims d;
        // 权重（行主序，PyTorch nn.Linear 布局；大矩阵 MatW 可 int8 常驻）
        readonly MatW Wqkv;      // [6144,1024]
        readonly MatW Wa;        // [16,1024]
        readonly MatW Wb;        // [16,1024]
        readonly MatW Wz;        // [2048,1024]
        readonly float[] conv;   // [6144,1,4] -> 取 [6144,4]
        readonly float[] Alog;   // [16]
        readonly float[] dtBias; // [16]
        readonly float[] normW;  // [128]
        readonly MatW Wout;      // [1024,2048]

        // 递归状态（跨 token 持续）：[head, kdim, vdim]
        readonly float[] state;
        // 因果卷积缓冲：最近 K-1 步的 in_proj_qkv 输出（增量解码时的左侧上下文）
        readonly float[] convBuf;

        // 可复用热路径缓冲区（按需扩容；decode 时 S=1，第一次之后不会再重新分配）
        float[] _mixed, _ext, _convOut, _a, _b, _z, _beta, _g, _core, _normed, _outp;
        readonly float[] _qh, _kh, _vh, _kvMem, _delta;   // 单 head 大小，S 无关，构造时定死

        public DeltaNet(DeltaNetDims dims, MatW wQkv, MatW wA, MatW wB, MatW wZ,
                        float[] conv1d, float[] aLog, float[] dtBiasW, float[] normWeight, MatW wOut)
        {
            d = dims;
            Wqkv = wQkv; Wa = wA; Wb = wB; Wz = wZ;
            conv = conv1d; Alog = aLog; dtBias = dtBiasW; normW = normWeight; Wout = wOut;
            state = new float[d.NumVHeads * d.KHeadDim * d.VHeadDim];
            convBuf = new float[(d.ConvK - 1) * d.ConvDim];
            _qh = new float[d.KHeadDim];
            _kh = new float[d.KHeadDim];
            _vh = new float[d.VHeadDim];
            _kvMem = new float[d.VHeadDim];
            _delta = new float[d.VHeadDim];
        }

        /// <summary>清空递归状态与卷积缓冲（新会话起点）。</summary>
        public void ResetState()
        {
            Array.Clear(state, 0, state.Length);
            Array.Clear(convBuf, 0, convBuf.Length);
        }

        static void EnsureCap(ref float[] buf, int len)
        {
            if (buf == null || buf.Length < len) buf = new float[len];
        }

        /// <summary>
        /// 处理一段序列。输入 x = [S, Hidden]（已过 input_layernorm），输出 [S, Hidden]（pre-residual）。
        /// 逐 token 递归，等价于 HF prefill 的 chunk 实现。
        /// </summary>
        public float[] Forward(float[] x, int S, DeltaNetDebug dbg = null)
        {
            int H = d.Hidden, K = d.ConvK;
            int keyDim = d.KeyDim, valDim = d.ValDim, convDim = d.ConvDim;
            int kdim = d.KHeadDim, vdim = d.VHeadDim, nvh = d.NumVHeads;

            // 1) in_proj_qkv: [S,6144]
            EnsureCap(ref _mixed, S * convDim);
            var mixed = _mixed;
            for (int t = 0; t < S; t++)
                QwenMath.MatVec(Wqkv, x, t * H, mixed, t * convDim);

            // 2) depthwise 因果卷积(kernel=K, 左侧上下文来自 convBuf, 无 bias) + SiLU
            //    ext = [convBuf(K-1 行) | mixed(S 行)]；out[t] = Σ_j w[:,j] * ext[t+j]
            int P = K - 1;
            EnsureCap(ref _ext, (P + S) * convDim);
            var ext = _ext;
            Array.Copy(convBuf, 0, ext, 0, P * convDim);
            Array.Copy(mixed, 0, ext, P * convDim, S * convDim);

            EnsureCap(ref _convOut, S * convDim);
            var convOut = _convOut;
            Array.Clear(convOut, 0, S * convDim);   // 下面是累加写入，必须先清零
            for (int t = 0; t < S; t++)
            {
                int ob = t * convDim;
                for (int j = 0; j < K; j++)
                {
                    int ib = (t + j) * convDim;
                    for (int c = 0; c < convDim; c++)
                        convOut[ob + c] += conv[c * K + j] * ext[ib + c];
                }
            }
            for (int i = 0; i < S * convDim; i++) convOut[i] = QwenMath.Silu(convOut[i]);
            if (dbg != null) { dbg.MixedConv = new float[S * convDim]; Array.Copy(convOut, dbg.MixedConv, S * convDim); }

            // 更新卷积缓冲为最近 K-1 行输入
            Array.Copy(ext, S * convDim, convBuf, 0, P * convDim);

            // 3) 门控参数 a,b,z
            EnsureCap(ref _a, S * nvh);
            EnsureCap(ref _b, S * nvh);
            EnsureCap(ref _z, S * valDim);
            var a = _a; var b = _b; var z = _z;
            for (int t = 0; t < S; t++)
            {
                QwenMath.MatVec(Wa, x, t * H, a, t * nvh);
                QwenMath.MatVec(Wb, x, t * H, b, t * nvh);
                QwenMath.MatVec(Wz, x, t * H, z, t * valDim);
            }
            EnsureCap(ref _beta, S * nvh);
            EnsureCap(ref _g, S * nvh);
            var beta = _beta; var g = _g;
            for (int t = 0; t < S; t++)
                for (int h = 0; h < nvh; h++)
                {
                    int idx = t * nvh + h;
                    beta[idx] = QwenMath.Sigmoid(b[idx]);
                    g[idx] = (float)(-Math.Exp(Alog[h]) * QwenMath.Softplus(a[idx] + dtBias[h]));
                }
            if (dbg != null)
            {
                dbg.G = new float[S * nvh]; Array.Copy(g, dbg.G, S * nvh);
                dbg.Beta = new float[S * nvh]; Array.Copy(beta, dbg.Beta, S * nvh);
            }

            // 4) 逐 token 递归（每 head 独立，state[h] 为 [kdim,vdim]）
            float scale = 1f / (float)Math.Sqrt(kdim);
            EnsureCap(ref _core, S * nvh * vdim);
            var core = _core;
            Array.Clear(core, 0, S * nvh * vdim);   // 下面是累加写入，必须先清零
            var qh = _qh; var kh = _kh; var vh = _vh; var kvMem = _kvMem; var delta = _delta;
            for (int t = 0; t < S; t++)
            {
                int cb = t * convDim;
                for (int h = 0; h < nvh; h++)
                {
                    // gather q,k,v（与 numpy 切片一致）
                    int qOff = cb + h * kdim;
                    int kOff = cb + keyDim + h * kdim;
                    int vOff = cb + 2 * keyDim + h * vdim;
                    float qss = 0f, kss = 0f;
                    for (int i = 0; i < kdim; i++)
                    {
                        float qv = convOut[qOff + i]; qh[i] = qv; qss += qv * qv;
                        float kv = convOut[kOff + i]; kh[i] = kv; kss += kv * kv;
                    }
                    for (int i = 0; i < vdim; i++) vh[i] = convOut[vOff + i];
                    // l2norm(eps=1e-6) over dim，q 再乘 scale
                    float qInv = 1f / (float)Math.Sqrt(qss + 1e-6);
                    float kInv = 1f / (float)Math.Sqrt(kss + 1e-6);
                    for (int i = 0; i < kdim; i++) { qh[i] = qh[i] * qInv * scale; kh[i] = kh[i] * kInv; }

                    int sb = h * kdim * vdim;
                    float decay = (float)Math.Exp(g[t * nvh + h]);
                    float bt = beta[t * nvh + h];

                    // S *= decay
                    for (int p = 0; p < kdim * vdim; p++) state[sb + p] *= decay;
                    // kv_mem[v] = Σ_k S[k,v]*kh[k]
                    Array.Clear(kvMem, 0, vdim);
                    for (int kk = 0; kk < kdim; kk++)
                    {
                        float kkv = kh[kk];
                        if (kkv == 0) continue;
                        int row = sb + kk * vdim;
                        for (int vv = 0; vv < vdim; vv++) kvMem[vv] += state[row + vv] * kkv;
                    }
                    // delta[v] = (vh[v]-kv_mem[v])*beta ; S[k,v] += kh[k]*delta[v]
                    for (int vv = 0; vv < vdim; vv++) delta[vv] = (vh[vv] - kvMem[vv]) * bt;
                    for (int kk = 0; kk < kdim; kk++)
                    {
                        float kkv = kh[kk];
                        int row = sb + kk * vdim;
                        for (int vv = 0; vv < vdim; vv++) state[row + vv] += kkv * delta[vv];
                    }
                    // core[v] = Σ_k S[k,v]*qh[k]
                    int coreOff = (t * nvh + h) * vdim;
                    for (int kk = 0; kk < kdim; kk++)
                    {
                        float q = qh[kk];
                        int row = sb + kk * vdim;
                        for (int vv = 0; vv < vdim; vv++) core[coreOff + vv] += state[row + vv] * q;
                    }
                }
            }
            if (dbg != null) { dbg.Core = new float[S * nvh * vdim]; Array.Copy(core, dbg.Core, S * nvh * vdim); }

            // 5) gated RMSNorm(over vdim) + out_proj
            EnsureCap(ref _normed, S * valDim);
            var normed = _normed;
            for (int t = 0; t < S; t++)
                for (int h = 0; h < nvh; h++)
                {
                    int coreOff = (t * nvh + h) * vdim;
                    int zOff = t * valDim + h * vdim;
                    int nOff = t * valDim + h * vdim;
                    // RMSNorm(core) * silu(z)
                    float ss = 0f;
                    for (int i = 0; i < vdim; i++) { float v = core[coreOff + i]; ss += v * v; }
                    float inv = 1f / (float)Math.Sqrt(ss / vdim + d.Eps);
                    for (int i = 0; i < vdim; i++)
                    {
                        float yn = normW[i] * (core[coreOff + i] * inv);
                        normed[nOff + i] = yn * QwenMath.Silu(z[zOff + i]);
                    }
                }
            if (dbg != null) { dbg.Normed = new float[S * valDim]; Array.Copy(normed, dbg.Normed, S * valDim); }

            EnsureCap(ref _outp, S * H);
            var outp = _outp;
            for (int t = 0; t < S; t++)
                QwenMath.MatVec(Wout, normed, t * valDim, outp, t * H);

            // 直接返回内部缓冲区：调用方（QwenRunner）拿到后会立刻整段累加进残差流，
            // 本层对象在那之前不会被再次调用，不存在覆写风险，不需要拷贝。
            return outp;
        }
    }
}
