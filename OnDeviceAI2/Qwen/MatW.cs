// MatW.cs — 2D 权重矩阵的双精度形态包装：fp32 或 int8(per-output-channel scale) 常驻。
// int8 形态省 4 倍内存（0.8B 权重 ~765MB），是手机端可行性的前提；
// 数学上 (Σ q·x)·scale ≡ Σ (q·scale)·x，与反量化路径等值（仅浮点舍入差异）。
using System;

namespace Yanshuai.Qwen
{
    public sealed class MatW
    {
        public readonly int Out;
        public readonly int In;
        public readonly float[] F;        // fp32 路径（调试/小张量）
        public readonly sbyte[] Q;        // int8 路径
        public readonly float[] Scale;    // [Out]

        MatW(int outF, int inF, float[] f, sbyte[] q, float[] scale)
        {
            Out = outF; In = inF; F = f; Q = q; Scale = scale;
        }

        public static MatW FromF32(float[] f, int outF, int inF)
        {
            if (f.Length != (long)outF * inF) throw new ArgumentException("MatW.FromF32 尺寸不符");
            return new MatW(outF, inF, f, null, null);
        }

        public static MatW FromQ8(sbyte[] q, float[] scale, int outF, int inF)
        {
            if (q.Length != (long)outF * inF || scale.Length != outF)
                throw new ArgumentException("MatW.FromQ8 尺寸不符");
            return new MatW(outF, inF, null, q, scale);
        }
    }
}
