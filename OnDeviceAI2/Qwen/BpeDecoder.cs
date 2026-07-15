// BpeDecoder.cs — byte-level BPE 解码（id → 文本）。
// GPT-2 式 byte↔unicode 映射：token 串里的每个字符映回原始字节，再按 UTF-8 组装。
// 编码（文本 → id）在 P3 实现；解码先行，供贪心续写演示。
using System;
using System.Collections.Generic;
using System.Text;

namespace Yanshuai.Qwen
{
    public sealed class BpeDecoder
    {
        readonly string[] vocab;
        static readonly Dictionary<char, byte> U2B = BuildUnicodeToByte();

        public BpeDecoder(string[] vocabTable)
        {
            vocab = vocabTable;
        }

        /// <summary>GPT-2 bytes_to_unicode 的逆映射。</summary>
        static Dictionary<char, byte> BuildUnicodeToByte()
        {
            var printable = new bool[256];
            for (int b = '!'; b <= '~'; b++) printable[b] = true;
            for (int b = 0xA1; b <= 0xAC; b++) printable[b] = true;
            for (int b = 0xAE; b <= 0xFF; b++) printable[b] = true;

            var map = new Dictionary<char, byte>(256);
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                if (printable[b]) map[(char)b] = (byte)b;
                else map[(char)(256 + n++)] = (byte)b;
            }
            return map;
        }

        public string Decode(IEnumerable<int> ids)
        {
            var bytes = new List<byte>(64);
            var sb = new StringBuilder();
            foreach (int id in ids)
            {
                string tok = (id >= 0 && id < vocab.Length) ? vocab[id] : null;
                if (tok == null) continue;
                if (tok.StartsWith("<|", StringComparison.Ordinal))
                {
                    Flush(bytes, sb);
                    sb.Append(tok);          // 特殊 token 原样展示
                    continue;
                }
                foreach (char c in tok)
                {
                    byte b;
                    if (U2B.TryGetValue(c, out b)) bytes.Add(b);
                    else { Flush(bytes, sb); sb.Append(c); }
                }
            }
            Flush(bytes, sb);
            return sb.ToString();
        }

        static void Flush(List<byte> bytes, StringBuilder sb)
        {
            if (bytes.Count == 0) return;
            sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
            bytes.Clear();
        }
    }
}
