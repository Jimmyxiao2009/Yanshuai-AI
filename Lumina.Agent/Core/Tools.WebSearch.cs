using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.ApplicationModel.Appointments;
using Windows.ApplicationModel.Contacts;
using Windows.System;
using Windows.System.Profile;

namespace yanshuai
{
    public static partial class FunctionCallEngine
    {
        private static async Task<string> ExecuteWebSearch(string argsJson, Conversation conv)
        {
            var query = ExtractJsonString(argsJson, "query");
            if (string.IsNullOrWhiteSpace(query))
                return "错误：搜索关键词为空";
            return await RunSearchAsync(query);
        }

        private static async Task<string> ExecuteFetchPage(string argsJson)
        {
            var url = ExtractJsonString(argsJson, "url");
            if (string.IsNullOrWhiteSpace(url))
                return "错误：URL 为空";

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                string host = uri.Host.ToLower();
                if (host == "google.com" || host.EndsWith(".google.com") ||
                    host == "google.com.hk" || host.EndsWith(".google.com.hk"))
                    return "错误：禁止访问 Google 域名，请使用其他搜索引擎。";
            }

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", AppSettings.FetchUserAgent);
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode)
                        return $"无法访问页面（{(int)resp.StatusCode}）";

                    string html = await resp.Content.ReadAsStringAsync();
                    html = _reScript.Replace(html, "");
                    html = _reStyle.Replace(html, "");
                    var text = _reTags.Replace(html, " ");
                    text = _reSpaces.Replace(text, "\n").Trim();

