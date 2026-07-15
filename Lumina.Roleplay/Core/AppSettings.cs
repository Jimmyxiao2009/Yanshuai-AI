using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
using Windows.UI.Xaml;

namespace yanshuai
{
    public static class AppSettings
    {
        // 缓存容器引用：LocalSettings 是 app 生命周期内稳定的单例，
        // 之前用 => 属性会让每次设置读写都重新跨 WinRT ABI 取一遍
        private static readonly ApplicationDataContainer Local = ApplicationData.Current.LocalSettings;

        public static bool IsDark
        {
            get => Local.Values.ContainsKey("DarkMode") && (bool)Local.Values["DarkMode"];
            set => Local.Values["DarkMode"] = value;
        }

        // ── Language ─────────────────────────────────────────────────────────
        private static Dictionary<string, string> _currentTrans;
        private static string _language;

        public static string Language
        {
            get
            {
                if (_language == null)
                    _language = Local.Values.ContainsKey("Language")
                        ? Local.Values["Language"] as string ?? "zh-CN" : "zh-CN";
                return _language;
            }
            set
            {
                _language = value ?? "zh-CN";
                Local.Values["Language"] = _language;
                LoadTranslations();
            }
        }

        public static void LoadTranslations()
        {
            if (Language == "zh-CN")
            {
                _currentTrans = null;
                return;
            }
            Translations.Data.TryGetValue(Language, out _currentTrans);
        }

        public static string[] AvailableLanguages => new[] { "zh-CN", "en", "ja", "ko", "es", "fr", "de" };

        /// <summary>Returns translated text. Falls back to English if translation not found.</summary>
        public static string S(string zh, string en)
        {
            if (Language == "zh-CN") return zh;
            if (_currentTrans != null && _currentTrans.TryGetValue(zh, out var t)) return t;
            return en;
        }

        public static bool JailbreakEnabled
        {
            get => Local.Values.ContainsKey("JailbreakEnabled") && (bool)Local.Values["JailbreakEnabled"];
            set => Local.Values["JailbreakEnabled"] = value;
        }

        public static string JailbreakPrompt
        {
            get => Local.Values.ContainsKey("JailbreakPrompt") ? Local.Values["JailbreakPrompt"] as string ?? "" : "";
            set => Local.Values["JailbreakPrompt"] = value;
        }

        // ── Default API ───────────────────────────────────────────────────────

        /// <summary>Global default API profile ID — used when a conversation has no ApiProfileId set.</summary>
        public static string DefaultApiProfileId
        {
            get => Local.Values.ContainsKey("DefaultApiId") ? Local.Values["DefaultApiId"] as string ?? "" : "";
            set => Local.Values["DefaultApiId"] = value;
        }

        // ── Character illustration as background ────────────────────────────────
        public static bool UseCharaIllustrationAsBg
        {
            get => Local.Values.ContainsKey("UseCharaIllustBg") && (bool)Local.Values["UseCharaIllustBg"];
            set => Local.Values["UseCharaIllustBg"] = value;
        }

        // ── OOBE ─────────────────────────────────────────────────────────────
        public static bool OobeCompleted
        {
            get => Local.Values.ContainsKey("OobeCompleted") && (bool)Local.Values["OobeCompleted"];
            set => Local.Values["OobeCompleted"] = value;
        }

        // ── Enter key behavior ───────────────────────────────────────────────────
        /// <summary>0=Enter发送(默认), 1=Enter换行Ctrl+Enter发送</summary>
        public static int EnterBehavior
        {
            get => Local.Values.ContainsKey("EnterBehavior") ? (int)Local.Values["EnterBehavior"] : 0;
            set => Local.Values["EnterBehavior"] = value;
        }

        // ── Startup behavior ─────────────────────────────────────────────────
        /// <summary>0=最近对话, 1=新对话, 2=对话列表</summary>
        public static int StartupBehavior
        {
            get => Local.Values.ContainsKey("StartupBehavior") ? (int)Local.Values["StartupBehavior"] : 0;
            set => Local.Values["StartupBehavior"] = value;
        }

        // ── 回复声音 ──────────────────────────────────────────────────────────
        public static bool ReplySoundEnabled
        {
            get => !Local.Values.ContainsKey("ReplySoundEnabled") || (bool)Local.Values["ReplySoundEnabled"];
            set => Local.Values["ReplySoundEnabled"] = value;
        }

        // ── Sub-agent (子智能体) ─────────────────────────────────────────────
        public static bool UseSubAgent
        {
            get => GetBool("UseSubAgent", false);
            set => SetBool("UseSubAgent", value);
        }
        public static string SubAgentUrl
        {
            get => GetString("SubAgentUrl", "");
            set => SetString("SubAgentUrl", value);
        }
        public static string SubAgentApiKey
        {
            get => GetString("SubAgentApiKey", "");
            set => SetString("SubAgentApiKey", value);
        }
        public static string SubAgentModel
        {
            get => GetString("SubAgentModel", "");
            set => SetString("SubAgentModel", value);
        }
        public static string SubAgentProviderType
        {
            get => GetString("SubAgentProviderType", "openai");
            set => SetString("SubAgentProviderType", value);
        }

