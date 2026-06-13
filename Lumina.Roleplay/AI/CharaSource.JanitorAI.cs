// CharaSource.JanitorAI — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceJanitorAI
    {
        private const string ApiBase = "https://api.jannyai.com";

        public static async Task<CharacterCard> DownloadAsync(string url, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(url) || http == null) return null;

            try
            {
                string uuid = url;

                var match = Regex.Match(url,
                    @"(?:janitorai|jannyai)\.com/characters/([a-f0-9\-]{32,})",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    match = Regex.Match(url,
                        @"^([a-f0-9]{8}-?[a-f0-9]{4}-?[a-f0-9]{4}-?[a-f0-9]{4}-?[a-f0-9]{12})$",
                        RegexOptions.IgnoreCase);
                }

                if (!match.Success) return null;
                uuid = match.Groups[1].Value;

                string postBody = $"{{\"characterId\":\"{uuid}\"}}";
                var content = new StringContent(postBody, System.Text.Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"[CharaSource] JannyAI download: {uuid}");
                var resp = await http.PostAsync($"{ApiBase}/api/v1/download", content);
                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[CharaSource] JannyAI download status: {(int)resp.StatusCode}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync();

                var urlMatch = Regex.Match(json, "\"downloadUrl\"\\s*:\\s*\"([^\"]+)\"");
                if (!urlMatch.Success) return null;

                string downloadUrl = urlMatch.Groups[1].Value;
                System.Diagnostics.Debug.WriteLine($"[CharaSource] JannyAI download URL: {downloadUrl}");

                var imgResp = await http.GetAsync(downloadUrl);
                if (!imgResp.IsSuccessStatusCode) return null;

                var bytes = await imgResp.Content.ReadAsByteArrayAsync();
                return ImportExportPage.ParseCharaPng(bytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] JannyAI error: {ex.Message}");
                return null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Character Tavern — 直接解析角色卡 PNG
    // ══════════════════════════════════════════════════════════════════════════

}