                    int depth = AppSettings.SearchResultDepth;
                    int limit = depth == 0 ? 2000 : depth == 1 ? 8000 : int.MaxValue;
                    return text.Length > limit ? text.Substring(0, limit) + "\n…（已截断）" : text;
                }
            }
            catch (Exception ex)
            {
                return $"Fetch 失败：{ex.Message}";
            }
        }

        // ── 搜索 API 池 ────────────────────────────────────────────────────

        private static async Task<List<SearchApiEntry>> GetSearchPoolAsync()
        {
            if (_searchApiPool != null) return _searchApiPool;
            var saved = await AppSettings.LoadSearchApisAsync();
            if (saved != null && saved.Count > 0)
                _searchApiPool = saved;
            else
                _searchApiPool = SearchSettingsPage.BuildDefaultEntriesPublic();
            return _searchApiPool;
        }

        private static async Task<List<string>> GetUsableSearxngAsync(IList<SearchApiEntry> pool)
        {
            if (_cachedSearxngUrls != null && (DateTime.Now - _searxngCacheTime).TotalMinutes < 15)
                return _cachedSearxngUrls;

            var instances = pool.Where(e => e.Type == "searxng" && e.Enabled && !string.IsNullOrEmpty(e.Value))
                                .Select(e => e.Value).ToArray();
            if (instances.Length == 0) return null;

            var results = new List<UrlLatency>(); var tasks = new List<Task>();
            var sem = new SemaphoreSlim(6);
            for (int i = 0; i < instances.Length; i++) { string inst = instances[i]; tasks.Add(Task.Run(async () => { await sem.WaitAsync(); try { var sw = Stopwatch.StartNew(); var hreq = new HttpRequestMessage(HttpMethod.Get, inst + "/search?q=test&format=json"); hreq.Headers.TryAddWithoutValidation("Accept", "application/json"); using (var c = new CancellationTokenSource(6000)) using (var r = await _http.SendAsync(hreq, c.Token)) { sw.Stop(); if ((int)r.StatusCode < 500) { lock (results) results.Add(new UrlLatency { Url = inst, Ms = sw.ElapsedMilliseconds }); } } } catch { } finally { sem.Release(); } })); }

            if (results.Count > 0)
            {
                _cachedSearxngUrls = results.OrderBy(r => r.Ms).Select(r => r.Url).ToList();
                _searxngCacheTime = DateTime.Now;
            }
            return _cachedSearxngUrls;
        }

        private static async Task<string> RunSearchAsync(string query)
        {
            var errors = new List<string>();
            try
            {
                var pool = await GetSearchPoolAsync();
                var enabled = pool.Where(e => e.Enabled).ToList();
                if (enabled.Count == 0) return "[搜索失败：未启用任何搜索源]";
                // 1. Tavily
                var tv = enabled.FirstOrDefault(e => e.Type == "tavily" && !string.IsNullOrEmpty(e.Value));
                if (tv != null) { try { string r = await RunTavilySearchAsync(query, tv.Value); if (!string.IsNullOrEmpty(r)) return r; } catch (Exception ex) { errors.Add("Tavily: " + ex.Message); } }
                // 2. DDG
                if (enabled.Any(e => e.Type == "ddg")) { try { var dreq = new HttpRequestMessage(HttpMethod.Get, "https://lite.duckduckgo.com/lite/?q=" + Uri.EscapeDataString(query)); dreq.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows Phone 10.0; Lumia 950) AppleWebKit/537.36"); using (var cts = new CancellationTokenSource(12000)) using (var dresp = await _http.SendAsync(dreq, cts.Token)) { if (dresp.IsSuccessStatusCode) { string r = ParseDdgLiteResults(await dresp.Content.ReadAsStringAsync()); if (!string.IsNullOrEmpty(r)) return r; } else errors.Add("DDG: " + (int)dresp.StatusCode); } } catch (Exception ex) { errors.Add("DDG: " + ex.Message); } }
                // 3. Bing
                var bi = enabled.FirstOrDefault(e => e.Type == "bing" && !string.IsNullOrEmpty(e.Value));
                if (bi != null) { try { string r = await RunBingSearchAsync(query, bi.Value); if (!string.IsNullOrEmpty(r)) return r; } catch (Exception ex) { errors.Add("Bing: " + ex.Message); } }
                // 4. SearXNG
                if (enabled.Any(e => e.Type == "searxng" && !string.IsNullOrEmpty(e.Value))) { var usable = await GetUsableSearxngAsync(pool); if (usable != null && usable.Count > 0) { foreach (string u in usable) { try { var url = u.TrimEnd('/') + "/search?q=" + Uri.EscapeDataString(query) + "&format=json&pageno=1"; var sreq = new HttpRequestMessage(HttpMethod.Get, url); sreq.Headers.TryAddWithoutValidation("Accept", "application/json"); using (var c = new CancellationTokenSource(10000)) using (var sr = await _http.SendAsync(sreq, c.Token)) { if (sr.IsSuccessStatusCode) { string r = ParseSearxngResults(await sr.Content.ReadAsStringAsync()); if (!string.IsNullOrEmpty(r)) return r; } } } catch (Exception ex) { errors.Add("SearXNG: " + ex.Message); } } } else errors.Add("SearXNG: 不可达"); }
                string d = errors.Count > 0 ? "\n详情：" + string.Join("; ", errors.Take(3)) : "";
                return "[无结果" + d + "]";
            }
            catch (Exception ex) { return $"[搜索异常：{ex.Message}]"; }
        }

        private static async Task<string> RunBingSearchAsync(string query, string subscriptionKey)
        {
            try
            {
                var url = "https://api.bing.microsoft.com/v7.0/search?q=" + Uri.EscapeDataString(query) + "&mkt=zh-CN&count=5";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", subscriptionKey);
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    return ParseBingResults(await resp.Content.ReadAsStringAsync());
                }
            }
            catch { return null; }
        }

        private static string ParseBingResults(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var sb = new StringBuilder();
            int idx = json.IndexOf("\"value\":");
            if (idx < 0) return null;
            int searchFrom = idx;
            int count = 0;
            while (count < 5)
            {
                int nameIdx = json.IndexOf("\"name\":", searchFrom);
                if (nameIdx < 0) break;
                string title = ExtractJsonString(json, nameIdx + 7);
                int urlIdx = json.IndexOf("\"url\":", nameIdx);
                if (urlIdx < 0) break;
                string url = ExtractJsonString(json, urlIdx + 6);
                int snipIdx = json.IndexOf("\"snippet\":", nameIdx);
                string snippet = snipIdx >= 0 ? ExtractJsonString(json, snipIdx + 10) : "";
                sb.AppendLine($"- {title}\n  {snippet}\n  {url}");
                searchFrom = urlIdx + 6;
                count++;
                if (sb.Length > 2000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string ParseSearxngResults(string json)
        {
            var sb = new StringBuilder();
            int arrStart = json.IndexOf("\"results\":");
            if (arrStart < 0) return null;
            int idx = json.IndexOf('[', arrStart);
            if (idx < 0) return null;

            int count = 0;
            while (count < 8)
            {
                int braceStart = json.IndexOf('{', idx);
                if (braceStart < 0) break;
                int braceEnd = json.IndexOf('}', braceStart);
                if (braceEnd < 0) break;

                string block = json.Substring(braceStart, braceEnd - braceStart + 1);
                string title   = ExtractJsonValue(block, "title");
                string content = ExtractJsonValue(block, "content");
                string url     = ExtractJsonValue(block, "url");

                if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(content))
                {
                    sb.AppendLine($"- {title}\n  {content}\n  {url}");
                    count++;
                }
                idx = braceEnd + 1;
                if (sb.Length > 3000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string ParseDdgLiteResults(string html)
        {
            var sb = new StringBuilder();
            int idx = 0;
            int count = 0;
            while (count < 5)
            {
                int linkIdx = html.IndexOf("<a rel=\"nofollow\"", idx);
                if (linkIdx < 0) break;
                int hrefStart = html.IndexOf("href=\"", linkIdx) + 6;
                if (hrefStart < 6) break;
                int hrefEnd = html.IndexOf('"', hrefStart);
                string url = hrefEnd > hrefStart ? html.Substring(hrefStart, hrefEnd - hrefStart) : "";

                int titleStart = html.IndexOf('>', hrefEnd) + 1;
                int titleEnd   = html.IndexOf("</a>", titleStart);
                string title = titleStart > 0 && titleEnd > titleStart
                    ? StripHtmlTags(html.Substring(titleStart, titleEnd - titleStart)).Trim()
                    : "";

                int snipIdx = html.IndexOf("result-snippet", titleEnd);
                string snippet = "";
                if (snipIdx >= 0 && snipIdx < titleEnd + 500)
                {
                    int snipStart = html.IndexOf('>', snipIdx) + 1;
                    int snipEnd   = html.IndexOf("</td>", snipStart);
                    if (snipStart > 0 && snipEnd > snipStart)
                        snippet = StripHtmlTags(html.Substring(snipStart, snipEnd - snipStart)).Trim();
                }

                if (!string.IsNullOrEmpty(title))
                {
                    sb.AppendLine($"- {title}\n  {snippet}\n  {url}");
                    count++;
                }
                idx = titleEnd > 0 ? titleEnd : linkIdx + 1;
                if (sb.Length > 3000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string StripHtmlTags(string html)
        {
            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ExtractJsonValue(string block, string key)
        {
            int keyIdx = block.IndexOf("\"" + key + "\":");
            if (keyIdx < 0) return "";
            int valStart = keyIdx + key.Length + 3;
            while (valStart < block.Length && block[valStart] == ' ') valStart++;
            if (valStart >= block.Length || block[valStart] != '"')
                return ExtractJsonRawValue(block, valStart);
            valStart++;
            var sb = new StringBuilder();
            while (valStart < block.Length)
            {
                char c = block[valStart++];
                if (c == '"') break;
                if (c == '\\' && valStart < block.Length)
                {
                    char esc = block[valStart++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        default: sb.Append(esc); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string ExtractJsonRawValue(string block, int start)
        {
            int end = start;
            while (end < block.Length && block[end] != ',' && block[end] != '}' && block[end] != ']')
                end++;
            string val = block.Substring(start, end - start).Trim();
            return val == "null" ? "" : val;
        }

        // ── File system tools ──────────────────────────────────────────────────

    }
}
