using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════════
    // ChatJson — 共享的聊天 API 请求 JSON 构建工具（Agent / Roleplay 共用）
    // 原本在两个项目的 MainPage 里各有一份逐字节相同的实现，现下沉到 Lumina.Core。
    // 依赖的 ApiRequestMessage 类型由各 app 自行定义（成员形状兼容），随 Shared
    // Project 在各自程序集内编译时绑定。
    // ══════════════════════════════════════════════════════════════════════════
    internal static class ChatJson
    {
        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // 快速路径：先扫描，无需转义则直接返回原串——这是 payload 构建最热的分配路径
            // （对每条消息/工具的每个字段、每个 agent 轮次都会调用），常见情况下零分配。
            int n = s.Length;
            bool needs = false;
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (c == '\\' || c == '"' || c < 0x20) { needs = true; break; }
            }
            if (!needs) return s;

            var r = new StringBuilder(n + 16);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if      (c == '\\') r.Append("\\\\");
                else if (c == '"')  r.Append("\\\"");
                else if (c == '\n') r.Append("\\n");
                else if (c == '\r') r.Append("\\r");
                else if (c == '\t') r.Append("\\t");
                else if (c < 0x20) r.Append($"\\u{(int)c:x4}"); // 其他控制字符
                else                r.Append(c);
            }
            return r.ToString();
        }

        /// <summary>从 JSON 字符串中按偏移读取引号包裹的值，并解码 \n \r \t 等转义。
        /// 统一自多处分叉副本（ApiModelFetcher 原丢 \r；CardCompleter 原不解码转义、
        /// 致 Gemini 响应里的 \n 变成字面量 n）。</summary>
        public static string ExtractJsonString(string json, int start)
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
                        default:  sb.Append(esc);  break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string BuildRequestJson(string model, List<ApiRequestMessage> messages, bool stream, bool supportsVision = false, bool isClaudeProvider = false)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append($"\"stream\":{(stream ? "true" : "false")},");

            if (isClaudeProvider)
            {
                sb.Append("\"max_tokens\":8192,");
                // Claude API: system 提取为顶层字段，messages 只含 user/assistant
                var sysMsgs  = messages.Where(m => m.Role == "system").ToList();
                var chatMsgs = messages.Where(m => m.Role != "system").ToList();
                if (sysMsgs.Count > 0)
                {
                    string sysContent = string.Join("\n\n", sysMsgs.Select(m => m.Content));
                    sb.Append($"\"system\":\"{EscapeJson(sysContent)}\",");
                }
                sb.Append("\"messages\":[");
                for (int i = 0; i < chatMsgs.Count; i++)
                {
                    var m = chatMsgs[i];
                    sb.Append("{");
                    sb.Append($"\"role\":\"{EscapeJson(m.Role)}\",");
                    if (supportsVision && !string.IsNullOrEmpty(m.ImageBase64) && m.Role == "user")
                    {
                        // Claude multimodal: content block array
                        sb.Append("\"content\":[");
                        sb.Append("{\"type\":\"image\",\"source\":{");
                        sb.Append($"\"type\":\"base64\",\"media_type\":\"{EscapeJson(m.ImageMimeType)}\",");
                        sb.Append($"\"data\":\"{m.ImageBase64}\"");
                        sb.Append("}},");
                        sb.Append("{\"type\":\"text\",");
                        sb.Append($"\"text\":\"{EscapeJson(m.Content)}\"");
                        sb.Append("}]");
                    }
                    else
                    {
                        sb.Append($"\"content\":\"{EscapeJson(m.Content)}\"");
                    }
                    sb.Append("}");
                    if (i < chatMsgs.Count - 1) sb.Append(",");
                }
                sb.Append("]");
            }
            else
            {
                sb.Append("\"messages\":[");
                for (int i = 0; i < messages.Count; i++)
                {
                    var m = messages[i];
                    sb.Append("{");
                    sb.Append($"\"role\":\"{EscapeJson(m.Role)}\",");
                    if (supportsVision && !string.IsNullOrEmpty(m.ImageBase64) && m.Role == "user")
                    {
                        // OpenAI multimodal: content is array
                        sb.Append("\"content\":[");
                        sb.Append("{");
                        sb.Append("\"type\":\"image_url\",");
                        sb.Append("\"image_url\":{");
                        sb.Append($"\"url\":\"data:{EscapeJson(m.ImageMimeType)};base64,{m.ImageBase64}\"");
                        sb.Append("}");
                        sb.Append("},");
                        sb.Append("{");
                        sb.Append("\"type\":\"text\",");
                        sb.Append($"\"text\":\"{EscapeJson(m.Content)}\"");
                        sb.Append("}");
                        sb.Append("]");
                    }
                    else
                    {
                        sb.Append($"\"content\":\"{EscapeJson(m.Content)}\"");
                    }
                    sb.Append("}");
                    if (i < messages.Count - 1) sb.Append(",");
                }
                sb.Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        public static string ExtractClaudeText(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            // 尝试流式 content_block_delta
            int deltaIdx = json.IndexOf("\"delta\"");
            if (deltaIdx >= 0)
            {
                int textIdx = json.IndexOf("\"text\":", deltaIdx);
                if (textIdx >= 0)
                    return ExtractJsonString(json, textIdx + 7);
            }
            // 尝试非流式 content array
            int contentIdx = json.IndexOf("\"content\"");
            if (contentIdx >= 0)
            {
                int textIdx = json.IndexOf("\"text\":", contentIdx);
                if (textIdx >= 0)
                    return ExtractJsonString(json, textIdx + 7);
            }
            return null;
        }
    }
}
