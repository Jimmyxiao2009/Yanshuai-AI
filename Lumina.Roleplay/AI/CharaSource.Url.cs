// CharaSource.Url — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceUrl
    {
        public static async Task<CharacterCard> DownloadAsync(string url, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(url) || http == null) return null;

            try
            {
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;

                var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                bool isPng = url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                          || contentType.Contains("image/png");

                if (isPng)
                {
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    return ImportExportPage.ParseCharaPng(bytes);
                }
                else
                {
                    var text = await resp.Content.ReadAsStringAsync();
                    var card = ImportExportPage.ParseCharaJson(text);
                    if (card != null) return card;

                    try
                    {
                        var match = Regex.Match(text, "\"data\"\\s*:\\s*(\\{.+\\})", RegexOptions.Singleline);
                        if (match.Success)
                            return ImportExportPage.ParseCharaJson(match.Groups[1].Value);
                    }
                    catch { }

                    return null;
                }
            }
            catch { return null; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Chub.ai (chub.ai — 原 CharacterHub)
    // ══════════════════════════════════════════════════════════════════════════

}
