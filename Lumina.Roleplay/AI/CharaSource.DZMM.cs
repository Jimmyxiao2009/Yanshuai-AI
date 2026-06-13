// CharaSource.DZMM — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceDZMM
    {
        private const string DefaultMirror = "https://www.duskpine.top";

        public static async Task<List<CharaSearchResult>> SearchAsync(string query, HttpClient http)
        {
            System.Diagnostics.Debug.WriteLine("[CharaSource] DZMM search: not available (needs auth, API returns 401)");
            return new List<CharaSearchResult>();
        }

        public static async Task<List<CharaSearchResult>> BrowseAsync(HttpClient http, int limit = 20)
        {
            // Strategy 1: Try tRPC API
            try
            {
                var results = await TryBrowseApiAsync(http, "newest", limit);
                if (results.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[CharaSource] DZMM browse: got {results.Count} from API");
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] DZMM API browse error: {ex.Message}");
            }

            // Strategy 2: Try homepage RSC parsing
            try
            {
                var results = await BrowseViaHomepageAsync(http, limit);
                if (results.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[CharaSource] DZMM browse: got {results.Count} from homepage");
                    return results;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] DZMM homepage browse error: {ex.Message}");
            }

            return new List<CharaSearchResult>();
        }

        private static async Task<List<CharaSearchResult>> TryBrowseApiAsync(HttpClient http, string type, int limit)
        {
            string input = "{\"json\":{\"type\":\"" + type + "\",\"limit\":" + limit + "}}";
            string url = $"{DefaultMirror}/api/trpc/home.getCards?input={Uri.EscapeDataString(input)}";

            System.Diagnostics.Debug.WriteLine($"[CharaSource] DZMM browse API: {url}");
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return new List<CharaSearchResult>();

            string text = await resp.Content.ReadAsStringAsync();
            return ParseBrowseResults(text);
        }

        private static List<CharaSearchResult> ParseBrowseResults(string text)
        {
            var results = new List<CharaSearchResult>();
            try
            {
                string dataArray = null;

                // Strategy A: "json" key with array value
                dataArray = JsonHelper.ExtractJsonArray(text, "json");

                // Strategy B: "json" key with object value → extract nested array
                if (dataArray == null)
                {
                    string jsonObj = JsonHelper.ExtractJsonObject(text, "json");
                    if (jsonObj != null && jsonObj.Length > 4)
                    {
                        int arrStart = jsonObj.IndexOf('[');
                        if (arrStart >= 0)
                        {
                            int arrEnd = JsonHelper.FindMatchingArray(jsonObj, arrStart);
                            if (arrEnd > 0)
                                dataArray = jsonObj.Substring(arrStart, arrEnd - arrStart + 1);
                        }
                    }
                }

                // Strategy C: "results" key (search format)
                if (dataArray == null || dataArray.Length < 10)
                    dataArray = JsonHelper.ExtractJsonArray(text, "results");

                // Strategy D: "data" key
                if (dataArray == null || dataArray.Length < 10)
                    dataArray = JsonHelper.ExtractJsonArray(text, "data");

                // Strategy E: any top-level array
                if (dataArray == null || dataArray.Length < 10)
                {
                    int arrStart = text.IndexOf('[');
                    if (arrStart >= 0)
                    {
                        int arrEnd = JsonHelper.FindMatchingArray(text, arrStart);
                        if (arrEnd > 0)
                            dataArray = text.Substring(arrStart, arrEnd - arrStart + 1);
                    }
                }

                if (dataArray == null || dataArray.Length < 4) return results;

                int idx = 0;
                while (idx < dataArray.Length)
                {
                    int objStart = dataArray.IndexOf('{', idx);
                    if (objStart < 0) break;
                    int objEnd = JsonHelper.FindMatchingBrace(dataArray, objStart);
                    if (objEnd < 0) break;

                    string obj = dataArray.Substring(objStart, objEnd - objStart + 1);

                    string id = JsonHelper.ExtractJsonString(obj, "id");
                    if (string.IsNullOrEmpty(id))
                    {
                        var idMatch = Regex.Match(obj, "\"id\"\\s*:\\s*(\\d+)");
                        if (idMatch.Success) id = idMatch.Groups[1].Value;
                    }
                    string name = JsonHelper.ExtractJsonString(obj, "name") ?? "";

                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    {
                        results.Add(new CharaSearchResult
                        {
                            Id = $"{DefaultMirror}/character/{id}",
                            Name = name,
                            Description = JsonHelper.ExtractJsonString(obj, "creatorNotes")
                                       ?? JsonHelper.ExtractJsonString(obj, "description") ?? "",
                            AvatarUrl = JsonHelper.ExtractJsonString(obj, "avatarUrl")
                                     ?? JsonHelper.ExtractJsonString(obj, "cardFilename") ?? "",
                            Source = "dzmm"
                        });
                    }

                    idx = objEnd + 1;
                }
            }
            catch { }

            return results;
        }

        private static async Task<List<CharaSearchResult>> BrowseViaHomepageAsync(HttpClient http, int limit = 20)
        {
            string url = $"{DefaultMirror}/";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return new List<CharaSearchResult>();

            string html = await resp.Content.ReadAsStringAsync();

            var rscBuilder = new System.Text.StringBuilder();
            var pushMatches = Regex.Matches(html,
                @"self\.__next_f\.push\(\[[^,]*,\s*""((?:[^""\\]|\\.)*)""\)\]",
                RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in pushMatches)
            {
                try { rscBuilder.Append(Regex.Unescape(m.Groups[1].Value)); }
                catch { }
            }

            string rscText = rscBuilder.ToString();
            var results = new List<CharaSearchResult>();
            var seenIds = new HashSet<string>();

            int pos = 0;
            while ((pos = rscText.IndexOf("\"creatorNotes\"", pos, StringComparison.Ordinal)) >= 0)
            {
                int objStart = rscText.LastIndexOf('{', pos);
                if (objStart < 0) { pos++; continue; }

                int objEnd = JsonHelper.FindMatchingBrace(rscText, objStart);
                if (objEnd < 0) { pos++; continue; }

                string obj = rscText.Substring(objStart, objEnd - objStart + 1);

                string id = ExtractRscString(obj, "id");
                if (string.IsNullOrEmpty(id))
                {
                    var idMatch = Regex.Match(obj, "\"id\"\\s*:\\s*(\\d+)");
                    if (idMatch.Success) id = idMatch.Groups[1].Value;
                }
                string name = ExtractRscString(obj, "name") ?? "";

                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name) && !seenIds.Contains(id))
                {
                    seenIds.Add(id);
                    results.Add(new CharaSearchResult
                    {
                        Id = $"{DefaultMirror}/character/{id}",
                        Name = name,
                        Description = ExtractRscString(obj, "creatorNotes") ?? "",
                        AvatarUrl = ExtractRscString(obj, "avatarUrl") ?? "",
                        Source = "dzmm"
                    });

                    if (results.Count >= limit) break;
                }

                pos = objEnd + 1;
            }

            return results;
        }

        public static async Task<CharacterCard> DownloadAsync(string url, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(url) || http == null) return null;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] DZMM download: {url}");
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;

                string html = await resp.Content.ReadAsStringAsync();
                return await ParsePageAsync(html, url, http);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] DZMM error: {ex.Message}");
                return null;
            }
        }

        private static async Task<CharacterCard> ParsePageAsync(string html, string pageUrl, HttpClient http)
        {
            try
            {
                var rscBuilder = new System.Text.StringBuilder();
                var pushMatches = Regex.Matches(html,
                    @"self\.__next_f\.push\(\[[^,]*,\s*""((?:[^""\\]|\\.)*)""\)\]",
                    RegexOptions.Singleline);

                foreach (System.Text.RegularExpressions.Match m in pushMatches)
                {
                    try
                    {
                        string segment = Regex.Unescape(m.Groups[1].Value);
                        rscBuilder.Append(segment);
                    }
                    catch { }
                }

                string rscText = rscBuilder.ToString();

                var card = new CharacterCard();

                string name = ExtractRscString(rscText, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    var ogMatch = Regex.Match(html, @"<meta\s+property=""og:title""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (ogMatch.Success)
                        name = ogMatch.Groups[1].Value.Replace(" | DZMM", "").Replace(" | 电子魅魔", "");
                }
                card.Name = name ?? "Unknown";

                card.Description = ExtractRscString(rscText, "creatorNotes") ?? "";
                card.FirstMessage = ExtractRscString(rscText, "firstMes") ?? "";
                card.Creator = ExtractRscString(rscText, "userId") ?? "";

                if (string.IsNullOrWhiteSpace(card.Description))
                {
                    var ogMatch = Regex.Match(html, @"<meta\s+property=""og:description""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (ogMatch.Success)
                        card.Description = ogMatch.Groups[1].Value;
                }

                string avatarUrl = ExtractRscString(rscText, "avatarUrl");
                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    var ogMatch = Regex.Match(html, @"<meta\s+property=""og:image""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (ogMatch.Success)
                        avatarUrl = ogMatch.Groups[1].Value;
                }

                if (!string.IsNullOrWhiteSpace(avatarUrl) && avatarUrl.Contains("rls.cheggpt.com"))
                {
                    var uri = new Uri(pageUrl);
                    avatarUrl = avatarUrl.Replace("rls.cheggpt.com", uri.Host);
                }

                if (!string.IsNullOrWhiteSpace(avatarUrl))
                {
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, avatarUrl);
                    if (img != null) { card.AvatarBase64 = img.Base64; card.AvatarMimeType = img.MimeType; }
                }

                // 立绘：尝试从 RSC 中找到其他图片字段（cardFilename 等）
                string illustRsc = ExtractRscString(rscText, "cardFilename")
                               ?? ExtractRscString(rscText, "imageUrl")
                               ?? ExtractRscString(rscText, "cardUrl");
                if (!string.IsNullOrWhiteSpace(illustRsc)
                    && !illustRsc.Equals(avatarUrl, StringComparison.OrdinalIgnoreCase))
                {
                    if (illustRsc.Contains("rls.cheggpt.com"))
                    {
                        var uri = new Uri(pageUrl);
                        illustRsc = illustRsc.Replace("rls.cheggpt.com", uri.Host);
                    }
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, illustRsc);
                    if (img != null) { card.IllustrationBase64 = img.Base64; card.IllustrationMimeType = img.MimeType; }
                }

                return card;
            }
            catch { return null; }
        }

        private static string ExtractRscString(string rscText, string fieldName)
        {
            try
            {
                var match = Regex.Match(rscText,
                    "\"" + fieldName + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                    RegexOptions.Singleline);
                if (match.Success)
                    return Regex.Unescape(match.Groups[1].Value);
                return null;
            }
            catch { return null; }
        }
    }
}
