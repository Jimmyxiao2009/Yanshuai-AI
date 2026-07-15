// FullAttention.cs — Qwen3.5 全注意力层（GQA + QK-Norm + partial RoPE + 输出门）
//
// 严格对齐 transformers 5.9 modeling_qwen3_5.py: Qwen3_5Attention.forward + apply_rotary_pos_emb。
// 数值蓝本：model_export/dump_attn_reference.py（已验证 vs HF max|Δ|=3.7e-7）。
//
// 维度（Qwen3.5-0.8B）：hidden=1024, q_heads=8, kv_heads=2(GQA 4:1), head_dim=256,
//   rotary_dim=64(partial 0.25), rope_theta=1e7。q_proj 输出 4096 = 每头[q256|gate256]。
//
// 累加精度用 float 不用 double（原因见 QwenMath.cs 顶部注释：量化误差远大于
// float/double 累加差异，且 ARMv7 NEON 不支持双精度）。RoPE 频率表用 double 算
// （只在 BuildRope 里算 S×half 次，不是热路径，保留 double 精度更稳）。
//
// 除 KV cache（本来就是增量增长设计）外，qg/kbuf/vbuf/query/gate/outp/res/scores
// 等 S 相关缓冲区也都是可复用实例字段，decode 期 S=1 用过一次就不再重新分配。
using System;

namespace Yanshuai.Qwen
{
    public sealed class FullAttentionDims
    {
        public int Hidden;       // 1024
        public int NumHeads;     // 8
        public int NumKVHeads;   // 2
        public int HeadDim;      // 256
        public int RotaryDim;    // 64
        public float RopeTheta;  // 1e7
        public float Eps;        // 1e-6
        public int QDim => NumHeads * HeadDim;      // 2048
        public int KVDim => NumKVHeads * HeadDim;   // 512
        public int Groups => NumHeads / NumKVHeads; // 4
    }

    public sealed class FullAttentionDebug
    {
        public float[] QNormed;     // [S, QDim]  q_norm 后(pre-rope)
        public float[] QRoped;      // [S, QDim]  rope 后
        public float[] AttnPreGate; // [S, QDim]  注意力输出(pre 输出门)
        public float[] Cos;         // [S, RotaryDim]
        public float[] Sin;         // [S, RotaryDim]
    }

    public sealed class FullAttention
    {
        readonly FullAttentionDims d;
        readonly MatW Wq, Wk, Wv, Wo;
        readonly float[] qNorm, kNorm;

        // KV cache（跨调用持续；norm+RoPE 之后的 k/v，按绝对位置追加）
        float[] kCache, vCache;
        int cachedLen;

        // 可复用热路径缓冲区（按需扩容）
        float[] _qg, _kbuf, _vbuf, _query, _gate, _cos, _sin, _outp, _res;
        float[] _scores;

        public FullAttention(FullAttentionDims dims, MatW wQ, MatW wK, MatW wV, MatW wO,
                             float[] qNormW, float[] kNormW)
        {
            d = dims; Wq = wQ; Wk = wK; Wv = wV; Wo = wO; qNorm = qNormW; kNorm = kNormW;
            kCache = new float[256 * d.KVDim];
            vCache = new float[256 * d.KVDim];
            cachedLen = 0;
        }

        /// <summary>清空 KV cache（新会话起点）。</summary>
        public void ResetState() => cachedLen = 0;

        static void EnsureCap(ref float[] buf, int len)
        {
            if (buf == null || buf.Length < len) buf = new float[len];
        }

        void EnsureCapacity(int totalTokens)
        {
            int need = totalTokens * d.KVDim;
            if (kCache.Length >= need) return;
            int cap = kCache.Length;
            while (cap < need) cap *= 2;
            Array.Resize(ref kCache, cap);
            Array.Resize(ref vCache, cap);
        }

