// OnEmbedder.cs — 纯 C# 嵌入推理引擎 (SIMD 加速版)
// 针对骁龙 800/801/808/810 (ARM NEON 128-bit, Vector<float>.Count=4)
// x86 SSE (Vector<float>.Count=8) 同样受益

using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;

namespace yanshuai
{
    public class OnEmbedder
    {
        private struct ModelHeader
        {
            public uint magic, version, hidden_dim, num_layers, num_heads,
                        vocab_size, max_seq_len, pooler_dim, ffn_dim,
                        tokenizer_type, need_merge, activation, model_type,
                        embedding_type, head_size, reserved;
        }

        private ModelHeader _header;
        private float[] _embedWeight, _posWeight, _embedLnWeight, _embedLnBias;
        private float[] _poolerWeight, _poolerBias;
        private List<TransformerLayer> _layers;
        private Dictionary<string, int> _vocab;
        private bool _initialized;

        // SIMD 宽 — NEON=4, SSE=8
        private static readonly int VW = Vector<float>.Count;

        private class TransformerLayer
        {
            public float[] attn_ln_w, attn_ln_b;
            public float[] qkv_w, qkv_b;
            public float[] out_w, out_b;
            public float[] ffn_ln_w, ffn_ln_b;
            public float[] ffn1_w, ffn1_b;
            public float[] ffn2_w, ffn2_b;
        }

        // ═══════════════════════ 公共接口 ═══════════════════════

        public bool Initialize() { _initialized = true; return true; }

        /// <summary>模型是否已成功加载（Agent 兼容 API）。</summary>
        public bool IsLoaded => _initialized && _embedWeight != null;

        public bool LoadModel(string modelPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(modelPath);
                int offset = 0;

                _header = ReadHeader(data, ref offset);
                if (_header.magic != 0x454D4245)
                    return false;

                uint vocabSize = BitConverter.ToUInt32(data, offset); offset += 4;
                _vocab = new Dictionary<string, int>();
                for (int i = 0; i < vocabSize; i++)
                {
                    uint tlen = BitConverter.ToUInt32(data, offset); offset += 4;
                    string token = System.Text.Encoding.UTF8.GetString(data, offset, (int)tlen); offset += (int)tlen;
                    int tid = (int)BitConverter.ToUInt32(data, offset); offset += 4;
                    _vocab[token] = tid;
                }

                uint merges = BitConverter.ToUInt32(data, offset); offset += 4;
                for (int i = 0; i < merges; i++)
                { uint mlen = BitConverter.ToUInt32(data, offset); offset += 4; offset += (int)mlen; }

                offset = ReadTensor(data, offset, out _embedWeight);
                offset = ReadTensor(data, offset, out _posWeight);
                offset = ReadTensor(data, offset, out _embedLnWeight);
                offset = ReadTensor(data, offset, out _embedLnBias);

                _layers = new List<TransformerLayer>();
                for (int l = 0; l < _header.num_layers; l++)
                {
                    var ly = new TransformerLayer();
                    offset = ReadTensor(data, offset, out ly.attn_ln_w);
                    offset = ReadTensor(data, offset, out ly.attn_ln_b);
                    offset = ReadTensor(data, offset, out ly.qkv_w);
                    offset = ReadTensor(data, offset, out ly.qkv_b);
                    offset = ReadTensor(data, offset, out ly.out_w);
                    offset = ReadTensor(data, offset, out ly.out_b);
                    offset = ReadTensor(data, offset, out ly.ffn_ln_w);
                    offset = ReadTensor(data, offset, out ly.ffn_ln_b);
                    offset = ReadTensor(data, offset, out ly.ffn1_w);
                    offset = ReadTensor(data, offset, out ly.ffn1_b);
                    offset = ReadTensor(data, offset, out ly.ffn2_w);
                    offset = ReadTensor(data, offset, out ly.ffn2_b);
                    _layers.Add(ly);
                }

                uint hasPooler = BitConverter.ToUInt32(data, offset); offset += 4;
                if (hasPooler != 0)
                {
                    offset = ReadTensor(data, offset, out _poolerWeight);
                    offset = ReadTensor(data, offset, out _poolerBias);
                }

                _initialized = true;
                return true;
            }
            catch { return false; }
        }

