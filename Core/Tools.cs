using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.ApplicationModel.Appointments;
using Windows.ApplicationModel.Contacts;
using Windows.System;
using Windows.System.Profile;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════
    // Tool 定义数据模型
    // ══════════════════════════════════════════════════════════════════════

    [DataContract]
    public class ToolParameterProperty
    {
        [DataMember(Name = "type")]        public string Type        { get; set; } = "string";
        [DataMember(Name = "description")] public string Description { get; set; } = "";
    }

    [DataContract]
    public class ToolParameters
    {
        [DataMember(Name = "type")]       public string       Type       { get; set; } = "object";
        [DataMember(Name = "properties")] public Dictionary<string, ToolParameterProperty> Properties { get; set; }
            = new Dictionary<string, ToolParameterProperty>();
        [DataMember(Name = "required")]   public List<string> Required   { get; set; } = new List<string>();
    }

    [DataContract]
    public class ToolFunction
    {
        [DataMember(Name = "name")]        public string        Name        { get; set; }
        [DataMember(Name = "description")] public string        Description { get; set; }
        [DataMember(Name = "parameters")]  public ToolParameters Parameters { get; set; }
    }

    [DataContract]
    public class ToolDefinition
    {
        [DataMember(Name = "type")]     public string      Type     { get; set; } = "function";
        [DataMember(Name = "function")] public ToolFunction Function { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Tool Call 响应模型（API 返回）
    // ══════════════════════════════════════════════════════════════════════

    [DataContract]
    public class ToolCallFunction
    {
        [DataMember(Name = "name")]      public string Name      { get; set; }
        [DataMember(Name = "arguments")] public string Arguments { get; set; }
    }

    [DataContract]
    public class ToolCall
    {
        [DataMember(Name = "id")]       public string           Id       { get; set; }
        [DataMember(Name = "type")]     public string           Type     { get; set; } = "function";
        [DataMember(Name = "function")] public ToolCallFunction Function { get; set; }
    }

    [DataContract]
    public class ApiMessageWithTools
    {
        [DataMember(Name = "role")]             public string      Role      { get; set; }
        [DataMember(Name = "content")]          public string      Content   { get; set; }
        [DataMember(Name = "tool_calls")]       public List<ToolCall> ToolCalls { get; set; }
        [DataMember(Name = "tool_call_id")]     public string      ToolCallId { get; set; }

        public string ImageBase64   { get; set; }
        public string ImageMimeType { get; set; }
    }

    [DataContract]
    public class ToolChoiceMessage
    {
        [DataMember(Name = "role")]       public string      Role       { get; set; }
        [DataMember(Name = "content")]    public string      Content    { get; set; }
        [DataMember(Name = "tool_calls")] public List<ToolCall> ToolCalls { get; set; }
    }

    [DataContract]
    public class ToolResponseChoice
    {
        [DataMember(Name = "index")]  public int               Index   { get; set; }
        [DataMember(Name = "message")] public ToolChoiceMessage Message { get; set; }
        [DataMember(Name = "finish_reason")] public string     FinishReason { get; set; }
    }

    [DataContract]
    public class ToolApiResponse
    {
        [DataMember(Name = "choices")] public List<ToolResponseChoice> Choices { get; set; }
        [DataMember(Name = "error")]   public ApiErrorDetail           Error   { get; set; }
        [DataMember(Name = "usage")]  public object                   Usage   { get; set; }
    }

    public class FunctionCallLoopResult
    {
        public string Content { get; set; }
        public string Reasoning { get; set; }
        public List<ApiMessageWithTools> AllMessages { get; set; }
    }

    /// <summary>
    /// 敏感工具执行前的权限确认回调。
    /// 参数：工具名、描述文本。返回 true 表示用户允许。
    /// </summary>
    public delegate System.Threading.Tasks.Task<bool> ToolPermissionCallback(string toolName, string description);
public delegate System.Threading.Tasks.Task<string> FolderAccessCallback(string requestedPath);
public delegate void ToolProgressCallback(string phase, string toolName, string detail);

    public class SearchResultItem
    {
        public string Title { get; set; }
        public string Snippet { get; set; }
        public string Url { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Function Calling 引擎
    // ══════════════════════════════════════════════════════════════════════

    public static class FunctionCallEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ── 设备识别 ──────────────────────────────────────────────────────
        public static bool FullTrust { get; set; } = false;

        public static bool IsDesktop { get; } =
            AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Desktop";
        public static bool IsMobile { get; } =
            AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Mobile";

        // ── UI 线程调度 ──────────────────────────────────────────────────
        private static async Task<bool> LaunchUriOnUiAsync(Uri uri)
        {
            var tcs = new TaskCompletionSource<bool>();
            var _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        bool ok = await Launcher.LaunchUriAsync(uri);
                        tcs.TrySetResult(ok);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
            return await tcs.Task;
        }

        // ── 搜索 API 池（懒加载） ──────────────────────────────────────────
        private class UrlLatency { public string Url; public long Ms; }
        private static List<SearchApiEntry> _searchApiPool = null;
        private static List<string> _cachedSearxngUrls = null;
        private static DateTime _searxngCacheTime = DateTime.MinValue;

        public static List<ToolDefinition> GetTools()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "web_search",
                        Description = "搜索互联网获取实时信息，如新闻、天气、百科知识等。无法打开 Google。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["query"] = new ToolParameterProperty { Type = "string", Description = "搜索关键词，简短精确" },
                                ["max_results"] = new ToolParameterProperty { Type = "string", Description = "返回结果数量，默认5" },
                            },
                            Required = new List<string> { "query" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "fetch_page",
                        Description = "读取指定 URL 的网页正文内容。不能用于 google.com。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["url"] = new ToolParameterProperty { Type = "string", Description = "要读取的完整 URL" },
                            },
                            Required = new List<string> { "url" }
                        }
                    }
                },

                // ── 文件系统 ────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "list_files",
                        Description = "列出指定目录中的文件和子目录。路径示例：sd:（SD卡）, 空=应用本地目录，或完整路径",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["path"] = new ToolParameterProperty { Type = "string", Description = "目录路径，sd:=SD卡根目录，空=应用本地目录，或完整路径" },
                            },
                            Required = new List<string> { }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "read_file",
                        Description = "读取文本文件的内容",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["path"] = new ToolParameterProperty { Type = "string", Description = "文件路径（支持 sd: 前缀）" },
                            },
                            Required = new List<string> { "path" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "write_file",
                        Description = "将内容写入文本文件（不存在则创建，存在则覆盖）",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["path"] = new ToolParameterProperty { Type = "string", Description = "文件路径（支持 sd: 前缀）" },
                                ["content"] = new ToolParameterProperty { Type = "string", Description = "要写入的内容" },
                            },
                            Required = new List<string> { "path", "content" }
                        }
                    }
                },

                // ── 文件夹访问权限 ──────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "request_folder_access",
                        Description = "当其他文件操作因权限不足而失败时，调用此工具请求用户授予指定文件夹的访问权限。用户通过系统文件夹选择器授权后，该文件夹及其子路径即可被 write_file、read_file、list_files 正常访问。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["path"] = new ToolParameterProperty { Type = "string", Description = "需要访问的文件夹路径（用于提示用户选择哪个文件夹）" },
                            },
                            Required = new List<string> { "path" }
                        }
                    }
                },

                // ── 日历 ────────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "calendar_list",
                        Description = "列出即将到来的日历事件",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["max_count"] = new ToolParameterProperty { Type = "string", Description = "最多返回事件数，默认10" },
                            },
                            Required = new List<string> { }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "calendar_create",
                        Description = "创建新的日历事件",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["title"] = new ToolParameterProperty { Type = "string", Description = "事件标题" },
                                ["start_time"] = new ToolParameterProperty { Type = "string", Description = "开始时间，格式 yyyy-MM-dd HH:mm" },
                                ["duration_minutes"] = new ToolParameterProperty { Type = "string", Description = "持续时间（分钟），默认60" },
                                ["location"] = new ToolParameterProperty { Type = "string", Description = "地点（可选）" },
                                ["details"] = new ToolParameterProperty { Type = "string", Description = "详细描述（可选）" },
                            },
                            Required = new List<string> { "title", "start_time" }
                        }
                    }
                },

                // ── 联系人 ──────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "contacts_search",
                        Description = "搜索联系人姓名或电话号码",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["query"] = new ToolParameterProperty { Type = "string", Description = "搜索关键词" },
                            },
                            Required = new List<string> { "query" }
                        }
                    }
                },

                // ── 电话 ────────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "make_call",
                        Description = "拨打电话（仅限手机端）",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["phone_number"] = new ToolParameterProperty { Type = "string", Description = "电话号码" },
                            },
                            Required = new List<string> { "phone_number" }
                        }
                    }
                },

                // ── 短信 ────────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "send_sms",
                        Description = "发送短信（仅限手机端）",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["phone_number"] = new ToolParameterProperty { Type = "string", Description = "接收号码" },
                                ["message"] = new ToolParameterProperty { Type = "string", Description = "短信内容" },
                            },
                            Required = new List<string> { "phone_number", "message" }
                        }
                    }
                },

                // ── 打开应用 ──────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "open_app",
                        Description = IsDesktop
                            ? "通过 URI 协议或应用名称打开 Windows 应用或系统设置页面。常用 URI：ms-settings:（设置）、ms-settings:network（网络）、ms-settings:bluetooth（蓝牙）、calculator:（计算器）、mail:（邮件）、bingmaps:（地图）、ms-clock:（闹钟）。也可传入已安装应用的包名。"
                            : "通过 URI 协议打开手机应用或系统页面。常用 URI：ms-settings:（设置）、tel:号码（电话）、sms:号码（短信）、mail:（邮件）、maps:（地图）、calculator:（计算器）、zune:music（音乐）。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["uri_or_name"] = new ToolParameterProperty { Type = "string", Description = "应用的 URI 协议（如 ms-settings:）或应用名称/包名" },
                            },
                            Required = new List<string> { "uri_or_name" }
                        }
                    }
                },
            };
        }

        public static async Task<ApiMessageWithTools> ExecuteToolAsync(
            ToolCall call, Conversation conv,
            ToolPermissionCallback permissionCallback = null,
            FolderAccessCallback folderAccessCallback = null)
        {
            var result = new ApiMessageWithTools
            {
                Role = "tool",
                ToolCallId = call.Id,
                Content = ""
            };

            try
            {
                string name = call.Function?.Name ?? "";
                string argsJson = call.Function?.Arguments ?? "{}";

                // ── 敏感工具权限检查（FullTrust 模式跳过） ──────────────────
                if (!FullTrust && IsSensitiveTool(name))
                {
                    string permKey = BuildPermKey(name, argsJson);
                    if (!await CheckPermissionAsync(permKey))
                    {
                        if (permissionCallback == null)
                        {
                            result.Content = "此操作需要用户授权，但当前不支持权限确认。";
                            return result;
                        }
                        string desc = BuildPermDescription(name, argsJson);
                        bool allowed = await permissionCallback(name, desc);
                        if (!allowed)
                        {
                            result.Content = "用户拒绝了此操作，请停止该任务或改用其他方式完成。";
                            return result;
                        }
                        await GrantPermissionAsync(permKey);
                    }
                }

                switch (name)
                {
                    case "web_search":
                        result.Content = await ExecuteWebSearch(argsJson, conv);
                        break;
                    case "fetch_page":
                        result.Content = await ExecuteFetchPage(argsJson);
                        break;
                    case "list_files":
                        result.Content = await ExecuteListFiles(argsJson);
                        break;
                    case "read_file":
                        result.Content = await ExecuteReadFile(argsJson);
                        break;
                    case "write_file":
                        result.Content = await ExecuteWriteFile(argsJson);
                        break;
                    case "request_folder_access":
                        result.Content = await ExecuteRequestFolderAccess(argsJson, folderAccessCallback);
                        break;
                    case "calendar_list":
                        result.Content = await ExecuteCalendarList(argsJson);
                        break;
                    case "calendar_create":
                        result.Content = await ExecuteCalendarCreate(argsJson);
                        break;
                    case "contacts_search":
                        result.Content = await ExecuteContactsSearch(argsJson);
                        break;
                    case "make_call":
                        result.Content = await ExecuteMakeCall(argsJson);
                        break;
                    case "send_sms":
                        result.Content = await ExecuteSendSms(argsJson);
                        break;
                    case "open_app":
                        result.Content = await ExecuteOpenApp(argsJson);
                        break;
                    default:
                        result.Content = $"错误：未知工具 \"{name}\"";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Content = $"工具执行出错：{ex.Message}";
            }

            return result;
        }

        // 返回当前已授权的文件夹路径列表（供系统提示注入）
        public static async Task<List<string>> GetGrantedFolderPathsAsync()
        {
            var result = new List<string>();
            var accessList = StorageApplicationPermissions.FutureAccessList;
            for (int i = 0; i < accessList.Entries.Count; i++)
            {
                try
                {
                    var entry = accessList.Entries[i];
                    // metadata 里存的是路径（我们写入时存了 folder.Path 作为 metadata）
                    if (!string.IsNullOrEmpty(entry.Metadata))
                    {
                        result.Add(entry.Metadata);
                        continue;
                    }
                    // 兜底：实际获取文件夹读路径
                    var folder = await accessList.GetFolderAsync(entry.Token);
                    result.Add(folder.Path);
                }
                catch { }
            }
            return result;
        }

        // 持久化已授权的敏感操作（key = 工具名:路径/标题）
        private static HashSet<string> _grantedPermissions = null;
        private static bool _permsLoaded = false;

        private static async Task LoadPermissionsAsync()
        {
            if (_permsLoaded) return;
            _permsLoaded = true;
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    "yanshuaiPerms.json", CreationCollisionOption.OpenIfExists);
                string json = await FileIO.ReadTextAsync(file);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    // 简单格式：每行一个 key
                    var keys = json.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    _grantedPermissions = new HashSet<string>(
                        keys.Select(k => k.Trim()), StringComparer.OrdinalIgnoreCase);
                    return;
                }
            }
            catch { }
            _grantedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static async Task SavePermissionsAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    "yanshuaiPerms.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, string.Join("\n", _grantedPermissions));
            }
            catch { }
        }

        private static async Task<bool> CheckPermissionAsync(string permKey)
        {
            await LoadPermissionsAsync();
            return _grantedPermissions.Contains(permKey);
        }

        private static async Task GrantPermissionAsync(string permKey)
        {
            await LoadPermissionsAsync();
            _grantedPermissions.Add(permKey);
            await SavePermissionsAsync();
        }

        public static void ResetGrantedPermissions() => _grantedPermissions?.Clear();

        private static bool IsSensitiveTool(string name)
            => name == "write_file" || name == "calendar_create";

        private static string BuildPermKey(string name, string argsJson)
        {
            if (name == "write_file")
                return "write_file:" + ExtractJsonString(argsJson, "path");
            if (name == "calendar_create")
                return "calendar_create:" + ExtractJsonString(argsJson, "title");
            return name;
        }

        private static string BuildPermDescription(string name, string argsJson)
        {
            if (name == "write_file")
            {
                string path = ExtractJsonString(argsJson, "path");
                string content = ExtractJsonString(argsJson, "content");
                int preview = Math.Min(content?.Length ?? 0, 80);
                string snippet = preview > 0 ? content.Substring(0, preview) + (content.Length > 80 ? "…" : "") : "";
                return $"写入文件：{path}\n内容预览：{snippet}";
            }
            if (name == "calendar_create")
            {
                string title = ExtractJsonString(argsJson, "title");
                string start = ExtractJsonString(argsJson, "start_time");
                string dur   = ExtractJsonString(argsJson, "duration_minutes");
                return $"创建日历事件：{title}\n时间：{start}，时长 {dur} 分钟";
            }
            return name;
        }

        private static async Task<string> ExecuteWebSearch(string argsJson, Conversation conv)
        {
            var query = ExtractJsonString(argsJson, "query");
            if (string.IsNullOrWhiteSpace(query))
                return "错误：搜索关键词为空";
            return await RunSearchAsync(query);
        }

        private static async Task<string> ExecuteFetchPage(string argsJson)
        {
            var url = ExtractJsonString(argsJson, "url");
            if (string.IsNullOrWhiteSpace(url))
                return "错误：URL 为空";

            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                string host = uri.Host.ToLower();
                if (host == "google.com" || host.EndsWith(".google.com") ||
                    host == "google.com.hk" || host.EndsWith(".google.com.hk"))
                    return "错误：禁止访问 Google 域名，请使用其他搜索引擎。";
            }

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", AppSettings.FetchUserAgent);
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode)
                        return $"无法访问页面（{(int)resp.StatusCode}）";

                    string html = await resp.Content.ReadAsStringAsync();
                    html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
                    html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
                    var text = Regex.Replace(html, @"<[^>]+>", " ");
                    text = Regex.Replace(text, @"\s{2,}", "\n").Trim();

                    int depth = AppSettings.SearchResultDepth;
                    int limit = depth == 0 ? 2000 : depth == 1 ? 8000 : int.MaxValue;
                    return text.Length > limit ? text.Substring(0, limit) + "\n…（已截断）" : text;
                }
            }
            catch (Exception ex)
            {
                return $"Fetch 失败：{ex.Message}";
            }
        }

        // ── 搜索 API 池 ────────────────────────────────────────────────────

        private static async Task<List<SearchApiEntry>> GetSearchPoolAsync()
        {
            if (_searchApiPool != null) return _searchApiPool;
            var saved = await AppSettings.LoadSearchApisAsync();
            if (saved != null && saved.Count > 0)
                _searchApiPool = saved;
            else
                _searchApiPool = SearchSettingsPage.BuildDefaultEntriesPublic();
            return _searchApiPool;
        }

        private static async Task<List<string>> GetUsableSearxngAsync(IList<SearchApiEntry> pool)
        {
            if (_cachedSearxngUrls != null && (DateTime.Now - _searxngCacheTime).TotalMinutes < 15)
                return _cachedSearxngUrls;

            var instances = pool.Where(e => e.Type == "searxng" && e.Enabled && !string.IsNullOrEmpty(e.Value))
                                .Select(e => e.Value).ToArray();
            if (instances.Length == 0) return null;

            var results = new List<UrlLatency>();
            var tasks = new List<Task>();

            for (int i = 0; i < instances.Length; i++)
            {
                string inst = instances[i];
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var hreq = new HttpRequestMessage(HttpMethod.Head, inst + "/search?q=test&format=json");
                        hreq.Headers.TryAddWithoutValidation("Accept", "application/json");
                        using (var cts2 = new System.Threading.CancellationTokenSource(8000))
                        using (var hresp = await _http.SendAsync(hreq, cts2.Token))
                        {
                            sw.Stop();
                            if ((int)hresp.StatusCode < 500)
                            {
                                lock (results)
                                    results.Add(new UrlLatency { Url = inst, Ms = sw.ElapsedMilliseconds });
                            }
                        }
                    }
                    catch { }
                }));
            }

            await Task.WhenAll(tasks);

            if (results.Count > 0)
            {
                _cachedSearxngUrls = results.OrderBy(r => r.Ms).Select(r => r.Url).ToList();
                _searxngCacheTime = DateTime.Now;
            }
            return _cachedSearxngUrls;
        }

        private static async Task<string> RunSearchAsync(string query)
        {
            try
            {
                var pool = await GetSearchPoolAsync();
                var enabled = pool.Where(e => e.Enabled).ToList();
                if (enabled.Count == 0)
                    return "[搜索失败：未启用任何搜索源，请前往设置开启]";

                // 1. Bing
                var bingEntry = enabled.FirstOrDefault(e => e.Type == "bing" && !string.IsNullOrEmpty(e.Value));
                if (bingEntry != null)
                {
                    string result = await RunBingSearchAsync(query, bingEntry.Value);
                    if (!string.IsNullOrEmpty(result)) return result;
                }

                // 2. SearXNG — 逐个尝试可用实例
                bool hasSearxng = enabled.Any(e => e.Type == "searxng" && !string.IsNullOrEmpty(e.Value));
                if (hasSearxng)
                {
                    var usable = await GetUsableSearxngAsync(pool);
                    if (usable != null && usable.Count > 0)
                    {
                        foreach (string baseUrl in usable)
                        {
                            try
                            {
                                var url = baseUrl.TrimEnd('/') + "/search?q=" + Uri.EscapeDataString(query) + "&format=json&pageno=1";
                                var sreq = new HttpRequestMessage(HttpMethod.Get, url);
                                sreq.Headers.TryAddWithoutValidation("Accept", "application/json");
                                using (var cts = new System.Threading.CancellationTokenSource(10000))
                                using (var sresp = await _http.SendAsync(sreq, cts.Token))
                                {
                                    if (sresp.IsSuccessStatusCode)
                                    {
                                        string r = ParseSearxngResults(await sresp.Content.ReadAsStringAsync());
                                        if (!string.IsNullOrEmpty(r)) return r;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                // 3. DuckDuckGo Lite
                if (enabled.Any(e => e.Type == "ddg"))
                {
                    try
                    {
                        var dreq = new HttpRequestMessage(HttpMethod.Get, "https://lite.duckduckgo.com/lite/?q=" + Uri.EscapeDataString(query));
                        dreq.Headers.TryAddWithoutValidation("User-Agent", AppSettings.FetchUserAgent);
                        using (var cts = new System.Threading.CancellationTokenSource(10000))
                        using (var dresp = await _http.SendAsync(dreq, cts.Token))
                        {
                            if (dresp.IsSuccessStatusCode)
                            {
                                string r = ParseDdgLiteResults(await dresp.Content.ReadAsStringAsync());
                                if (!string.IsNullOrEmpty(r)) return r;
                            }
                        }
                    }
                    catch { }
                }

                return "[搜索无结果：所有搜索源均已尝试但未返回有效数据，请检查网络或稍后重试]";
            }
            catch (Exception ex)
            {
                return $"[搜索异常：{ex.Message}]";
            }
        }

        private static async Task<string> RunBingSearchAsync(string query, string subscriptionKey)
        {
            try
            {
                var url = "https://api.bing.microsoft.com/v7.0/search?q=" + Uri.EscapeDataString(query) + "&mkt=zh-CN&count=5";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", subscriptionKey);
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    return ParseBingResults(await resp.Content.ReadAsStringAsync());
                }
            }
            catch { return null; }
        }

        private static string ParseBingResults(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var sb = new StringBuilder();
            int idx = json.IndexOf("\"value\":");
            if (idx < 0) return null;
            int searchFrom = idx;
            int count = 0;
            while (count < 5)
            {
                int nameIdx = json.IndexOf("\"name\":", searchFrom);
                if (nameIdx < 0) break;
                string title = ExtractJsonString(json, nameIdx + 7);
                int urlIdx = json.IndexOf("\"url\":", nameIdx);
                if (urlIdx < 0) break;
                string url = ExtractJsonString(json, urlIdx + 6);
                int snipIdx = json.IndexOf("\"snippet\":", nameIdx);
                string snippet = snipIdx >= 0 ? ExtractJsonString(json, snipIdx + 10) : "";
                sb.AppendLine($"- {title}\n  {snippet}\n  {url}");
                searchFrom = urlIdx + 6;
                count++;
                if (sb.Length > 2000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string ParseSearxngResults(string json)
        {
            var sb = new StringBuilder();
            int arrStart = json.IndexOf("\"results\":");
            if (arrStart < 0) return null;
            int idx = json.IndexOf('[', arrStart);
            if (idx < 0) return null;

            int count = 0;
            while (count < 8)
            {
                int braceStart = json.IndexOf('{', idx);
                if (braceStart < 0) break;
                int braceEnd = json.IndexOf('}', braceStart);
                if (braceEnd < 0) break;

                string block = json.Substring(braceStart, braceEnd - braceStart + 1);
                string title   = ExtractJsonValue(block, "title");
                string content = ExtractJsonValue(block, "content");
                string url     = ExtractJsonValue(block, "url");

                if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(content))
                {
                    sb.AppendLine($"- {title}\n  {content}\n  {url}");
                    count++;
                }
                idx = braceEnd + 1;
                if (sb.Length > 3000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string ParseDdgLiteResults(string html)
        {
            var sb = new StringBuilder();
            int idx = 0;
            int count = 0;
            while (count < 5)
            {
                int linkIdx = html.IndexOf("<a rel=\"nofollow\"", idx);
                if (linkIdx < 0) break;
                int hrefStart = html.IndexOf("href=\"", linkIdx) + 6;
                if (hrefStart < 6) break;
                int hrefEnd = html.IndexOf('"', hrefStart);
                string url = hrefEnd > hrefStart ? html.Substring(hrefStart, hrefEnd - hrefStart) : "";

                int titleStart = html.IndexOf('>', hrefEnd) + 1;
                int titleEnd   = html.IndexOf("</a>", titleStart);
                string title = titleStart > 0 && titleEnd > titleStart
                    ? StripHtmlTags(html.Substring(titleStart, titleEnd - titleStart)).Trim()
                    : "";

                int snipIdx = html.IndexOf("result-snippet", titleEnd);
                string snippet = "";
                if (snipIdx >= 0 && snipIdx < titleEnd + 500)
                {
                    int snipStart = html.IndexOf('>', snipIdx) + 1;
                    int snipEnd   = html.IndexOf("</td>", snipStart);
                    if (snipStart > 0 && snipEnd > snipStart)
                        snippet = StripHtmlTags(html.Substring(snipStart, snipEnd - snipStart)).Trim();
                }

                if (!string.IsNullOrEmpty(title))
                {
                    sb.AppendLine($"- {title}\n  {snippet}\n  {url}");
                    count++;
                }
                idx = titleEnd > 0 ? titleEnd : linkIdx + 1;
                if (sb.Length > 3000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string StripHtmlTags(string html)
        {
            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ExtractJsonValue(string block, string key)
        {
            int keyIdx = block.IndexOf("\"" + key + "\":");
            if (keyIdx < 0) return "";
            int valStart = keyIdx + key.Length + 3;
            while (valStart < block.Length && block[valStart] == ' ') valStart++;
            if (valStart >= block.Length || block[valStart] != '"')
                return ExtractJsonRawValue(block, valStart);
            valStart++;
            var sb = new StringBuilder();
            while (valStart < block.Length)
            {
                char c = block[valStart++];
                if (c == '"') break;
                if (c == '\\' && valStart < block.Length)
                {
                    char esc = block[valStart++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        default: sb.Append(esc); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string ExtractJsonRawValue(string block, int start)
        {
            int end = start;
            while (end < block.Length && block[end] != ',' && block[end] != '}' && block[end] != ']')
                end++;
            string val = block.Substring(start, end - start).Trim();
            return val == "null" ? "" : val;
        }

        // ── File system tools ──────────────────────────────────────────────────

        private static async Task<StorageFolder> ResolveFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ApplicationData.Current.LocalFolder;

            if (path.StartsWith("sd:", StringComparison.OrdinalIgnoreCase))
            {
                var sds = await KnownFolders.RemovableDevices.GetFoldersAsync();
                if (sds.Count == 0) throw new Exception("未找到 SD 卡");
                string sub = path.Substring(3).TrimStart('\\', '/');
                return string.IsNullOrEmpty(sub) ? sds[0] : await sds[0].GetFolderAsync(sub);
            }

            if (path.StartsWith("public:", StringComparison.OrdinalIgnoreCase))
            {
                StorageFolder pub;
                if (IsMobile)
                {
                    pub = KnownFolders.DocumentsLibrary;
                }
                else
                {
                    try { pub = await StorageFolder.GetFolderFromPathAsync(@"C:\Users\Public"); }
                    catch { pub = ApplicationData.Current.LocalFolder; }
                }
                string sub = path.Substring(7).TrimStart('\\', '/');
                return string.IsNullOrEmpty(sub) ? pub : await pub.GetFolderAsync(sub);
            }

            // 手机端不接受盘符路径
            if (IsMobile && path.Length >= 2 && path[1] == ':')
                throw new UnauthorizedAccessException(
                    "此设备为手机，不支持盘符路径 " + path + "。请使用 sd:、public: 前缀或通过 request_folder_access 授权。");

            // 先尝试 KnownFolders（不需要 broadFileSystemAccess）
            string normPath = path.Replace('/', '\\');
            StorageFolder known = await TryGetKnownFolder(normPath);
            if (known != null) return known;

            // 从 FutureAccessList 中查找已授权的文件夹（精确匹配或父路径匹配）
            var accessList = StorageApplicationPermissions.FutureAccessList;
            for (int i = 0; i < accessList.Entries.Count; i++)
            {
                try
                {
                    var entry = accessList.Entries[i];
                    var folder = await accessList.GetFolderAsync(entry.Token);
                    string folderNorm = folder.Path.Replace('/', '\\').TrimEnd('\\');
                    // 精确匹配：normPath 就是这个授权文件夹本身
                    if (string.Equals(normPath.TrimEnd('\\'), folderNorm, StringComparison.OrdinalIgnoreCase))
                        return folder;
                    // 子路径匹配：normPath 在授权文件夹之下
                    string folderPrefix = folderNorm + "\\";
                    if (normPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = normPath.Substring(folderPrefix.Length).TrimEnd('\\');
                        return string.IsNullOrEmpty(relative) ? folder : await folder.GetFolderAsync(relative);
                    }
                }
                catch { /* 条目已过期，跳过 */ }
            }

            // 没有匹配 — 提示请求授权
            throw new UnauthorizedAccessException(
                "路径 \"" + path + "\" 不在任何已授权文件夹范围内。请先调用 request_folder_access 让用户授权包含该路径的文件夹。");
        }

        // 尝试通过 KnownFolders 访问系统库（不需要 broadFileSystemAccess）
        private static async Task<StorageFolder> TryGetKnownFolder(string normPath)
        {
            // 枚举所有 KnownFolder 根路径
            var knownRoots = new List<StorageFolder>();
            var factories = new Func<StorageFolder>[]
            {
                () => KnownFolders.PicturesLibrary,
                () => KnownFolders.MusicLibrary,
                () => KnownFolders.VideosLibrary,
                () => KnownFolders.DocumentsLibrary,
                () => KnownFolders.CameraRoll,
                () => KnownFolders.SavedPictures,
            };
            foreach (var f in factories)
            {
                try { knownRoots.Add(f()); } catch { }
            }

            foreach (var kf in knownRoots)
            {
                if (kf == null) continue;
                try
                {
                    string kfNorm = kf.Path.Replace('/', '\\').TrimEnd('\\');
                    // 精确匹配
                    if (string.Equals(normPath.TrimEnd('\\'), kfNorm, StringComparison.OrdinalIgnoreCase))
                        return kf;
                    // 子路径
                    string kfPrefix = kfNorm + "\\";
                    if (normPath.StartsWith(kfPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = normPath.Substring(kfPrefix.Length).TrimEnd('\\');
                        if (string.IsNullOrEmpty(relative)) return kf;
                        // 逐级获取子文件夹
                        StorageFolder cur = kf;
                        foreach (var seg in relative.Split('\\'))
                        {
                            if (string.IsNullOrEmpty(seg)) continue;
                            cur = await cur.GetFolderAsync(seg);
                        }
                        return cur;
                    }
                }
                catch { /* 当前 KnownFolder 无法访问子路径，跳过 */ }
            }
            return null;
        }

        private static async Task<StorageFile> ResolveFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空");

            if (path.StartsWith("sd:", StringComparison.OrdinalIgnoreCase))
            {
                var sds = await KnownFolders.RemovableDevices.GetFoldersAsync();
                if (sds.Count == 0) throw new Exception("未找到 SD 卡");
                string filePath = path.Substring(3).TrimStart('\\', '/');
                int sep = filePath.LastIndexOf('\\');
                if (sep < 0) return await sds[0].CreateFileAsync(filePath, CreationCollisionOption.OpenIfExists);
                var folder = await sds[0].GetFolderAsync(filePath.Substring(0, sep));
                return await folder.CreateFileAsync(filePath.Substring(sep + 1), CreationCollisionOption.OpenIfExists);
            }

            if (path.StartsWith("public:", StringComparison.OrdinalIgnoreCase))
            {
                StorageFolder pub;
                if (IsMobile)
                {
                    pub = KnownFolders.DocumentsLibrary;
                }
                else
                {
                    try { pub = await StorageFolder.GetFolderFromPathAsync(@"C:\Users\Public"); }
                    catch { pub = ApplicationData.Current.LocalFolder; }
                }
                string filePath = path.Substring(7).TrimStart('\\', '/');
                int sep = filePath.LastIndexOf('\\');
                if (sep < 0) return await pub.CreateFileAsync(filePath, CreationCollisionOption.OpenIfExists);
                var folder = await pub.GetFolderAsync(filePath.Substring(0, sep));
                return await folder.CreateFileAsync(filePath.Substring(sep + 1), CreationCollisionOption.OpenIfExists);
            }

            // 手机端不接受盘符路径
            if (IsMobile && path.Length >= 2 && path[1] == ':')
                throw new UnauthorizedAccessException(
                    "此设备为手机，不支持盘符路径 " + path + "。请使用 sd:、public: 前缀或通过 request_folder_access 授权。");

            // 先尝试 KnownFolders（图片/音乐/视频等系统库，无需 broadFileSystemAccess）
            string normPath = path.Replace('/', '\\');
            StorageFolder knownForFile = await TryGetKnownFolder(System.IO.Path.GetDirectoryName(normPath) ?? normPath);
            if (knownForFile != null)
            {
                string fileName = System.IO.Path.GetFileName(normPath);
                if (!string.IsNullOrEmpty(fileName))
                    return await knownForFile.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            }

            // 从 FutureAccessList 中查找已授权的父文件夹（用于文件写入/读取）
            var accessList = StorageApplicationPermissions.FutureAccessList;
            for (int i = 0; i < accessList.Entries.Count; i++)
            {
                try
                {
                    var entry = accessList.Entries[i];
                    var folder = await accessList.GetFolderAsync(entry.Token);
                    string folderNorm = folder.Path.Replace('/', '\\').TrimEnd('\\');
                    string folderPrefix = folderNorm + "\\";
                    if (normPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = normPath.Substring(folderPrefix.Length);
                        int sep = relative.LastIndexOf('\\');
                        if (sep < 0)
                            return await folder.CreateFileAsync(relative, CreationCollisionOption.OpenIfExists);
                        var subFolder = await folder.GetFolderAsync(relative.Substring(0, sep));
                        return await subFolder.CreateFileAsync(relative.Substring(sep + 1), CreationCollisionOption.OpenIfExists);
                    }
                }
                catch { /* 条目已过期或文件夹已删除，跳过 */ }
            }

            // 没有匹配的已授权文件夹 — 直接失败并提示
            throw new UnauthorizedAccessException(
                "路径 \"" + path + "\" 不在任何已授权文件夹范围内。请先调用 request_folder_access 让用户授权包含该路径的文件夹。");
        }

        private static async Task<string> ExecuteListFiles(string argsJson)
        {
            string path = ExtractJsonString(argsJson, "path");
            try
            {
                StorageFolder folder = await ResolveFolder(path);
                var items = await folder.GetItemsAsync();
                var sb = new StringBuilder();
                sb.AppendLine("目录: " + folder.Path + "  (" + items.Count + " 项)");
                sb.AppendLine();
                foreach (var item in items.OrderBy(f => f.Name))
                {
                    if (item is StorageFolder d)
                        sb.AppendLine("  [目录] " + d.Name + "/");
                }
                foreach (var item in items.OrderBy(f => f.Name))
                {
                    if (item is StorageFile f)
                    {
                        var props = await f.GetBasicPropertiesAsync();
                        string size = props.Size > 1024 * 1024
                            ? string.Format("{0:F1}MB", props.Size / (1024.0 * 1024.0))
                            : props.Size > 1024
                                ? string.Format("{0:F1}KB", props.Size / 1024.0)
                                : props.Size + "B";
                        sb.AppendLine("  [文件] " + f.Name + "  (" + size + ")");
                    }
                }
                return sb.ToString();
            }
            catch (UnauthorizedAccessException uae)
            {
                return "权限不足，请调用 request_folder_access 请求用户授权该文件夹。\n详情: " + uae.Message;
            }
            catch (ArgumentException ae)
            {
                return "路径无法访问（" + ae.Message + "）。请调用 request_folder_access 让用户通过系统文件夹选择器授权该路径。";
            }
            catch (Exception ex)
            {
                return "目录列出失败: " + ex.Message + "。如果是权限问题，请调用 request_folder_access 请求授权。";
            }
        }

        private static async Task<string> ExecuteReadFile(string argsJson)
        {
            string path = ExtractJsonString(argsJson, "path");
            try
            {
                StorageFile file = await ResolveFile(path);
                string text = await FileIO.ReadTextAsync(file);
                if (text.Length > 8000)
                    text = text.Substring(0, 8000) + "\n\n[内容过长，已截断]";
                return text;
            }
            catch (UnauthorizedAccessException uae)
            {
                return "权限不足，请调用 request_folder_access 请求用户授权该文件夹。\n详情: " + uae.Message;
            }
            catch (ArgumentException ae)
            {
                return "路径无法访问（" + ae.Message + "）。请调用 request_folder_access 让用户通过系统文件夹选择器授权该路径。";
            }
            catch (Exception ex)
            {
                return "读取失败: " + ex.Message + "。如果是权限问题，请调用 request_folder_access 请求授权。";
            }
        }

        private static async Task<string> ExecuteWriteFile(string argsJson)
        {
            string path = ExtractJsonString(argsJson, "path");
            string content = ExtractJsonString(argsJson, "content");
            if (string.IsNullOrWhiteSpace(path))
                return "错误：文件路径不能为空";
            try
            {
                StorageFile file = await ResolveFile(path);
                await FileIO.WriteTextAsync(file, content ?? "");
                return "已写入 " + path + "（" + (content?.Length ?? 0) + " 字符）";
            }
            catch (UnauthorizedAccessException uae)
            {
                return "权限不足，请调用 request_folder_access 请求用户授权该文件夹。\n详情: " + uae.Message;
            }
            catch (ArgumentException ae)
            {
                // UWP 沙箱拒绝访问时抛 ArgumentException（Value does not fall within the expected range）
                return "路径无法访问（" + ae.Message + "）。请调用 request_folder_access 让用户通过系统文件夹选择器授权该路径。";
            }
            catch (Exception ex)
            {
                return "写入失败: " + ex.Message + "。如果是权限问题，请调用 request_folder_access 请求授权。";
            }
        }

        private static async Task<string> ExecuteRequestFolderAccess(string argsJson, FolderAccessCallback callback)
        {
            string path = ExtractJsonString(argsJson, "path");
            if (string.IsNullOrWhiteSpace(path))
                return "错误：路径不能为空";
            if (callback == null)
                return "此设备不支持文件夹授权";
            try
            {
                string grantedPath = await callback(path);
                if (string.IsNullOrEmpty(grantedPath))
                    return "用户拒绝了文件夹访问请求，请改用其他方式完成任务，或请求用户手动将文件保存到可访问的位置。";
                return "已授权访问文件夹: " + grantedPath + "。请重试之前的文件操作。";
            }
            catch (Exception ex)
            {
                return "授权过程出错: " + ex.Message;
            }
        }

        // ── Calendar tools ─────────────────────────────────────────────────

        private static async Task<string> ExecuteCalendarList(string argsJson)
        {
            string maxStr = ExtractJsonString(argsJson, "max_count");
            int maxCount = 10;
            if (!string.IsNullOrEmpty(maxStr)) int.TryParse(maxStr, out maxCount);
            if (maxCount < 1) maxCount = 1;
            if (maxCount > 50) maxCount = 50;

            try
            {
                var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite);
                if (store == null) return "日历服务不可用";

                var findRaw = await store.FindAppointmentsAsync(DateTimeOffset.Now, TimeSpan.FromDays(30));
                var find = (findRaw != null) ? findRaw.ToList() : new List<Appointment>();
                if (find.Count == 0) return "未来30天内没有日历事件。";

                var sb = new StringBuilder();
                sb.AppendLine("即将到来的日历事件：");
                int count = 0;
                foreach (var a in find.OrderBy(a => a.StartTime))
                {
                    if (count >= maxCount) break;
                    string loc = string.IsNullOrEmpty(a.Location) ? "" : " @ " + a.Location;
                    sb.AppendLine(string.Format("  [{0}] {1}  ({2:yyyy-MM-dd HH:mm}–{3:HH:mm}){4}",
                        count + 1, a.Subject ?? "(无标题)", a.StartTime, a.StartTime.Add(a.Duration), loc));
                    count++;
                }
                if (find.Count > maxCount)
                    sb.AppendLine(string.Format("...以及另外 {0} 个事件", find.Count - maxCount));
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "获取日历失败: " + ex.Message;
            }
        }

        private static async Task<string> ExecuteCalendarCreate(string argsJson)
        {
            string title = ExtractJsonString(argsJson, "title");
            string startStr = ExtractJsonString(argsJson, "start_time");
            string durStr = ExtractJsonString(argsJson, "duration_minutes");
            string location = ExtractJsonString(argsJson, "location");
            string details = ExtractJsonString(argsJson, "details");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(startStr))
                return "错误：标题和开始时间不能为空";

            int dur = 60;
            if (!string.IsNullOrEmpty(durStr)) int.TryParse(durStr, out dur);
            if (dur < 1) dur = 1;

            DateTimeOffset start;
            if (!DateTimeOffset.TryParse(startStr, out start))
                return "错误：时间格式无效，请使用 yyyy-MM-dd HH:mm 格式";

            try
            {
                var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite);
                if (store == null) return "日历服务不可用";

                var apt = new Appointment
                {
                    Subject = title ?? "",
                    Location = location ?? "",
                    Details = details ?? "",
                    StartTime = start,
                    Duration = TimeSpan.FromMinutes(dur),
                    AllDay = false,
                };

                await store.ShowAddAppointmentAsync(apt, new Windows.Foundation.Rect());
                return string.Format("已创建日历事件「{0}」于 {1:yyyy-MM-dd HH:mm}（持续 {2} 分钟）",
                    title, start, dur);
            }
            catch (Exception ex)
            {
                return "创建日历事件失败: " + ex.Message;
            }
        }

        // ── Contacts tools ─────────────────────────────────────────────────

        private static async Task<string> ExecuteContactsSearch(string argsJson)
        {
            string query = ExtractJsonString(argsJson, "query");
            if (string.IsNullOrWhiteSpace(query))
                return "错误：搜索关键词不能为空";

            try
            {
                var store = await ContactManager.RequestStoreAsync();
                if (store == null) return "联系人服务不可用";

                var allContacts = await store.FindContactsAsync();
                var filtered = allContacts != null
                    ? allContacts.Where(c => (c.DisplayName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || c.Phones.Any(p => (p.Number ?? "").Contains(query))
                        || c.Emails.Any(e => (e.Address ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList()
                    : null;
                if (filtered == null || filtered.Count == 0)
                    return "未找到匹配的联系人。";

                var sb = new StringBuilder();
                sb.AppendLine(string.Format("找到 {0} 个联系人：", filtered.Count));
                foreach (var c in filtered)
                {
                    string name = c.DisplayName ?? "(无姓名)";
                    string phones = string.Join(", ", c.Phones.Select(p => p.Number));
                    string emails = string.Join(", ", c.Emails.Select(e => e.Address));
                    sb.AppendLine("  " + name);
                    if (!string.IsNullOrEmpty(phones))
                        sb.AppendLine("    电话: " + phones);
                    if (!string.IsNullOrEmpty(emails))
                        sb.AppendLine("    邮箱: " + emails);
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "搜索联系人失败: " + ex.Message;
            }
        }

        // ── Phone tools ────────────────────────────────────────────────────

        private static async Task<string> ExecuteMakeCall(string argsJson)
        {
            string number = ExtractJsonString(argsJson, "phone_number");
            if (string.IsNullOrWhiteSpace(number))
                return "错误：电话号码不能为空";

            if (!IsMobile)
                return "拨号功能仅支持手机端。当前设备: " + AnalyticsInfo.VersionInfo.DeviceFamily;

            try
            {
                var uri = new Uri("tel:" + Uri.EscapeDataString(number));
                await LaunchUriOnUiAsync(uri);
                return "已打开拨号界面: " + number;
            }
            catch (Exception ex)
            {
                return "拨号失败: " + ex.Message;
            }
        }

        // ── SMS tools ──────────────────────────────────────────────────────

        private static async Task<string> ExecuteSendSms(string argsJson)
        {
            string number = ExtractJsonString(argsJson, "phone_number");
            string message = ExtractJsonString(argsJson, "message");
            if (string.IsNullOrWhiteSpace(number))
                return "错误：号码不能为空";

            if (!IsMobile)
                return "短信功能仅支持手机端。当前设备: " + AnalyticsInfo.VersionInfo.DeviceFamily;

            try
            {
                var uri = new Uri("sms:" + Uri.EscapeDataString(number) + "?body=" + Uri.EscapeDataString(message ?? ""));
                await LaunchUriOnUiAsync(uri);
                return "已打开短信界面: " + number;
            }
            catch (Exception ex)
            {
                return "打开短信界面失败: " + ex.Message;
            }
        }

        // ── Open app ──────────────────────────────────────────────────────

        private static async Task<string> ExecuteOpenApp(string argsJson)
        {
            string uriOrName = ExtractJsonString(argsJson, "uri_or_name");
            if (string.IsNullOrWhiteSpace(uriOrName))
                return "错误：请提供应用 URI 或名称";

            try
            {
                // 尝试作为 URI 直接启动
                if (uriOrName.Contains(":"))
                {
                    Uri uri;
                    if (Uri.TryCreate(uriOrName, UriKind.Absolute, out uri))
                    {
                        bool success = await LaunchUriOnUiAsync(uri);
                        return success ? "已启动: " + uriOrName
                            : "无法启动该 URI（应用可能未安装或协议不支持）: " + uriOrName;
                    }
                    return "无效的 URI 格式: " + uriOrName;
                }

                // 非 URI：尝试按包名查找并启动
                try
                {
                    var pkgManager = new Windows.Management.Deployment.PackageManager();
                    var pkgs = pkgManager.FindPackagesForUser("");
                    var match = pkgs.FirstOrDefault(p =>
                        (p.DisplayName ?? "").IndexOf(uriOrName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (p.Id?.Name ?? "").IndexOf(uriOrName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null)
                    {
                        var entries = await match.GetAppListEntriesAsync();
                        if (entries.Count > 0)
                        {
                            await entries[0].LaunchAsync();
                            return "已启动应用: " + (match.DisplayName ?? uriOrName);
                        }
                    }
                }
                catch { }

                // 最后尝试作为 URI 加上冒号再试
                string guessUri = uriOrName + ":";
                Uri guess;
                if (Uri.TryCreate(guessUri, UriKind.Absolute, out guess))
                {
                    bool success = await LaunchUriOnUiAsync(guess);
                    if (success) return "已启动: " + guessUri;
                }

                return "无法找到或启动应用: " + uriOrName + "。请确认应用名称或提供完整的 URI 协议（如 ms-settings:）。";
            }
            catch (Exception ex)
            {
                return "启动应用失败: " + ex.Message;
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            string needle = "\"" + key + "\"";
            int ki = json.IndexOf(needle);
            if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + needle.Length);
            if (ci < 0) return "";
            ci++;
            while (ci < json.Length && (json[ci] == ' ' || json[ci] == '\t')) ci++;
            if (ci >= json.Length || json[ci] != '"') return "";
            ci++;
            var sb = new System.Text.StringBuilder();
            while (ci < json.Length)
            {
                char c = json[ci++];
                if (c == '"') break;
                if (c == '\' && ci < json.Length)
                {
                    char esc = json[ci++];
                    switch (esc)
                    {
                        case '"':  sb.Append('"'); break;
                        case '\': sb.Append('\'); break;
                        case '/':  sb.Append('/'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (ci + 3 < json.Length)
                            {
                                string hex = json.Substring(ci, 4);
                                int code;
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                                    sb.Append((char)code);
                                ci += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
        // 按偏移量从 JSON 字符串中读取引号包裹的值
        private static string ExtractJsonString(string json, int start)
        {
            while (start < json.Length && json[start] != '"') start++;
            if (start >= json.Length) return "";
            start++;
            var sb = new StringBuilder();
            while (start < json.Length)
            {
                char c = json[start++];
                if (c == '"') break;
                if (c == '\\' && start < json.Length)
                {
                    char esc = json[start++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default:  sb.Append(esc);  break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string BuildToolRequestJson(string model, List<ApiMessageWithTools> messages, List<ToolDefinition> tools)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["stream"] = false,
                ["tools"] = tools,
                ["tool_choice"] = "auto"
            };

            using (var ms = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(typeof(Dictionary<string, object>));
                ser.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static string BuildToolRequestJsonManual(string model, List<ApiMessageWithTools> msgs, List<ToolDefinition> tools)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(EscapeJson(model)).Append("\",");
            sb.Append("\"messages\":[");
            sb.Append(string.Join(",", msgs.Select(m => SerializeMessage(m))));
            sb.Append("],");
            sb.Append("\"stream\":false,");
            sb.Append("\"tools\":[");
            sb.Append(string.Join(",", tools.Select(t => SerializeTool(t))));
            sb.Append("],");
            sb.Append("\"tool_choice\":\"auto\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string SerializeMessage(ApiMessageWithTools m)
        {
            var sb = new StringBuilder();
            sb.Append("{\"role\":\"").Append(EscapeJson(m.Role)).Append("\"");
            if (m.Content != null)
                sb.Append(",\"content\":\"").Append(EscapeJson(m.Content)).Append("\"");
            if (m.ToolCallId != null)
                sb.Append(",\"tool_call_id\":\"").Append(EscapeJson(m.ToolCallId)).Append("\"");
            if (m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                sb.Append(",\"tool_calls\":[");
                sb.Append(string.Join(",", m.ToolCalls.Select(tc => SerializeToolCall(tc))));
                sb.Append("]");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string SerializeToolCall(ToolCall tc)
        {
            return $"{{\"id\":\"{EscapeJson(tc.Id)}\",\"type\":\"function\",\"function\":{{\"name\":\"{EscapeJson(tc.Function.Name)}\",\"arguments\":\"{EscapeJson(tc.Function.Arguments)}\"}}}}";
        }

        private static string SerializeTool(ToolDefinition t)
        {
            var func = t.Function;
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"function\",\"function\":{");
            sb.Append("\"name\":\"").Append(EscapeJson(func.Name)).Append("\",");
            sb.Append("\"description\":\"").Append(EscapeJson(func.Description)).Append("\",");
            sb.Append("\"parameters\":{\"type\":\"object\",\"properties\":{");
            var props = func.Parameters.Properties;
            bool first = true;
            foreach (var kv in props)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(EscapeJson(kv.Key)).Append("\":{\"type\":\"").Append(EscapeJson(kv.Value.Type)).Append("\",\"description\":\"").Append(EscapeJson(kv.Value.Description)).Append("\"}");
            }
            sb.Append("},\"required\":[");
            sb.Append(string.Join(",", func.Parameters.Required.Select(r => $"\"{EscapeJson(r)}\"")));
            sb.Append("]}}");
            sb.Append("}");
            return sb.ToString();
        }

        // 差距三：裁剪旧 tool results，保留最近 6 条完整，之前的截断到 500 字符
        private static void TrimToolResults(List<ApiMessageWithTools> msgs)
        {
            const int keepFull = 6;
            const int maxOldLen = 500;
            var toolMsgs = msgs.Where(m => m.Role == "tool").ToList();
            int toTrim = toolMsgs.Count - keepFull;
            if (toTrim <= 0) return;
            int trimmed = 0;
            foreach (var m in msgs)
            {
                if (m.Role != "tool") continue;
                if (trimmed >= toTrim) break;
                if ((m.Content?.Length ?? 0) > maxOldLen)
                    m.Content = m.Content.Substring(0, maxOldLen) + "\n…[已截断以节省 context]";
                trimmed++;
            }
        }

        private static string SummarizeArgs(string toolName, string args)
        {
            string q = ExtractJsonString(args, "query");
            if (!string.IsNullOrEmpty(q)) return "\"" + q + "\"";
            string p = ExtractJsonString(args, "path") ?? ExtractJsonString(args, "uri_or_name");
            if (!string.IsNullOrEmpty(p)) return p;
            string u = ExtractJsonString(args, "url");
            if (!string.IsNullOrEmpty(u)) return u;
            string t = ExtractJsonString(args, "title");
            if (!string.IsNullOrEmpty(t)) return "\"" + t + "\"";
            if (args.Length > 80) return args.Substring(0, 80) + "...";
            return args;
        }

        private static string SummarizeToolResult(string toolName, string result)
        {
            if (string.IsNullOrEmpty(result)) return "空";
            if (result.StartsWith("错误")) return result.Substring(0, Math.Min(60, result.Length));
            if (result.StartsWith("权限不足")) return "权限不足";
            if (result.StartsWith("用户拒绝")) return "用户拒绝";
            if (result.StartsWith("已写入")) return result;
            if (result.StartsWith("已授权")) return "授权成功";
            if (result.StartsWith("已启动")) return result;
            // 搜索结果 / 读取结果：统计行数/字数
            int lineCount = result.Split('\n').Length;
            if (lineCount > 2) return lineCount + " 行 · " + result.Length + " 字符";
            if (result.Length > 60) return result.Substring(0, 60) + "...";
            return result;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        public static async Task<FunctionCallLoopResult>
            RunFunctionCallLoopAsync(ApiProfile profile, List<ApiMessageWithTools> initialMessages, Conversation conv,
                ToolPermissionCallback permissionCallback = null,
                FolderAccessCallback folderAccessCallback = null,
                ToolProgressCallback progressCallback = null)
        {
            var allMessages = new List<ApiMessageWithTools>(initialMessages);
            int maxTurns = 90;
            bool isClaude = string.Equals(profile.ProviderType, "claude", StringComparison.OrdinalIgnoreCase);

            for (int turn = 0; turn < maxTurns; turn++)
            {
                string requestJson = isClaude
                    ? BuildClaudeToolRequestJson(profile.Model, allMessages, GetTools())
                    : BuildToolRequestJsonManual(profile.Model, allMessages, GetTools());

                var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                if (isClaude)
                {
                    req.Headers.TryAddWithoutValidation("x-api-key", profile.ApiKey);
                    req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                }
                else
                {
                    req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
                }
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                string body;
                using (var resp = await _http.SendAsync(req))
                {
                    body = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        return new FunctionCallLoopResult
                        {
                            Content = $"HTTP {(int)resp.StatusCode}：{body}",
                            Reasoning = "",
                            AllMessages = allMessages
                        };
                    }
                }

                ToolChoiceMessage msg;
                if (isClaude)
                {
                    msg = ParseClaudeToolResponse(body);
                }
                else
                {
                    ToolApiResponse response;
                    try
                    {
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        {
                            var ser = new DataContractJsonSerializer(typeof(ToolApiResponse));
                            response = (ToolApiResponse)ser.ReadObject(ms);
                        }
                    }
                    catch
                    {
                        return new FunctionCallLoopResult
                        {
                            Content = body,
                            Reasoning = "",
                            AllMessages = allMessages
                        };
                    }

                    if (response?.Choices == null || response.Choices.Count == 0)
                        return new FunctionCallLoopResult
                        {
                            Content = "API 返回为空",
                            Reasoning = "",
                            AllMessages = allMessages
                        };

                    msg = response.Choices[0].Message;
                }

                var assistantMsg = new ApiMessageWithTools
                {
                    Role = "assistant",
                    Content = msg.Content ?? "",
                    ToolCalls = msg.ToolCalls,
                };
                allMessages.Add(assistantMsg);

                if (msg.ToolCalls == null || msg.ToolCalls.Count == 0)
                {
                    return new FunctionCallLoopResult
                    {
                        Content = msg.Content ?? "",
                        Reasoning = "",
                        AllMessages = allMessages
                    };
                }

                foreach (var toolCall in msg.ToolCalls)
                {
                    string tn = toolCall.Function?.Name ?? "";
                    string args = toolCall.Function?.Arguments ?? "{}";
                    progressCallback?.Invoke("start", tn, SummarizeArgs(tn, args));
                }

                // 差距一修复：并发执行同一 turn 的所有工具调用
                var turnLogEntries = new ConcurrentBag<ToolCallLogEntry>();
                var toolTasks = msg.ToolCalls.Select(async toolCall =>
                {
                    string tn   = toolCall.Function?.Name ?? "";
                    string args = toolCall.Function?.Arguments ?? "{}";
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var result = await ExecuteToolAsync(toolCall, conv, permissionCallback, folderAccessCallback);
                    sw.Stop();
                    string resultBrief = SummarizeToolResult(tn, result.Content ?? "");
                    bool isError = (result.Content ?? "").StartsWith("错误")
                        || (result.Content ?? "").StartsWith("权限不足")
                        || (result.Content ?? "").Contains("失败")
                        || (result.Content ?? "").Contains("用户拒绝");
                    progressCallback?.Invoke(isError ? "error" : "done", tn, resultBrief);
                    // 记录工具日志
                    turnLogEntries.Add(new ToolCallLogEntry
                    {
                        Phase     = isError ? "error" : "done",
                        ToolName  = tn,
                        ArgsJson  = args,
                        Result    = result.Content ?? "",
                        ElapsedMs = sw.ElapsedMilliseconds,
                    });
                    return result;
                }).ToList();

                var toolResults = await Task.WhenAll(toolTasks);
                foreach (var r in toolResults)
                    allMessages.Add(r);

                // 写入日志
                ApiLogger.LogToolCalls(profile.Model, new List<ToolCallLogEntry>(turnLogEntries));

                // 差距三修复：裁剪过长的 tool result，避免 context 爆炸
                TrimToolResults(allMessages);
            }

            return new FunctionCallLoopResult
            {
                Content = "已达到最大工具调用轮数，请简化你的回答",
                Reasoning = "",
                AllMessages = allMessages
            };
        }

        // ── Claude 专用 JSON 构建 ──────────────────────────────────────────

        private static string BuildClaudeToolRequestJson(string model, List<ApiMessageWithTools> msgs, List<ToolDefinition> tools)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append("\"max_tokens\":8192,");
            sb.Append("\"stream\":false,");

            var sysMsgs = msgs.Where(m => m.Role == "system").ToList();
            if (sysMsgs.Count > 0)
                sb.Append($"\"system\":\"{EscapeJson(string.Join("\n\n", sysMsgs.Select(m => m.Content)))}\",");

            sb.Append("\"tools\":[");
            sb.Append(string.Join(",", tools.Select(t => SerializeClaudeTool(t))));
            sb.Append("],");
            sb.Append("\"tool_choice\":{\"type\":\"auto\"},");

            var chatMsgs = msgs.Where(m => m.Role != "system").ToList();
            sb.Append("\"messages\":[");
            sb.Append(string.Join(",", chatMsgs.Select(m => SerializeClaudeMessage(m))));
            sb.Append("]}");
            return sb.ToString();
        }

        private static string SerializeClaudeTool(ToolDefinition t)
        {
            var func = t.Function;
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\":\"{EscapeJson(func.Name)}\",");
            sb.Append($"\"description\":\"{EscapeJson(func.Description)}\",");
            sb.Append("\"input_schema\":{\"type\":\"object\",\"properties\":{");
            bool first = true;
            foreach (var kv in func.Parameters.Properties)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{EscapeJson(kv.Key)}\":{{\"type\":\"{EscapeJson(kv.Value.Type)}\",\"description\":\"{EscapeJson(kv.Value.Description)}\"}}");
            }
            sb.Append("},\"required\":[");
            sb.Append(string.Join(",", func.Parameters.Required.Select(r => $"\"{EscapeJson(r)}\"")));
            sb.Append("]}}");
            return sb.ToString();
        }

        private static string SerializeClaudeMessage(ApiMessageWithTools m)
        {
            // tool result → user 角色包裹
            if (m.Role == "tool")
            {
                return $"{{\"role\":\"user\",\"content\":[{{\"type\":\"tool_result\",\"tool_use_id\":\"{EscapeJson(m.ToolCallId)}\",\"content\":\"{EscapeJson(m.Content)}\"}}]}}";
            }
            // assistant with tool_calls → content 数组含 tool_use 块
            if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append("{\"role\":\"assistant\",\"content\":[");
                if (!string.IsNullOrEmpty(m.Content))
                    sb.Append($"{{\"type\":\"text\",\"text\":\"{EscapeJson(m.Content)}\"}},");
                sb.Append(string.Join(",", m.ToolCalls.Select(tc =>
                    $"{{\"type\":\"tool_use\",\"id\":\"{EscapeJson(tc.Id)}\",\"name\":\"{EscapeJson(tc.Function.Name)}\",\"input\":{tc.Function.Arguments ?? "{}"}}}")));
                sb.Append("]}");
                return sb.ToString();
            }
            // normal message
            return $"{{\"role\":\"{EscapeJson(m.Role)}\",\"content\":\"{EscapeJson(m.Content ?? "")}\"}}";
        }

        private static ToolChoiceMessage ParseClaudeToolResponse(string body)
        {
            var msg = new ToolChoiceMessage { Role = "assistant", Content = "" };
            int contentIdx = body.IndexOf("\"content\":");
            if (contentIdx < 0) return msg;

            int arrStart = body.IndexOf('[', contentIdx);
            if (arrStart < 0) return msg;

            var toolCalls = new List<ToolCall>();
            var textParts = new StringBuilder();
            int depth = 0;
            int blockStart = -1;

            for (int i = arrStart; i < body.Length; i++)
            {
                if (body[i] == '{')
                {
                    if (depth == 0) blockStart = i;
                    depth++;
                }
                else if (body[i] == '}')
                {
                    depth--;
                    if (depth == 0 && blockStart >= 0)
                    {
                        string block = body.Substring(blockStart, i - blockStart + 1);
                        string type = ExtractJsonString(block, "type");
                        if (type == "text")
                        {
                            textParts.Append(ExtractJsonString(block, "text"));
                        }
                        else if (type == "tool_use")
                        {
                            string id   = ExtractJsonString(block, "id");
                            string name = ExtractJsonString(block, "name");
                            string inputJson = "{}";
                            int inputIdx = block.IndexOf("\"input\":");
                            if (inputIdx >= 0)
                            {
                                int inputStart = block.IndexOf('{', inputIdx + 8);
                                if (inputStart >= 0)
                                {
                                    int inputEnd = FindMatchingBrace(block, inputStart);
                                    if (inputEnd > inputStart)
                                        inputJson = block.Substring(inputStart, inputEnd - inputStart + 1);
                                }
                            }
                            toolCalls.Add(new ToolCall
                            {
                                Id = id,
                                Type = "function",
                                Function = new ToolCallFunction { Name = name, Arguments = inputJson }
                            });
                        }
                        blockStart = -1;
                    }
                }
                else if (body[i] == ']' && depth == 0) break;
            }

            msg.Content = textParts.ToString();
            if (toolCalls.Count > 0) msg.ToolCalls = toolCalls;
            return msg;
        }

        private static int FindMatchingBrace(string s, int start)
        {
            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
    }
}