using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace yanshuai
{
    /// <summary>
    /// PLAA 模型 API 客户端。
    /// 连接 westd 服务器上部署的 Qwen-3-4B-PLAA 模型，
    /// 发送聊天请求并读取返回的 S_t / e_mem 潜状态。
    /// </summary>
    public class PlaaApiClient
    {
        private readonly HttpClient _http;
        private string _baseUrl;

        public PlaaApiClient()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _baseUrl = AppSettings.PlaaServerUrl;
        }

        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = value?.TrimEnd('/') ?? "";
        }

        public string ApiKey { get; set; }

        /// <summary>
        /// 向 PLAA 模型发送一次对话请求。
        /// </summary>
        /// <param name="messages">OpenAI 格式的 messages 数组</param>
        /// <param name="systemPrompt">角色 system prompt</param>
        /// <returns>PLAA 响应（含文本 + 潜状态）</returns>
        public async Task<PlaaResponse> SendAsync(List<ChatMessage> messages, string systemPrompt = "")
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = "qwen3-4b-plaa",
                ["messages"] = BuildMessages(messages, systemPrompt),
                ["stream"] = false,
                ["return_latent"] = true,      // PLAA 专用：返回 S_t / e_mem
                ["max_tokens"] = 512,
                ["temperature"] = 0.7,
            };

            var json = Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/chat/completions")
            {
                Content = content,
            };
            if (!string.IsNullOrEmpty(ApiKey))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");

            var resp = await _http.SendAsync(request);
            resp.EnsureSuccessStatusCode();

            var respJson = await resp.Content.ReadAsStringAsync();
            return ParsePlaaResponse(respJson);
        }

        /// <summary>
        /// 支持 SSE 流式 + 潜状态推送的版本（用于实时显示）。
        /// </summary>
        public async Task<PlaaResponse> SendStreamAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            Action<string> onToken,
            Action<string> onLatentUpdate)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = "qwen3-4b-plaa",
                ["messages"] = BuildMessages(messages, systemPrompt),
                ["stream"] = true,
                ["return_latent"] = true,
                ["max_tokens"] = 512,
                ["temperature"] = 0.7,
            };

            var json = Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/chat/completions")
            {
                Content = content,
            };
            if (!string.IsNullOrEmpty(ApiKey))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");

            using (var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                using (var stream = await resp.Content.ReadAsStreamAsync())
                using (var reader = new System.IO.StreamReader(stream))
                {
                    var fullText = new StringBuilder();
                    string latentState = "";
                    int promptTokens = 0, completionTokens = 0, cachedTokens = 0, totalTokens = 0;

                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;
                        // SSE 规范：data: 后的单个空格可选，部分网关发 data:{...}（无空格）
                        if (line.StartsWith("data:"))
                        {
                            var data = line.Substring(5);
                            if (data.StartsWith(" ")) data = data.Substring(1);
                            data = data.Trim();
                            if (data == "[DONE]") break;
                            try
                            {
                                var chunk = ParseStreamChunk(data);
                                if (chunk != null)
                                {
                                    if (!string.IsNullOrEmpty(chunk.Text))
                                    {
                                        onToken?.Invoke(chunk.Text);
                                        fullText.Append(chunk.Text);
                                    }
                                    if (!string.IsNullOrEmpty(chunk.LatentStateJson))
                                    {
                                        onLatentUpdate?.Invoke(chunk.LatentStateJson);
                                        latentState = chunk.LatentStateJson;
                                    }
                                    // usage 通常只在末帧（或 include_usage 的最终块）上报一次，
                                    // 不是逐帧累加值；取 max 会被中间的偏大/部分值卡住。
                                    // 改为「任一字段有上报即整体覆盖」，与 MainPage.Response.cs 的「末值生效」一致。
                                    if (chunk.TotalTokens > 0 || chunk.PromptTokens > 0 || chunk.CompletionTokens > 0)
                                    {
                                        promptTokens     = chunk.PromptTokens;
                                        completionTokens = chunk.CompletionTokens;
                                        cachedTokens     = chunk.CachedTokens;
                                        totalTokens      = chunk.TotalTokens;
                                    }
                                }
                            }
                            catch { /* skip malformed chunks */ }
                        }
                    }

                    return new PlaaResponse
                    {
                        Text = fullText.ToString(),
                        LatentStateJson = latentState,
                        PromptTokens     = promptTokens,
                        CompletionTokens = completionTokens,
                        CachedTokens     = cachedTokens,
                        TotalTokens      = totalTokens,
                    };
                }
            }
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private List<Dictionary<string, string>> BuildMessages(List<ChatMessage> messages, string systemPrompt)
        {
            var result = new List<Dictionary<string, string>>();
            if (!string.IsNullOrEmpty(systemPrompt))
                result.Add(new Dictionary<string, string> { ["role"] = "system", ["content"] = systemPrompt });
            foreach (var m in messages)
                result.Add(new Dictionary<string, string> { ["role"] = m.Role, ["content"] = m.Content });
            return result;
        }

        private string Serialize(Dictionary<string, object> body)
        {
            var root = new JsonObject();
            foreach (var kv in body)
            {
                if (kv.Value is string s)
                    root[kv.Key] = JsonValue.CreateStringValue(s);
                else if (kv.Value is bool b)
                    root[kv.Key] = JsonValue.CreateBooleanValue(b);
                else if (kv.Value is int i)
                    root[kv.Key] = JsonValue.CreateNumberValue(i);
                else if (kv.Value is double d)
                    root[kv.Key] = JsonValue.CreateNumberValue(d);
                else if (kv.Value is List<Dictionary<string, string>> msgList)
                {
                    var arr = new JsonArray();
                    foreach (var msg in msgList)
                    {
                        var obj = new JsonObject();
                        foreach (var p in msg)
                            obj[p.Key] = JsonValue.CreateStringValue(p.Value ?? "");
                        arr.Add(obj);
                    }
                    root[kv.Key] = arr;
                }
            }
            return root.Stringify();
        }

        private PlaaResponse ParsePlaaResponse(string json)
        {
            try
            {
                var root = JsonObject.Parse(json);
                var choices = root.GetNamedArray("choices", new JsonArray());
                string text = "";
                string latent = "";
                if (choices.Count > 0)
                {
                    var msg = choices[0].GetObject().GetNamedObject("message", new JsonObject());
                    text   = msg.GetNamedString("content", "");
                    latent = msg.GetNamedString("latent_state_json", "");
                }
                var usage = ParseUsage(root);
                return new PlaaResponse
                {
                    Text = text,
                    LatentStateJson = latent,
                    PromptTokens    = usage.PromptTokens,
                    CompletionTokens= usage.CompletionTokens,
                    CachedTokens    = usage.CachedTokens,
                    TotalTokens     = usage.TotalTokens,
                };
            }
            catch (Exception ex)
            {
                return new PlaaResponse { Text = $"[Parse Error: {ex.Message}]" };
            }
        }

        private PlaaResponse ParseStreamChunk(string data)
        {
            var root = JsonObject.Parse(data);
            var choices = root.GetNamedArray("choices", new JsonArray());
            string text   = "";
            string latent = "";
            if (choices.Count > 0)
            {
                var delta = choices[0].GetObject().GetNamedObject("delta", new JsonObject());
                text   = delta.GetNamedString("content", "");
                latent = delta.GetNamedString("latent_state_json", "");
            }
            // 末帧通常带 finish_reason + usage block
            var usage = ParseUsage(root);
            return new PlaaResponse
            {
                Text             = text,
                LatentStateJson  = latent,
                PromptTokens     = usage.PromptTokens,
                CompletionTokens = usage.CompletionTokens,
                CachedTokens     = usage.CachedTokens,
                TotalTokens      = usage.TotalTokens,
            };
        }

        /// <summary>
        /// 解析 OpenAI 风格 / Anthropic 风格的 usage 块。
        /// OpenAI: usage.prompt_tokens / completion_tokens / total_tokens
        ///         usage.prompt_tokens_details.cached_tokens
        /// Anthropic: usage.input_tokens / output_tokens / cache_read_input_tokens
        /// </summary>
        private static UsageInfo ParseUsage(JsonObject root)
        {
            var u = new UsageInfo();
            if (root == null) return u;
            if (!root.ContainsKey("usage")) return u;
            var usage = root.GetNamedObject("usage", new JsonObject());

            // OpenAI 兼容字段
            u.PromptTokens     = (int)usage.GetNamedNumber("prompt_tokens", 0);
            u.CompletionTokens = (int)usage.GetNamedNumber("completion_tokens", 0);
            u.TotalTokens      = (int)usage.GetNamedNumber("total_tokens", 0);

            // Anthropic 风格（fallback）
            if (u.PromptTokens == 0)     u.PromptTokens     = (int)usage.GetNamedNumber("input_tokens", 0);
            if (u.CompletionTokens == 0) u.CompletionTokens = (int)usage.GetNamedNumber("output_tokens", 0);

            // OpenAI: usage.prompt_tokens_details.cached_tokens
            if (usage.ContainsKey("prompt_tokens_details"))
            {
                var details = usage.GetNamedObject("prompt_tokens_details", new JsonObject());
                u.CachedTokens = (int)details.GetNamedNumber("cached_tokens", 0);
            }
            // Anthropic: usage.cache_read_input_tokens
            if (u.CachedTokens == 0)
                u.CachedTokens = (int)usage.GetNamedNumber("cache_read_input_tokens", 0);

            if (u.TotalTokens == 0 && (u.PromptTokens > 0 || u.CompletionTokens > 0))
                u.TotalTokens = u.PromptTokens + u.CompletionTokens;

            return u;
        }

        private struct UsageInfo
        {
            public int PromptTokens;
            public int CompletionTokens;
            public int CachedTokens;
            public int TotalTokens;
        }

        // ── 数据模型 ───────────────────────────────────────────────────────

        public class PlaaResponse
        {
            public string Text { get; set; } = "";
            public string LatentStateJson { get; set; } = "";
            public bool HasLatent => !string.IsNullOrEmpty(LatentStateJson);
            /// <summary>提示 token（来自 usage）</summary>
            public int PromptTokens { get; set; } = 0;
            /// <summary>补全 token（来自 usage）</summary>
            public int CompletionTokens { get; set; } = 0;
            /// <summary>提示命中缓存的 token（OpenAI prompt_tokens_details.cached_tokens / Anthropic cache_read_input_tokens）</summary>
            public int CachedTokens { get; set; } = 0;
            /// <summary>本次响应总 token</summary>
            public int TotalTokens { get; set; } = 0;
        }

        public class ChatMessage
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }
    }
}
