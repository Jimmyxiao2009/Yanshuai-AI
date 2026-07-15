// QwenRunner.cs — Qwen3.5-0.8B 整网自回归推理（P2c）
//
// 结构（见 docs/qwen3_5-0_8b-arch-reference.md §5）：
//   x = embed[id]（int8 行反量化）
//   × 24 层: x += Attn(RMSNorm₁₊w(x)) ; x += SwiGLU(RMSNorm₁₊w(x))
//     — 层类型按 layer_types：18 × Gated DeltaNet（递归状态） + 6 × 全注意力（KV cache）
//   final RMSNorm₁₊w → logits = x · embedᵀ（tie，int8 点积）
// 增量式：首次 Forward = prefill，之后逐 token 调用即解码；Reset() 开新会话。
//
// x/h/xn 是可复用的实例缓冲区（按需扩容，不随每次 Forward 调用重新分配）；
// 注意它们的实际长度是 S*H 而不是 buf.Length（扩容后旧缓冲区可能比当前调用更大），
// 循环边界和 collect 快照都必须显式用 S*H，不能用 .Length。
using System;
using System.Collections.Generic;

namespace Yanshuai.Qwen
{
    public sealed class QwenRunner
    {
        readonly QwenModel M;
        readonly DeltaNet[] deltaNets;       // 按层索引（full 层为 null）
        readonly FullAttention[] attns;      // 按层索引（linear 层为 null）
        readonly SwiGluMlp[] mlps;

        float[] _x, _h;
        readonly float[] _xn;   // 大小固定为 Hidden，构造时定死

        public QwenRunner(QwenModel m)
        {
            M = m;
            deltaNets = new DeltaNet[m.NumLayers];
            attns = new FullAttention[m.NumLayers];
            mlps = new SwiGluMlp[m.NumLayers];
            _xn = new float[m.Hidden];

            var dnDims = new DeltaNetDims
            {
                Hidden = m.Hidden,
                NumKHeads = m.LinKHeads,
                NumVHeads = m.LinVHeads,
                KHeadDim = m.LinKDim,
                VHeadDim = m.LinVDim,
                ConvK = m.ConvK,
                Eps = m.Eps,
            };
            var atDims = new FullAttentionDims
            {
                Hidden = m.Hidden,
                NumHeads = m.NumHeads,
                NumKVHeads = m.NumKVHeads,
                HeadDim = m.HeadDim,
                RotaryDim = m.RotaryDim,
                RopeTheta = m.RopeTheta,
                Eps = m.Eps,
            };

            for (int i = 0; i < m.NumLayers; i++)
            {
                QwenLayer L = m.Layers[i];
                if (L.IsFull)
                    attns[i] = new FullAttention(atDims, L.Wq, L.Wk, L.Wv, L.Wo, L.QNorm, L.KNorm);
                else
                    deltaNets[i] = new DeltaNet(dnDims, L.Wqkv, L.Wa, L.Wb, L.Wz,
                                                L.Conv, L.ALog, L.DtBias, L.NormW, L.Wout);
                mlps[i] = new SwiGluMlp(m.Hidden, m.Intermediate, L.Gate, L.Up, L.Down);
            }
        }

        /// <summary>清空全部会话状态（KV cache / DeltaNet 递归 / 卷积缓冲）。</summary>
        public void Reset()
        {
            for (int i = 0; i < M.NumLayers; i++)
            {
                if (deltaNets[i] != null) deltaNets[i].ResetState();
                if (attns[i] != null) attns[i].ResetState();
            }
        }

        static void EnsureCap(ref float[] buf, int len)
        {
            if (buf == null || buf.Length < len) buf = new float[len];
        }

        /// <summary>
        /// 处理一段 token（增量式，位置接在上次之后），返回末位 logits[vocab]。
        /// collect 非空时依次收集 [embed, 层1输出, …, 层L输出]（各 [S*Hidden]，pre-final-norm）。
        /// </summary>
        public float[] Forward(int[] ids, List<float[]> collect = null)
        {
            int S = ids.Length, H = M.Hidden;
            int len = S * H;

            EnsureCap(ref _x, len);
            EnsureCap(ref _h, len);
            var x = _x; var h = _h;

            // embed：int8 行反量化
            for (int t = 0; t < S; t++)
            {
                int row = ids[t];
                float sc = M.EmbedScale[row];
                long b = (long)row * H;
                for (int i = 0; i < H; i++)
                    x[t * H + i] = M.EmbedQ8[b + i] * sc;
            }
            if (collect != null) collect.Add(Snapshot(x, len));

            for (int li = 0; li < M.NumLayers; li++)
            {
                QwenLayer L = M.Layers[li];

                for (int t = 0; t < S; t++)
                    QwenMath.RmsNorm(x, t * H, H, L.Ln1, M.Eps, h, t * H);
                float[] attnOut = L.IsFull ? attns[li].Forward(h, S) : deltaNets[li].Forward(h, S);
                for (int i = 0; i < len; i++) x[i] += attnOut[i];

                for (int t = 0; t < S; t++)
                    QwenMath.RmsNorm(x, t * H, H, L.Ln2, M.Eps, h, t * H);
                float[] mlpOut = mlps[li].Forward(h, S);
                for (int i = 0; i < len; i++) x[i] += mlpOut[i];

                if (collect != null) collect.Add(Snapshot(x, len));
            }

            // final norm（只需末位）→ tie lm_head（int8 点积）
            QwenMath.RmsNorm(x, (S - 1) * H, H, M.FinalNorm, M.Eps, _xn, 0);
            return LmHeadLogits(_xn);
        }

        static float[] Snapshot(float[] buf, int len)
        {
            var copy = new float[len];
            Array.Copy(buf, copy, len);
            return copy;
        }

        /// <summary>logits[v] = scale[v] · Σ_i q8[v,i]·xn[i]（tie 复用嵌入表）。</summary>
        public float[] LmHeadLogits(float[] xn)
        {
            int H = M.Hidden, V = M.VocabSize;
            var logits = new float[V];
            System.Threading.Tasks.Parallel.For(0, V, v =>
            {
                long b = (long)v * H;
                float s = 0f;
                for (int i = 0; i < H; i++)
                    s += M.EmbedQ8[b + i] * xn[i];
                logits[v] = s * M.EmbedScale[v];
            });
            return logits;
        }

        public static int ArgMax(float[] v)
        {
            int best = 0;
            for (int i = 1; i < v.Length; i++)
                if (v[i] > v[best]) best = i;
            return best;
        }
    }
}
