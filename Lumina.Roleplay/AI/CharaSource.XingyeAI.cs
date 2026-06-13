// CharaSource.XingyeAI — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceXingyeAI
    {
        private const string ApiBase = "https://m.xingyeai.com/weaver/api/v1";

        public static async Task<List<CharaSearchResult>> SearchAsync(string query, HttpClient http)
        {
            System.Diagnostics.Debug.WriteLine("[CharaSource] XingyeAI search: not available (API returns 400)");
            return new List<CharaSearchResult>();
        }

        public static async Task<List<CharaSearchResult>> BrowseAsync(HttpClient http, int count = 8)
        {
            var results = new List<CharaSearchResult>();
            var seenIds = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    string apiUrl = $"{ApiBase}/npc/get_share_npc_data";
                    var body = new StringContent("{\"npc_id\":0}", System.Text.Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync(apiUrl, body);
                    if (!resp.IsSuccessStatusCode) continue;

                    string json = await resp.Content.ReadAsStringAsync();
                    string npcId = JsonHelper.ExtractJsonString(json, "npc_id");
                    if (string.IsNullOrEmpty(npcId))
                    {
                        var numMatch = Regex.Match(json, "\"npc_id\"\\s*:\\s*(\\d+)");
                        if (numMatch.Success) npcId = numMatch.Groups[1].Value;
                    }
                    if (string.IsNullOrEmpty(npcId) || seenIds.Contains(npcId)) continue;
                    seenIds.Add(npcId);

                    results.Add(new CharaSearchResult
                    {
                        Id = npcId,
                        Name = JsonHelper.ExtractJsonString(json, "npc_name") ?? "Unknown",
                        Description = JsonHelper.ExtractJsonString(json, "npc_desc") ?? "",
                        AvatarUrl = JsonHelper.ExtractJsonString(json, "head_img_url") ?? "",
                        Source = "xingye"
                    });
                }
                catch { }
            }

            return results;
        }

        public static async Task<CharacterCard> DownloadAsync(string urlOrId, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(urlOrId) || http == null) return null;

            try
            {
                string npcId = urlOrId;
                var idMatch = Regex.Match(urlOrId, @"(\d{10,})");
                if (idMatch.Success)
                    npcId = idMatch.Groups[1].Value;

                if (!long.TryParse(npcId, out long npcIdLong))
                    return null;

                string apiUrl = $"{ApiBase}/npc/get_share_npc_data";
                var body = new StringContent($"{{\"npc_id\":{npcIdLong}}}", System.Text.Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"[CharaSource] XingyeAI download: npc_id={npcIdLong}");
                var resp = await http.PostAsync(apiUrl, body);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync();
                return await ParseCharacterAsync(json, http);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] XingyeAI error: {ex.Message}");
                return null;
            }
        }

        private static async Task<CharacterCard> ParseCharacterAsync(string json, HttpClient http)
        {
            try
            {
                string firstMsg = JsonHelper.ExtractJsonString(json, "prologue");
                if (string.IsNullOrEmpty(firstMsg))
                {
                    // 也许是对象（含 text/content/value 子字段）
                    string prologueObj = JsonHelper.ExtractJsonObject(json, "prologue");
                    if (prologueObj != null)
                    {
                        firstMsg = JsonHelper.ExtractJsonString(prologueObj, "text")
                                ?? JsonHelper.ExtractJsonString(prologueObj, "content")
                                ?? JsonHelper.ExtractJsonString(prologueObj, "value") ?? "";
                    }
                }
                if (string.IsNullOrEmpty(firstMsg))
                {
                    firstMsg = JsonHelper.ExtractJsonString(json, "first_mes")
                            ?? JsonHelper.ExtractJsonString(json, "firstMessage")
                            ?? JsonHelper.ExtractJsonString(json, "first_message")
                            ?? JsonHelper.ExtractJsonString(json, "greeting") ?? "";
                }
                string firstMsgPreview = (firstMsg ?? "").Length > 60 ? firstMsg.Substring(0, 60) + "..." : (firstMsg ?? "");
                System.Diagnostics.Debug.WriteLine($"[CharaSource] XingyeAI: firstMsg='{firstMsgPreview}' prologue_str={JsonHelper.ExtractJsonString(json, "prologue") ?? "null"} prologue_obj={(JsonHelper.ExtractJsonObject(json, "prologue") ?? "null")}");

                var card = new CharacterCard
                {
                    Name = JsonHelper.ExtractJsonString(json, "npc_name") ?? "Unknown",
                    Description = JsonHelper.ExtractJsonString(json, "npc_desc") ?? "",
                    FirstMessage = firstMsg,
                    Creator = JsonHelper.ExtractJsonString(json, "owner_id") ?? "",
                };

                string headImg = JsonHelper.ExtractJsonString(json, "head_img_url");
                if (!string.IsNullOrWhiteSpace(headImg))
                {
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, headImg);
                    if (img != null) { card.AvatarBase64 = img.Base64; card.AvatarMimeType = img.MimeType; }
                }

                string baseImg = JsonHelper.ExtractJsonString(json, "base_img_url");
                if (!string.IsNullOrWhiteSpace(baseImg))
                {
                    var img = await SourceUtil.DownloadImageAsBase64Async(http, baseImg);
                    if (img != null) { card.IllustrationBase64 = img.Base64; card.IllustrationMimeType = img.MimeType; }
                }

                return card;
            }
            catch { return null; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 云酒馆 (purrly.ai) — 需 Bearer Token（自动获取游客 token）
    // ══════════════════════════════════════════════════════════════════════════

}