        // ── 思考过程默认展开 ──────────────────────────────────────────────────
        public static bool ReasoningExpandedByDefault
        {
            get => Local.Values.ContainsKey("ReasoningExpandedByDefault") && (bool)Local.Values["ReasoningExpandedByDefault"];
            set => Local.Values["ReasoningExpandedByDefault"] = value;
        }

        // ── RAG debug ───────────────────────────────────────────────────────
        public static bool RagDebugEnabled
        {
            get => Local.Values.ContainsKey("RagDebugEnabled") && (bool)Local.Values["RagDebugEnabled"];
            set => Local.Values["RagDebugEnabled"] = value;
        }

        // ── Source toggles (NetworkCharaPage) ─────────────────────────────────
        public static bool SourceEnabled_chub
        {
            get => GetBool("SourceEnabled_chub", true);
            set => SetBool("SourceEnabled_chub", value);
        }
        public static bool SourceEnabled_huayu
        {
            get => GetBool("SourceEnabled_huayu", true);
            set => SetBool("SourceEnabled_huayu", value);
        }
        public static bool SourceEnabled_xingye
        {
            get => GetBool("SourceEnabled_xingye", true);
            set => SetBool("SourceEnabled_xingye", value);
        }
        public static bool SourceEnabled_quack
        {
            get => GetBool("SourceEnabled_quack", true);
            set => SetBool("SourceEnabled_quack", value);
        }
        public static bool SourceEnabled_dzmm
        {
            get => GetBool("SourceEnabled_dzmm", true);
            set => SetBool("SourceEnabled_dzmm", value);
        }

        // ── Theme / Font / Accent ─────────────────────────────────────────────

        public static string ThemeName
        {
            get => GetString("ThemeName", "Mindscape");   // 默认：心象（全新设计语言）
            set => SetString("ThemeName", value);
        }

        public static string ChineseFontFamily
        {
            get => GetString("ChineseFontFamily", "ThemeDefault");
            set => SetString("ChineseFontFamily", value);
        }

        public static string EnglishFontFamily
        {
            get => GetString("EnglishFontFamily", "ThemeDefault");
            set => SetString("EnglishFontFamily", value);
        }

        public static string CustomAccentHex
        {
            get => GetString("CustomAccentHex", null);
            set => SetString("CustomAccentHex", value);
        }

        // ── Custom theme colors ──────────────────────────────────────────────

        public static string CustomTheme_PageBg
        {
            get => GetString("CustomTheme_PageBg", "#F5EFE0");
            set => SetString("CustomTheme_PageBg", value);
        }
        public static string CustomTheme_Accent
        {
            get => GetString("CustomTheme_Accent", "#A33A2C");
            set => SetString("CustomTheme_Accent", value);
        }
        public static string CustomTheme_Text
        {
            get => GetString("CustomTheme_Text", "#2B2620");
            set => SetString("CustomTheme_Text", value);
        }
        public static string CustomTheme_Surface
        {
            get => GetString("CustomTheme_Surface", "#EFE5D2");
            set => SetString("CustomTheme_Surface", value);
        }
        public static string CustomTheme_Border
        {
            get => GetString("CustomTheme_Border", "#C9B295");
            set => SetString("CustomTheme_Border", value);
        }
        public static string CustomTheme_UserBubble
        {
            get => GetString("CustomTheme_UserBubble", "#A33A2C");
            set => SetString("CustomTheme_UserBubble", value);
        }
        public static string CustomTheme_AiBubble
        {
            get => GetString("CustomTheme_AiBubble", "#FAF4E8");
            set => SetString("CustomTheme_AiBubble", value);
        }
        public static string CustomTheme_MutedText
        {
            get => GetString("CustomTheme_MutedText", "#6E5A48");
            set => SetString("CustomTheme_MutedText", value);
        }
        public static string CustomTheme_ChromeBg
        {
            get => GetString("CustomTheme_ChromeBg", "#2B2620");
            set => SetString("CustomTheme_ChromeBg", value);
        }
        public static string CustomTheme_SubtlPanel
        {
            get => GetString("CustomTheme_SubtlPanel", "#F0E5D0");
            set => SetString("CustomTheme_SubtlPanel", value);
        }
        public static string CustomTheme_Panel
        {
            get => GetString("CustomTheme_Panel", "#FAF4E8");
            set => SetString("CustomTheme_Panel", value);
        }
        public static string CustomTheme_PaneHeader
        {
            get => GetString("CustomTheme_PaneHeader", "#3D352B");
            set => SetString("CustomTheme_PaneHeader", value);
        }

        // ── Group sort order ──────────────────────────────────────────────────
        // 用两个平行 List 代替 Dictionary，避免序列化问题，同时兼容无 ValueTuple 的 C# 版本
        private const string GroupOrderKeysKey  = "GroupOrderKeys";
        private const string GroupOrderValsKey  = "GroupOrderVals";

