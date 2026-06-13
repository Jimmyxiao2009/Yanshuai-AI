// CharaSource.cs — 多平台角色卡下载适配器
// 每个平台一个静态类，统一返回 CharacterCard

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════════
    // 搜索结果模型
    // ══════════════════════════════════════════════════════════════════════════

    public class CharaSearchResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string AvatarUrl { get; set; }
        public string Source { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // JSON 辅助方法（所有平台共享）
    // ══════════════════════════════════════════════════════════════════════════

    internal static class JsonHelper
    {
        internal static string ExtractJsonString(string json, string key)
        {
            var match = Regex.Match(json,
                "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.Singleline);
            if (!match.Success) return null;
            return Regex.Unescape(match.Groups[1].Value);
        }

        internal static int FindMatchingBrace(string text, int start)
        {
            if (start >= text.Length || text[start] != '{') return -1;
            int depth = 1;
            bool inString = false;
            for (int i = start + 1; i < text.Length; i++)
            {
                if (inString)
                {
                    if (text[i] == '\\') { i++; continue; }
                    if (text[i] == '"') inString = false;
                    continue;
                }
                if (text[i] == '"') { inString = true; continue; }
                if (text[i] == '{') depth++;
                if (text[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        internal static int FindMatchingArray(string text, int start)
        {
            if (start >= text.Length || text[start] != '[') return -1;
            int depth = 1;
            bool inString = false;
            for (int i = start + 1; i < text.Length; i++)
            {
                if (inString)
                {
                    if (text[i] == '\\') { i++; continue; }
                    if (text[i] == '"') inString = false;
                    continue;
                }
                if (text[i] == '"') { inString = true; continue; }
                if (text[i] == '[') depth++;
                if (text[i] == ']') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        internal static string ExtractJsonArray(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int arrStart = json.IndexOf('[', keyIdx);
            if (arrStart < 0) return null;
            int arrEnd = FindMatchingArray(json, arrStart);
            if (arrEnd < 0) return null;
            return json.Substring(arrStart, arrEnd - arrStart + 1);
        }

        internal static string ExtractJsonObject(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIdx < 0) return null;
            int objStart = json.IndexOf('{', keyIdx);
            if (objStart < 0) return null;
            int objEnd = FindMatchingBrace(json, objStart);
            if (objEnd < 0) return null;
            return json.Substring(objStart, objEnd - objStart + 1);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 通用工具方法（所有平台共享）
    // ══════════════════════════════════════════════════════════════════════════

    internal class ImageDownloadResult
    {
        public string Base64 { get; set; }
        public string MimeType { get; set; }
    }

    internal static class SourceUtil
    {
        internal static string HtmlDecode(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                    .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&#x27;", "'")
                    .Replace("&#x2F;", "/").Replace("&nbsp;", " ");
        }

        internal static async Task<ImageDownloadResult> DownloadImageAsBase64Async(
            HttpClient http, string imageUrl, string referer = null)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || http == null)
                return null;

            // 补全相对协议 URL（//cdn.example.com/img.jpg）
            if (imageUrl.StartsWith("//"))
                imageUrl = "https:" + imageUrl;

            if (!imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, imageUrl);
                if (!string.IsNullOrEmpty(referer))
                    req.Headers.TryAddWithoutValidation("Referer", referer);
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes == null || bytes.Length < 64) return null;

                return new ImageDownloadResult
                {
                    Base64 = Convert.ToBase64String(bytes),
                    MimeType = resp.Content.Headers.ContentType?.MediaType ?? "image/jpeg"
                };
            }
            catch
            {
                return null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 通用 URL 直链下载
    // ══════════════════════════════════════════════════════════════════════════

}
