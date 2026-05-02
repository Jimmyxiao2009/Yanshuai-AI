using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════
    // Tool 定义数据模型
    // ══════════════════════════════════════════════════════════════════════

    [DataContract]
    public class ToolParameterProperty
    {
        [DataMember(Name = "type")]        public string Type        { get; set; } = "string";
        [DataMember(Name = "description")] public string Description { get; set; } = "";
    }

    [DataContract]
    public class ToolParameters
    {
        [DataMember(Name = "type")]       public string       Type       { get; set; } = "object";
        [DataMember(Name = "properties")] public Dictionary<string, ToolParameterProperty> Properties { get; set; }
            = new Dictionary<string, ToolParameterProperty>();
        [DataMember(Name = "required")]   public List<string> Required   { get; set; } = new List<string>();
    }

    [DataContract]
    public class ToolFunction
    {
        [DataMember(Name = "name")]        public string        Name        { get; set; }
        [DataMember(Name = "description")] public string        Description { get; set; }
        [DataMember(Name = "parameters")]  public ToolParameters Parameters { get; set; }
    }

    [DataContract]
    public class ToolDefinition
    {
        [DataMember(Name = "type")]     public string      Type     { get; set; } = "function";
        [DataMember(Name = "function")] public ToolFunction Function { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Tool Call 响应模型（API 返回）
    // ══════════════════════════════════════════════════════════════════════

    [DataContract]
    public class ToolCallFunction
    {
        [DataMember(Name = "name")]      public string Name      { get; set; }
        [DataMember(Name = "arguments")] public string Arguments { get; set; }
    }

    [DataContract]
    public class ToolCall
    {
        [DataMember(Name = "id")]       public string           Id       { get; set; }
        [DataMember(Name = "type")]     public string           Type     { get; set; } = "function";
        [DataMember(Name = "function")] public ToolCallFunction Function { get; set; }
    }

    /// <summary>
    /// 扩展 ApiRequestMessage 以支持 tool_calls 和 tool 类型消息
    /// </summary>
    [DataContract]
    public class ApiMessageWithTools
    {
        [DataMember(Name = "role")]             public string      Role      { get; set; }
        [DataMember(Name = "content")]          public string      Content   { get; set; }
        [DataMember(Name = "tool_calls")]       public List<ToolCall> ToolCalls { get; set; }
        [DataMember(Name = "tool_call_id")]     public string      ToolCallId { get; set; }

        // 非序列化辅助
        public string ImageBase64   { get; set; }
        public string ImageMimeType { get; set; }
    }

    [DataContract]
    public class ToolChoiceMessage
    {
        [DataMember(Name = "role")]       public string      Role       { get; set; }
        [DataMember(Name = "content")]    public string      Content    { get; set; }
        [DataMember(Name = "tool_calls")] public List<ToolCall> ToolCalls { get; set; }
    }

    [DataContract]
    public class ToolResponseChoice
    {
        [DataMember(Name = "index")]  public int               Index   { get; set; }
        [DataMember(Name = "message")] public ToolChoiceMessage Message { get; set; }
        [DataMember(Name = "finish_reason")] public string     FinishReason { get; set; }
    }

    [DataContract]
    public class ToolApiResponse
    {
        [DataMember(Name = "choices")] public List<ToolResponseChoice> Choices { get; set; }
        [DataMember(Name = "error")]   public ApiErrorDetail           Error   { get; set; }
        [DataMember(Name = "usage")]  public object                   Usage   { get; set; }
    }

    public class FunctionCallLoopResult
    {
        public string Content { get; set; }
        public string Reasoning { get; set; }
        public List<ApiMessageWithTools> AllMessages { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Function Calling 引擎
    // ══════════════════════════════════════════════════════════════════════

    public static class FunctionCallEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>注册的所有工具</summary>
        public static List<ToolDefinition> GetTools()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "web_search",
                        Description = "搜索互联网获取实时信息，如新闻、天气、百科知识等。无法打开 Google。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["query"] = new ToolParameterProperty { Type = "string", Description = "搜索关键词，简短精确" },
                                ["max_results"] = new ToolParameterProperty { Type = "string", Description = "返回结果数量，默认5" },
                            },
                            Required = new List<string> { "query" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "fetch_page",
                        Description = "读取指定 URL 的网页正文内容。不能用于 google.com。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["url"] = new ToolParameterProperty { Type = "string", Description = "要读取的完整 URL" },
                            },
                            Required = new List<string> { "url" }
                        }
                    }
                },
            };
        }

        /// <summary>
        /// 执行单个工具调用，返回 tool 消息
        /// </summary>
        public static async Task<ApiMessageWithTools> ExecuteToolAsync(ToolCall call, Conversation conv)
        {
            var result = new ApiMessageWithTools
            {
                Role = "tool",
                ToolCallId = call.Id,
                Content = ""
            };

            try
            {
                string name = call.Function?.Name ?? "";
                string argsJson = call.Function?.Arguments ?? "{}";

                switch (name)
                {
                    case "web_search":
                        result.Content = await ExecuteWebSearch(argsJson, conv);
                        break;
                    case "fetch_page":
                        result.Content = await ExecuteFetchPage(argsJson);
                        break;
                    default:
                        result.Content = $"错误：未知工具 \"{name}\"";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Content = $"工具执行出错：{ex.Message}";
            }

            return result;
        }

        private static async Task<string> ExecuteWebSearch(string argsJson, Conversation conv)
        {
            var query = ExtractJsonString(argsJson, "query");
            var maxResultsStr = ExtractJsonString(argsJson, "max_results");
            int maxResults = 5;
            if (!string.IsNullOrEmpty(maxResultsStr))
                int.TryParse(maxResultsStr, out maxResults);

            if (string.IsNullOrWhiteSpace(query))
                return "错误：搜索关键词为空";

            // 复用 MainPage 的搜索逻辑（通过 SearchSettingsPage）
            var results = await SearchAsync(query, maxResults);
            return FormatSearchResults(results, query);
        }

        private static async Task<string> ExecuteFetchPage(string argsJson)
        {
            var url = ExtractJsonString(argsJson, "url");
            if (string.IsNullOrWhiteSpace(url))
                return "错误：URL 为空";

            // 阻止抓取 Google（防止被墙）
            if (url.IndexOf("google.com", StringComparison.OrdinalIgnoreCase) >= 0)
                return "错误：不允许抓取 Google 域名";

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode)
                        return $"HTTP {(int)resp.StatusCode}：{resp.ReasonPhrase}";

                    string html = await resp.Content.ReadAsStringAsync();
                    // 简单提取正文（去掉 HTML 标签）
                    var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
                    text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                    text = text.Trim();

                    if (text.Length > 8000)
                        text = text.Substring(0, 8000) + "\n\n[内容过长，已截断]";

                    return text;
                }
            }
            catch (Exception ex)
            {
                return $"抓取失败：{ex.Message}";
            }
        }

        // ── 搜索逻辑（提取自 MainPage）─────────────────────────────────────

        private static async Task<List<(string Title, string Snippet, string Url)>> SearchAsync(string query, int maxResults)
        {
            var results = new List<(string, string, string)>();

            try
            {
                // 默认用 SearXNG
                string baseUrl = AppSettings.SearchBaseUrl;
                if (string.IsNullOrWhiteSpace(baseUrl))
                    baseUrl = "https://searx.be";

                string searchUrl = $"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json&language=zh-CN";
                var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                var resp = await _http.SendAsync(req);

                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    results = ParseSearxngResults(json);
                }
            }
            catch
            {
                // 降级到 DuckDuckGo 非官方 API
                try
                {
                    string ddgUrl = $"https://lite.duckduckgo.com/lite/?q={Uri.EscapeDataString(query)}";
                    var req = new HttpRequestMessage(HttpMethod.Get, ddgUrl);
                    var resp = await _http.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        string html = await resp.Content.ReadAsStringAsync();
                        results = ParseDdgResults(html);
                    }
                }
                catch { }
            }

            return results.Take(maxResults).ToList();
        }

        private static List<(string, string, string)> ParseSearxngResults(string json)
        {
            var results = new List<(string, string, string)>();
            try
            {
                // 简单的 JSON 解析（不依赖反序列化）
var resultsSection = json.Split(new[] { "\"results\":[" }, StringSplitOptions.None);
                if (resultsSection.Length < 2) return results;

                int end = resultsSection[1].IndexOf(']');
                if (end < 0) return results;

                var items = resultsSection[1].Substring(0, end).Split(new[] { "}," }, StringSplitOptions.None);
                foreach (var item in items)
                {
                    string title   = ExtractJsonString(item, "title");
                    string content = ExtractJsonString(item, "content");
                    string url     = ExtractJsonString(item, "url");
                    if (!string.IsNullOrEmpty(title))
                        results.Add((title, content, url));
                }
            }
            catch { }
            return results;
        }

        private static List<(string, string, string)> ParseDdgResults(string html)
        {
            var results = new List<(string, string, string)>();
            try
            {
                var lines = html.Split('\n');
                for (int i = 0; i < lines.Length - 4; i++)
                {
                    if (lines[i].Contains("class=\"result-link\"") && i + 4 < lines.Length)
                    {
                        var titleMatch = System.Text.RegularExpressions.Regex.Match(lines[i], ">(.*?)</a>");
                        var urlMatch = System.Text.RegularExpressions.Regex.Match(lines[i + 2], ">(.*?)<");
                        var snippetMatch = System.Text.RegularExpressions.Regex.Match(lines[i + 4], ">(.*?)<");
                        string title = titleMatch.Success ? titleMatch.Groups[1].Value : "";
                        string url = urlMatch.Success ? urlMatch.Groups[1].Value : "";
                        string snippet = snippetMatch.Success ? snippetMatch.Groups[1].Value : "";
                        if (!string.IsNullOrEmpty(title))
                            results.Add((title, snippet, url));
                    }
                }
            }
            catch { }
            return results;
        }

        private static string FormatSearchResults(List<(string Title, string Snippet, string Url)> results, string query)
        {
            if (results.Count == 0)
                return $"搜索 \"{query}\" 未找到结果。";

            var sb = new StringBuilder();
            sb.AppendLine($"以下是 \"{query}\" 的搜索结果：");
            sb.AppendLine();
            for (int i = 0; i < results.Count; i++)
            {
                sb.AppendLine($"[{i + 1}] {results[i].Title}");
                if (!string.IsNullOrEmpty(results[i].Snippet))
                    sb.AppendLine($"    {results[i].Snippet}");
                if (!string.IsNullOrEmpty(results[i].Url))
                    sb.AppendLine($"    URL: {results[i].Url}");
                sb.AppendLine();
            }
            sb.Append("请根据以上信息回答用户问题。");

            return sb.ToString();
        }

        // ── 辅助 ──────────────────────────────────────────────────────────────

        private static string ExtractJsonString(string json, string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json,
                $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
            if (match.Success)
                return match.Groups[1].Value;

            // 尝试不转义
            var match2 = System.Text.RegularExpressions.Regex.Match(json,
                $"\"{key}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return match2.Success ? match2.Groups[1].Value.Replace("\\\"", "\"").Replace("\\n", "\n") : "";
        }

        /// <summary>
        /// 构建含 tools 的 API 请求 JSON
        /// </summary>
        public static string BuildToolRequestJson(string model, List<ApiMessageWithTools> messages, List<ToolDefinition> tools)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["stream"] = false,
                ["tools"] = tools,
                ["tool_choice"] = "auto"
            };

            using (var ms = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(typeof(Dictionary<string, object>));
                ser.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }

            // 手动构建 JSON （DataContractJsonSerializer 对 Dictionary 序列化不可靠）
        }

        /// <summary>手动构建工具请求 JSON（兼容 UWP）</summary>
        public static string BuildToolRequestJsonManual(string model, List<ApiMessageWithTools> msgs, List<ToolDefinition> tools)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(EscapeJson(model)).Append("\",");
            sb.Append("\"messages\":[");
            sb.Append(string.Join(",", msgs.Select(m => SerializeMessage(m))));
            sb.Append("],");
            sb.Append("\"stream\":false,");
            sb.Append("\"tools\":[");
            sb.Append(string.Join(",", tools.Select(t => SerializeTool(t))));
            sb.Append("],");
            sb.Append("\"tool_choice\":\"auto\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string SerializeMessage(ApiMessageWithTools m)
        {
            var sb = new StringBuilder();
            sb.Append("{\"role\":\"").Append(EscapeJson(m.Role)).Append("\"");
            if (m.Content != null)
                sb.Append(",\"content\":\"").Append(EscapeJson(m.Content)).Append("\"");
            if (m.ToolCallId != null)
                sb.Append(",\"tool_call_id\":\"").Append(EscapeJson(m.ToolCallId)).Append("\"");
            if (m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                sb.Append(",\"tool_calls\":[");
                sb.Append(string.Join(",", m.ToolCalls.Select(tc => SerializeToolCall(tc))));
                sb.Append("]");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string SerializeToolCall(ToolCall tc)
        {
            return $"{{\"id\":\"{EscapeJson(tc.Id)}\",\"type\":\"function\",\"function\":{{\"name\":\"{EscapeJson(tc.Function.Name)}\",\"arguments\":\"{EscapeJson(tc.Function.Arguments)}\"}}}}";
        }

        private static string SerializeTool(ToolDefinition t)
        {
            var func = t.Function;
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"function\",\"function\":{");
            sb.Append("\"name\":\"").Append(EscapeJson(func.Name)).Append("\",");
            sb.Append("\"description\":\"").Append(EscapeJson(func.Description)).Append("\",");
            sb.Append("\"parameters\":{\"type\":\"object\",\"properties\":{");
            var props = func.Parameters.Properties;
            bool first = true;
            foreach (var kv in props)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(EscapeJson(kv.Key)).Append("\":{\"type\":\"").Append(EscapeJson(kv.Value.Type)).Append("\",\"description\":\"").Append(EscapeJson(kv.Value.Description)).Append("\"}");
            }
            sb.Append("},\"required\":[");
            sb.Append(string.Join(",", func.Parameters.Required.Select(r => $"\"{EscapeJson(r)}\"")));
            sb.Append("]}}");
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>
        /// Function Calling 主循环：发送请求→处理工具调用→再发送→直到得到自然语言回答
        /// </summary>
        public static async Task<FunctionCallLoopResult>
            RunFunctionCallLoopAsync(ApiProfile profile, List<ApiMessageWithTools> initialMessages, Conversation conv)
        {
            var allMessages = new List<ApiMessageWithTools>(initialMessages);
            int maxTurns = 5; // 防止无限循环

            for (int turn = 0; turn < maxTurns; turn++)
            {
                string requestJson = BuildToolRequestJsonManual(profile.Model, allMessages, GetTools());

                var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                ToolApiResponse response;
                using (var resp = await _http.SendAsync(req))
                {
                    string body = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        return new FunctionCallLoopResult
                        {
                            Content = $"HTTP {(int)resp.StatusCode}：{body}",
                            Reasoning = "",
                            AllMessages = allMessages
                        };
                    }

                    try
                    {
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        {
                            var ser = new DataContractJsonSerializer(typeof(ToolApiResponse));
                            response = (ToolApiResponse)ser.ReadObject(ms);
                        }
                    }
                    catch
                    {
                        return new FunctionCallLoopResult
                        {
                            Content = body,
                            Reasoning = "",
                            AllMessages = allMessages
                        };
                    }
                }

                if (response?.Choices == null || response.Choices.Count == 0)
                    return new FunctionCallLoopResult
                    {
                        Content = "API 返回为空",
                        Reasoning = "",
                        AllMessages = allMessages
                    };

                var choice = response.Choices[0];
                var msg = choice.Message;

                // 记录 assistant 消息
                var assistantMsg = new ApiMessageWithTools
                {
                    Role = "assistant",
                    Content = msg.Content ?? "",
                    ToolCalls = msg.ToolCalls,
                };
                allMessages.Add(assistantMsg);

                // 检查是否有工具调用
                if (msg.ToolCalls == null || msg.ToolCalls.Count == 0)
                {
                    // 没有工具调用 → 这就是最终回答
                    return new FunctionCallLoopResult
                    {
                        Content = msg.Content ?? "",
                        Reasoning = "",
                        AllMessages = allMessages
                    };
                }

                // 执行所有工具调用
                foreach (var toolCall in msg.ToolCalls)
                {
                    var result = await ExecuteToolAsync(toolCall, conv);
                    allMessages.Add(result);
                }
            }

            return new FunctionCallLoopResult
            {
                Content = "已达到最大工具调用轮数，请简化你的回答",
                Reasoning = "",
                AllMessages = allMessages
            };
        }
    }
}

