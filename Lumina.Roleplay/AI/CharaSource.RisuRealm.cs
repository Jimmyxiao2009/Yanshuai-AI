// CharaSource.RisuRealm — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceRisuRealm
    {
        private const string ApiBase = "https://realm.risuai.net";

        public static async Task<CharacterCard> DownloadAsync(string urlOrId, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(urlOrId) || http == null) return null;

            try
            {
                string id = urlOrId;
                if (urlOrId.StartsWith("https://") || urlOrId.StartsWith("http://"))
                {
                    var match = Regex.Match(urlOrId, @"/(?:character|download/(?:png|json|charx)-v[23])/([a-f0-9]{64}|[a-f0-9]{32})", RegexOptions.IgnoreCase);
                    if (match.Success)
                        id = match.Groups[1].Value;
                    else
                    {
                        match = Regex.Match(urlOrId, "/([a-f0-9]{64}|[a-f0-9]{32})(?:\\?|#|$)");
                        if (match.Success)
                            id = match.Groups[1].Value;
                    }
                }

                string[] formats = { "png-v2", "png-v3", "json-v2", "json-v3" };
                foreach (var fmt in formats)
                {
                    string url = $"{ApiBase}/api/v1/download/{fmt}/{id}";
                    System.Diagnostics.Debug.WriteLine($"[CharaSource] RisuRealm download: {url}");
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        if (fmt.StartsWith("png"))
                        {
                            var bytes = await resp.Content.ReadAsByteArrayAsync();
                            return ImportExportPage.ParseCharaPng(bytes);
                        }
                        else
                        {
                            var text = await resp.Content.ReadAsStringAsync();
                            return ImportExportPage.ParseCharaJson(text);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CharaSource] RisuRealm error: {ex.Message}");
                return null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // JannyAI (formerly JanitorAI) — 通过 API 下载角色卡
    // ══════════════════════════════════════════════════════════════════════════

}