        /// <summary>读取所有分组的排序权重（越小越靠前）</summary>
        public static void GetGroupOrders(out List<string> keys, out List<int> orders)
        {
            keys   = new List<string>();
            orders = new List<int>();
            if (!Local.Values.ContainsKey(GroupOrderKeysKey)) return;

            string raw = Local.Values[GroupOrderKeysKey] as string ?? "";
            string rawV = Local.Values.ContainsKey(GroupOrderValsKey)
                          ? Local.Values[GroupOrderValsKey] as string ?? "" : "";

            string[] ks = raw.Split('\n');
            string[] vs = rawV.Split('\n');
            for (int i = 0; i < ks.Length && i < vs.Length; i++)
            {
                string k = ks[i].Trim();
                if (string.IsNullOrEmpty(k)) continue;
                int v;
                if (!int.TryParse(vs[i].Trim(), out v)) v = i * 10;
                keys.Add(k);
                orders.Add(v);
            }
        }

        public static void SaveGroupOrders(List<string> keys, List<int> orders)
        {
            Local.Values[GroupOrderKeysKey] = string.Join("\n", keys);
            Local.Values[GroupOrderValsKey] = string.Join("\n", orders.Select(v => v.ToString()));
        }

        /// <summary>获取某个组的排序权重，未记录则返回默认值</summary>
        public static int GetGroupOrder(string groupKey)
        {
            List<string> keys; List<int> orders;
            GetGroupOrders(out keys, out orders);
            int idx = keys.IndexOf(groupKey);
            return idx >= 0 ? orders[idx] : int.MaxValue;
        }

        /// <summary>整体更新排序表（重新给所有组编号，步长10）</summary>
        public static void RenumberGroupOrders(List<string> orderedKeys)
        {
            var vals = new List<int>();
            for (int i = 0; i < orderedKeys.Count; i++) vals.Add(i * 10);
            SaveGroupOrders(orderedKeys, vals);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public static void ApplyTheme(FrameworkElement element)
        {
            if (element != null)
                element.RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light;
        }

        /// <summary>同时设Page.RequestedTheme，确保BottomAppBar也跟随主题</summary>
        public static void ApplyTheme(FrameworkElement element, Windows.UI.Xaml.Controls.Page page)
        {
            var theme = IsDark ? ElementTheme.Dark : ElementTheme.Light;
            if (element != null) element.RequestedTheme = theme;
            if (page != null)    page.RequestedTheme    = theme;
        }

        /// <summary>
        /// 运行时切换主题/配色后，强制让子树里的 {ThemeResource} 重新解析。
        /// 仅设 RequestedTheme=目标值在“值未变化”时（如换主题但仍是浅色）不会触发重解析，
        /// 导致自定义 ThemeDictionary 画刷不刷新（主题选了不生效、深色洗白）。
        /// 这里先切到相反主题再切回目标，逼框架完整重解析一次，最终落在目标主题上。
        /// </summary>
        public static void ApplyThemeForced(FrameworkElement element, Windows.UI.Xaml.Controls.Page page)
        {
            var target = IsDark ? ElementTheme.Dark : ElementTheme.Light;
            var other  = IsDark ? ElementTheme.Light : ElementTheme.Dark;
            if (element != null) { element.RequestedTheme = other; element.RequestedTheme = target; }
            if (page != null)    { page.RequestedTheme    = other; page.RequestedTheme    = target; }
        }


        public static bool GetBool(string key, bool defaultValue = false)
            => Local.Values.ContainsKey(key) ? (bool)Local.Values[key] : defaultValue;

        public static void SetBool(string key, bool value)
            => Local.Values[key] = value;

        public static string GetString(string key, string defaultValue = "")
            => Local.Values.ContainsKey(key) ? Local.Values[key] as string ?? defaultValue : defaultValue;

        public static string SearchBaseUrl
        {
            get => Local.Values.ContainsKey("SearchBaseUrl") ? Local.Values["SearchBaseUrl"] as string ?? "https://searx.be" : "https://searx.be";
            set => Local.Values["SearchBaseUrl"] = value;
        }

        public static void SetString(string key, string value)
            => Local.Values[key] = value;

        /// <summary>If UseSubAgent is enabled and SubAgentUrl is set, returns a temporary ApiProfile
        /// using sub-agent config. Otherwise returns null.</summary>
        public static ApiProfile GetSubAgentProfile()
        {
            if (UseSubAgent && !string.IsNullOrEmpty(SubAgentUrl))
            {
                return new ApiProfile
                {
                    Id   = "__subagent__",
                    Name = "子智能体",
                    Url  = SubAgentUrl,
                    ApiKey = SubAgentApiKey,
                    Model  = SubAgentModel,
                    ProviderType = SubAgentProviderType,
                };
            }
            return null;
        }

        /// <summary>同步从byte[]加载BitmapImage（用于非async context如属性getter）</summary>
        public static Windows.UI.Xaml.Media.Imaging.BitmapImage LoadBitmapSync(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
            try
            {
                var buf = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes);
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    // InMemoryRandomAccessStream写内存是同步完成的，GetResults()安全
                    ms.WriteAsync(buf).GetResults();
                    ms.Seek(0);
                    bmp.SetSource(ms);
                }
            }
            catch { return null; }
            return bmp;
        }

    }
}