        public float[] Encode(string text)
        {
            if (!_initialized || _embedWeight == null)
                return new float[(int)_header.hidden_dim];

            var ids = Tokenize(text);
            if (ids.Count == 0) return new float[(int)_header.hidden_dim];

            int seqLen = ids.Count, H = (int)_header.hidden_dim;

            // 双缓冲：预分配两片缓冲区，逐层交换，避免每层 new float[]
            float[] bufA = new float[seqLen * H];
            float[] bufB = new float[seqLen * H];
            EmbeddingLookup(ids, bufA, H);

            for (int l = 0; l < _header.num_layers; l++)
            {
                Array.Clear(bufB, 0, bufB.Length);
                TransformerForward(l, bufA, bufB, seqLen, H);
                var tmp = bufA; bufA = bufB; bufB = tmp;
            }

            float[] result = new float[H];
            MeanPool(bufA, seqLen, H, result);
            L2Normalize(result);
            return result;
        }

        /// <summary>If UseSubAgent is enabled and SubAgentUrl is set, returns a temporary
        /// ApiProfile using sub-agent config. Otherwise falls back to the default profile.</summary>
#if ROLEPLAY
        public static ApiProfile GetEmbedProfile()
        {
            var sub = AppSettings.GetSubAgentProfile();
            if (sub != null) return sub;
            return DataManager.GetProfileForConversation(null);
        }
#endif

