// CharaSource.CharacterHub — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceCharacterHub
    {
        private const string ApiBase = "https://api.chub.ai";

        public static async Task<List<CharaSearchResult>> SearchAsync(string query, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(query) || http == null)
                return new List<CharaSearchResult>();

            try
            {
                string url = $"{ApiBase}/search?search={Uri.EscapeDataString(query)}&first=40&nsfw=true&namespace=characters";
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub search: {url}");
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub search status: {(int)resp.StatusCode}");
                    return new List<CharaSearchResult>();
                }

                string json = await resp.Content.ReadAsStringAsync();
                return ParseSearchResults(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub search error: {ex.Message}");
                return new List<CharaSearchResult>();
            }
        }

        public static async Task<List<CharaSearchResult>> BrowseAsync(HttpClient http, int page = 1)
        {
            if (http == null) return new List<CharaSearchResult>();

            try
            {
                string url = $"{ApiBase}/search?search=&first=40&page={page}&sort=star_count&nsfw=true&namespace=characters";
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub browse: {url}");
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub browse status: {(int)resp.StatusCode}");
                    return new List<CharaSearchResult>();
                }

                string json = await resp.Content.ReadAsStringAsync();
                return ParseSearchResults(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub browse error: {ex.Message}");
                return new List<CharaSearchResult>();
            }
        }

        public static async Task<CharacterCard> DownloadAsync(string fullPath, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || http == null) return null;

            try
            {
                string url = $"{ApiBase}/api/characters/{fullPath}?full=true";
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub download: {url}");
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub download status: {(int)resp.StatusCode}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync();
                var card = ParseCharacterDetail(json);
                if (card != null)
                {
                    string avatarUrl = ExtractJsonString(json, "avatar_url")
                                   ?? ExtractJsonString(json, "max_res_url") ?? "";
                    if (!string.IsNullOrWhiteSpace(avatarUrl))
                    {
                        var img = await SourceUtil.DownloadImageAsBase64Async(http, avatarUrl);
                        if (img != null) { card.AvatarBase64 = img.Base64; card.AvatarMimeType = img.MimeType; }
                    }

                    // 立绘：max_res_url（全尺寸图）作为立绘
                    string illustUrl = ExtractJsonString(json, "max_res_url");
                    if (!string.IsNullOrWhiteSpace(illustUrl)
                        && !illustUrl.Equals(avatarUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        var img = await SourceUtil.DownloadImageAsBase64Async(http, illustUrl);
                        if (img != null) { card.IllustrationBase64 = img.Base64; card.IllustrationMimeType = img.MimeType; }
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Parsed card: {(card != null ? card.Name : "null")}");
                return card;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Chub download error: {ex.Message}");
                return null;
            }
        }

        private static List<CharaSearchResult> ParseSearchResults(string json)
        {
            var list = new List<CharaSearchResult>();
            try
            {
                string nodesSection = ExtractJsonArray(json, "nodes");
                if (nodesSection == null) return list;
                int idx = 0;
                while (idx < nodesSection.Length)
                {
                    int objStart = nodesSection.IndexOf('{', idx);
                    if (objStart < 0) break;
                    int objEnd = FindMatchingBrace(nodesSection, objStart);
                    if (objEnd < 0) break;

                    var obj = nodesSection.Substring(objStart, objEnd - objStart + 1);
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
                var fullPath = ExtractJsonString(json, "fullPath");
                var name = ExtractJsonString(json, "name");
                if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(name))
                    return null;

                return new CharaSearchResult
                {
                    Id = fullPath,
                    Name = name,
                    Description = ExtractJsonString(json, "tagline")
                               ?? ExtractJsonString(json, "description") ?? "",
                    AvatarUrl = ExtractJsonString(json, "avatar_url")
                              ?? ExtractJsonString(json, "max_res_url") ?? "",
                    Source = "chub"
                };
            }
            catch { return null; }
        }

        private static CharacterCard ParseCharacterDetail(string json)
        {
            try
            {
                int defnKeyIdx = json.IndexOf("\"definition\"", StringComparison.Ordinal);
                if (defnKeyIdx < 0) return null;
                int defnStart = json.IndexOf('{', defnKeyIdx);
                if (defnStart < 0) return null;
                int defnEnd = FindMatchingBrace(json, defnStart);
                if (defnEnd < 0) return null;

                string defn = json.Substring(defnStart, defnEnd - defnStart + 1);

                var card = new CharacterCard
                {
                    Name = ExtractJsonString(defn, "name") ?? "Unknown",
                    Description = ExtractJsonString(defn, "description") ?? "",
                    Personality = ExtractJsonString(defn, "personality")
                               ?? ExtractJsonString(defn, "tavern_personality") ?? "",
                    FirstMessage = ExtractJsonString(defn, "first_message") ?? "",
                    MesExample = ExtractJsonString(defn, "example_dialogs") ?? "",
                    Scenario = ExtractJsonString(defn, "scenario") ?? "",
                    SystemPrompt = ExtractJsonString(defn, "system_prompt") ?? "",
                    PostHistoryInstructions = ExtractJsonString(defn, "post_history_instructions") ?? "",
                    Creator = ExtractJsonString(defn, "creator") ?? ExtractJsonString(json, "fullPath") ?? "",
                };

                return card;
            }
            catch { return null; }
        }

        // ── JSON 辅助（委托到共享的 JsonHelper）──
        private static string ExtractJsonString(string json, string key) => JsonHelper.ExtractJsonString(json, key);
        private static string ExtractJsonArray(string json, string key) => JsonHelper.ExtractJsonArray(json, key);
        private static int FindMatchingBrace(string text, int start) => JsonHelper.FindMatchingBrace(text, start);
        private static int FindMatchingArray(string text, int start) => JsonHelper.FindMatchingArray(text, start);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RisuRealm (realm.risuai.net)
    // ══════════════════════════════════════════════════════════════════════════

}
