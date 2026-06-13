// CharaSource.CharacterTavern — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceCharacterTavern
    {
        public static async Task<CharacterCard> DownloadAsync(string pageUrl, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(pageUrl) || http == null) return null;

            try
            {
                var match = Regex.Match(pageUrl,
                    @"character-tavern\.com/character/([^/]+)/([^/?#]+)",
                    RegexOptions.IgnoreCase);
                if (!match.Success) return null;

                string author = match.Groups[1].Value;
                string slug = match.Groups[2].Value;
                string pngUrl = $"https://cards.character-tavern.com/{author}/{slug}.png";

                System.Diagnostics.Debug.WriteLine($"[CharaSource] Character Tavern download: {pngUrl}");
                var resp = await http.GetAsync(pngUrl);
                if (!resp.IsSuccessStatusCode) return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                return ImportExportPage.ParseCharaPng(bytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] Character Tavern error: {ex.Message}");
                return null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AICharacterCards.com — REST API 直接下载 PNG
    // ══════════════════════════════════════════════════════════════════════════

}