        /// <summary>构造 RoPE cos/sin 表（标准 NeoX 式，前 RotaryDim 维），绝对位置 posStart..posStart+S-1。cos/sin: [S, RotaryDim]。</summary>
        public void BuildRope(int S, float[] cos, float[] sin, int posStart = 0)
        {
            int rot = d.RotaryDim, half = rot / 2;
            for (int p = 0; p < S; p++)
                for (int i = 0; i < half; i++)
                {
                    double invFreq = 1.0 / Math.Pow(d.RopeTheta, (2.0 * i) / rot);
                    double ang = (double)(posStart + p) * invFreq;
                    float c = (float)Math.Cos(ang), s = (float)Math.Sin(ang);
                    cos[p * rot + i] = c; cos[p * rot + i + half] = c;
                    sin[p * rot + i] = s; sin[p * rot + i + half] = s;
                }
        }

        // 对单个 head 向量 [HeadDim] 原地施加 RoPE（前 RotaryDim 维）
        void ApplyRope(float[] buf, int off, float[] cos, float[] sin, int pos)
        {
            int rot = d.RotaryDim, half = rot / 2, cb = pos * rot;
            for (int i = 0; i < half; i++)
            {
                float x1 = buf[off + i];
                float x2 = buf[off + i + half];
                float c = cos[cb + i], s = sin[cb + i];          // cos[i+half]==cos[i]
                buf[off + i] = x1 * c - x2 * s;
                buf[off + i + half] = x2 * c + x1 * s;
            }
        }

