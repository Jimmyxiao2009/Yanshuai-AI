// QwenModel.cs — .llmmodel(v2) 加载器（与 export_qwen.py 位级互为镜像）
//
// 布局: Header(128B) | layer_types[L] | Tokenizer(vocab+merges) | 权重张量流
// 张量块: ndim(u32) dims[ndim](u32) quant(u32)
//         fp32 → f32×N ; int8 → scale f32×dims[0] + int8×N (per-output-channel 对称)
// 词嵌入表保持 int8 常驻（254M 参数，兼作 tie lm_head）；其余矩阵反量化为 fp32。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yanshuai.Qwen
{
    /// <summary>单层权重（按层类型二选一填充；Ln/MLP 共有）。大矩阵 int8 常驻（MatW）。</summary>
    public sealed class QwenLayer
    {
        public bool IsFull;
        public float[] Ln1, Ln2;
        // full attention
        public MatW Wq, Wk, Wv, Wo;
        public float[] QNorm, KNorm;
        // deltanet
        public MatW Wqkv, Wa, Wb, Wz, Wout;
        public float[] Conv, ALog, DtBias, NormW;
        // mlp
        public MatW Gate, Up, Down;
    }

    public sealed class QwenModel
    {
        // ── header ──
        public int Hidden, NumLayers, NumHeads, NumKVHeads, HeadDim, Intermediate;
        public int VocabSize, MaxSeqLen, ConvK, LinVHeads, LinKHeads, LinVDim, LinKDim;
        public int RotaryDim, EosId, EosId2;
        public float RopeTheta, Eps;
        public byte[] LayerTypes;          // 0=linear 1=full

        // ── tokenizer ──
        public string[] Vocab;             // id → token 串（byte-level BPE 表示）
        public string[] Merges;            // "a b" 合并规则，按优先级排列

        // ── 权重 ──
        public sbyte[] EmbedQ8;            // [vocab*hidden] int8
        public float[] EmbedScale;         // [vocab]
        public QwenLayer[] Layers;
        public float[] FinalNorm;

        public static QwenModel Load(string path)
        {
            var m = new QwenModel();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var br = new BinaryReader(fs))
            {
                // ── header ──
                byte[] hdr = br.ReadBytes(128);
                uint magic = BitConverter.ToUInt32(hdr, 0);
                if (magic != 0x4C4C4D32) throw new InvalidDataException("QwenModel: bad magic");
                m.Hidden = BitConverter.ToInt32(hdr, 12);
                m.NumLayers = BitConverter.ToInt32(hdr, 16);
                m.NumHeads = BitConverter.ToInt32(hdr, 20);
                m.NumKVHeads = BitConverter.ToInt32(hdr, 24);
                m.HeadDim = BitConverter.ToInt32(hdr, 28);
                m.Intermediate = BitConverter.ToInt32(hdr, 32);
                m.VocabSize = BitConverter.ToInt32(hdr, 36);
                m.MaxSeqLen = BitConverter.ToInt32(hdr, 40);
                m.RopeTheta = BitConverter.ToSingle(hdr, 44);
                m.Eps = BitConverter.ToSingle(hdr, 48);
                m.ConvK = BitConverter.ToInt32(hdr, 56);
                m.LinVHeads = BitConverter.ToInt32(hdr, 60);
                m.LinKHeads = BitConverter.ToInt32(hdr, 64);
                m.LinVDim = BitConverter.ToInt32(hdr, 68);
                m.LinKDim = BitConverter.ToInt32(hdr, 72);
                m.EosId = BitConverter.ToInt32(hdr, 88);
                m.EosId2 = BitConverter.ToInt32(hdr, 92);
                m.RotaryDim = BitConverter.ToInt32(hdr, 96);

                m.LayerTypes = br.ReadBytes(m.NumLayers);

                // ── tokenizer ──
                int nVocab = br.ReadInt32();
                m.Vocab = new string[m.VocabSize];
                for (int i = 0; i < nVocab; i++)
                {
                    int len = br.ReadInt32();
                    string tok = Encoding.UTF8.GetString(br.ReadBytes(len));
                    int id = br.ReadInt32();
                    if (id >= 0 && id < m.VocabSize) m.Vocab[id] = tok;
                }
                int nMerges = br.ReadInt32();
                m.Merges = new string[nMerges];
                for (int i = 0; i < nMerges; i++)
                {
                    int len = br.ReadInt32();
                    m.Merges[i] = Encoding.UTF8.GetString(br.ReadBytes(len));
                }

                // ── embed（int8 常驻）──
                ReadInt8Tensor(br, out m.EmbedScale, out m.EmbedQ8, m.VocabSize, m.Hidden);

                // ── 层 ──
                m.Layers = new QwenLayer[m.NumLayers];
                for (int i = 0; i < m.NumLayers; i++)
                {
                    var L = new QwenLayer { IsFull = m.LayerTypes[i] == 1 };
                    L.Ln1 = ReadF32(br);
                    if (L.IsFull)
                    {
                        L.Wq = ReadMat(br);
                        L.Wk = ReadMat(br);
                        L.Wv = ReadMat(br);
                        L.Wo = ReadMat(br);
                        L.QNorm = ReadF32(br);
                        L.KNorm = ReadF32(br);
                    }
                    else
                    {
                        L.Wqkv = ReadMat(br);
                        L.Wa = ReadMat(br);
                        L.Wb = ReadMat(br);
                        L.Wz = ReadMat(br);
                        L.Conv = ReadF32(br);
                        L.ALog = ReadF32(br);
                        L.DtBias = ReadF32(br);
                        L.NormW = ReadF32(br);
                        L.Wout = ReadMat(br);
                    }
                    L.Ln2 = ReadF32(br);
                    L.Gate = ReadMat(br);
                    L.Up = ReadMat(br);
                    L.Down = ReadMat(br);
                    m.Layers[i] = L;
                }

                m.FinalNorm = ReadF32(br);
            }
            return m;
        }

        /// <summary>读一个 2D 权重为 MatW：int8 直接常驻，fp32 原样。</summary>
        static MatW ReadMat(BinaryReader br)
        {
            int ndim = br.ReadInt32();
            var dims = new int[ndim];
            long count = 1;
            for (int i = 0; i < ndim; i++) { dims[i] = br.ReadInt32(); count *= dims[i]; }
            int quant = br.ReadInt32();
            int outF = dims[0];
            int inF = (int)(count / outF);
            if (quant == 0)
            {
                return MatW.FromF32(ReadFloats(br, count), outF, inF);
            }
            if (quant == 1)
            {
                float[] scale = ReadFloats(br, outF);
                sbyte[] q = ReadSBytes(br, count);
                return MatW.FromQ8(q, scale, outF, inF);
            }
            throw new InvalidDataException("QwenModel: unsupported quant " + quant);
        }

        /// <summary>读一个 fp32 张量（norm/conv 等小张量，导出端恒 fp32）。</summary>
        static float[] ReadF32(BinaryReader br)
        {
            int ndim = br.ReadInt32();
            long count = 1;
            for (int i = 0; i < ndim; i++) count *= br.ReadInt32();
            int quant = br.ReadInt32();
            if (quant != 0) throw new InvalidDataException("QwenModel: 期望 fp32 张量");
            return ReadFloats(br, count);
        }

        static void ReadInt8Tensor(BinaryReader br, out float[] scale, out sbyte[] q8, int expectOut, int expectIn)
        {
            int ndim = br.ReadInt32();
            var dims = new int[ndim];
            long count = 1;
            for (int i = 0; i < ndim; i++) { dims[i] = br.ReadInt32(); count *= dims[i]; }
            int quant = br.ReadInt32();
            if (quant != 1 || dims[0] != expectOut || count != (long)expectOut * expectIn)
                throw new InvalidDataException("QwenModel: embed 张量与 header 不符（需 int8 导出）");
            scale = ReadFloats(br, expectOut);
            q8 = ReadSBytes(br, count);
        }

        /// <summary>分块读入 sbyte[]（避免超大临时 byte[] 的双份峰值）。</summary>
        static sbyte[] ReadSBytes(BinaryReader br, long count)
        {
            var q = new sbyte[count];
            var buf = new byte[1 << 22];
            long off = 0;
            while (off < count)
            {
                int want = (int)Math.Min(count - off, buf.Length);
                int r = br.Read(buf, 0, want);
                if (r <= 0) throw new EndOfStreamException();
                Buffer.BlockCopy(buf, 0, q, (int)off, r);
                off += r;
            }
            return q;
        }

        static float[] ReadFloats(BinaryReader br, long count)
        {
            var data = new float[count];
            byte[] raw = ReadBytesExact(br, count * 4);
            Buffer.BlockCopy(raw, 0, data, 0, (int)(count * 4));
            return data;
        }

        static byte[] ReadBytesExact(BinaryReader br, long n)
        {
            var buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = br.Read(buf, off, (int)Math.Min(n - off, 1 << 24));
                if (r <= 0) throw new EndOfStreamException();
                off += r;
            }
            return buf;
        }
    }
}
