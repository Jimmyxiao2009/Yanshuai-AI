// SwiGluMlp.cs — Qwen3.5 前馈层：down( silu(gate(x)) * up(x) )。无 bias、无内部 norm。
// g/u/act 大小固定为 inter，只需分配一次；outp 随 S 增长按需扩容。
using System;

namespace Yanshuai.Qwen
{
    public sealed class SwiGluMlp
    {
        readonly int hidden, inter;
        readonly MatW gate, up, down;   // gate/up [inter,hidden]; down [hidden,inter]
        readonly float[] _g, _u, _act;  // 与 S 无关，构造时定死
        float[] _outp;

        public SwiGluMlp(int hidden, int intermediate, MatW gateW, MatW upW, MatW downW)
        {
            this.hidden = hidden; inter = intermediate; gate = gateW; up = upW; down = downW;
            _g = new float[inter]; _u = new float[inter]; _act = new float[inter];
        }

        /// <summary>x = [S, hidden] → [S, hidden]。</summary>
        public float[] Forward(float[] x, int S)
        {
            if (_outp == null || _outp.Length < S * hidden) _outp = new float[S * hidden];
            var g = _g; var u = _u; var act = _act; var outp = _outp;
            for (int t = 0; t < S; t++)
            {
                QwenMath.MatVec(gate, x, t * hidden, g, 0);
                QwenMath.MatVec(up, x, t * hidden, u, 0);
                for (int i = 0; i < inter; i++) act[i] = QwenMath.Silu(g[i]) * u[i];
                QwenMath.MatVec(down, act, 0, outp, t * hidden);
            }
            // 直接返回内部缓冲区，理由同 DeltaNet.cs/FullAttention.cs。
            return outp;
        }
    }
}