        /// <summary>
        /// x = [S, Hidden]（已过 input_layernorm）→ [S, Hidden]（pre-residual）。
        /// 增量式：本批 token 的绝对位置从 cachedLen 开始，k/v 追加进 KV cache，
        /// 因果注意力覆盖 cache 全部历史。首次调用（cache 空）即 prefill。
        /// </summary>
        public float[] Forward(float[] x, int S, FullAttentionDebug dbg = null)
        {
            int H = d.Hidden, nh = d.NumHeads, nkv = d.NumKVHeads, hd = d.HeadDim;
            int qDim = d.QDim, kvDim = d.KVDim, groups = d.Groups;
            int posStart = cachedLen;
            int total = posStart + S;

            // 1) 投影
            EnsureCap(ref _qg, S * nh * hd * 2);
            EnsureCap(ref _kbuf, S * kvDim);
            EnsureCap(ref _vbuf, S * kvDim);
            var qg = _qg; var kbuf = _kbuf; var vbuf = _vbuf;
            for (int t = 0; t < S; t++)
            {
                QwenMath.MatVec(Wq, x, t * H, qg, t * nh * hd * 2);
                QwenMath.MatVec(Wk, x, t * H, kbuf, t * kvDim);
                QwenMath.MatVec(Wv, x, t * H, vbuf, t * kvDim);
            }

            // 2) 拆 query / gate（每头 [q256|gate256]）
            EnsureCap(ref _query, S * qDim);
            EnsureCap(ref _gate, S * qDim);
            var query = _query; var gate = _gate;
            for (int t = 0; t < S; t++)
                for (int h = 0; h < nh; h++)
                {
                    int src = t * nh * hd * 2 + h * hd * 2;
                    int dst = t * qDim + h * hd;
                    Array.Copy(qg, src, query, dst, hd);
                    Array.Copy(qg, src + hd, gate, dst, hd);
                }

            // 3) q_norm / k_norm（(1+weight) RMSNorm，over head_dim）
            for (int t = 0; t < S; t++)
            {
                for (int h = 0; h < nh; h++)
                    QwenMath.RmsNorm(query, t * qDim + h * hd, hd, qNorm, d.Eps, query, t * qDim + h * hd);
                for (int h = 0; h < nkv; h++)
                    QwenMath.RmsNorm(kbuf, t * kvDim + h * hd, hd, kNorm, d.Eps, kbuf, t * kvDim + h * hd);
            }
            if (dbg != null) { dbg.QNormed = new float[S * qDim]; Array.Copy(query, dbg.QNormed, S * qDim); }

            // 4) RoPE（绝对位置 posStart+t）
            EnsureCap(ref _cos, S * d.RotaryDim);
            EnsureCap(ref _sin, S * d.RotaryDim);
            var cos = _cos; var sin = _sin;
            BuildRope(S, cos, sin, posStart);
            if (dbg != null)
            {
                dbg.Cos = new float[S * d.RotaryDim]; Array.Copy(cos, dbg.Cos, S * d.RotaryDim);
                dbg.Sin = new float[S * d.RotaryDim]; Array.Copy(sin, dbg.Sin, S * d.RotaryDim);
            }
            for (int t = 0; t < S; t++)
            {
                for (int h = 0; h < nh; h++) ApplyRope(query, t * qDim + h * hd, cos, sin, t);
                for (int h = 0; h < nkv; h++) ApplyRope(kbuf, t * kvDim + h * hd, cos, sin, t);
            }
            if (dbg != null) { dbg.QRoped = new float[S * qDim]; Array.Copy(query, dbg.QRoped, S * qDim); }

            // 5) k/v 追加进 cache（norm+RoPE 之后）
            EnsureCapacity(total);
            Array.Copy(kbuf, 0, kCache, posStart * kvDim, S * kvDim);
            Array.Copy(vbuf, 0, vCache, posStart * kvDim, S * kvDim);

            // 6) 因果 GQA 注意力（query 的绝对位置 P 覆盖 cache[0..P]）
            float scaling = 1f / (float)Math.Sqrt(hd);
            EnsureCap(ref _outp, S * qDim);
            var outp = _outp;
            Array.Clear(outp, 0, S * qDim);   // 下面是累加写入，必须先清零
            EnsureCap(ref _scores, total);
            var scores = _scores;
            for (int qh = 0; qh < nh; qh++)
            {
                int kvh = qh / groups;
                for (int ti = 0; ti < S; ti++)
                {
                    int absPos = posStart + ti;
                    int qOff = ti * qDim + qh * hd;
                    float mx = float.NegativeInfinity;
                    for (int tj = 0; tj <= absPos; tj++)
                    {
                        int kOff = tj * kvDim + kvh * hd;
                        float s = 0f;
                        for (int dd = 0; dd < hd; dd++) s += query[qOff + dd] * kCache[kOff + dd];
                        s *= scaling;
                        scores[tj] = s;
                        if (s > mx) mx = s;
                    }
                    float sum = 0f;
                    for (int tj = 0; tj <= absPos; tj++) { scores[tj] = (float)Math.Exp(scores[tj] - mx); sum += scores[tj]; }
                    float invSum = 1f / sum;
                    int oOff = ti * qDim + qh * hd;
                    for (int tj = 0; tj <= absPos; tj++)
                    {
                        float p = scores[tj] * invSum;
                        int vOff = tj * kvDim + kvh * hd;
                        for (int dd = 0; dd < hd; dd++) outp[oOff + dd] += p * vCache[vOff + dd];
                    }
                }
            }
            if (dbg != null) { dbg.AttnPreGate = new float[S * qDim]; Array.Copy(outp, dbg.AttnPreGate, S * qDim); }
            cachedLen = total;

            // 7) 输出门 sigmoid(gate) + o_proj
            for (int i = 0; i < S * qDim; i++) outp[i] *= QwenMath.Sigmoid(gate[i]);
            EnsureCap(ref _res, S * H);
            var res = _res;
            for (int t = 0; t < S; t++)
                QwenMath.MatVec(Wo, outp, t * qDim, res, t * H);

            // 直接返回内部缓冲区：调用方拿到后立刻整段累加进残差流，本层对象在那之前
            // 不会被再次调用，不需要拷贝（DeltaNet.cs 同理，理由写在那边）。
            return res;
        }
    }
}
