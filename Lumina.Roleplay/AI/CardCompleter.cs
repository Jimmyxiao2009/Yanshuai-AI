using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace yanshuai
{
    // Response models for Claude non-streaming format
    [DataContract] internal class ClaudeContentBlock  { [DataMember(Name = "text")] public string Text { get; set; } }
    [DataContract] internal class ClaudeResponseMessage
    {
        [DataMember(Name = "content")] public List<ClaudeContentBlock> Content { get; set; }
    }

    // JSON payload expected from LLM
    [DataContract]
    internal class CompletedFields
    {
        [DataMember(Name = "first_message")] public string first_message { get; set; } = "";
        [DataMember(Name = "personality")]   public string personality   { get; set; } = "";
        [DataMember(Name = "scenario")]      public string scenario      { get; set; } = "";
        [DataMember(Name = "system_prompt")] public string system_prompt { get; set; } = "";
    }

    /// <summary>
    /// Sends a non-streaming request to the configured API profile to fill in
    /// missing fields (first_message / personality / scenario / system_prompt)
    /// based on existing card info.
    /// </summary>
    public static class CardCompleter
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        public static async Task<CharacterCard> CompleteCardAsync(CharacterCard card, string instruction = null)
        {
            if (card == null) return null;

            bool hasInstruction = !string.IsNullOrWhiteSpace(instruction);

            var profile = AppSettings.GetSubAgentProfile()
                ?? DataManager.GetProfileForConversation(null);
            if (profile == null || string.IsNullOrEmpty(profile.ApiKey) || string.IsNullOrEmpty(profile.Url))
            {
                System.Diagnostics.Debug.WriteLine("CardCompleter: no valid API profile");
                return card;
            }

            string systemPrompt = "";
            if (hasInstruction)
            {
                systemPrompt = "你是一个角色卡调整专家。根据用户的指令，修改角色卡对应字段。\n"
                    + "规则：\n"
                    + "1. 只修改用户指令涉及的字段，其他字段保持原样不变。\n"
                    + "2. 严格输出 JSON，不要其他文字、不要 markdown 代码块。\n"
                    + "3. 输出的 JSON 必须包含所有四个字段，未修改的字段原样抄回。\n"
                    + "用户指令：\"" + instruction.Trim() + "\"\n"
                    + "各字段要求：\n"
                    + "- first_message：以角色口吻写一段有画面感的开场白（50-200字），说话用「」，动作用* *。\n"
                    + "- personality：性格特征、语气、行为习惯、好恶。\n"
                    + "- scenario：当前场景的时间、地点、氛围。\n"
                    + "- system_prompt：角色扮演规则与回应要求。\n"
                    + "输出格式：\n"
                    + "{\"first_message\":\"...\",\"personality\":\"...\","
                    + "\"scenario\":\"...\",\"system_prompt\":\"...\"}";
            }
            else
            {
                systemPrompt = "你是一个角色卡补全与润色专家。基于给出的角色信息，补全缺失字段。\n"
                    + "规则：\n"
                    + "1. 已有字段保持原样，不修改。只补全空白字段。\n"
                    + "2. 严格输出 JSON，不要其他文字、不要 markdown 代码块。\n"
                    + "各字段要求：\n"
                    + "- first_message：以角色口吻写一段有画面感的开场白（50-200字）。包含场景描写和第一句对话，让用户立即进入角色扮演。说话用「」，动作用* *。\n"
                    + "- personality：性格特征、语气、行为习惯、好恶（100-300字）。\n"
                    + "- scenario：当前场景的时间、地点、氛围（50-200字）。\n"
                    + "- system_prompt：角色扮演规则与回应要求（100-500字），包含行为限制、语言风格、角色一致性要求等。\n"
                    + "输出格式：\n"
                    + "{\"first_message\":\"...\",\"personality\":\"...\","
                    + "\"scenario\":\"...\",\"system_prompt\":\"...\"}";
            }

            string userContent = $"## 角色名称\n{card.Name}\n\n"
                + (string.IsNullOrEmpty(card.Tags) ? "" : $"## 标签\n{card.Tags}\n\n")
                + $"## 角色描述\n{card.Description}\n\n"
                + (string.IsNullOrEmpty(card.CreatorNotes) ? "" : $"## 详细设定\n{card.CreatorNotes}\n\n")
                + (string.IsNullOrEmpty(card.Personality) ? "" : $"## 已有性格设定\n{card.Personality}\n\n")
                + (string.IsNullOrEmpty(card.Scenario) ? "" : $"## 已有情境设定\n{card.Scenario}\n\n")
                + (string.IsNullOrEmpty(card.SystemPrompt) ? "" : $"## 已有系统提示词\n{card.SystemPrompt}\n\n")
                + (string.IsNullOrEmpty(card.FirstMessage) ? "" : $"## 已有开场白\n{card.FirstMessage}");

            string requestJson = BuildRequest(profile.Model, systemPrompt, userContent, profile.ProviderType == "claude");

            try
            {
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
                    string body = await resp.Content.ReadAsStringAsync();
                    string raw = ExtractText(body, profile.ProviderType == "claude");
                    var fields = ParseJson(raw);
                    if (fields != null)
                    {
                        if (!string.IsNullOrEmpty(fields.first_message))
                            card.FirstMessage = hasInstruction ? fields.first_message
                                : (string.IsNullOrEmpty(card.FirstMessage) ? fields.first_message : card.FirstMessage);
                        if (!string.IsNullOrEmpty(fields.personality))
                            card.Personality = hasInstruction ? fields.personality
                                : (string.IsNullOrEmpty(card.Personality) ? fields.personality : card.Personality);
                        if (!string.IsNullOrEmpty(fields.scenario))
                            card.Scenario = hasInstruction ? fields.scenario
                                : (string.IsNullOrEmpty(card.Scenario) ? fields.scenario : card.Scenario);
                        if (!string.IsNullOrEmpty(fields.system_prompt))
                            card.SystemPrompt = hasInstruction ? fields.system_prompt
                                : (string.IsNullOrEmpty(card.SystemPrompt) ? fields.system_prompt : card.SystemPrompt);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CardCompleter error: " + ex.Message);
            }

            return card;
        }

        private static string BuildRequest(string model, string system, string userContent, bool isClaude)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append("\"stream\":false,");

            if (isClaude)
            {
                sb.Append("\"max_tokens\":4096,");
                sb.Append($"\"system\":\"{EscapeJson(system)}\",");
                sb.Append("\"messages\":[");
                sb.Append("{\"role\":\"user\",");
                sb.Append($"\"content\":\"{EscapeJson(userContent)}\"");
                sb.Append("}");
                sb.Append("]");
            }
            else
            {
                sb.Append("\"messages\":[");
                sb.Append("{\"role\":\"system\",");
                sb.Append($"\"content\":\"{EscapeJson(system)}\"");
                sb.Append("},");
                sb.Append("{\"role\":\"user\",");
                sb.Append($"\"content\":\"{EscapeJson(userContent)}\"");
                sb.Append("}");
                sb.Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string ExtractText(string body, bool isClaude)
        {
            if (string.IsNullOrEmpty(body)) return null;

            // OpenAI format: choices[0].message.content
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                {
                    var resp = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                    string openAiContent = resp?.Choices?.Count > 0 ? resp.Choices[0]?.Message?.Content : null;
                    if (!string.IsNullOrEmpty(openAiContent)) return openAiContent;
                }
            }
            catch { }

            // Claude format: content[0].text
            if (isClaude)
            {
                try
                {
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                    {
                        var resp = (ClaudeResponseMessage)new DataContractJsonSerializer(typeof(ClaudeResponseMessage)).ReadObject(ms);
                        if (resp?.Content?.Count > 0 && !string.IsNullOrEmpty(resp.Content[0].Text))
                            return resp.Content[0].Text;
                    }
                }
                catch { }
            }

            // Gemini format fallback
            string gemini = ExtractGeminiText(body);
            if (!string.IsNullOrEmpty(gemini)) return gemini;

            return body;
        }

        private static CompletedFields ParseJson(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            // Try direct deserialization
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(raw)))
                {
                    return (CompletedFields)new DataContractJsonSerializer(typeof(CompletedFields)).ReadObject(ms);
                }
            }
            catch { }

            // Fallback: try to find JSON object in the text (in case LLM wrapped it in markdown)
            int braceStart = raw.IndexOf('{');
            int braceEnd = raw.LastIndexOf('}');
            if (braceStart >= 0 && braceEnd > braceStart)
            {
                string json = raw.Substring(braceStart, braceEnd - braceStart + 1);
                try
                {
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        return (CompletedFields)new DataContractJsonSerializer(typeof(CompletedFields)).ReadObject(ms);
                    }
                }
                catch { }
            }

            return null;
        }

        // 与 Lumina.Core/AI/ChatJson.EscapeJson 逐字节相同，委托共享实现
        internal static string EscapeJson(string s) => ChatJson.EscapeJson(s);

        internal static string ExtractGeminiText(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int candIdx = json.IndexOf("\"candidates\"");
            if (candIdx < 0) return null;
            int partsIdx = json.IndexOf("\"parts\"", candIdx);
            if (partsIdx < 0) return null;
            int textIdx = json.IndexOf("\"text\":", partsIdx);
            if (textIdx < 0) return null;
            return ExtractJsonString(json, textIdx + 7);
        }

        // 统一到 Lumina.Core/AI/ChatJson。原本地副本不解码转义（\\n 当字面量 n），
        // 导致 ExtractGeminiText 解析 Gemini 响应时换行等转义丢失——已随共享实现修复。
        internal static string ExtractJsonString(string json, int start) => ChatJson.ExtractJsonString(json, start);
    }
}
