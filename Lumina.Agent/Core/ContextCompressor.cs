using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace yanshuai
{
    /// <summary>
    /// 上下文压缩器 — OpenCode/Claude Code 风格的 auto-compact：
    /// 当上下文使用率超过阈值时，把"除最近 N 条外的所有消息"压缩成一段单一摘要；
    /// 原始消息保留在列表中（仅在 API 调用时跳过），用户仍可滚动查看。
    /// </summary>
    public static class ContextCompressor
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // ── 阈值 / 策略常量 ────────────────────────────────────────────
        /// <summary>默认压缩阈值：上下文使用率达到 80% 时触发</summary>
        public const double DefaultThreshold = 0.80;
        /// <summary>保留最近 N 条消息不压缩，剩余全部 compact</summary>
        public const int    DefaultKeepRecent = 4;
        /// <summary>消息总数低于此值不压缩</summary>
        public const int    MinMessagesToCompact = 6;

        // ── Token 估算 ─────────────────────────────────────────────────

        /// <summary>
        /// 简单 token 估算：中文≈1.5 token/字，英文≈0.75 token/词，混合取平均
        /// </summary>
        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int chineseChars = 0;
            int otherChars = 0;
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF || c >= 0x3000 && c <= 0x303F || c >= 0xFF00 && c <= 0xFFEF)
                    chineseChars++;
                else if (!char.IsWhiteSpace(c))
                    otherChars++;
            }

            double tokens = chineseChars * 1.5 + otherChars * 0.25;
            int wordCount = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            double wordEstimate = wordCount * 1.3;

            return (int)Math.Max(tokens, wordEstimate);
        }

        /// <summary>估算一组消息的总 token 数</summary>
        public static int EstimateConversationTokens(IList<ConversationMessage> messages)
        {
            int total = 0;
            foreach (var msg in messages)
            {
                total += EstimateTokens(msg.Content) + 4; // 每条消息有 role/格式开销
                if (!string.IsNullOrEmpty(msg.ReasoningContent))
                    total += EstimateTokens(msg.ReasoningContent);
            }
            return total;
        }

        /// <summary>获取格式化的 token 显示文本</summary>
        public static string FormatTokenCount(int tokens)
        {
            if (tokens < 1000) return tokens.ToString();
            if (tokens < 10000) return string.Format("{0:F1}k", tokens / 1000.0);
            if (tokens < 1000000) return string.Format("{0:F0}k", tokens / 1000.0);
            return string.Format("{0:F1}M", tokens / 1000000.0);
        }

        // ── 上下文使用率（auto-compact 核心 API）────────────────────────

        /// <summary>返回 (used, limit, percent) 三元组，供 UI 进度条使用</summary>
        public static ContextUsage GetContextUsage(Conversation conv, ApiProfile profile)
        {
            if (conv == null || profile == null)
                return new ContextUsage(0, 0, 0);
            int used   = EstimateConversationTokens(conv.Messages);
            int limit  = ModelContextLimits.GetEffectiveLimit(profile);
            if (limit <= 0) limit = 8192;
            int percent = (int)Math.Min(100, used * 100 / limit);
            return new ContextUsage(used, limit, percent);
        }

        // ── Auto-compact 主逻辑 ────────────────────────────────────────

        /// <summary>
        /// 检查是否需要 compact；如果需要则执行压缩。
        /// 返回 CompactResult 描述本次操作。
        /// </summary>
        /// <param name="conv">当前对话</param>
        /// <param name="profile">API 配置（用于取 context limit 和调 summary API）</param>
        /// <param name="onProgress">进度回调（"正在生成摘要…" 等）</param>
        /// <param name="threshold">触发阈值（0~1），默认 0.80</param>
        /// <param name="keepRecent">保留最近 N 条不压缩，默认 4</param>
        public static async Task<CompactResult> CompressIfNeededAsync(
            Conversation conv, ApiProfile profile,
            Action<string> onProgress = null,
            double threshold = DefaultThreshold,
            int keepRecent = DefaultKeepRecent,
            System.Threading.CancellationToken ct = default)
        {
            if (conv == null || profile == null)
                return new CompactResult(false, 0, 0, 0, "参数为空");

            if (conv.Messages.Count < MinMessagesToCompact)
                return new CompactResult(false, 0, 0, 0, "消息太少");

            int contextLimit = ModelContextLimits.GetEffectiveLimit(profile);
            int currentTokens = EstimateConversationTokens(conv.Messages);
            int percent = (int)Math.Min(100, currentTokens * 100 / contextLimit);

            if (percent < threshold * 100)
                return new CompactResult(false, currentTokens, contextLimit, percent, "未达阈值");

            // 跳过已经被压缩过的范围
            int startIdx = Math.Max(0, conv.SummarizedUpTo);
            int availableCount = conv.Messages.Count - startIdx;
            int willCompressCount = Math.Max(0, availableCount - keepRecent);

            if (willCompressCount < 2)
                return new CompactResult(false, currentTokens, contextLimit, percent, "可压缩条数不足");

            var toCompress = conv.Messages.Skip(startIdx).Take(willCompressCount).ToList();

            onProgress?.Invoke("⏳ 正在压缩上下文…");
            string newSummary = await GenerateSummaryAsync(toCompress, profile, onProgress, ct);
            onProgress?.Invoke("⏳ 整理摘要…");

            if (string.IsNullOrEmpty(newSummary))
                return new CompactResult(false, currentTokens, contextLimit, percent, "生成摘要失败");

            // ── 替换式写入（OpenCode / Claude Code 风格：不堆叠）────────
            conv.ContextSummary = newSummary;
            conv.SummarizedUpTo = startIdx + willCompressCount;
            conv.LastCompactAt  = DateTime.Now;
            // 重置周期计数
            conv.ExchangesSinceLastSummary = 0;

            int afterTokens = EstimateConversationTokens(
                conv.Messages.Skip(conv.SummarizedUpTo).ToList())
                + EstimateTokens(conv.ContextSummary) + 16;
            int afterPercent = (int)Math.Min(100, afterTokens * 100 / contextLimit);

            onProgress?.Invoke($"✓ 上下文已压缩（{percent}% → {afterPercent}%）");

            return new CompactResult(
                compacted: true,
                used: afterTokens,
                limit: contextLimit,
                percent: afterPercent,
                note: $"压缩了 {willCompressCount} 条消息",
                compactedCount: willCompressCount);
        }

        /// <summary>
        /// 强制执行一次 compact（不检查阈值），供设置页"立即压缩"按钮使用。
        /// </summary>
        public static async Task<CompactResult> ForceCompactAsync(
            Conversation conv, ApiProfile profile,
            Action<string> onProgress = null,
            int keepRecent = DefaultKeepRecent)
        {
            return await CompressIfNeededAsync(conv, profile, onProgress,
                threshold: 0.0, keepRecent: keepRecent);
        }

        // ── 摘要块 + 可发送消息 ────────────────────────────────────────


        /// <summary>
        /// 旧 API 兼容：返回摘要文本块（用于内联到 system prompt）。
        /// 摘要块以 system 角色注入，说明之前对话的要点。null 表示无需注入。
        /// </summary>
        public static string BuildSummarySystemBlock(Conversation conv)
        {
            if (conv == null || string.IsNullOrEmpty(conv.ContextSummary))
                return null;
            return "【对话历史摘要】以下是对话早期内容的压缩摘要，请基于此上下文继续对话。" +
                   "摘要中可能省略了细节，如需精确信息请让用户重述。\n\n" + conv.ContextSummary;
        }

        // ── 内部方法 ──────────────────────────────────────────────────

        private static async Task<string> GenerateSummaryAsync(
            List<ConversationMessage> messages, ApiProfile profile, Action<string> onProgress, System.Threading.CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个对话压缩助手。请将以下对话历史压缩为结构化摘要，**只输出摘要内容本身**。");
            sb.AppendLine();
            sb.AppendLine("要求：");
            sb.AppendLine("- 保留所有关键事实、决定、用户偏好、技术细节");
            sb.AppendLine("- 保留代码片段中的关键逻辑（可省略通用样板）");
            sb.AppendLine("- 保留未完成的任务和用户的明确意图");
            sb.AppendLine("- 使用要点列表组织，按主题分组");
            sb.AppendLine("- 去除寒暄、重复确认、礼节性表达");
            sb.AppendLine("- 输出长度控制在 800-1500 字");
            sb.AppendLine();
            sb.AppendLine("=== 对话开始 ===");
            foreach (var m in messages)
            {
                string role = m.Role == "user" ? "用户" : "AI";
                string content = m.Content ?? "";
                if (content.Length > 2000)
                    content = content.Substring(0, 2000) + "…[已截断]";
                sb.AppendLine($"【{role}】");
                sb.AppendLine(content);
                sb.AppendLine();
            }
            sb.AppendLine("=== 对话结束 ===");
            sb.AppendLine();
            sb.AppendLine("请输出结构化摘要：");

            onProgress?.Invoke("⏳ 调用 LLM 生成摘要…");

            try
            {
                string requestJson = BuildSimpleRequest(profile.Model, sb.ToString(), profile.ProviderType == "claude");

                var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                if (profile.ProviderType == "claude")
                {
                    req.Headers.TryAddWithoutValidation("x-api-key", profile.ApiKey);
                    req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                }
                else
                {
                    req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
                }
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req, ct))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var body = await resp.Content.ReadAsStringAsync();
                    return ExtractContent(body, profile.ProviderType == "claude");
                }
            }
            catch { return null; }
        }

        private static string BuildSimpleRequest(string model, string userMessage, bool isClaude)
        {
            string escaped = EscapeJson(userMessage);
            if (isClaude)
            {
                return $"{{\"model\":\"{EscapeJson(model)}\",\"max_tokens\":2048,\"messages\":[{{\"role\":\"user\",\"content\":\"{escaped}\"}}]}}";
            }
            return $"{{\"model\":\"{EscapeJson(model)}\",\"stream\":false,\"messages\":[{{\"role\":\"user\",\"content\":\"{escaped}\"}}]}}";
        }

        private static string ExtractContent(string body, bool isClaude)
        {
            if (isClaude)
            {
                int textIdx = body.IndexOf("\"text\":");
                if (textIdx >= 0)
                {
                    int start = textIdx + 7;
                    return ExtractJsonString(body, start);
                }
                return null;
            }
            // OpenAI format
            int contentIdx = body.IndexOf("\"content\":");
            if (contentIdx < 0) return null;
            int valStart = contentIdx + 10;
            return ExtractJsonString(body, valStart);
        }

        private static string ExtractJsonString(string json, int start)
        {
            while (start < json.Length && json[start] != '"') start++;
            if (start >= json.Length) return "";
            start++;
            var sb = new StringBuilder();
            while (start < json.Length)
            {
                char c = json[start++];
                if (c == '"') break;
                if (c == '\\' && start < json.Length)
                {
                    char esc = json[start++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(esc); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // 统一到 Lumina.Core/AI/ChatJson（原 .Replace 链漏控制字符转义；含快速路径）
        private static string EscapeJson(string s) => ChatJson.EscapeJson(s);
    }

    /// <summary>上下文使用情况</summary>
    public struct ContextUsage
    {
        public int Used;
        public int Limit;
        public int Percent;
        public ContextUsage(int u, int l, int p) { Used = u; Limit = l; Percent = p; }
        public string UsedLabel  => ContextCompressor.FormatTokenCount(Used);
        public string LimitLabel => ContextCompressor.FormatTokenCount(Limit);
    }

    /// <summary>auto-compact 操作结果</summary>
    public struct CompactResult
    {
        public bool   Compacted;
        public int    Used;
        public int    Limit;
        public int    Percent;
        public string Note;
        public int    CompactedCount;

        public CompactResult(bool compacted, int used, int limit, int percent, string note, int compactedCount = 0)
        {
            Compacted = compacted;
            Used = used; Limit = limit; Percent = percent;
            Note = note; CompactedCount = compactedCount;
        }

        public string UsedLabel  => ContextCompressor.FormatTokenCount(Used);
        public string LimitLabel => ContextCompressor.FormatTokenCount(Limit);
    }
}
