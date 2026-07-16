// CharaSource.Huayu — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceHuayu
    {
        private const string ApiBase = "https://girlgirlgirl.xyz/go/api";

        public static async Task<List<CharaSearchResult>> SearchAsync(string query, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(query) || http == null)
                return new List<CharaSearchResult>();

            try
            {
                string url = $"{ApiBase}/explore/search?keywords={Uri.EscapeDataString(query)}&page=1&limit=30&app_type=1&lang=zh-Hans";
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu search: {url}");
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return new List<CharaSearchResult>();

                string json = await resp.Content.ReadAsStringAsync();
                return ParseSearchResults(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu search error: {ex.Message}");
                return new List<CharaSearchResult>();
            }
        }

        public static async Task<List<CharaSearchResult>> BrowseAsync(HttpClient http, int page = 1)
        {
            if (http == null) return new List<CharaSearchResult>();

            try
            {
                string url = $"{ApiBase}/explore/search?keywords=&page={page}&limit=30&app_type=1&lang=zh-Hans";
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu browse: {url}");
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return new List<CharaSearchResult>();

                string json = await resp.Content.ReadAsStringAsync();
                return ParseSearchResults(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu browse error: {ex.Message}");
                return new List<CharaSearchResult>();
            }
        }

        public static async Task<CharacterCard> DownloadAsync(string urlOrId, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(urlOrId) || http == null) return null;

            try
            {
                string id = urlOrId;
                var idMatch = Regex.Match(urlOrId, @"(?:explore/installed/|apps/)([a-f0-9\-]{32,})", RegexOptions.IgnoreCase);
                if (idMatch.Success)
                    id = idMatch.Groups[1].Value;

                string apiUrl = $"{ApiBase}/apps/{id}";
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu download: {apiUrl}");
                var resp = await http.GetAsync(apiUrl);
                if (!resp.IsSuccessStatusCode) return null;

                string body = await resp.Content.ReadAsStringAsync();

                // 检测 HTML 响应（花屿部分角色页返回 HTML 而非 JSON）
                string trimmed = body.TrimStart();
                if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine("[CharaSource] Huayu: got HTML response, extracting basic info");
                    return await ParseHtmlPage(body, http);
                }

                return await ParseCharacterDetail(body, http);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu download error: {ex.Message}");
                return null;
            }
        }

        private static async Task<CharacterCard> ParseHtmlPage(string html, HttpClient http)
        {
            try
            {
                var card = new CharacterCard();
                const string huayuReferer = "https://girlgirlgirl.xyz/";

                // 1. 从 <title> 提取名称
                var titleMatch = Regex.Match(html,
                    @"<title>([^<]+)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (titleMatch.Success)
                {
                    string title = titleMatch.Groups[1].Value.Trim();
                    int dot = title.IndexOf("·", StringComparison.Ordinal);
                    if (dot > 0) title = title.Substring(0, dot).Trim();
                    card.Name = title;
                }
                card.Name = string.IsNullOrWhiteSpace(card.Name) ? "Unknown" : card.Name;

                // 2. 从 og:description 或 .content div 提取描述
                var ogDesc = Regex.Match(html,
                    @"<meta\s+property=""og:description""\s+content=""([^""]*)""",
                    RegexOptions.IgnoreCase);
                if (ogDesc.Success)
                {
                    card.Description = SourceUtil.HtmlDecode(ogDesc.Groups[1].Value);
                }
                else
                {
                    var contentBuilder = new System.Text.StringBuilder();
                    var contentMatches = Regex.Matches(html,
                        @"<div[^>]*class=""content""[^>]*>(.*?)</div>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    foreach (System.Text.RegularExpressions.Match m in contentMatches)
                    {
                        string text = Regex.Replace(m.Groups[1].Value, @"<[^>]+>", " ", RegexOptions.Singleline);
                        text = Regex.Replace(text, @"\s+", " ").Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            text = SourceUtil.HtmlDecode(text);
                            if (contentBuilder.Length > 0) contentBuilder.Append("\n\n");
                            contentBuilder.Append(text);
                        }
                    }
                    card.Description = contentBuilder.ToString();
                }

                // 3. 从 og:image 提取头像
                var ogImg = Regex.Match(html,
                    @"<meta\s+(?:property=""og:image""|name=""og:image"")\s+content=""([^""]*)""",
                    RegexOptions.IgnoreCase);
                if (!ogImg.Success)
                    ogImg = Regex.Match(html,
                        @"<meta\s+content=""([^""]+)""\s+property=""og:image""",
                        RegexOptions.IgnoreCase);
                string coverUrl = ogImg.Success ? SourceUtil.HtmlDecode(ogImg.Groups[1].Value) : null;
                if (!string.IsNullOrWhiteSpace(coverUrl))
                {
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, coverUrl, huayuReferer);
                    if (img != null) { card.AvatarBase64 = img.Base64; card.AvatarMimeType = img.MimeType; }
                }

                // 4. 立绘：优先找角色图（排除 favicon、logo、icon、avatar 等 UI 小图）
                // 匹配 src 或 data-src，跳过明显的 UI 元素图
                string illustUrl = null;
                var imgMatches = Regex.Matches(html,
                    @"<img[^>]+(?:src|data-src)=""([^""]+)""",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (System.Text.RegularExpressions.Match m in imgMatches)
                {
                    string u = SourceUtil.HtmlDecode(m.Groups[1].Value);
                    if (string.IsNullOrWhiteSpace(u)) continue;
                    if (u.StartsWith("//")) u = "https:" + u;
                    // 跳过 favicon、logo、icon、头像缩略图、data URI
                    if (u.StartsWith("data:")) continue;
                    if (Regex.IsMatch(u, @"favicon|/logo|/icon|\.ico|avatar|thumb|_small|_mini",
                        RegexOptions.IgnoreCase)) continue;
                    // 跳过与封面相同的图
                    if (!string.IsNullOrEmpty(coverUrl) &&
                        u.Equals(coverUrl, StringComparison.OrdinalIgnoreCase)) continue;
                    illustUrl = u;
                    break;
                }
                if (!string.IsNullOrWhiteSpace(illustUrl))
                {
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, illustUrl, huayuReferer);
                    if (img != null) { card.IllustrationBase64 = img.Base64; card.IllustrationMimeType = img.MimeType; }
                }



                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu HTML parsed: name='{card.Name}', hasAvatar={card.HasAvatar}, hasIllust={card.HasIllustration}");
                return card;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Huayu HTML parse error: {ex.Message}");
                return null;
            }
        }

        private static List<CharaSearchResult> ParseSearchResults(string json)
        {
            var list = new List<CharaSearchResult>();
            try
            {
                string appsSection = JsonHelper.ExtractJsonArray(json, "apps");
                if (appsSection == null) return list;

                int idx = 0;
                while (idx < appsSection.Length)
                {
                    int objStart = appsSection.IndexOf('{', idx);
                    if (objStart < 0) break;
                    int objEnd = JsonHelper.FindMatchingBrace(appsSection, objStart);
                    if (objEnd < 0) break;

                    var obj = appsSection.Substring(objStart, objEnd - objStart + 1);
                    var item = ParseSearchItem(obj);
                    if (item != null) list.Add(item);
                    idx = objEnd + 1;
                }
            }
            catch { }
            return list;
        }

        private static CharaSearchResult ParseSearchItem(string json)
        {
            try
            {
                string id = JsonHelper.ExtractJsonString(json, "id");
                string name = JsonHelper.ExtractJsonString(json, "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    return null;

                return new CharaSearchResult
                {
                    Id = id,
                    Name = name,
                    Description = JsonHelper.ExtractJsonString(json, "summary") ?? "",
                    AvatarUrl = JsonHelper.ExtractJsonString(json, "cover") ?? "",
                    Source = "huayu"
                };
            }
            catch { return null; }
        }

        private static async Task<CharacterCard> ParseCharacterDetail(string json, HttpClient http)
        {
            try
            {
                string appsSection = JsonHelper.ExtractJsonObject(json, "apps");
                if (appsSection == null) appsSection = json;

                var tagsList = new List<string>();
                string tagsArray = JsonHelper.ExtractJsonArray(appsSection, "tags");
                if (tagsArray != null)
                {
                    int ti = 0;
                    while ((ti = tagsArray.IndexOf("\"name\"", ti, StringComparison.Ordinal)) >= 0)
                    {
                        int tagNameStart = tagsArray.IndexOf('"', ti + 7);
                        if (tagNameStart < 0) break;
                        int tagNameEnd = tagsArray.IndexOf('"', tagNameStart + 1);
                        if (tagNameEnd < 0) break;
                        string tag = tagsArray.Substring(tagNameStart + 1, tagNameEnd - tagNameStart - 1);
                        if (!string.IsNullOrWhiteSpace(tag)) tagsList.Add(tag);
                        ti = tagNameEnd + 1;
                    }
                }

                var card = new CharacterCard
                {
                    Name = JsonHelper.ExtractJsonString(appsSection, "name") ?? "Unknown",
                    Description = JsonHelper.ExtractJsonString(appsSection, "summary") ?? "",
                    CreatorNotes = JsonHelper.ExtractJsonString(appsSection, "description") ?? "",
                    Creator = JsonHelper.ExtractJsonString(appsSection, "account_name") ?? "",
                    Tags = string.Join(", ", tagsList),
                };

                const string huayuReferer = "https://girlgirlgirl.xyz/";

                string coverUrl = JsonHelper.ExtractJsonString(appsSection, "cover");
                if (!string.IsNullOrWhiteSpace(coverUrl))
                {
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, coverUrl, huayuReferer);
                    if (img != null) { card.AvatarBase64 = img.Base64; card.AvatarMimeType = img.MimeType; }
                }

                // 立绘：从 description HTML 中提取第一张图
                string descHtml = JsonHelper.ExtractJsonString(appsSection, "description");
                if (!string.IsNullOrWhiteSpace(descHtml))
                {
                    // 匹配 src="..." 或 src='...'，支持 data-src 懒加载
                    var imgMatch = Regex.Match(descHtml,
                        @"<img[^>]+(?:src|data-src)=(?:(?:""([^""]+)"")|(?:'([^']+)'))",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    string illustUrl = imgMatch.Groups[1].Success ? imgMatch.Groups[1].Value
                                     : (imgMatch.Groups[2].Success ? imgMatch.Groups[2].Value : null);
                    if (!string.IsNullOrWhiteSpace(illustUrl))
                    {
                        var img = await SourceUtil.DownloadImageAsBase64Async(http, illustUrl, huayuReferer);
                        if (img != null) { card.IllustrationBase64 = img.Base64; card.IllustrationMimeType = img.MimeType; }
                    }
                }



                return card;
            }
            catch { return null; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 星野 (m.xingyeai.com) — 获取详情无需认证，搜索暂不可用
    // ══════════════════════════════════════════════════════════════════════════

}
