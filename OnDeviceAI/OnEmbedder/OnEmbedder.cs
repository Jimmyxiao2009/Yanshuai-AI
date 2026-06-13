// OnEmbedder.cs - 纯 C# 嵌入推理引擎
// 读取 .embmodel 文件并在 CPU 上执行推理
// 替代 C++/D3D 方案（因本机无 C++ 编译环境）

using System;
using System.IO;
using System.Collections.Generic;

namespace yanshuai
{
    /// <summary>
    /// 嵌入引擎 — 读取 .embmodel 并在 CPU 执行推理
    /// </summary>
    public class OnEmbedder
    {
        // ══════════════════════════════════════════════════════════════
        // .embmodel 文件头
        // ══════════════════════════════════════════════════════════════

        private struct ModelHeader
        {
            public uint magic;           // 0x454D4245
            public uint version;
            public uint hidden_dim;
            public uint num_layers;
            public uint num_heads;
            public uint vocab_size;
            public uint max_seq_len;
            public uint pooler_dim;
            public uint ffn_dim;
            public uint tokenizer_type;
            public uint need_merge;
            public uint activation;
            public uint model_type;
            public uint embedding_type;
            public uint head_size;
            public uint reserved;
        }

        // ══════════════════════════════════════════════════════════════
        // 模型数据
        // ══════════════════════════════════════════════════════════════

        private ModelHeader _header;
        private float[] _embedWeight;     // [vocab_size, hidden_dim]
        private float[] _posWeight;       // [max_pos, hidden_dim]
        private float[] _embedLnWeight;   // [hidden_dim]
        private float[] _embedLnBias;     // [hidden_dim]

        private List<TransformerLayer> _layers;
        private float[] _poolerWeight;
        private float[] _poolerBias;

        // Tokenizer 词表
        private Dictionary<string, int> _vocab;

        private bool _initialized = false;

        private class TransformerLayer
        {
            public float[] attn_ln_w, attn_ln_b;
            public float[] qkv_w, qkv_b;
            public float[] out_w, out_b;
            public float[] ffn_ln_w, ffn_ln_b;
            public float[] ffn1_w, ffn1_b;
            public float[] ffn2_w, ffn2_b;
        }

        // ══════════════════════════════════════════════════════════════
        // 公共接口
        // ══════════════════════════════════════════════════════════════

        public bool Initialize()
        {
            _initialized = true;
            return true;
        }

        public bool LoadModel(string modelPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(modelPath);
                int offset = 0;

                // 读取 header
                _header = ReadHeader(data, ref offset);
                if (_header.magic != 0x454D4245)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnEmbedder] Invalid magic: 0x{_header.magic:X8}");
                    return false;
                }

                // 读取 tokenizer 词表
                uint vocabSize = BitConverter.ToUInt32(data, offset); offset += 4;
                _vocab = new Dictionary<string, int>();
                for (int i = 0; i < vocabSize; i++)
                {
                    uint tlen = BitConverter.ToUInt32(data, offset); offset += 4;
                    string token = System.Text.Encoding.UTF8.GetString(data, offset, (int)tlen); offset += (int)tlen;
                    int tid = (int)BitConverter.ToUInt32(data, offset); offset += 4;
                    _vocab[token] = tid;
                }

                // 跳过 merge 规则
                uint merges = BitConverter.ToUInt32(data, offset); offset += 4;
                for (int i = 0; i < merges; i++)
                {
                    uint mlen = BitConverter.ToUInt32(data, offset); offset += 4;
                    offset += (int)mlen;
                }

                // 读取嵌入权重
                offset = ReadTensor(data, offset, out _embedWeight);
                offset = ReadTensor(data, offset, out _posWeight);
                offset = ReadTensor(data, offset, out _embedLnWeight);
                offset = ReadTensor(data, offset, out _embedLnBias);

                // 读取 Transformer 层
                _layers = new List<TransformerLayer>();
                for (int l = 0; l < _header.num_layers; l++)
                {
                    var layer = new TransformerLayer();
                    offset = ReadTensor(data, offset, out layer.attn_ln_w);
                    offset = ReadTensor(data, offset, out layer.attn_ln_b);
                    offset = ReadTensor(data, offset, out layer.qkv_w);
                    offset = ReadTensor(data, offset, out layer.qkv_b);
                    offset = ReadTensor(data, offset, out layer.out_w);
                    offset = ReadTensor(data, offset, out layer.out_b);
                    offset = ReadTensor(data, offset, out layer.ffn_ln_w);
                    offset = ReadTensor(data, offset, out layer.ffn_ln_b);
                    offset = ReadTensor(data, offset, out layer.ffn1_w);
                    offset = ReadTensor(data, offset, out layer.ffn1_b);
                    offset = ReadTensor(data, offset, out layer.ffn2_w);
                    offset = ReadTensor(data, offset, out layer.ffn2_b);
                    _layers.Add(layer);
                }

