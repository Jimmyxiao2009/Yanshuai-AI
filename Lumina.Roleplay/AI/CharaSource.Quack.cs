// CharaSource.Quack — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceQuack
    {
        private const string ApiBase = "https://purrly.ai";
        private static string _cachedToken;
        private static DateTime _tokenExpiry = DateTime.MinValue;

        private static async Task<string> GetTokenAsync(HttpClient http)
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            try
            {
                var body = new StringContent(
                    "{\"guestCode\":\"\",\"webFingerprint\":\"uwp-roleplay-client\"}",
                    System.Text.Encoding.UTF8, "application/json");

                var resp = await http.PostAsync($"{ApiBase}/api/users/guest-login", body);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync();
                string token = JsonHelper.ExtractJsonString(json, "token");
                if (!string.IsNullOrEmpty(token))
                {
                    _cachedToken = token;
                    _tokenExpiry = DateTime.UtcNow.AddHours(23);
                }
                return token;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<List<CharaSearchResult>> SearchAsync(string query, HttpClient http)
        {
            System.Diagnostics.Debug.WriteLine("[CharaSource] Quack search: not available (API returns 500)");
            return new List<CharaSearchResult>();
        }

        public static async Task<CharacterCard> DownloadAsync(string urlOrId, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(urlOrId) || http == null) return null;

            try
            {
                string shareId = urlOrId;
                var idMatch = Regex.Match(urlOrId, @"(?:share/|studioCard/info\?id=)([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
                if (!idMatch.Success)
                {
                    idMatch = Regex.Match(urlOrId, @"^([a-zA-Z0-9_-]{20,})$");
                }
                if (idMatch.Success)
                    shareId = idMatch.Groups[1].Value;

                string token = await GetTokenAsync(http);
                if (string.IsNullOrEmpty(token))
                {
                    System.Diagnostics.Debug.WriteLine("[CharaSource] Quack: failed to get guest token");
                    return null;
                }

                string apiUrl = $"{ApiBase}/api/v1/japanStudioCard/info?id={shareId}";
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Quack download: {apiUrl}");

                var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync();
                return await ParseCardAsync(json, http);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Quack error: {ex.Message}");
                return null;
            }
        }

        private static async Task<CharacterCard> ParseCardAsync(string json, HttpClient http)
        {
            try
            {
                string dataSection = JsonHelper.ExtractJsonObject(json, "data");
                if (dataSection == null) return null;

                var card = new CharacterCard
                {
                    Name = JsonHelper.ExtractJsonString(dataSection, "name") ?? "Unknown",
                    Description = JsonHelper.ExtractJsonString(dataSection, "intro") ?? "",
                    Creator = JsonHelper.ExtractJsonString(dataSection, "author") ?? "",
                };

                string charListSection = JsonHelper.ExtractJsonArray(dataSection, "charList");
                if (charListSection != null)
                {
                    int firstObj = charListSection.IndexOf('{');
                    if (firstObj >= 0)
                    {
                        int firstEnd = JsonHelper.FindMatchingBrace(charListSection, firstObj);
                        if (firstEnd > 0)
                        {
                            string firstChar = charListSection.Substring(firstObj, firstEnd - firstObj + 1);
                            card.SystemPrompt = JsonHelper.ExtractJsonString(firstChar, "prompt") ?? "";

                            string attrsSection = JsonHelper.ExtractJsonArray(firstChar, "attrs");
                            if (attrsSection != null)
                            {
                                int attrIdx = 0;
                                while ((attrIdx = attrsSection.IndexOf("\"label\"", attrIdx, StringComparison.Ordinal)) >= 0)
                                {
                                    int objStart = attrsSection.LastIndexOf('{', attrIdx);
                                    if (objStart < 0) { attrIdx++; continue; }
                                    int objEnd = JsonHelper.FindMatchingBrace(attrsSection, objStart);
                                    if (objEnd < 0) { attrIdx++; continue; }

                                    string attrObj = attrsSection.Substring(objStart, objEnd - objStart + 1);
                                    string label = JsonHelper.ExtractJsonString(attrObj, "label") ?? "";
                                    string value = JsonHelper.ExtractJsonString(attrObj, "value") ?? "";

                                    if (label.Equals("Personality", StringComparison.OrdinalIgnoreCase))
                                        card.Personality = value;

                                    attrIdx = objEnd + 1;
                                }
                            }
                        }
                    }
                }

                string prologueSection = JsonHelper.ExtractJsonObject(dataSection, "prologue");
                if (prologueSection != null)
                {
                    string greetingsSection = JsonHelper.ExtractJsonArray(prologueSection, "greetings");
                    if (greetingsSection != null)
                    {
                        int gObj = greetingsSection.IndexOf('{');
                        if (gObj >= 0)
                        {
                            int gEnd = JsonHelper.FindMatchingBrace(greetingsSection, gObj);
                            if (gEnd > 0)
                            {
                                string greeting = greetingsSection.Substring(gObj, gEnd - gObj + 1);
                                card.FirstMessage = JsonHelper.ExtractJsonString(greeting, "value") ?? "";
                            }
                        }
                    }
                }

                string coverFilename = JsonHelper.ExtractJsonString(dataSection, "cover");
                if (!string.IsNullOrWhiteSpace(coverFilename))
                {
                    string coverUrl = $"{ApiBase}/{coverFilename}";
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, coverUrl);
                    if (img != null) { card.AvatarBase64 = img.Base64; card.AvatarMimeType = img.MimeType; }
                    // 立绘：cover 通常是全身图，也用作立绘
                    if (img != null) { card.IllustrationBase64 = img.Base64; card.IllustrationMimeType = img.MimeType; }
                }

                return card;
            }
            catch { return null; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DZMM (电子魅魔，镜像站) — 解析 HTML/RSC 流获取角色数据，搜索需登录（暂不可用）
    // ══════════════════════════════════════════════════════════════════════════

}