        /// <summary>SIMD 余弦相似度</summary>
        public static double CosineSim(float[] a, float[] b)
        {
            int len = a.Length, i = 0, vEnd = len - VW;
            var dot = Vector<float>.Zero;
            var sqA = Vector<float>.Zero;
            var sqB = Vector<float>.Zero;
            for (; i <= vEnd; i += VW)
            {
                var va = new Vector<float>(a, i);
                var vb = new Vector<float>(b, i);
                dot += va * vb;
                sqA += va * va;
                sqB += vb * vb;
            }
            double d = 0, na = 0, nb = 0;
            for (int k = 0; k < VW; k++) { d += dot[k]; na += sqA[k]; nb += sqB[k]; }
            for (; i < len; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            double denom = Math.Sqrt(na) * Math.Sqrt(nb);
            return denom > 0 ? d / denom : 0;
        }

        // ═══════════════════════ SIMD 核心 ═══════════════════════

        /// <summary>LayerNorm stats（SIMD 加速 mean/std）</summary>
        private static void VectorStats(float[] arr, int start, int count, out float mean, out float sumSq)
        {
            var sumVec = Vector<float>.Zero;
            var sqVec  = Vector<float>.Zero;
            int i = 0, vEnd = count - VW;
            for (; i <= vEnd; i += VW)
            {
                var v = new Vector<float>(arr, start + i);
                sumVec += v;
                sqVec  += v * v;
            }
            float sum = 0, sq = 0;
            for (int k = 0; k < VW; k++) { sum += sumVec[k]; sq += sqVec[k]; }
            for (; i < count; i++)
            {
                float v = arr[start + i]; sum += v; sq += v * v;
            }
            mean = sum / count;
            sumSq = sq;
        }

        /// <summary>向量-矩阵乘法：dst[j] = bias[j] + sum_k( src[k] × W[j*K + k] )
        /// W 是 (M × K) 按行主序存储，M=ffn_dim 或 hidden_dim</summary>
        private static void VecMatMul(float[] src, float[] W, float[] bias, float[] dst, int M, int K)
        {
            for (int j = 0; j < M; j++)
            {
                float s = bias[j];
                int rowOff = j * K;
                int k = 0, vEnd = K - VW;
                var acc = Vector<float>.Zero;
                for (; k <= vEnd; k += VW)
                {
                    acc += new Vector<float>(src, k) * new Vector<float>(W, rowOff + k);
                }
                for (int t = 0; t < VW; t++) s += acc[t];
                for (; k < K; k++) s += src[k] * W[rowOff + k];
                dst[j] = s;
            }
        }

        private static void GeLUInPlace(float[] v)
        {
            float a = 0.044715f, r = 0.7978845608f; // sqrt(2/PI)
            for (int i = 0; i < v.Length; i++)
            {
                float x = v[i], x3 = x * x * x;
                v[i] = 0.5f * x * (1f + (float)Math.Tanh(r * (x + a * x3)));
            }
        }

        // ═══════════════════════ 推理流水线 ═══════════════════════

        private void EmbeddingLookup(List<int> ids, float[] output, int H)
        {
            for (int i = 0; i < ids.Count && i < _header.max_seq_len; i++)
            {
                int tid = ids[i], pos = i, outOff = i * H;
                int embOff = (tid >= 0 && tid < _header.vocab_size) ? tid * H : -1;
                int posOff = pos * H, vEnd = H - VW;

                if (embOff >= 0)
                    Array.Copy(_embedWeight, embOff, output, outOff, H);
                else
                    Array.Clear(output, outOff, H);

                // Add position encoding (SIMD)
                if (posOff + H <= _posWeight.Length)
                {
                    int j = 0;
                    for (; j <= vEnd; j += VW)
                        (new Vector<float>(output, outOff + j) + new Vector<float>(_posWeight, posOff + j))
                            .CopyTo(output, outOff + j);
                    for (; j < H; j++)
                        output[outOff + j] += _posWeight[posOff + j];
                }
                else
                {
                    // 尾部（超过 posWeight 长度的位置取0）
                    int valid = _posWeight.Length - posOff;
                    for (int j = 0; j < valid; j++) output[outOff + j] += _posWeight[posOff + j];
                }

                // LayerNorm (SIMD stats)
                VectorStats(output, outOff, H, out float mean, out float sqr);
                float invStd = 1f / (float)Math.Sqrt(sqr / H - mean * mean + 1e-12f);
                int jj = 0;
                for (; jj <= vEnd; jj += VW)
                {
                    var vec = new Vector<float>(output, outOff + jj);
                    vec = (vec - new Vector<float>(mean)) * new Vector<float>(invStd)
                        * new Vector<float>(_embedLnWeight, jj)
                        + new Vector<float>(_embedLnBias, jj);
                    vec.CopyTo(output, outOff + jj);
                }
                for (; jj < H; jj++)
                    output[outOff + jj] = (output[outOff + jj] - mean) * invStd * _embedLnWeight[jj] + _embedLnBias[jj];
            }
        }

        private void TransformerForward(int layerIdx, float[] input, float[] output, int seqLen, int H)
        {
            var ly = _layers[layerIdx];
            int F = (int)_header.ffn_dim;

            // 预分配临时数组，每层只分配一次，各位置复用
            float[] ln = new float[H];
            float[] ffnH = new float[F];
            float[] ffnOut = new float[H];
            int vEnd = H - VW;

            for (int i = 0; i < seqLen; i++)
            {
                int off = i * H;

                // Pre-LN with SIMD stats
                VectorStats(input, off, H, out float mean, out float sqr);
                float invStd = 1f / (float)Math.Sqrt(sqr / H - mean * mean + 1e-12f);
                int j = 0;
                for (; j <= vEnd; j += VW)
                {
                    var v = new Vector<float>(input, off + j);
                    v = (v - new Vector<float>(mean)) * new Vector<float>(invStd)
                        * new Vector<float>(ly.attn_ln_w, j)
                        + new Vector<float>(ly.attn_ln_b, j);
                    v.CopyTo(ln, j);
                }
                for (; j < H; j++)
                    ln[j] = (input[off + j] - mean) * invStd * ly.attn_ln_w[j] + ly.attn_ln_b[j];

                // FFN → ffnDim (SIMD VecMatMul)
                VecMatMul(ln, ly.ffn1_w, ly.ffn1_b, ffnH, F, H);
                GeLUInPlace(ffnH);

                // FFN → hiddenDim
                VecMatMul(ffnH, ly.ffn2_w, ly.ffn2_b, ffnOut, H, F);

                // Residual
                for (j = 0; j < H; j++)
                    output[off + j] = input[off + j] + ffnOut[j];
            }
        }

        private static void MeanPool(float[] hidden, int seqLen, int H, float[] result)
        {
            // 行优先遍历：按 i 循环，hidden[i*H .. i*H+H) 连续访存
            // 替换原跨步 stride=H 的 gather 模式，L1 缓存利用率大幅提高
            int vEnd = H - VW;
            for (int i = 0; i < seqLen; i++)
            {
                int off = i * H;
                int j = 0;
                for (; j <= vEnd; j += VW)
                    (new Vector<float>(hidden, off + j) + new Vector<float>(result, j)).CopyTo(result, j);
                for (; j < H; j++)
                    result[j] += hidden[off + j];
            }

            float inv = 1f / seqLen;
            int jj = 0;
            for (; jj <= vEnd; jj += VW)
                (new Vector<float>(result, jj) * new Vector<float>(inv)).CopyTo(result, jj);
            for (; jj < H; jj++)
                result[jj] *= inv;
        }

        private static void L2Normalize(float[] vec)
        {
            float sumSq = 0; int i = 0, vEnd = vec.Length - VW;
            var sqAcc = Vector<float>.Zero;
            for (; i <= vEnd; i += VW) sqAcc += new Vector<float>(vec, i) * new Vector<float>(vec, i);
            for (int k = 0; k < VW; k++) sumSq += sqAcc[k];
            for (; i < vec.Length; i++) sumSq += vec[i] * vec[i];
            float inv = 1f / ((float)Math.Sqrt(sumSq) + 1e-12f);
            for (i = 0; i <= vEnd; i += VW)
            { var v = new Vector<float>(vec, i) * new Vector<float>(inv); v.CopyTo(vec, i); }
            for (; i < vec.Length; i++) vec[i] *= inv;
        }

        // ═══════════════════════ Tokenizer ═══════════════════════

        private List<int> Tokenize(string text)
        {
            var ids = new List<int>();
            if (_vocab == null) return ids;
            ids.Add(101);
            var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in words)
            {
                if (_vocab.TryGetValue(w, out int id)) ids.Add(id);
                else
                {
                    foreach (char c in w)
                    {
                        string s = c.ToString();
                        ids.Add(_vocab.TryGetValue(s, out int cid) ? cid : 100);
                    }
                }
            }
            ids.Add(102);
            // max_seq_len 来自模型文件头（不可信）：<1 时跳过截断，避免 RemoveRange(-1,…) 抛异常
            if (_header.max_seq_len >= 1 && ids.Count > _header.max_seq_len)
            {
                ids.RemoveRange((int)_header.max_seq_len - 1, ids.Count - (int)_header.max_seq_len);
                ids[ids.Count - 1] = 102;
            }
            return ids;
        }

