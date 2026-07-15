// BpeTokenizer.cs — byte-level BPE 编码（文本 → token id），P3。
// 与 HF fast tokenizer 对齐：GPT-4 式预切分正则 → UTF-8 字节 → byte-unicode 映射
// → 按 merges 优先级贪心合并 → vocab 查表。
// 预切分正则取自 qwen3_5 tokenizer.json（pre_tokenizer.Split.pattern）。
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Yanshuai.Qwen
{
    public sealed class BpeTokenizer
    {
        // qwen3_5 tokenizer.json 原样正则（.NET 语法兼容）
        static readonly Regex Pre = new Regex(
            "(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\\r\\n\\p{L}\\p{N}]?[\\p{L}\\p{M}]+|\\p{N}| ?[^\\s\\p{L}\\p{M}\\p{N}]+[\\r\\n]*|\\s*[\\r\\n]+|\\s+(?!\\S)|\\s+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        readonly Dictionary<string, int> vocab;        // token 串 → id
        readonly Dictionary<string, int> mergeRank;    // "a b" → 优先级（越小越先合并）
        readonly Regex specialSplit;                   // 特殊 token 原子匹配（<|im_start|>、<think> 等）
        readonly Dictionary<string, int> specialIds;
        static readonly char[] ByteToChar = BuildByteToChar();

        /// <param name="firstSpecialId">此 id 起（含）的词表项视为特殊 token，编码时原子匹配不进 BPE。</param>
        public BpeTokenizer(string[] vocabTable, string[] merges, int firstSpecialId = -1)
        {
            vocab = new Dictionary<string, int>(vocabTable.Length, StringComparer.Ordinal);
            for (int i = 0; i < vocabTable.Length; i++)
            {
                if (vocabTable[i] != null && !vocab.ContainsKey(vocabTable[i]))
                    vocab[vocabTable[i]] = i;
            }
            mergeRank = new Dictionary<string, int>(merges.Length, StringComparer.Ordinal);
            for (int i = 0; i < merges.Length; i++)
            {
                if (!mergeRank.ContainsKey(merges[i]))
                    mergeRank[merges[i]] = i;
            }

            if (firstSpecialId >= 0)
            {
                specialIds = new Dictionary<string, int>(StringComparer.Ordinal);
                var parts = new List<string>();
                for (int i = firstSpecialId; i < vocabTable.Length; i++)
                {
                    if (vocabTable[i] == null) continue;
                    specialIds[vocabTable[i]] = i;
                    parts.Add(Regex.Escape(vocabTable[i]));
                }
                if (parts.Count > 0)
                {
                    parts.Sort((a, b) => b.Length.CompareTo(a.Length));   // 长者优先
                    specialSplit = new Regex(string.Join("|", parts), RegexOptions.Compiled);
                }
            }
        }

        /// <summary>GPT-2 bytes_to_unicode 正映射。</summary>
        static char[] BuildByteToChar()
        {
            var printable = new bool[256];
            for (int b = '!'; b <= '~'; b++) printable[b] = true;
            for (int b = 0xA1; b <= 0xAC; b++) printable[b] = true;
            for (int b = 0xAE; b <= 0xFF; b++) printable[b] = true;
            var map = new char[256];
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                map[b] = printable[b] ? (char)b : (char)(256 + n++);
            }
            return map;
        }

        public List<int> Encode(string text)
        {
            var ids = new List<int>(text.Length / 2 + 4);
            if (specialSplit == null)
            {
                EncodePlain(text, ids);
                return ids;
            }
            // 特殊 token 原子切分：先按特殊 token 分段，普通段落走 BPE
            int pos = 0;
            foreach (Match sm in specialSplit.Matches(text))
            {
                if (sm.Index > pos) EncodePlain(text.Substring(pos, sm.Index - pos), ids);
                ids.Add(specialIds[sm.Value]);
                pos = sm.Index + sm.Length;
            }
            if (pos < text.Length) EncodePlain(text.Substring(pos), ids);
            return ids;
        }

        void EncodePlain(string text, List<int> ids)
        {
            foreach (Match mt in Pre.Matches(text))
            {
                if (mt.Length == 0) continue;
                EncodePiece(mt.Value, ids);
            }
        }

        void EncodePiece(string piece, List<int> ids)
        {
            // UTF-8 字节 → byte-unicode 符号序列
            byte[] bytes = Encoding.UTF8.GetBytes(piece);
            var word = new List<string>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
                word.Add(ByteToChar[bytes[i]].ToString());

            // 贪心 BPE：反复合并优先级最高（rank 最小）的相邻对
            while (word.Count > 1)
            {
                int bestRank = int.MaxValue, bestPos = -1;
                for (int i = 0; i < word.Count - 1; i++)
                {
                    int rank;
                    if (mergeRank.TryGetValue(word[i] + " " + word[i + 1], out rank) && rank < bestRank)
                    {
                        bestRank = rank;
                        bestPos = i;
                    }
                }
                if (bestPos < 0) break;
                word[bestPos] = word[bestPos] + word[bestPos + 1];
                word.RemoveAt(bestPos + 1);
            }

            foreach (string sym in word)
            {
                int id;
                if (vocab.TryGetValue(sym, out id))
                {
                    ids.Add(id);
                }
                else
                {
                    // 极端兜底：逐字节回退（正常 byte-level 词表必含全部单字节）
                    foreach (char c in sym)
                    {
                        if (vocab.TryGetValue(c.ToString(), out id)) ids.Add(id);
                    }
                }
            }
        }
    }
}
