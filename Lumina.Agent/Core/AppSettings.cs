using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;

namespace yanshuai
{
    public static class AppSettings
    {
        // 缓存容器引用：LocalSettings 是 app 生命周期内稳定的单例，
        // 之前用 => 属性会让每次设置读写都重新跨 WinRT ABI 取一遍（每个 getter 还引用两次）
        private static readonly ApplicationDataContainer Local = ApplicationData.Current.LocalSettings;

        public static bool IsDark
        {
            get => Local.Values.ContainsKey("DarkMode") && (bool)Local.Values["DarkMode"];
            set => Local.Values["DarkMode"] = value;
        }

        public static bool IsEnglish
        {
            get => Local.Values.ContainsKey("LangEn") && (bool)Local.Values["LangEn"];
            set => Local.Values["LangEn"] = value;
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

        // ── Web 搜索 ──────────────────────────────────────────────────────────
        // ── PLAA 评估 ────────────────────────────────────────────────────────
        /// <summary>PLAA 服务器地址（预留）</summary>
        public static string PlaaServerUrl
        {
            get => Local.Values.ContainsKey("PlaaServerUrl") ? Local.Values["PlaaServerUrl"] as string ?? "" : "";
            set => Local.Values["PlaaServerUrl"] = value;
        }

        /// <summary>PLAA 服务器 API Key</summary>
        public static string PlaaApiKey
        {
            get => Local.Values.ContainsKey("PlaaApiKey") ? Local.Values["PlaaApiKey"] as string ?? "" : "";
            set => Local.Values["PlaaApiKey"] = value;
        }

        /// <summary>0=SearXNG, 1=DuckDuckGo</summary>
        public static int SearchProvider
        {
            get
            {
                if (!Local.Values.ContainsKey("SearchProvider")) return 0;
                int v = (int)Local.Values["SearchProvider"];
                // 旧版枚举：0=Serper,1=Brave,2=SearXNG,3=DuckDuckGo；现只有2项
                // 旧值2→新0(SearXNG), 旧值3→新1(DuckDuckGo), 其余→0
                if (v == 2) return 0;
                if (v == 3) return 1;
                if (v < 0 || v > 1) return 0;
                return v;
            }
            set => Local.Values["SearchProvider"] = value;
        }
        public static string SearchApiKey
        {
            get => Local.Values.ContainsKey("SearchApiKey") ? Local.Values["SearchApiKey"] as string ?? "" : "";
            set => Local.Values["SearchApiKey"] = value;
        }
        /// <summary>0=仅摘要(2000字), 1=完整正文(8000字), 2=完整页面(不截断)</summary>
        public static int SearchResultDepth
        {
            get => Local.Values.ContainsKey("SearchResultDepth") ? (int)Local.Values["SearchResultDepth"] : 0;
            set => Local.Values["SearchResultDepth"] = value;
        }

        /// <summary>SearXNG 实例 URL，例如 https://searx.example.com</summary>
        public static string SearchBaseUrl
        {
            get => Local.Values.ContainsKey("SearchBaseUrl") ? Local.Values["SearchBaseUrl"] as string ?? "" : "";
            set => Local.Values["SearchBaseUrl"] = value;
        }

        // ── Agent 最大工具调用轮次 ────────────────────────────────────────────
        public static int MaxToolTurns
        {
            get => Local.Values.ContainsKey("MaxToolTurns") ? (int)Local.Values["MaxToolTurns"] : 15;
            set => Local.Values["MaxToolTurns"] = value;
        }

        // ── AI 工作目录 ───────────────────────────────────────────────────────
        /// <summary>用户授权的 AI 工作目录路径（read_file/write_file/list_files 在此目录内操作）。
        /// 实际访问凭据存于 FutureAccessList，按此 Metadata 路径匹配检索（跨会话稳定）。</summary>
        public static string WorkingDirPath
        {
            get => Local.Values.ContainsKey("WorkingDirPath") ? Local.Values["WorkingDirPath"] as string ?? "" : "";
            set => Local.Values["WorkingDirPath"] = value;
        }

        // ── 回复声音 ──────────────────────────────────────────────────────────
        public static bool ReplySoundEnabled
        {
            get => !Local.Values.ContainsKey("ReplySoundEnabled") || (bool)Local.Values["ReplySoundEnabled"];
            set => Local.Values["ReplySoundEnabled"] = value;
        }

        // ── 思考过程默认展开 ──────────────────────────────────────────────────
        public static bool ReasoningExpandedByDefault
        {
            get => Local.Values.ContainsKey("ReasoningExpandedByDefault") && (bool)Local.Values["ReasoningExpandedByDefault"];
            set => Local.Values["ReasoningExpandedByDefault"] = value;
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

        // ── Allow AI to fetch search engine pages directly ────────────────────
        public static bool AllowFetchSearchPage
        {
            get => Local.Values.ContainsKey("AllowFetchSearchPage") && (bool)Local.Values["AllowFetchSearchPage"];
            set => Local.Values["AllowFetchSearchPage"] = value;
        }

        // ── Fetch User-Agent ──────────────────────────────────────────────────        // 预设索引：0=WM10 IE, 1=Chrome, 2=Edge, 3=Safari, 4=Firefox, 5=自定义
        public static int FetchUAPreset
        {
            get => Local.Values.ContainsKey("FetchUAPreset") ? (int)Local.Values["FetchUAPreset"] : 1;
            set => Local.Values["FetchUAPreset"] = value;
        }

        public static string FetchUACustom
        {
            get => Local.Values.ContainsKey("FetchUACustom") ? Local.Values["FetchUACustom"] as string ?? "" : "";
            set => Local.Values["FetchUACustom"] = value;
        }

        private static readonly string[] _uaPresets = new[]
        {
            // 0: Windows Phone 10 / IE Mobile 11
            "Mozilla/5.0 (Windows Phone 10.0; Android 6.0.1; Microsoft; Lumia 950) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/52.0.2743.116 Mobile Safari/537.36 Edge/15.15254",
            // 1: Chrome (Windows)
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            // 2: Edge (Windows)
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0",
            // 3: Safari (macOS)
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_4) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
            // 4: Firefox (Windows)
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
        };

        /// <summary>返回当前生效的 User-Agent 字符串</summary>
        public static string FetchUserAgent
        {
            get
            {
                int idx = FetchUAPreset;
                if (idx >= 0 && idx < _uaPresets.Length) return _uaPresets[idx];
                // idx == 5：自定义
                string custom = FetchUACustom;
                return string.IsNullOrWhiteSpace(custom) ? _uaPresets[1] : custom;
            }
        }

        // ── 长记忆(全局,取代旧的每会话 Conversation.Memory* 字段) ─────────────
        /// <summary>启用 AI 自动总结/注入长记忆(全局)。</summary>
        public static bool MemoryEnabled
        {
            get => Local.Values.ContainsKey("MemoryEnabled") && (bool)Local.Values["MemoryEnabled"];
            set => Local.Values["MemoryEnabled"] = value;
        }
        /// <summary>用于记忆总结的 API 配置 ID(空=用对话当前 API)。</summary>
        public static string MemoryApiProfileId
        {
            get => Local.Values.ContainsKey("MemoryApiId") ? Local.Values["MemoryApiId"] as string ?? "" : "";
            set => Local.Values["MemoryApiId"] = value;
        }
        /// <summary>每隔几轮对话总结一次记忆。</summary>
        public static int MemorySummaryInterval
        {
            get => Local.Values.ContainsKey("MemorySumInterval") ? (int)Local.Values["MemorySumInterval"] : 10;
            set => Local.Values["MemorySumInterval"] = value;
        }
        /// <summary>每隔几轮注入一次记忆到上下文。</summary>
        public static int MemoryInjectInterval
        {
            get => Local.Values.ContainsKey("MemoryInjInterval") ? (int)Local.Values["MemoryInjInterval"] : 1;
            set => Local.Values["MemoryInjInterval"] = value;
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

        public static string S(string zh, string en) => IsEnglish ? en : zh;

        public static bool GetBool(string key, bool defaultValue = false)
            => Local.Values.ContainsKey(key) ? (bool)Local.Values[key] : defaultValue;

        public static void SetBool(string key, bool value)
            => Local.Values[key] = value;

        public static string GetString(string key, string defaultValue = "")
            => Local.Values.ContainsKey(key) ? Local.Values[key] as string ?? defaultValue : defaultValue;

        public static void SetString(string key, string value)
            => Local.Values[key] = value;

        // ── 搜索 API 池持久化 ──────────────────────────────────────────────
        private const string SearchApisFileName = "search_apis.json";

        public static async Task<List<SearchApiEntry>> LoadSearchApisAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(SearchApisFileName).AsTask();
                using (var s = await file.OpenStreamForReadAsync())
                {
                    var ser = new DataContractJsonSerializer(typeof(List<SearchApiEntry>));
                    return (List<SearchApiEntry>)ser.ReadObject(s);
                }
            }
            catch { return null; }
        }

