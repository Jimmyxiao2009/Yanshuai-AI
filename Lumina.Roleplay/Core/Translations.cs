using System.Collections.Generic;

namespace yanshuai
{
    public static partial class Translations
    {
        /// <summary>Language code → (Chinese key → translated text).</summary>
        public static readonly Dictionary<string, Dictionary<string, string>> Data = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Display name of each language in each language. [langCode] → [displayLangCode] → name</summary>
        public static readonly Dictionary<string, Dictionary<string, string>> LangNames = new Dictionary<string, Dictionary<string, string>>();

        static Translations()
        {
            // ── Language names (self-name + how it appears in each language) ──────
            foreach (var lang in new[] { "zh-CN", "en", "ja", "ko", "es", "fr", "de" })
                LangNames[lang] = new Dictionary<string, string>();

            LangNames["zh-CN"]["zh-CN"] = "简体中文";
            LangNames["zh-CN"]["en"]    = "简体中文";
            LangNames["zh-CN"]["ja"]    = "简体中文";
            LangNames["zh-CN"]["ko"]    = "简体中文";
            LangNames["zh-CN"]["es"]    = "简体中文";
            LangNames["zh-CN"]["fr"]    = "简体中文";
            LangNames["zh-CN"]["de"]    = "简体中文";

            LangNames["en"]["zh-CN"] = "English";
            LangNames["en"]["en"]    = "English";
            LangNames["en"]["ja"]    = "英語";
            LangNames["en"]["ko"]    = "영어";
            LangNames["en"]["es"]    = "Inglés";
            LangNames["en"]["fr"]    = "Anglais";
            LangNames["en"]["de"]    = "Englisch";

            LangNames["ja"]["zh-CN"] = "日本語";
            LangNames["ja"]["en"]    = "Japanese";
            LangNames["ja"]["ja"]    = "日本語";
            LangNames["ja"]["ko"]    = "일본어";
            LangNames["ja"]["es"]    = "Japonés";
            LangNames["ja"]["fr"]    = "Japonais";
            LangNames["ja"]["de"]    = "Japanisch";

            LangNames["ko"]["zh-CN"] = "한국어";
            LangNames["ko"]["en"]    = "Korean";
            LangNames["ko"]["ja"]    = "韓国語";
            LangNames["ko"]["ko"]    = "한국어";
            LangNames["ko"]["es"]    = "Coreano";
            LangNames["ko"]["fr"]    = "Coréen";
            LangNames["ko"]["de"]    = "Koreanisch";

            LangNames["es"]["zh-CN"] = "Español";
            LangNames["es"]["en"]    = "Spanish";
            LangNames["es"]["ja"]    = "スペイン語";
            LangNames["es"]["ko"]    = "스페인어";
            LangNames["es"]["es"]    = "Español";
            LangNames["es"]["fr"]    = "Espagnol";
            LangNames["es"]["de"]    = "Spanisch";

            LangNames["fr"]["zh-CN"] = "Français";
            LangNames["fr"]["en"]    = "French";
            LangNames["fr"]["ja"]    = "フランス語";
            LangNames["fr"]["ko"]    = "프랑스어";
            LangNames["fr"]["es"]    = "Francés";
            LangNames["fr"]["fr"]    = "Français";
            LangNames["fr"]["de"]    = "Französisch";

            LangNames["de"]["zh-CN"] = "Deutsch";
            LangNames["de"]["en"]    = "German";
            LangNames["de"]["ja"]    = "ドイツ語";
            LangNames["de"]["ko"]    = "독일어";
            LangNames["de"]["es"]    = "Alemán";
            LangNames["de"]["fr"]    = "Allemand";
            LangNames["de"]["de"]    = "Deutsch";

            // ── Translation data ────────────────────────────────────────────
            LoadEn();
            LoadJa();
            LoadKo();
            LoadEs();
            LoadFr();
            LoadDe();
        }

        static void D(string lang, Dictionary<string, string> entries)
        {
            Data[lang] = entries;
        }

        /// <summary>Get the display name of a language in the given display language.</summary>
        public static string GetLangName(string langCode, string displayLang)
        {
            if (LangNames.TryGetValue(langCode, out var names) &&
                names.TryGetValue(displayLang, out var name))
                return name;
            return langCode;
        }
    }
}
