using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;

namespace yanshuai
{
    /// <summary>带向量的记忆条目</summary>
    [DataContract]
    public class MemoryItem
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Text { get; set; }
        /// <summary>嵌入向量（可为空，待异步生成）</summary>
        [DataMember] public float[] Embedding { get; set; }
        [DataMember] public DateTime Timestamp { get; set; } = DateTime.Now;
        /// <summary>重要性 0.0~1.0，由摘要模型打分</summary>
        [DataMember] public double Importance { get; set; } = 0.5;
        /// <summary>来源对话ID</summary>
        [DataMember] public string SourceConversationId { get; set; }
        /// <summary>访问次数（用于LRU淘汰）</summary>
        [DataMember] public int AccessCount { get; set; } = 0;
        /// <summary>标签分类（如 "fact", "preference", "event"）</summary>
        [DataMember] public string Category { get; set; } = "general";
    }

    /// <summary>检索结果</summary>
    public class SearchResult
    {
        public MemoryItem Item { get; set; }
        public double Score { get; set; }
    }

    /// <summary>记忆向量库服务</summary>
    public static class MemoryStore
    {
        private static List<MemoryItem> _items = new List<MemoryItem>();
        private const string FileName = "memory_store.json";
        private static readonly object _lock = new object();

        public static IReadOnlyList<MemoryItem> Items
        {
            get { lock (_lock) return _items.ToList(); }
        }

        public static async Task LoadAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(FileName);
                using (var s = await file.OpenStreamForReadAsync())
                {
                    var ser = new DataContractJsonSerializer(typeof(List<MemoryItem>));
                    lock (_lock) _items = (List<MemoryItem>)ser.ReadObject(s) ?? new List<MemoryItem>();
                }
            }
            catch { lock (_lock) _items = new List<MemoryItem>(); }
        }

        public static async Task SaveAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
                using (var s = await file.OpenStreamForWriteAsync())
                {
                    lock (_lock)
                    {
                        var ser = new DataContractJsonSerializer(typeof(List<MemoryItem>));
                        ser.WriteObject(s, _items);
                    }
                }
            }
            catch { }
        }

        public static void Add(MemoryItem item)
        {
            lock (_lock)
            {
                _items.Add(item);
                // 上限10000条，超了淘汰最不重要的
                if (_items.Count > 10000)
                {
                    _items = _items.OrderBy(i => i.Importance * (1 + Math.Log10(i.AccessCount + 1)))
                                   .Skip(_items.Count - 8000).ToList();
                }
            }
        }

        public static void Remove(string id)
        {
            lock (_lock) _items.RemoveAll(i => i.Id == id);
        }

        /// <summary>两阶段语义检索：Stage 1 嵌入粗排 → Stage 2 混合评分</summary>
        public static List<SearchResult> Search(float[] queryEmbedding, int topK = 5, double minScore = 0.3)
        {
            if (queryEmbedding == null || queryEmbedding.Length == 0)
                return new List<SearchResult>();

            lock (_lock)
            {
                var candidates = _items
                    .Where(i => i.Embedding != null && i.Embedding.Length == queryEmbedding.Length)
                    .Select(i => new SearchResult
                    {
                        Item = i,
                        Score = OnEmbedder.CosineSim(queryEmbedding, i.Embedding)
                    })
                    .Where(x => x.Score >= minScore)
                    .OrderByDescending(x => x.Score)
                    .Take(topK * 4) // Stage 1: 粗召回更多候选
                    .ToList();

                // Stage 2: 混合评分（嵌入 + 重要性）
                foreach (var r in candidates)
                {
                    double importanceBoost = r.Item.Importance * 0.2;
                    r.Score = r.Score * 0.8 + importanceBoost;
                }

                var results = candidates.OrderByDescending(x => x.Score).Take(topK).ToList();

                // 更新访问次数
                foreach (var hit in results)
                    hit.Item.AccessCount++;

                return results;
            }
        }

        /// <summary>关键词搜索（无嵌入时的降级方案）</summary>
        // ── BM25 本地检索 ──────────────────────────────────────────────

        /// <summary>BM25 参数</summary>
        private const double K1 = 1.2;
        private const double B = 0.75;

        /// <summary>获取文档词数（缓存）</summary>
        private static int DocWordCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(new[] { ' ', '\t', '\n', '\r', '，', '。', '？', '！', '、', '：', '；', '(', ')', '（', '）' },
                StringSplitOptions.RemoveEmptyEntries).Length;
        }

        public static List<SearchResult> KeywordSearch(string query, int topK = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SearchResult>();

            // 分词
            var queryTerms = query.ToLower().Split(new[] { ' ', '，', '。', '？', '！', '\n', '\r', '\t',
                '、', '：', '；', '(', ')', '（', '）', '"', '"' },
                StringSplitOptions.RemoveEmptyEntries)
                .Distinct().ToArray();

            if (queryTerms.Length == 0) return new List<SearchResult>();

            lock (_lock)
            {
                if (_items.Count == 0) return new List<SearchResult>();

                // 预计算：总文档数、每文档词数、平均文档长度
                int totalDocs = _items.Count;
                var docLengths = new Dictionary<string, int>(); // id → word count
                int totalWords = 0;
                foreach (var item in _items)
                {
                    int wc = DocWordCount(item.Text ?? "");
                    docLengths[item.Id] = wc;
                    totalWords += wc;
                }
                double avgDocLength = totalWords / (double)totalDocs;

                // 预计算 IDF
                var idfCache = new Dictionary<string, double>();
                foreach (var term in queryTerms)
                {
                    int docsWithTerm = 0;
                    foreach (var item in _items)
                    {
                        if ((item.Text ?? "").ToLower().Contains(term))
                            docsWithTerm++;
                    }
                    // BM25 IDF formula: log((N - n + 0.5) / (n + 0.5) + 1)
                    double idf = Math.Log((totalDocs - docsWithTerm + 0.5) / (docsWithTerm + 0.5) + 1.0);
                    idfCache[term] = idf;
                }

                // 计算 BM25 得分
                var scored = new List<SearchResult>();
                foreach (var item in _items)
                {
                    string textLower = (item.Text ?? "").ToLower();
                    int docLen = docLengths[item.Id];
                    double score = 0;

                    foreach (var term in queryTerms)
                    {
                        // 词频 TF
                        int tf = 0;
                        int idx = 0;
                        while ((idx = textLower.IndexOf(term, idx, StringComparison.Ordinal)) != -1)
                        {
                            tf++;
                            idx += term.Length;
                        }

                        if (tf == 0) continue;

                        double idf = idfCache[term];
                        // BM25
                        score += idf * (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * docLen / avgDocLength));
                    }

                    if (score > 0)
                        scored.Add(new SearchResult { Item = item, Score = score });
                }

                // 排序取 Top-K
                var results = scored.OrderByDescending(x => x.Score).Take(topK).ToList();

                foreach (var hit in results)
                    hit.Item.AccessCount++;

                return results;
            }
        }

        /// <summary>清理低重要性/过期的记忆</summary>
        public static async Task ConsolidateAsync()
        {
            lock (_lock)
            {
                int before = _items.Count;
                // 保留重要性>0.3的，或者30天内访问过的
                var cutoff = DateTime.Now.AddDays(-30);
                _items = _items
                    .Where(i => i.Importance > 0.3 || i.Timestamp > cutoff)
                    .ToList();

                // 合并相似记忆（相同嵌入的取重要性高的保留）
                var merged = new List<MemoryItem>();
                var groups = _items.GroupBy(i => i.Text.Trim().ToLower());
                foreach (var g in groups)
                {
                    var best = g.OrderByDescending(i => i.Importance).First();
                    merged.Add(best);
                }
                _items = merged;

                System.Diagnostics.Debug.WriteLine(
                    $"MemoryStore: {before} → {_items.Count} (consolidated)");
            }
            await SaveAsync();
        }
    }

    /// <summary>RAG 检索器：处理嵌入生成 + 检索 + 上下文构建</summary>
    public static class RagRetriever
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>用API生成文本嵌入向量</summary>
        public static async Task<float[]> GetEmbeddingAsync(string text, ApiProfile profile)
        {
            if (string.IsNullOrWhiteSpace(text) || profile == null) return null;

            // 构建 embeddings API 请求
            var payload = new
            {
                input = text.Length > 8000 ? text.Substring(0, 8000) : text,
                model = profile.Model.StartsWith("text-embedding") ? profile.Model : "text-embedding-ada-002"
            };

            string json;
            using (var ms = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(payload.GetType());
                ser.WriteObject(ms, payload);
                json = Encoding.UTF8.GetString(ms.ToArray());
            }

            try
            {
                // OpenAI兼容嵌入API：POST /v1/embeddings
                string embedUrl = profile.Url;
                if (embedUrl.Contains("/chat/completions"))
                    embedUrl = embedUrl.Replace("/chat/completions", "/embeddings");
                else if (!embedUrl.EndsWith("/embeddings"))
                    embedUrl = embedUrl.TrimEnd('/') + "/embeddings";

                var req = new HttpRequestMessage(HttpMethod.Post, embedUrl);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var body = await resp.Content.ReadAsStringAsync();
                    return ParseEmbeddingResponse(body);
                }
            }
            catch { return null; }
        }

        private static float[] ParseEmbeddingResponse(string json)
        {
            try
            {
                // 简化解析：找 "embedding":[ 后的浮点数数组
                int start = json.IndexOf("\"embedding\":[");
                if (start < 0) return null;
                start += 12; // skip "embedding":[
                int end = json.IndexOf(']', start);
                if (end < 0) return null;

                var parts = json.Substring(start, end - start).Split(',');
                var vec = new List<float>(parts.Length);
                foreach (var p in parts)
                {
                    if (float.TryParse(p.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                        vec.Add(v);
                }
                return vec.ToArray();
            }
            catch { return null; }
        }

        /// <summary>检索并构建 RAG 上下文文本</summary>
        public static async Task<string> BuildRagContextAsync(string userInput, ApiProfile profile, string conversationId = null)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return null;

            // 1. 先尝试用嵌入检索
            var embedding = await GetEmbeddingAsync(userInput, profile);
            List<SearchResult> results;

            if (embedding != null && embedding.Length > 0)
            {
                results = MemoryStore.Search(embedding, topK: 5, minScore: 0.3);
            }
            else
            {
                // 2. 降级：关键词搜索
                results = MemoryStore.KeywordSearch(userInput, topK: 5);
            }

            if (results.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("【相关记忆（RAG检索）】");
            foreach (var r in results)
            {
                sb.AppendLine($"- [{r.Item.Category}] {r.Item.Text} (相关度: {r.Score:P0})");
            }

            return sb.ToString();
        }
    }
}
