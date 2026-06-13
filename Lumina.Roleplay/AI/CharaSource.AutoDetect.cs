// CharaSource.AutoDetect — 角色卡下载适配器（拆分自 CharaSource.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace yanshuai
{
    public static class SourceAutoDetect
    {
        public static async Task<CharacterCard> DownloadAsync(string url, HttpClient http)
        {
            if (string.IsNullOrWhiteSpace(url) || http == null) return null;

            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();

                if (host.Contains("chub.ai") || host.Contains("characterhub"))
                {
                    var idMatch = Regex.Match(url, @"chub\.ai/(?:characters/)?([^/]+/[^/?#]+)", RegexOptions.IgnoreCase);
                    if (idMatch.Success)
                        return await SourceCharacterHub.DownloadAsync(idMatch.Groups[1].Value, http);
                    return await SourceUrl.DownloadAsync(url, http);
                }

                if (host.Contains("realm.risuai.net"))
                    return await SourceRisuRealm.DownloadAsync(url, http);

                if (host.Contains("character-tavern.com"))
                    return await SourceCharacterTavern.DownloadAsync(url, http);

                if (host.Contains("aicharactercards.com"))
                    return await SourceAICharacterCards.DownloadAsync(url, http);

                if (host.Contains("jannyai") || host.Contains("janitorai"))
                    return await SourceJanitorAI.DownloadAsync(url, http);

                if (host.Contains("dzmm") || host.Contains("duskpine") || host.Contains("fendal")
                    || host.Contains("kelphin") || host.Contains("turfle") || host.Contains("velvetpaw"))
                    return await SourceDZMM.DownloadAsync(url, http);

                if (host.Contains("girlgirlgirl"))
                    return await SourceHuayu.DownloadAsync(url, http);

                if (host.Contains("xingyeai"))
                    return await SourceXingyeAI.DownloadAsync(url, http);

                if (host.Contains("purrly.ai"))
                    return await SourceQuack.DownloadAsync(url, http);

                return await SourceUrl.DownloadAsync(url, http);
            }
            catch { return await SourceUrl.DownloadAsync(url, http); }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 花屿 (girlgirlgirl.xyz) — REST API，无需认证
    // ══════════════════════════════════════════════════════════════════════════

}
