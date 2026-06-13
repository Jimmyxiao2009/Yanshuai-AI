using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace yanshuai
{
    /// <summary>
    /// 记忆管理器 — 全局记忆 + 项目记忆的提取、构建、编辑
    /// </summary>
    public static class MemoryManager
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // ── 记忆提取 ──────────────────────────────────────────────────────

        /// <summary>
        /// 从最近对话中自动提取记忆条目，存入全局记忆和/或项目记忆。
        /// </summary>
        public static async Task AutoExtractMemoryAsync(
            Conversation conv, ApiProfile profile, int recentCount = 10)
        {
            if (conv == null || profile == null) return;
            if (conv.Messages.Count < 4) return;

            var recent = conv.Messages
                .Skip(Math.Max(0, conv.Messages.Count - recentCount))
                .Where(m => m.Role == "user" || m.Role == "assistant")
                .ToList();
            if (recent.Count < 2) return;

            string extracted = await CallExtractMemoryAsync(recent, profile);
            if (string.IsNullOrEmpty(extracted)) return;

            var newItems = ParseMemoryItems(extracted, conv.Id);
            if (newItems.Count == 0) return;

            // 存入全局记忆
            if (DataManager.Data.GlobalMemories == null)
                DataManager.Data.GlobalMemories = new List<MemoryItem>();

            // 去重用 HashSet（O(n)）替代逐条 .Any 线性扫描（O(n²)）；并对 null Text 安全
            // （原 m.Text.Equals(...) 在已存在记忆 Text 为 null 时会 NPE）
            var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in DataManager.Data.GlobalMemories) globalSeen.Add(m.Text ?? "");
            foreach (var item in newItems)
            {
                if (globalSeen.Add(item.Text ?? ""))   // Add 返回 false=已存在
                    DataManager.Data.GlobalMemories.Add(item);
            }

            // 如果对话属于某个项目，也存入项目记忆
            if (!string.IsNullOrEmpty(conv.ProjectId))
            {
                var project = DataManager.Data.Projects?.FirstOrDefault(p => p.Id == conv.ProjectId);
                if (project != null)
                {
                    if (project.ProjectMemories == null)
                        project.ProjectMemories = new List<MemoryItem>();
                    var projSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in project.ProjectMemories) projSeen.Add(m.Text ?? "");
                    foreach (var item in newItems)
                    {
                        if (projSeen.Add(item.Text ?? ""))
                        {
                            var copy = new MemoryItem
                            {
                                Text = item.Text,
                                Category = item.Category,
                                Importance = item.Importance,
                                SourceConversationId = item.SourceConversationId,
                            };
                            project.ProjectMemories.Add(copy);
                        }
                    }
                }
            }

            // 全局记忆上限 500 条，超出淘汰最旧最不重要的
            if (DataManager.Data.GlobalMemories.Count > 500)
            {
                DataManager.Data.GlobalMemories = DataManager.Data.GlobalMemories
                    .OrderByDescending(m => m.Importance * 0.7 + (m.Timestamp > DateTime.Now.AddDays(-7) ? 0.3 : 0))
                    .Take(400).ToList();
            }

            await DataManager.SaveAsync();
        }

        // ── 记忆上下文构建 ────────────────────────────────────────────────

        /// <summary>
        /// 构建记忆上下文文本（全局 + 项目），注入到 system prompt。
        /// </summary>
        public static string BuildMemoryContext(string projectId = null, string userInput = null)
        {
            var sb = new StringBuilder();
            var memories = new List<MemoryItem>();

            // 全局记忆
            if (DataManager.Data.GlobalMemories != null && DataManager.Data.GlobalMemories.Count > 0)
                memories.AddRange(DataManager.Data.GlobalMemories);

            // 项目记忆
            if (!string.IsNullOrEmpty(projectId))
            {
                var project = DataManager.Data.Projects?.FirstOrDefault(p => p.Id == projectId);
                if (project?.ProjectMemories != null && project.ProjectMemories.Count > 0)
                    memories.AddRange(project.ProjectMemories);
            }

            if (memories.Count == 0) return null;

            // 如果有用户输入，按相关性排序（简单关键词匹配）
            List<MemoryItem> relevant;
            if (!string.IsNullOrEmpty(userInput))
            {
                relevant = memories
                    .Select(m => new { Item = m, Score = ComputeRelevance(m.Text, userInput) + m.Importance * 0.3 })
                    .OrderByDescending(x => x.Score)
                    .Take(15)
                    .Where(x => x.Score > 0.1)
                    .Select(x => x.Item)
                    .ToList();
            }
            else
            {
                relevant = memories
                    .OrderByDescending(m => m.Importance)
                    .Take(10)
                    .ToList();
            }

            if (relevant.Count == 0) return null;

            sb.AppendLine("【长期记忆】你记住了以下关于用户的信息：");
            foreach (var m in relevant)
            {
                string cat = string.IsNullOrEmpty(m.Category) ? "" : $"[{m.Category}] ";
                sb.AppendLine($"- {cat}{m.Text}");
            }

            return sb.ToString();
        }

        /// <summary>获取全局记忆列表（供编辑页使用）</summary>
        public static List<MemoryItem> GetGlobalMemories()
            => DataManager.Data.GlobalMemories ?? new List<MemoryItem>();

        /// <summary>删除一条全局记忆</summary>
        public static void RemoveGlobalMemory(string id)
        {
            DataManager.Data.GlobalMemories?.RemoveAll(m => m.Id == id);
        }

        /// <summary>编辑一条全局记忆</summary>
        public static void UpdateGlobalMemory(string id, string newText)
        {
            var item = DataManager.Data.GlobalMemories?.FirstOrDefault(m => m.Id == id);
            if (item != null) item.Text = newText;
        }

        /// <summary>手动添加一条全局记忆</summary>
        public static void AddGlobalMemory(string text, string category = "fact")
        {
            if (DataManager.Data.GlobalMemories == null)
                DataManager.Data.GlobalMemories = new List<MemoryItem>();
            DataManager.Data.GlobalMemories.Add(new MemoryItem
            {
                Text = text,
                Category = category,
                Importance = 0.8,
            });
        }

        // ── 内部方法 ──────────────────────────────────────────────────────

        private static double ComputeRelevance(string memoryText, string query)
        {
            if (string.IsNullOrEmpty(memoryText) || string.IsNullOrEmpty(query)) return 0;
            string memLower = memoryText.ToLower();
            string queryLower = query.ToLower();

            // 简单关键词重叠计算
            var queryWords = queryLower.Split(new[] { ' ', '，', '。', '？', '！', '\n' },
                StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
            if (queryWords.Length == 0) return 0;

            int matches = queryWords.Count(w => memLower.Contains(w));
            return (double)matches / queryWords.Length;
        }

        private static async Task<string> CallExtractMemoryAsync(
            List<ConversationMessage> messages, ApiProfile profile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("从以下对话中提取值得长期记住的信息。");
            sb.AppendLine("提取规则：");
            sb.AppendLine("- 用户的偏好、习惯、个人信息");
            sb.AppendLine("- 重要的事实或决定");
            sb.AppendLine("- 用户明确要求记住的内容");
            sb.AppendLine("- 不要提取临时性的、无意义的信息");
            sb.AppendLine();
            sb.AppendLine("输出格式（每行一条，以类别标签开头）：");
            sb.AppendLine("[preference] 用户喜欢...");
            sb.AppendLine("[fact] 用户的工作是...");
            sb.AppendLine("[event] 用户计划...");
            sb.AppendLine();
            sb.AppendLine("如果没有值得记忆的内容，输出：无");
            sb.AppendLine();
            sb.AppendLine("对话记录：");
            foreach (var m in messages)
            {
                string role = m.Role == "user" ? "用户" : "AI";
                string content = m.Content ?? "";
                if (content.Length > 300)
                    content = content.Substring(0, 300) + "…";
                sb.AppendLine($"{role}：{content}");
            }

            try
            {
                string requestJson = BuildSimpleRequest(profile.Model, sb.ToString(),
                    profile.ProviderType == "claude");

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

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    var body = await resp.Content.ReadAsStringAsync();
                    return ExtractContent(body, profile.ProviderType == "claude");
                }
            }
            catch { return null; }
        }

        private static List<MemoryItem> ParseMemoryItems(string extracted, string convId)
        {
            var items = new List<MemoryItem>();
            if (string.IsNullOrEmpty(extracted) || extracted.Trim() == "无") return items;

            foreach (var line in extracted.Split('\n'))
            {
                var trimmed = line.Trim().TrimStart('-', ' ');
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed == "无") continue;

                string category = "general";
                string text = trimmed;

                // 解析 [category] 前缀
                if (trimmed.StartsWith("[") && trimmed.IndexOf(']') > 0)
                {
                    int end = trimmed.IndexOf(']');
                    category = trimmed.Substring(1, end - 1).ToLower().Trim();
                    text = trimmed.Substring(end + 1).Trim();
                }

                if (text.Length < 3) continue; // 太短无意义

                items.Add(new MemoryItem
                {
                    Text = text,
                    Category = category,
                    Importance = category == "preference" ? 0.8 : category == "fact" ? 0.7 : 0.6,
                    SourceConversationId = convId,
                });
            }
            return items;
        }

        private static string BuildSimpleRequest(string model, string userMessage, bool isClaude)
        {
            string escaped = EscapeJson(userMessage);
            if (isClaude)
                return $"{{\"model\":\"{EscapeJson(model)}\",\"max_tokens\":1024,\"messages\":[{{\"role\":\"user\",\"content\":\"{escaped}\"}}]}}";
            return $"{{\"model\":\"{EscapeJson(model)}\",\"stream\":false,\"messages\":[{{\"role\":\"user\",\"content\":\"{escaped}\"}}]}}";
        }

        private static string ExtractContent(string body, bool isClaude)
        {
            if (isClaude)
            {
                int textIdx = body.IndexOf("\"text\":");
                if (textIdx >= 0) return ExtractJsonString(body, textIdx + 7);
                return null;
            }
            int contentIdx = body.IndexOf("\"content\":");
            if (contentIdx < 0) return null;
            return ExtractJsonString(body, contentIdx + 10);
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
}
