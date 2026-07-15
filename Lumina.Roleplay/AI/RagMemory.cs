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

    public class MemorySummaryResult
    {
        public List<string> Items { get; set; } = new List<string>();
        public int PoolItemCount { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string Error { get; set; } = "";
    }

    public class RagContextResult
    {
        public string Context { get; set; } = "";
        public int HitCount { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string Mode { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public static class MemoryPipeline
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        public static async Task<MemorySummaryResult> SummarizeAndStoreAsync(Conversation conv)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new MemorySummaryResult();

            try
            {
                if (conv == null) return result;

                var memApiId = conv.MemoryApiProfileId;
                var memProfile = string.IsNullOrEmpty(memApiId)
                    ? DataManager.GetProfileForConversation(conv)
                    : DataManager.Data.ApiProfiles.Find(p => p.Id == memApiId);
                if (memProfile == null)
                {
                    result.Error = "没有可用的记忆总结 API";
                    return result;
                }

                int count = conv.MemorySummaryInterval * 2;
                var recent = conv.Messages
                    .Skip(Math.Max(0, conv.Messages.Count - count))
                    .Where(m => m.Role == "user" || m.Role == "assistant")
                    .ToList();
                if (recent.Count == 0) return result;

                var sb = new StringBuilder();
                sb.AppendLine("从以下对话中提取值得记忆的要点。规则：");
                sb.AppendLine("- 只提取角色对用户的新认知、重要事件、用户偏好、关键事实");
                sb.AppendLine("- 不要重复已有的常识性信息");
                sb.AppendLine("- 每条以「-」开头，简洁完整的一句话");
                sb.AppendLine();
                foreach (var m in recent)
                    sb.AppendLine($"{(m.Role == "user" ? "用户" : "AI")}：{m.Content}");

                var payload = new ApiRequest
                {
                    Model = memProfile.Model,
                    Stream = false,
                    Messages = new List<ApiRequestMessage>
                    {
                        new ApiRequestMessage { Role = "user", Content = sb.ToString() }
                    }
                };

                string requestJson;
                using (var ms = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(ApiRequest)).WriteObject(ms, payload);
                    requestJson = Encoding.UTF8.GetString(ms.ToArray());
                }

                var req = new HttpRequestMessage(HttpMethod.Post, memProfile.Url);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {memProfile.ApiKey}");
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        result.Error = "记忆总结请求失败：" + (int)resp.StatusCode;
                        return result;
                    }

                    var body = await resp.Content.ReadAsStringAsync();
                    ApiResponse parsed;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);

                    var text = (parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null) ?? "";
                    result.Items = text.Split('\n')
                        .Select(l => l.TrimStart('-', ' ', '\t').Trim())
                        .Where(l => l.Length > 0)
                        .Distinct()
                        .ToList();
                }

                if (result.Items.Count == 0) return result;

                if (conv.MemoryItems == null) conv.MemoryItems = new List<string>();
                var pool = DialoguePoolManager.GetPool(conv.CharacterCardId, conv.UserProfileId);
                foreach (var item in result.Items)
                {
                    if (!conv.MemoryItems.Contains(item))
                        conv.MemoryItems.Add(item);
                    if (pool != null && pool.Settings.EnableSharedMemory)
                    {
                        int beforeCount = pool.SharedMemories?.Count ?? 0;
                        pool.AddSharedMemory(item, conv.Id, 0.6f);
                        int afterCount = pool.SharedMemories?.Count ?? 0;
                        if (afterCount > beforeCount) result.PoolItemCount++;
                    }
                }

                await DataManager.SaveAsync();
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            }

            return result;
        }

        /// <summary>从最近对话中自动提取深层记忆（人物画像 + 印象 + 互动经历 + 事实），更新到池的 CharacterProfile。</summary>
        public static async Task<string> ExtractDeepMemoryAsync(Conversation conv, DialoguePool pool)
        {
            if (conv == null || pool == null || pool.Profile == null) return null;
            if (!pool.Settings.AutoSummarizeConversations) return null;
            if (conv.Messages.Count < 3) return null;

            var memApiId = conv.MemoryApiProfileId;
            var memProfile = string.IsNullOrEmpty(memApiId)
                ? DataManager.GetProfileForConversation(conv)
                : DataManager.Data.ApiProfiles.Find(p => p.Id == memApiId);
            if (memProfile == null) return null;

            var profile = pool.Profile;
            if (profile.CoreTraits == null) profile.CoreTraits = new List<string>();
            if (profile.ExperienceItems == null) profile.ExperienceItems = new List<string>();
            if (profile.KnownFacts == null) profile.KnownFacts = new List<string>();

            var sb = new StringBuilder();
            sb.AppendLine("你是一个角色认知提取助手。分析以下对话，提取角色对用户的新认知。");
            sb.AppendLine();
            sb.AppendLine("## 现有认知（只补充新增内容，不要重复已有内容）");
            if (!string.IsNullOrWhiteSpace(profile.UserPortrait))
                sb.AppendLine("现有总体认知：" + profile.UserPortrait);
            if (profile.CoreTraits.Count > 0)
                sb.AppendLine("现有印象：" + string.Join("、", profile.CoreTraits));
            if (profile.ExperienceItems.Count > 0)
                sb.AppendLine("现有互动经历：" + string.Join("、", profile.ExperienceItems));
            if (profile.KnownFacts.Count > 0)
                sb.AppendLine("现有事实：" + string.Join("、", profile.KnownFacts));
            sb.AppendLine("当前好感度：" + profile.Favorability + "/100（可根据新对话内容增减）");

            sb.AppendLine();
            sb.AppendLine("## 最近对话");
            var recent = conv.Messages
                .Skip(Math.Max(0, conv.Messages.Count - 20))
                .Where(m => m.Role == "user" || m.Role == "assistant")
                .ToList();
            foreach (var m in recent)
                sb.AppendLine($"【{(m.Role == "user" ? "用户" : "角色")}】{m.Content}");

            sb.AppendLine();
            sb.AppendLine("## 输出格式（严格按此格式，每部分若无新内容则留空该区块标题下方行）：");
            sb.AppendLine("[总体认知]");
            sb.AppendLine("更新后的总体认知描述（一句话概括角色对用户的最新整体认知，如果无需变化则复制现有认知）");
            sb.AppendLine("[印象]");
            sb.AppendLine("新印象条目，每行一条");
            sb.AppendLine("[互动经历]");
            sb.AppendLine("新互动经历，每行一条");
            sb.AppendLine("[事实]");
            sb.AppendLine("新事实条目，每行一条");
            sb.AppendLine("[好感度]");
            sb.AppendLine("根据对话情感基调，给出一个 -10 到 +10 之间的整数好感度变化值（正=更友好，负=更疏远），仅输出数字");

            var payload = new ApiRequest
            {
                Model = memProfile.Model,
                Stream = false,
                Messages = new List<ApiRequestMessage>
                {
                    new ApiRequestMessage { Role = "user", Content = sb.ToString() }
                }
            };

            string requestJson;
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ApiRequest)).WriteObject(ms, payload);
                requestJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            var req = new HttpRequestMessage(HttpMethod.Post, memProfile.Url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {memProfile.ApiKey}");
            req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            string responseText;
            using (var resp = await _http.SendAsync(req))
            {
                if (!resp.IsSuccessStatusCode) return null;
                var body = await resp.Content.ReadAsStringAsync();
                ApiResponse parsed;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                    parsed = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                responseText = (parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null) ?? "";
            }

            if (string.IsNullOrWhiteSpace(responseText)) return null;

            // Parse sections
            var portrait = ExtractSection(responseText, "[总体认知]");
            var traits = ExtractSection(responseText, "[印象]");
            var experiences = ExtractSection(responseText, "[互动经历]");
            var facts = ExtractSection(responseText, "[事实]");

            // Update portrait (replace with new holistic view)
            if (!string.IsNullOrWhiteSpace(portrait))
                profile.UserPortrait = portrait;

            // Merge impressions (add new unique items)
            MergeUniqueItems(profile.CoreTraits, traits);
            // Merge experiences
            MergeUniqueItems(profile.ExperienceItems, experiences);
            // Merge facts
            MergeUniqueItems(profile.KnownFacts, facts);

            // 同时沉淀为池级 RAG 记忆
            if (pool.Settings.EnableSharedMemory)
            {
                var items = new List<string>();
                if (!string.IsNullOrWhiteSpace(portrait))
                    items.Add("角色对我的总体认知：" + portrait);
                if (!string.IsNullOrWhiteSpace(traits))
                    items.AddRange(traits.Split('\n')
                        .Select(l => l.Trim('-', ' ', '\t', '\r').Trim())
                        .Where(l => l.Length > 0)
                        .Select(l => "印象：" + l));
                if (!string.IsNullOrWhiteSpace(experiences))
                    items.AddRange(experiences.Split('\n')
                        .Select(l => l.Trim('-', ' ', '\t', '\r').Trim())
                        .Where(l => l.Length > 0)
                        .Select(l => "互动：" + l));
                if (!string.IsNullOrWhiteSpace(facts))
                    items.AddRange(facts.Split('\n')
                        .Select(l => l.Trim('-', ' ', '\t', '\r').Trim())
                        .Where(l => l.Length > 0)
                        .Select(l => "事实：" + l));
                foreach (var item in items)
                    pool.AddSharedMemory(item, conv.Id, 0.7f);
            }

            profile.LastUpdated = DateTime.Now;

            // 解析好感度变化
            var favText = ExtractSection(responseText, "[好感度]");
            if (!string.IsNullOrWhiteSpace(favText))
            {
                var match = System.Text.RegularExpressions.Regex.Match(favText, @"-?\d+");
                if (match.Success && int.TryParse(match.Value, out int change))
                {
                    // 钳制到文档化的 0–100 区间，避免长期累积漂移到负数或远超 100
                    profile.Favorability = Math.Max(0, Math.Min(100, profile.Favorability + change));
                    profile.FavorabilityTrend = change > 0 ? "up" : change < 0 ? "down" : "stable";
                }
            }

            await DataManager.SaveAsync();

            return responseText;
        }

        private static string ExtractSection(string text, string header)
        {
            string clean = header.Trim('[', ']', '【', '】', ':', '：');
            string[] patterns = { $"[{clean}]", $"【{clean}】", $"{clean}:", $"{clean}：" };

            int idx = -1;
            string matchedPattern = null;
            foreach (var pat in patterns)
            {
                idx = text.IndexOf(pat, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    matchedPattern = pat;
                    break;
                }
            }

            if (idx < 0) return null;
            int start = idx + matchedPattern.Length;
            if (start >= text.Length) return null;

            string[] headers = { "总体认知", "印象", "互动经历", "事实", "好感度" };
            int end = text.Length;
            foreach (var h in headers)
            {
                if (h.Equals(clean, StringComparison.OrdinalIgnoreCase)) continue;
                string[] nextPatterns = { $"[{h}]", $"【{h}】", $"{h}:", $"{h}：" };
                foreach (var np in nextPatterns)
                {
                    int ni = text.IndexOf(np, start, StringComparison.OrdinalIgnoreCase);
                    if (ni >= 0 && ni < end) end = ni;
                }
            }

            var section = text.Substring(start, end - start).Trim();
            if (string.IsNullOrWhiteSpace(section)) return null;

            var lines = section.Split('\n')
                .Select(l => l.Trim('-', ' ', '\t', '\r').Trim())
                .Where(l => l.Length > 0)
                .ToList();

            if (lines.Count == 1 && clean == "总体认知")
                return lines[0];

            if (lines.Count == 0) return null;
            return string.Join("\n", lines);
        }

        private static void MergeUniqueItems(List<string> target, string newItems)
        {
            if (string.IsNullOrWhiteSpace(newItems)) return;
            var items = newItems.Split('\n')
                .Select(l => l.Trim('-', ' ', '\t', '\r').Trim())
                .Where(l => l.Length > 0)
                .ToList();
            foreach (var item in items)
            {
                bool exists = target.Any(t =>
                    string.Equals(t.Trim(), item, StringComparison.OrdinalIgnoreCase) ||
                    t.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!exists)
                    target.Add(item);
            }
        }
    }
}