        public static async Task SaveSearchApisAsync(List<SearchApiEntry> entries)
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(SearchApisFileName, CreationCollisionOption.ReplaceExisting).AsTask();
                using (var s = await file.OpenStreamForWriteAsync())
                {
                    var ser = new DataContractJsonSerializer(typeof(List<SearchApiEntry>));
                    ser.WriteObject(s, entries);
                }
            }
            catch { }
        }

        // ── RAG / 离线嵌入 ───────────────────────────────────────────────────
        /// <summary>启用本地嵌入引擎进行离线 RAG 检索</summary>
        public static bool RagEnabled
        {
            get => Local.Values.ContainsKey("RagEnabled") && (bool)Local.Values["RagEnabled"];
            set => Local.Values["RagEnabled"] = value;
        }

        /// <summary>每次检索返回的结果数</summary>
        public static int RagTopK
        {
            get => Local.Values.ContainsKey("RagTopK") ? (int)Local.Values["RagTopK"] : 5;
            set => Local.Values["RagTopK"] = value;
        }

        /// <summary>语义相似度阈值 (0.0~1.0)，低于此值的结果被丢弃</summary>
        public static double RagSimilarityThreshold
        {
            get => Local.Values.ContainsKey("RagSimThresh") ? (double)Local.Values["RagSimThresh"] : 0.3;
            set => Local.Values["RagSimThresh"] = value;
        }

        /// <summary>同步从byte[]加载BitmapImage（用于非async context如属性getter）</summary>
        public static Windows.UI.Xaml.Media.Imaging.BitmapImage LoadBitmapSync(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
            try
            {
                using (var ms = new InMemoryRandomAccessStream())
                {
                    ms.WriteAsync(bytes.AsBuffer()).AsTask().Wait();
                    ms.Seek(0);
                    bmp.SetSource(ms);
                }
            }
            catch { return null; }
            return bmp;
        }

        // ── 新对话附加上下文 ──────────────────────────────────────────

        /// <summary>新建对话时，从最近 N 个对话中各取 M 条消息</summary>
        public static int NewConvContextCount
        {
            get => Local.Values.ContainsKey("NewCtxCnt") ? (int)Local.Values["NewCtxCnt"] : 0;
            set => Local.Values["NewCtxCnt"] = value;
        }

        /// <summary>每个对话取最近几条消息</summary>
        public static int NewConvMessageCount
        {
            get => Local.Values.ContainsKey("NewCtxMsgCnt") ? (int)Local.Values["NewCtxMsgCnt"] : 0;
            set => Local.Values["NewCtxMsgCnt"] = value;
        }

    }
}