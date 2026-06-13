// DialoguePool.cs — 对话池系统
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace yanshuai
{
    // RAG 结果辅助类 (替代 ValueTuple)
    internal class RagCandidate { public string Text; public double EmbedScore; public float Importance; }
    internal class RankedResult { public string Text; public double FinalScore; }
    // ══════════════════════════════════════════════════════════════════════════
    // 对话池 — 一个角色卡 + 一个用户人设对应一个池
    // ══════════════════════════════════════════════════════════════════════════

    [DataContract]
    public class DialoguePool
    {
        [DataMember] public string CharacterCardId { get; set; }
        [DataMember] public string UserProfileId { get; set; } = "";
        [DataMember] public string PoolName { get; set; } = "新对话池";

        /// <summary>此池中所有对话的 ID 列表</summary>
        [DataMember] public List<string> ConversationIds { get; set; } = new List<string>();

        /// <summary>池级 RAG 记忆项。字段名保留 SharedMemories 以兼容旧数据。</summary>
        [DataMember] public List<PoolMemoryItem> SharedMemories { get; set; } = new List<PoolMemoryItem>();

        /// <summary>角色画像（跨对话累积/进化）</summary>
        [DataMember] public CharacterProfile Profile { get; set; } = new CharacterProfile();

        /// <summary>池级别设置</summary>
        [DataMember] public PoolSettings Settings { get; set; } = new PoolSettings();

        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        [DataMember] public DateTime UpdatedAt { get; set; } = DateTime.Now;
        [DataMember] public int TotalExchanges { get; set; } = 0;

        // ══════════════════════════════════════════════════════════════════════
        // 运行时缓存（不序列化）
        // ══════════════════════════════════════════════════════════════════════

        [IgnoreDataMember] public List<Conversation> CachedConversations { get; set; } = new List<Conversation>();

        /// <summary>加载完成后调用，刷新运行时引用</summary>
        public void RefreshCache()
        {
            if (CachedConversations == null)
                CachedConversations = new List<Conversation>();
            CachedConversations.Clear();

            if (DataManager.Data?.Conversations == null) return;

            foreach (var cid in ConversationIds)
            {
                var conv = DataManager.Data.Conversations.Find(c => c.Id == cid);
                if (conv != null && (string.IsNullOrEmpty(conv.UserProfileId) || conv.UserProfileId == UserProfileId))
                {
                    if (string.IsNullOrEmpty(conv.UserProfileId))
                        conv.UserProfileId = UserProfileId;
                    CachedConversations.Add(conv);
                }
            }
        }

        /// <summary>将对话加入池</summary>
        public void AddConversation(Conversation conv)
        {
            if (conv == null) return;
            conv.CharacterCardId = CharacterCardId;
            conv.UserProfileId = UserProfileId;

            if (!ConversationIds.Contains(conv.Id))
                ConversationIds.Add(conv.Id);
            if (!CachedConversations.Contains(conv))
                CachedConversations.Add(conv);

            UpdatedAt = DateTime.Now;
        }

        /// <summary>从池中移除对话并清理其产生的记忆</summary>
        public void RemoveConversation(string convId)
        {
            bool changed = ConversationIds.Remove(convId);
            changed = CachedConversations.RemoveAll(c => c.Id == convId) > 0 || changed;
            if (SharedMemories != null)
                changed = SharedMemories.RemoveAll(m => m.SourceConversationId == convId) > 0 || changed;
            if (changed)
                UpdatedAt = DateTime.Now;
        }

        /// <summary>获取池级 RAG 记忆项。</summary>
        public List<string> GetAllMemoryItems()
        {
            var all = new List<string>();

            foreach (var mem in SharedMemories)
            {
                if (!string.IsNullOrEmpty(mem.Summary))
                    all.Add(mem.Summary);
            }

            return all
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();
        }

        // ── 嵌入缓存 ───────────────────────────────────────────────────
        // 将 OnEmbedder.Encode 结果缓存到 PoolMemoryItem.EmbeddingCache，
        // 避免每次搜索都对所有记忆重新编码（最核心的性能优化）。

        /// <summary>确保所有记忆都有嵌入缓存（同步，搜索路径中惰性调用）</summary>
        public void EnsureEmbeddingCache(OnEmbedder embedder)
        {
            if (embedder == null || SharedMemories == null) return;

            // 先处理已持久化的嵌入（零成本恢复）
            foreach (var mem in SharedMemories)
            {
                if (mem.EmbeddingCache == null && mem.Embedding != null && mem.Embedding.Length > 0)
                    mem.EmbeddingCache = mem.Embedding;
            }

            // 剩下的需要真正编码，Parallel.ForEach 多核并行
            var toCompute = SharedMemories
                .Where(m => m.EmbeddingCache == null && !string.IsNullOrEmpty(m.Summary))
                .ToList();
            if (toCompute.Count == 0) return;

            try
            {
                // SD810: 4×A57(大核) + 4×A53(小核)，只跑在大核上避免拖到小核
                int dop = Math.Min(4, Environment.ProcessorCount);
                System.Threading.Tasks.Parallel.ForEach(toCompute,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = dop },
                    mem => { mem.EmbeddingCache = embedder.Encode(mem.Summary); });
            }
            catch
            {
                // Parallel.ForEach 失败 → 降级串行
                foreach (var mem in toCompute)
                {
                    try { mem.EmbeddingCache = embedder.Encode(mem.Summary); } catch { }
                }
            }
        }

        /// <summary>后台构建嵌入缓存（从 PrefetchCloudRagAsync 调用）</summary>
        public System.Threading.Tasks.Task BuildEmbeddingCacheAsync()
        {
            var embedder = AppState.Embedder;
            if (embedder == null || SharedMemories == null)
                return System.Threading.Tasks.Task.CompletedTask;

            // 收集未缓存的记忆（排除已有持久化 Embedding 的）
            var toCompute = SharedMemories
                .Where(m => m.EmbeddingCache == null && !string.IsNullOrEmpty(m.Summary)
                         && (m.Embedding == null || m.Embedding.Length == 0))
                .ToList();
            if (toCompute.Count == 0)
                return System.Threading.Tasks.Task.CompletedTask;

            int dop = Math.Min(4, Environment.ProcessorCount);
            return System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.Threading.Tasks.Parallel.ForEach(toCompute,
                        new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = dop },
                        mem => { mem.EmbeddingCache = embedder.Encode(mem.Summary); });
                }
                catch
                {
                    foreach (var mem in toCompute)
                    {
                        try { mem.EmbeddingCache = embedder.Encode(mem.Summary); } catch { }
                    }
                }
            });
        }

        // ── 去重后的记忆列表（按重要性保留最佳副本）───────────────

        private List<PoolMemoryItem> GetDistinctMemories()
        {
            if (SharedMemories == null || SharedMemories.Count == 0)
                return new List<PoolMemoryItem>();

            return SharedMemories
                .Where(m => !string.IsNullOrWhiteSpace(m.Summary))
                .GroupBy(m => m.Summary.Trim().ToLower())
                .Select(g => g.OrderByDescending(m => m.Importance).First())
                .ToList();
        }

        /// <summary>云端重排 RAG：嵌入粗排 → DeepSeek/OpenAI 精排</summary>
        public async System.Threading.Tasks.Task<List<string>> SearchMemoriesCloudAsync(
            string query, ApiProfile profile, int topK = 5)
        {
            var embedder = AppState.Embedder;
            if (embedder != null)
                EnsureEmbeddingCache(embedder);

            var allMemories = GetAllMemoryItems();
            if (allMemories.Count == 0) return new List<string>();

            // Stage 1: 嵌入粗排 → top 20（使用缓存，避免重新编码）
            var candidates = new List<RagCandidate>();
            if (embedder != null)
            {
                try
                {
                    var qv = embedder.Encode(query);
                    if (qv != null && qv.Length > 0 && SharedMemories != null)
                    {
                        foreach (var mem in SharedMemories)
                        {
                            if (string.IsNullOrEmpty(mem.Summary) || mem.EmbeddingCache == null) continue;
                            double sim = OnEmbedder.CosineSim(qv, mem.EmbeddingCache);
                            if (sim >= 0.2)
                                candidates.Add(new RagCandidate { Text = mem.Summary.Trim(), EmbedScore = sim, Importance = mem.Importance });
                        }
                        candidates = candidates
                            .GroupBy(c => c.Text.ToLower())
                            .Select(g => g.First())
                            .OrderByDescending(c => c.EmbedScore)
                            .Take(20)
                            .ToList();
                    }
                }
                catch { }
            }

            if (candidates.Count == 0) return SearchMemories(query, topK);
            var texts = candidates.Select(c => c.Text).ToList();

            // Stage 2: 云端精排
            var ranked = await RagRetriever.RerankWithApiAsync(query, texts, profile, topK);

            if (ranked != null && ranked.Count > 0)
                return ranked.Select(i => texts[i]).ToList();

            // 云端失败 → 降级本地混合重排
            return candidates.Take(topK).Select(c => c.Text).ToList();
        }

        /// <summary>两阶段 RAG 检索：嵌入粗排 → 混合精排（纯本地）</summary>
        public List<string> SearchMemories(string query, int topK = 5)
        {
            if (SharedMemories == null || SharedMemories.Count == 0) return new List<string>();

            var distinctMemories = GetDistinctMemories();
            if (distinctMemories.Count == 0) return new List<string>();

            int recallN = Math.Min(topK * 4, distinctMemories.Count);
            var candidates = new List<RagCandidate>();
            var embedder = AppState.Embedder;

            // ── Stage 1: 嵌入粗排（使用缓存，避免重新编码全部记忆）──
            if (embedder != null && Settings.EnableRAG)
            {
                try
                {
                    EnsureEmbeddingCache(embedder);
                    var queryVec = embedder.Encode(query);
                    if (queryVec != null && queryVec.Length > 0)
                    {
                        foreach (var mem in distinctMemories)
                        {
                            if (mem.EmbeddingCache == null) continue;
                            double sim = OnEmbedder.CosineSim(queryVec, mem.EmbeddingCache);
                            if (sim >= 0.2)
                                candidates.Add(new RagCandidate { Text = mem.Summary.Trim(), EmbedScore = sim, Importance = mem.Importance });
                        }
                        candidates = candidates.OrderByDescending(c => c.EmbedScore)
                                               .Take(recallN).ToList();
                    }
                }
                catch { /* 降级 */ }
            }

            // 降级：关键词预召回（用去重后的文本列表）
            var memoryTexts = distinctMemories.Select(m => m.Summary.Trim()).Distinct().ToList();
            if (candidates.Count == 0)
            {
                var kw = QueryTokens(query);
                foreach (var mem in memoryTexts)
                {
                    int hits = CountKeywordHits(mem, kw);
                    if (hits > 0)
                    {
                        float imp = GetMemoryImportanceFast(mem, distinctMemories);
                        candidates.Add(new RagCandidate { Text = mem, EmbedScore = hits / (double)kw.Length, Importance = imp });
                    }
                }
            }
            if (candidates.Count == 0) return new List<string>();

            // ── Stage 2: 混合精排 ──
            //   score = α·embed  +  β·bm25  +  γ·importance
            //   α=0.5, β=0.3, γ=0.2
            var queryTokens = QueryTokens(query);
            var reranked = new List<RankedResult>();

            foreach (var c in candidates)
            {
                double bm25 = Bm25Overlap(c.Text, queryTokens, memoryTexts.Count);
                double normEmbed = Math.Min(1.0, c.EmbedScore);
                double normBm25 = Math.Min(1.0, bm25 * 2.0);
                double final = 0.5 * normEmbed + 0.3 * normBm25 + 0.2 * c.Importance;
                reranked.Add(new RankedResult { Text = c.Text, FinalScore = final });
            }

            return reranked.OrderByDescending(r => r.FinalScore)
                          .Take(topK)
                          .Select(r => r.Text)
                          .ToList();
        }

        // ── 重排辅助 ──────────────────────────────────────────────────

        private static float GetMemoryImportanceFast(string text, List<PoolMemoryItem> memories)
        {
            var lower = text.ToLower();
            foreach (var m in memories)
                if (!string.IsNullOrEmpty(m.Summary) && m.Summary.Trim().ToLower() == lower)
                    return m.Importance;
            return 0.5f;
        }

        private static string[] QueryTokens(string q)
        {
            return q.Split(new[] { ' ', '\t', '\n', '，', '。', '？', '！', '、', '：', '；' },
                       StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.ToLower())
                    .Where(t => t.Length > 1)
                    .Distinct()
                    .ToArray();
        }

        private static int CountKeywordHits(string text, string[] tokens)
        {
            var lower = text.ToLower();
            return tokens.Count(t => lower.Contains(t));
        }

        private static double Bm25Overlap(string doc, string[] queryTokens, int totalDocs)
        {
            // 简化 BM25 重叠度: 加权词频 / 归一化
            var lower = doc.ToLower();
            double score = 0;
            foreach (var term in queryTokens)
            {
                int tf = 0, idx = 0;
                while ((idx = lower.IndexOf(term, idx, StringComparison.Ordinal)) != -1)
                    { tf++; idx += term.Length; }
                if (tf > 0)
                    score += (tf * 2.2) / (tf + 1.2); // BM25 simplified: k1=1.2
            }
            return score;
        }

        /// <summary>添加池级 RAG 记忆</summary>
        public void AddSharedMemory(string summary, string sourceConvId = "", float importance = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(summary)) return;
            if (SharedMemories == null) SharedMemories = new List<PoolMemoryItem>();

            var existing = SharedMemories.FirstOrDefault(m =>
                string.Equals(m.Summary?.Trim(), summary.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Importance = Math.Max(existing.Importance, importance);
                existing.AccessCount++;
                UpdatedAt = DateTime.Now;
                return;
            }

            // 添加时同步计算嵌入（以后搜索无需重新编码）
            float[] embedding = null;
            var embedder = AppState.Embedder;
            if (embedder != null)
            {
                try { embedding = embedder.Encode(summary.Trim()); }
                catch { }
            }

            SharedMemories.Add(new PoolMemoryItem
            {
                Id = Guid.NewGuid().ToString(),
                Summary = summary.Trim(),
                EmbeddingCache = embedding,
                Embedding = embedding,
                SourceConversationId = sourceConvId,
                Importance = importance,
                CreatedAt = DateTime.Now
            });

            // 限制记忆数量
            if (SharedMemories.Count > Settings.MaxMemories)
            {
                SharedMemories = SharedMemories
                    .OrderByDescending(m => m.Importance)
                    .Take(Settings.MaxMemories)
                    .ToList();
            }

            UpdatedAt = DateTime.Now;
        }

        /// <summary>添加对话摘要到池记忆</summary>
        public async Task SummarizeAndStore(Conversation conv, string apiProfileId)
        {
            if (conv.Messages.Count < Settings.MinMessagesForSummary) return;

            // 用 API 生成摘要（后续可加离线摘要）
            var summary = $"[{conv.Title}] {conv.Messages.Count}条消息，最后更新于{conv.UpdatedAt:MM-dd HH:mm}";
            AddSharedMemory(summary, conv.Id, 0.5f);

            // 更新角色对用户的认知统计
            Profile.TotalConversations = CachedConversations.Count;

            await DataManager.SaveAsync();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 角色对用户的认知（跨对话累积）
    // ══════════════════════════════════════════════════════════════════════════

}