        // ═══════════════════════ 二进制读取 ═══════════════════════

        private static ModelHeader ReadHeader(byte[] data, ref int off)
        {
            ModelHeader h;
            h.magic = LE32(data, ref off); h.version = LE32(data, ref off);
            h.hidden_dim = LE32(data, ref off); h.num_layers = LE32(data, ref off);
            h.num_heads = LE32(data, ref off); h.vocab_size = LE32(data, ref off);
            h.max_seq_len = LE32(data, ref off); h.pooler_dim = LE32(data, ref off);
            h.ffn_dim = LE32(data, ref off); h.tokenizer_type = LE32(data, ref off);
            h.need_merge = LE32(data, ref off); h.activation = LE32(data, ref off);
            h.model_type = LE32(data, ref off); h.embedding_type = LE32(data, ref off);
            h.head_size = LE32(data, ref off); h.reserved = LE32(data, ref off);
            return h;
        }

        private static uint LE32(byte[] d, ref int o) { uint v = BitConverter.ToUInt32(d, o); o += 4; return v; }

        private static int ReadTensor(byte[] data, int offset, out float[] values)
        {
            int dims = (int)BitConverter.ToUInt32(data, offset); offset += 4;
            int total = 1;
            for (int i = 0; i < dims; i++) { uint d = BitConverter.ToUInt32(data, offset); offset += 4; total *= (int)d; }
            offset += dims * 4;
            values = new float[total];
            Buffer.BlockCopy(data, offset, values, 0, total * 4);
            return offset + total * 4;
        }
    }
}
