// QwenMath.cs — Qwen 推理引擎的基础数学算子（纯 C#，与 HF/numpy 逐位对齐）
//
// 累加精度：全部用 float，不用 double。原因两条：
//   1) 架构本身就是 fp32（DeltaNet 状态 mamba_ssm_dtype=float32），int8 量化引入的
//      误差（per-channel scale 量化）远大于 float 累加 vs double 累加的差异，
//      早前用 double 只是为了跟 dump_reference.py 的 numpy fp64 参考逐位对齐，
//      不是模型真的需要。
//   2) ARMv7 NEON 不支持双精度 SIMD——用 float 是未来上 NEON 向量化的前提，
//      而且大多数移动端 FPU 的双精度吞吐本来就明显低于单精度。
using System;

namespace Yanshuai.Qwen
{
    /// <summary>低层数学算子。先求正确，后求速度。</summary>
    public static class QwenMath
    {
        /// <summary>
        /// 线性层 y = W·x（无 bias）。W 为行主序 [outF, inF]（PyTorch nn.Linear 权重布局）。
        /// y[o] = Σ_i W[o*inF + i] * x[xOff + i]
        /// </summary>
        public static void Linear(float[] W, int outF, int inF, float[] x, int xOff, float[] y, int yOff)
        {
            // 大矩阵按输出行并行；每行独立顺序累加，结果与串行逐位一致
            if ((long)outF * inF >= (1 << 18))
            {
                System.Threading.Tasks.Parallel.For(0, outF, o =>
                {
                    float s = 0f;
                    int wb = o * inF;
                    for (int i = 0; i < inF; i++)
                        s += W[wb + i] * x[xOff + i];
                    y[yOff + o] = s;
                });
                return;
            }
            for (int o = 0; o < outF; o++)
            {
                float s = 0f;
                int wb = o * inF;
                for (int i = 0; i < inF; i++)
                    s += W[wb + i] * x[xOff + i];
                y[yOff + o] = s;
            }
        }

        /// <summary>y = W·x，W 为 MatW（fp32 或 int8 常驻）。行主序 [Out, In]。</summary>
        public static void MatVec(MatW w, float[] x, int xOff, float[] y, int yOff)
        {
            if (w.F != null)
            {
                Linear(w.F, w.Out, w.In, x, xOff, y, yOff);
                return;
            }
            int outF = w.Out, inF = w.In;
            var q = w.Q; var scale = w.Scale;
            if ((long)outF * inF >= (1 << 18))
            {
                System.Threading.Tasks.Parallel.For(0, outF, o =>
                {
                    float s = 0f;
                    long wb = (long)o * inF;
                    for (int i = 0; i < inF; i++)
                        s += q[wb + i] * x[xOff + i];
                    y[yOff + o] = s * scale[o];
                });
                return;
            }
            for (int o = 0; o < outF; o++)
            {
                float s = 0f;
                long wb = (long)o * inF;
                for (int i = 0; i < inF; i++)
                    s += q[wb + i] * x[xOff + i];
                y[yOff + o] = s * scale[o];
            }
        }

        public static float Sigmoid(float v) => 1f / (1f + (float)Math.Exp(-v));

        public static float Silu(float v) => v * Sigmoid(v);

        /// <summary>softplus(x) = log(1 + e^x)，大值时退化为 x（防溢出）。</summary>
        public static float Softplus(float v) => v > 20f ? v : (float)Math.Log(1.0 + Math.Exp(v));

        /// <summary>
        /// Qwen3_5RMSNorm（零中心化，无 bias）：y = (1 + weight) ⊙ ( x / sqrt(mean(x²) + eps) )。over 最后一维。
        /// 用于 q_norm/k_norm/input_layernorm/post_attention_layernorm/final norm。
        /// 注意：DeltaNet 内部的 gated RMSNorm 用的是普通 weight（在 DeltaNet.cs 内联，不走这里）。
        /// </summary>
        public static void RmsNorm(float[] x, int off, int dim, float[] weight, float eps, float[] outBuf, int outOff)
        {
            float ss = 0f;
            for (int i = 0; i < dim; i++) { float v = x[off + i]; ss += v * v; }
            float inv = 1f / (float)Math.Sqrt(ss / dim + eps);
            for (int i = 0; i < dim; i++)
                outBuf[outOff + i] = (1f + weight[i]) * (x[off + i] * inv);
        }
    }
}
