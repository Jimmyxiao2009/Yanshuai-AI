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
    // ══════════════════════════════════════════════════════════════════════════
    // RagMemory — 共享 RAG 记忆层（Agent / Roleplay 共用，原本两边各有一份并已分叉）
    // 统一时采用 Agent 的实现：MemoryStore 两阶段语义检索 + 优化版 BM25；并入 Roleplay
    // 独有的 RagRetriever.RerankWithApiAsync。Roleplay 专属的 MemoryPipeline /
    // MemorySummaryResult / RagContextResult 仍留在 Lumina.Roleplay/AI/RagMemory.cs。
    // ══════════════════════════════════════════════════════════════════════════

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

        public static void Clear()
        {
            lock (_lock) _items.Clear();
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

                // 预计算：每文档的小写文本 + 词数（只算一次）。
                // 之前 IDF 循环对每个 query term 都把整个语料 ToLower 一遍，
                // 评分循环又对每篇文档 ToLower 一次——这里统一预计算并复用。
                int totalDocs = _items.Count;
                var lowers = new string[totalDocs];
                var lens   = new int[totalDocs];
                int totalWords = 0;
                for (int d = 0; d < totalDocs; d++)
                {
                    string t = _items[d].Text ?? "";
                    lowers[d] = t.ToLower();
                    int wc = DocWordCount(t);
                    lens[d] = wc;
                    totalWords += wc;
                }
                // 全部文档为空/纯分隔符时 totalWords==0，避免 docLen/avgDocLength 产生 NaN/Infinity
                double avgDocLength = totalWords > 0 ? totalWords / (double)totalDocs : 1.0;

                // 预计算 IDF（用预先 ToLower 的文本 + Ordinal 查找，与下方 TF 统计一致）
                var idfCache = new Dictionary<string, double>();
                foreach (var term in queryTerms)
                {
                    int docsWithTerm = 0;
                    for (int d = 0; d < totalDocs; d++)
                    {
                        if (lowers[d].IndexOf(term, StringComparison.Ordinal) >= 0)
                            docsWithTerm++;
                    }
                    // BM25 IDF formula: log((N - n + 0.5) / (n + 0.5) + 1)
                    double idf = Math.Log((totalDocs - docsWithTerm + 0.5) / (docsWithTerm + 0.5) + 1.0);
                    idfCache[term] = idf;
                }

                // 计算 BM25 得分
                var scored = new List<SearchResult>();
                for (int d = 0; d < totalDocs; d++)
                {
                    var item = _items[d];
                    string textLower = lowers[d];
                    int docLen = lens[d];
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
                var groups = _items.GroupBy(i => (i.Text ?? "").Trim().ToLower());
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

    [DataContract]
    public class EmbeddingRequest
    {
        [DataMember(Name = "input")] public string Input { get; set; }
        [DataMember(Name = "model")] public string Model { get; set; }
    }

    /// <summary>RAG 检索器：处理嵌入生成 + 检索 + 上下文构建</summary>
    public static class RagRetriever
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>用API生成文本嵌入向量</summary>
        public static async Task<float[]> GetEmbeddingAsync(string text, ApiProfile profile, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text) || profile == null) return null;
            if (!IsNetworkAvailable()) return null;

            // 构建 embeddings API 请求
            var modelName = !string.IsNullOrEmpty(profile.EmbeddingModel) ? profile.EmbeddingModel : (profile.Model ?? "");
            var payload = new EmbeddingRequest
            {
                Input = text.Length > 8000 ? text.Substring(0, 8000) : text,
                Model = modelName
            };

            string json;
            using (var ms = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(typeof(EmbeddingRequest));
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

                using (var resp = await _http.SendAsync(req, ct))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var body = await resp.Content.ReadAsStringAsync();
                    return ParseEmbeddingResponse(body);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RAG] GetEmbedding failed: {ex.Message}"); return null; }
        }

        private static float[] ParseEmbeddingResponse(string json)
        {
            try
            {
                // 找 "embedding" 后的第一个 '['，从其后开始。
                // 修复：原实现 start += 12 比 "embedding":[ 的长度(13)少一位，会把第一个分量
                // 连同 '[' 一起喂给 float.TryParse 而解析失败，导致 embedding 第 0 维被悄悄丢掉；
                // 改为定位 '[' 后一位，同时兼容冒号后带空格的格式。
                int kpos = json.IndexOf("\"embedding\"");
                if (kpos < 0) return null;
                int start = json.IndexOf('[', kpos);
                if (start < 0) return null;
                start += 1; // skip '['
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

        /// <summary>云端重排：将粗排候选发给 DeepSeek/OpenAI 做精排</summary>
        /// <returns>重排后的索引列表（只含 top-K）</returns>
        public static async Task<List<int>> RerankWithApiAsync(string query,
            List<string> candidates, ApiProfile profile, int topK = 5, System.Threading.CancellationToken ct = default)
        {
            if (candidates.Count == 0 || profile == null) return null;
            if (!IsNetworkAvailable()) return null;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("根据查询对以下记忆进行相关性排序，只返回最相关的")
                  .Append(topK).AppendLine("条编号。");
                sb.Append("查询：").AppendLine(query);
                sb.AppendLine("记忆：");
                for (int i = 0; i < candidates.Count; i++)
                    sb.Append('[').Append(i).Append("] ").AppendLine(candidates[i].Substring(0,
                        Math.Min(200, candidates[i].Length)));
                sb.Append("只返回编号，如：");
                var example = new List<int>();
                for (int i = 0; i < Math.Min(topK, candidates.Count); i++)
                    example.Add(i);
                sb.Append(string.Join(",", example));

                var messages = new[]
                {
                    new { role = "system", content = "你是一个记忆重排助手。根据对话上下文，从候选记忆中筛选最相关的。只返回编号，用逗号分隔。" },
                    new { role = "user", content = sb.ToString() }
                };

                var payload = new
                {
                    model = profile.Model,
                    messages,
                    stream = false,
                    max_tokens = 50,
                    temperature = 0.0
                };

                string json;
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(payload.GetType());
                    ser.WriteObject(ms, payload);
                    json = Encoding.UTF8.GetString(ms.ToArray());
                }

                var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req, ct))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var body = await resp.Content.ReadAsStringAsync();

                    // 只解析模型答复正文里的编号，不能用整个 HTTP/JSON 包体：
                    // 否则 created/index/token 计数等落在 [0,candidates.Count) 的整数会被误当成排名。
                    ApiResponse parsed;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                    var content = (parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null) ?? "";
                    if (string.IsNullOrWhiteSpace(content)) return null; // 触发本地重排降级

                    // 解析答复正文中的编号
                    var ids = new List<int>();
                    foreach (var part in content.Split(',', '，', ' ', '\n', '\r',
                        '[', ']', '{', '}', '"', '\t'))
                    {
                        if (int.TryParse(part.Trim(), out int id) &&
                            id >= 0 && id < candidates.Count && !ids.Contains(id))
                            ids.Add(id);
                    }
                    return ids.Count > 0 ? ids.Take(topK).ToList() : null;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RAG] Rerank failed: {ex.Message}"); return null; }
        }

        /// <summary>检索并构建 RAG 上下文文本</summary>
        public static async Task<string> BuildRagContextAsync(string userInput, ApiProfile profile, string conversationId = null, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return null;

            // 1. 先尝试用嵌入检索
            var embedding = await GetEmbeddingAsync(userInput, profile, ct);
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

        private static bool IsNetworkAvailable()
        {
            try
            {
                var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                return profile != null && profile.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess;
            }
            catch { return true; }
        }
    }
}
