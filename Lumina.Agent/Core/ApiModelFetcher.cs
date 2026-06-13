using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace yanshuai
{
    /// <summary>
    /// 从 API 供应商获取可用模型列表和模型能力信息
    /// </summary>
    public static class ApiModelFetcher
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>
        /// 获取供应商的模型列表。支持 OpenAI 兼容格式 (/v1/models) 和 Claude。
        /// </summary>
        public static async Task<List<ModelInfo>> FetchModelsAsync(ApiProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.ApiKey))
                return null;

            try
            {
                if (profile.ProviderType == "claude")
                    return GetClaudeModels();

                // OpenAI-compatible: GET /v1/models
                string modelsUrl = GetModelsEndpoint(profile.Url);
                var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    string body = await resp.Content.ReadAsStringAsync();
                    return ParseModelsResponse(body);
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 测试 API 连接是否有效（发送简单请求）
        /// </summary>
        public static async Task<string> TestConnectionAsync(ApiProfile profile)
        {
            if (profile == null) return "配置为空";
            if (string.IsNullOrEmpty(profile.ApiKey)) return "API Key 为空";
            if (string.IsNullOrEmpty(profile.Url)) return "URL 为空";

            try
            {
                string requestJson;
                if (profile.ProviderType == "claude")
                {
                    requestJson = $"{{\"model\":\"{EscapeJson(profile.Model)}\",\"max_tokens\":16,\"messages\":[{{\"role\":\"user\",\"content\":\"hi\"}}]}}";
                }
                else
                {
                    requestJson = $"{{\"model\":\"{EscapeJson(profile.Model)}\",\"stream\":false,\"max_tokens\":16,\"messages\":[{{\"role\":\"user\",\"content\":\"hi\"}}]}}";
                }

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
                    if (resp.IsSuccessStatusCode)
                        return "✓ 连接成功";
                    string body = await resp.Content.ReadAsStringAsync();
                    // Extract error message
                    string errMsg = ExtractErrorMessage(body);
                    return $"✗ HTTP {(int)resp.StatusCode}: {errMsg}";
                }
            }
            catch (TaskCanceledException)
            {
                return "✗ 连接超时";
            }
            catch (Exception ex)
            {
                return $"✗ {ex.Message}";
            }
        }

        /// <summary>自动检测模型是否支持视觉</summary>
        public static bool DetectVisionSupport(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            string lower = modelName.ToLower();
            // Models known to support vision
            if (lower.Contains("vision") || lower.Contains("4o") || lower.Contains("gpt-4.1"))
                return true;
            if (lower.Contains("gemini")) return true;
            if (lower.Contains("claude") && !lower.Contains("instant")) return true;
            if (lower.Contains("pixtral") || lower.Contains("llava")) return true;
            return false;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static string GetModelsEndpoint(string chatUrl)
        {
            // /v1/chat/completions → /v1/models
            if (chatUrl.Contains("/chat/completions"))
                return chatUrl.Replace("/chat/completions", "/models");
            // /v1/messages → try /v1/models
            if (chatUrl.Contains("/messages"))
                return chatUrl.Replace("/messages", "/models");
            // Append /models
            return chatUrl.TrimEnd('/') + "/models";
        }

        private static List<ModelInfo> ParseModelsResponse(string json)
        {
            var models = new List<ModelInfo>();
            // Parse "data": [...] array
            int dataIdx = json.IndexOf("\"data\":");
            if (dataIdx < 0)
            {
                // Some providers return array directly
                dataIdx = json.IndexOf('[');
                if (dataIdx < 0) return models;
            }
            else
            {
                dataIdx = json.IndexOf('[', dataIdx);
                if (dataIdx < 0) return models;
            }

            // Find all "id": "..." entries
            int idx = dataIdx;
            while (true)
            {
                int idIdx = json.IndexOf("\"id\":", idx);
                if (idIdx < 0) break;
                string id = ExtractJsonString(json, idIdx + 5);
                if (!string.IsNullOrEmpty(id) && !id.Contains("embedding") && !id.Contains("tts") && !id.Contains("whisper") && !id.Contains("dall-e"))
                {
                    models.Add(new ModelInfo
                    {
                        Id = id,
                        SupportsVision = DetectVisionSupport(id),
                        ContextLength = ModelContextLimits.GetLimit(id, 0),
                    });
                }
                idx = idIdx + 5;
            }

            return models.OrderBy(m => m.Id).ToList();
        }

        private static List<ModelInfo> GetClaudeModels()
        {
            // Claude models are not listable via API, return known models
            return new List<ModelInfo>
            {
                new ModelInfo { Id = "claude-opus-4-8", SupportsVision = true, ContextLength = 200000 },
                new ModelInfo { Id = "claude-sonnet-4-6", SupportsVision = true, ContextLength = 200000 },
                new ModelInfo { Id = "claude-haiku-4-5-20251001", SupportsVision = true, ContextLength = 200000 },
            };
        }

        private static string ExtractErrorMessage(string body)
        {
            int msgIdx = body.IndexOf("\"message\":");
            if (msgIdx >= 0)
            {
                string msg = ExtractJsonString(body, msgIdx + 10);
                if (!string.IsNullOrEmpty(msg)) return msg.Length > 100 ? msg.Substring(0, 100) : msg;
            }
            return body.Length > 80 ? body.Substring(0, 80) + "…" : body;
        }

        // 统一到 Lumina.Core/AI/ChatJson（原本地副本：ExtractJsonString 丢 \r、
        // EscapeJson 漏 \t 与控制字符转义——均为 bug，已随共享实现修复）
        private static string ExtractJsonString(string json, int start) => ChatJson.ExtractJsonString(json, start);

        private static string EscapeJson(string s) => ChatJson.EscapeJson(s);
    }

    /// <summary>模型信息</summary>
    public class ModelInfo
    {
        public string Id { get; set; }
        public bool SupportsVision { get; set; }
        public int ContextLength { get; set; }

        public string DisplayLabel
        {
            get
            {
                string ctx = ContextLength > 0 ? $" ({ContextLength / 1000}k)" : "";
                string vision = SupportsVision ? " 👁" : "";
                return Id + ctx + vision;
            }
        }
    }
}