                // Pooler
                uint hasPooler = BitConverter.ToUInt32(data, offset); offset += 4;
                if (hasPooler != 0)
                {
                    offset = ReadTensor(data, offset, out _poolerWeight);
                    offset = ReadTensor(data, offset, out _poolerBias);
                }

                System.Diagnostics.Debug.WriteLine($"[OnEmbedder] 模型加载完成: {_header.hidden_dim}d, {_header.num_layers}层");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnEmbedder] 加载失败: {ex.Message}");
                return false;
            }
        }

        public float[] Encode(string text)
        {
            if (!_initialized || _embedWeight == null)
                return new float[(int)_header.hidden_dim];

            // 1. Tokenize
            var ids = Tokenize(text);
            if (ids.Count == 0)
                return new float[(int)_header.hidden_dim];

            int seqLen = ids.Count;
            int hiddenDim = (int)_header.hidden_dim;

            // 2. Embedding Lookup + Position + LayerNorm
            float[] hidden = new float[seqLen * hiddenDim];
            EmbeddingLookup(ids, hidden);

            // 3. Transformer layers (CPU 简化版 — 后续可优化)
            for (int l = 0; l < _header.num_layers; l++)
            {
                float[] nextHidden = new float[seqLen * hiddenDim];
                TransformerForward(l, hidden, nextHidden);
                hidden = nextHidden;
            }

            // 4. Mean Pooling + L2 normalize
            float[] result = new float[hiddenDim];
            MeanPool(hidden, seqLen, result);
            L2Normalize(result);

            return result;
        }

        // ══════════════════════════════════════════════════════════════
        // 内部方法
        // ══════════════════════════════════════════════════════════════

        private void EmbeddingLookup(List<int> ids, float[] output)
        {
            int hiddenDim = (int)_header.hidden_dim;
            for (int i = 0; i < ids.Count && i < _header.max_seq_len; i++)
            {
                int tid = ids[i];
                int posId = i;
                for (int j = 0; j < hiddenDim; j++)
                {
                    float tokVal = (tid >= 0 && tid < _header.vocab_size) ?
                        _embedWeight[tid * hiddenDim + j] : 0f;
                    float posVal = (posId * hiddenDim + j < _posWeight.Length) ?
                        _posWeight[posId * hiddenDim + j] : 0f;
                    float val = tokVal + posVal;

                    // LayerNorm
                    float mean = 0f, sqSum = 0f;
                    // Compute stats for this position
                    // (simplified: pre-compute per position)
                    output[i * hiddenDim + j] = val;
                }

                // LayerNorm for this position
                float sum = 0f, sumSq = 0f;
                for (int j = 0; j < hiddenDim; j++)
                {
                    float v = output[i * hiddenDim + j];
                    sum += v;
                    sumSq += v * v;
                }
                float m = sum / hiddenDim;
                float invStd = 1f / (float)Math.Sqrt(sumSq / hiddenDim - m * m + 1e-12f);
                for (int j = 0; j < hiddenDim; j++)
                {
                    float v = output[i * hiddenDim + j];
                    output[i * hiddenDim + j] = (v - m) * invStd * _embedLnWeight[j] + _embedLnBias[j];
                }
            }
        }

        private void TransformerForward(int layerIdx, float[] input, float[] output)
        {
            var layer = _layers[layerIdx];
            int seqLen = input.Length / (int)_header.hidden_dim;
            int hiddenDim = (int)_header.hidden_dim;

            // 简化版: LayerNorm + 平均池化作为 Transformer 替代
            // 完整版需要实现 MHA + FFN
            for (int i = 0; i < seqLen; i++)
            {
                int baseIdx = i * hiddenDim;

                // Pre-LN
                float sum = 0, sumSq = 0;
                for (int j = 0; j < hiddenDim; j++)
                {
                    float v = input[baseIdx + j];
                    sum += v;
                    sumSq += v * v;
                }
                float mean = sum / hiddenDim;
                float invStd = 1f / (float)Math.Sqrt(sumSq / hiddenDim - mean * mean + 1e-12f);

                float[] normed = new float[hiddenDim];
                for (int j = 0; j < hiddenDim; j++)
                {
                    normed[j] = (input[baseIdx + j] - mean) * invStd * layer.attn_ln_w[j] + layer.attn_ln_b[j];
                }

                // Simplified self-attention (identity — will be GPU shader)
                // FFN: GeLU( x @ W1 + b1 ) @ W2 + b2
                int ffnDim = (int)_header.ffn_dim;
                float[] ffnIn = new float[ffnDim];
                for (int j = 0; j < ffnDim; j++)
                {
                    float s = layer.ffn1_b[j];
                    for (int k = 0; k < hiddenDim; k++)
                        s += normed[k] * layer.ffn1_w[j * hiddenDim + k];
                    ffnIn[j] = Gelu(s);
                }
                for (int j = 0; j < hiddenDim; j++)
                {
                    float s = layer.ffn2_b[j];
                    for (int k = 0; k < ffnDim; k++)
                        s += ffnIn[k] * layer.ffn2_w[j * ffnDim + k];
                    output[baseIdx + j] = input[baseIdx + j] + s;
                }
            }
        }

        private const float PI = 3.14159265358979323846f;

        private static float Gelu(float x)
        {
            return 0.5f * x * (1f + (float)Math.Tanh(
                Math.Sqrt(2f / PI) * (x + 0.044715f * x * x * x)));
        }

        private void MeanPool(float[] hidden, int seqLen, float[] result)
        {
            int hiddenDim = (int)_header.hidden_dim;
            for (int j = 0; j < hiddenDim; j++)
            {
                float sum = 0;
                for (int i = 0; i < seqLen; i++)
                    sum += hidden[i * hiddenDim + j];
                result[j] = sum / seqLen;
            }
        }

        private void L2Normalize(float[] vec)
        {
            float sumSq = 0;
            for (int i = 0; i < vec.Length; i++)
                sumSq += vec[i] * vec[i];
            float invNorm = 1f / ((float)Math.Sqrt(sumSq) + 1e-12f);
            for (int i = 0; i < vec.Length; i++)
                vec[i] *= invNorm;
        }

        private List<int> Tokenize(string text)
        {
            var ids = new List<int>();
            if (_vocab == null) return ids;

            ids.Add(101); // [CLS]

            var words = text.Split(new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (_vocab.TryGetValue(word, out int id))
                {
                    ids.Add(id);
                }
                else
                {
                    // 单字 fallback
                    foreach (char c in word)
                    {
                        string s = c.ToString();
                        if (_vocab.TryGetValue(s, out int cid))
                            ids.Add(cid);
                        else
                            ids.Add(100); // [UNK]
                    }
                }
            }

            ids.Add(102); // [SEP]

            // Truncate
            if (ids.Count > _header.max_seq_len)
            {
                ids.RemoveRange((int)_header.max_seq_len - 1, ids.Count - (int)_header.max_seq_len);
                ids[ids.Count - 1] = 102;
            }

            return ids;
        }

        // ══════════════════════════════════════════════════════════════
        // 二进制读取帮助
        // ══════════════════════════════════════════════════════════════

        private static ModelHeader ReadHeader(byte[] data, ref int offset)
        {
            ModelHeader h = new ModelHeader();
            h.magic = BitConverter.ToUInt32(data, offset); offset += 4;
            h.version = BitConverter.ToUInt32(data, offset); offset += 4;
            h.hidden_dim = BitConverter.ToUInt32(data, offset); offset += 4;
            h.num_layers = BitConverter.ToUInt32(data, offset); offset += 4;
            h.num_heads = BitConverter.ToUInt32(data, offset); offset += 4;
            h.vocab_size = BitConverter.ToUInt32(data, offset); offset += 4;
            h.max_seq_len = BitConverter.ToUInt32(data, offset); offset += 4;
            h.pooler_dim = BitConverter.ToUInt32(data, offset); offset += 4;
            h.ffn_dim = BitConverter.ToUInt32(data, offset); offset += 4;
            h.tokenizer_type = BitConverter.ToUInt32(data, offset); offset += 4;
            h.need_merge = BitConverter.ToUInt32(data, offset); offset += 4;
            h.activation = BitConverter.ToUInt32(data, offset); offset += 4;
            h.model_type = BitConverter.ToUInt32(data, offset); offset += 4;
            h.embedding_type = BitConverter.ToUInt32(data, offset); offset += 4;
            h.head_size = BitConverter.ToUInt32(data, offset); offset += 4;
            h.reserved = BitConverter.ToUInt32(data, offset); offset += 4;
            return h;
        }

        private static int ReadTensor(byte[] data, int offset, out float[] values)
        {
            int dims = (int)BitConverter.ToUInt32(data, offset); offset += 4;
            int total = 1;
            for (int i = 0; i < dims; i++)
            {
                uint d = BitConverter.ToUInt32(data, offset); offset += 4;
                total *= (int)d;
            }
            // Skip strides
            offset += dims * 4;

            values = new float[total];
            Buffer.BlockCopy(data, offset, values, 0, total * 4);
            offset += total * 4;
            return offset;
        }
    }
}
