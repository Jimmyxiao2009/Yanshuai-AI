// CharaSource.AICharacterCards — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceAICharacterCards
    {
        public static async Task<CharacterCard> DownloadAsync(string pageUrl, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(pageUrl) || http == null) return null;

            try
            {
                var match = Regex.Match(pageUrl,
                    @"aicharactercards\.com/character/([^/]+)/([^?#]+)",
                    RegexOptions.IgnoreCase);
                if (!match.Success) return null;

                string author = match.Groups[1].Value;
                string title = match.Groups[2].Value;
                string apiUrl = $"https://aicharactercards.com/wp-json/pngapi/v1/image/{author}/{title}";

                System.Diagnostics.Debug.WriteLine($"[CharaSource] AICC download: {apiUrl}");
                var resp = await http.GetAsync(apiUrl);
                if (!resp.IsSuccessStatusCode) return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                return ImportExportPage.ParseCharaPng(bytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] AICC error: {ex.Message}");
                return null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // URL 智能路由：根据域名自动选择下载方式
    // ══════════════════════════════════════════════════════════════════════════

}
