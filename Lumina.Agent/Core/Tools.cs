using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
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
        [DataMember(Name = "reasoning_content")] public string ReasoningContent { get; set; }
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
public delegate void ToolProgressCallback(string phase, string toolName, string detail, string args = "", string result = "");
public delegate void ToolTextContentCallback(string intermediateText);
public delegate void ToolReasoningCallback(string reasoningToken);
public delegate System.Threading.Tasks.Task<string> AskUserCallback(string title, List<AskQuestion> questions);

public class AskQuestion
{
    public string Id { get; set; }
    public string Text { get; set; }
    public string Type { get; set; } = "text";
    public string Options { get; set; }
    public string Placeholder { get; set; }
    public string Default { get; set; }
}

    public class SearchResultItem
    {
        public string Title { get; set; }
        public string Snippet { get; set; }
        public string Url { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Function Calling 引擎
    // ══════════════════════════════════════════════════════════════════════

    public static partial class FunctionCallEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        private static readonly System.Threading.SemaphoreSlim _permissionLock = new System.Threading.SemaphoreSlim(1, 1);

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

        // 工具定义是静态不变的——构建一次后缓存，避免每个工具轮次都重新分配
        // ~20 个 ToolDefinition + 嵌套字典/列表（之前每轮 agent loop 重建并重新序列化）
        private static List<ToolDefinition> _toolsCache;

        // fetch_page HTML 清洗用的正则——缓存为静态实例，避免每次抓取都重新编译模式串
        private static readonly Regex _reScript  = new Regex(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase);
        private static readonly Regex _reStyle   = new Regex(@"<style[\s\S]*?</style>", RegexOptions.IgnoreCase);
        private static readonly Regex _reTags    = new Regex(@"<[^>]+>");
        private static readonly Regex _reSpaces  = new Regex(@"\s{2,}");

        public static List<ToolDefinition> GetTools()
        {
            return _toolsCache ?? (_toolsCache = BuildTools());
        }

        private static List<ToolDefinition> BuildTools()
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
                        Description = "列出目录中的文件和子目录。留空 path = 列出当前工作目录（推荐）；也可传 sd:（SD卡）或完整路径",
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
                        Description = "读取工作目录中某个文本文件的内容。路径由 App 在用户授权的工作目录内管理，你只需给出文件名。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["name"] = new ToolParameterProperty { Type = "string", Description = "文件名（含后缀，如 notes.txt），不要包含目录或盘符" },
                            },
                            Required = new List<string> { "name" }
                        }
                    }
                },
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "write_file",
                        Description = "在用户授权的工作目录中写入文本文件（不存在则创建，存在则覆盖）。路径完全由 App 管理，你只需提供文件名、后缀和内容——不要提供任何目录或盘符。若尚未设置工作目录，会自动请用户选择。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["name"] = new ToolParameterProperty { Type = "string", Description = "文件名（不含目录，如 notes 或 notes.md）" },
                                ["ext"] = new ToolParameterProperty { Type = "string", Description = "文件后缀，不含点（如 txt、md、json、cs）。若 name 已含后缀可留空" },
                                ["content"] = new ToolParameterProperty { Type = "string", Description = "要写入的内容" },
                            },
                            Required = new List<string> { "name", "content" }
                        }
                    }
                },

                // ── 工作目录 ────────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "set_working_directory",
                        Description = "请用户选择/更换 AI 的工作目录（通过系统文件夹选择器）。read_file、write_file、list_files 都在该目录内操作。当还没有工作目录、或需要切换目录时调用。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>(),
                            Required = new List<string>()
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

                // ── 读图 ─────────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "read_image",
                        Description = "读取本地图片文件（png/jpg/gif/webp）并将其内容发送给视觉模型分析。调用后模型即可「看到」该图片。需要视觉功能支持的 API。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["path"] = new ToolParameterProperty { Type = "string", Description = "图片文件的本地路径" },
                            },
                            Required = new List<string> { "path" }
                        }
                    }
                },

                // ── 媒体控制 ─────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "media_control",
                        Description = "控制系统媒体播放（播放/暂停/下一曲/上一曲/停止）或调节系统音量",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["action"] = new ToolParameterProperty { Type = "string", Description = "操作：play, pause, play_pause, next, previous, stop, volume_up, volume_down, mute, unmute" },
                                ["volume"] = new ToolParameterProperty { Type = "string", Description = "目标音量（0-100），仅当 action=set_volume 时使用" },
                            },
                            Required = new List<string> { "action" }
                        }
                    }
                },

                // ── 子代理 ───────────────────────────────────────────────────
                new ToolDefinition
                {
                    Function = new ToolFunction
                    {
                        Name = "spawn_subagent",
                        Description = "派生一个子代理执行独立任务。子代理拥有独立的对话上下文和工具访问权限，适合处理需要多步工具调用的子任务（如深度研究、文件批量处理等）。子代理完成后将结果摘要返回给你。",
                        Parameters = new ToolParameters
                        {
                            Properties = new Dictionary<string, ToolParameterProperty>
                            {
                                ["task"] = new ToolParameterProperty { Type = "string", Description = "子代理要完成的任务描述，要具体明确" },
                                ["context"] = new ToolParameterProperty { Type = "string", Description = "提供给子代理的背景信息（可选）" },
                            },
                            Required = new List<string> { "task" }
                        }
                    }
                },

                new ToolDefinition { Function = new ToolFunction { Name = "mcp_list", Description = "列出所有已启用的 MCP 服务器", Parameters = new ToolParameters { Properties = new Dictionary<string, ToolParameterProperty>(), Required = new List<string>() } } },
                new ToolDefinition { Function = new ToolFunction { Name = "mcp_call", Description = "调用 MCP 服务器的指定工具", Parameters = new ToolParameters { Properties = new Dictionary<string, ToolParameterProperty> { ["server"] = new ToolParameterProperty { Type = "string", Description = "服务器名称" }, ["tool"] = new ToolParameterProperty { Type = "string", Description = "工具名" }, ["args"] = new ToolParameterProperty { Type = "string", Description = "参数 JSON（可选）" } }, Required = new List<string> { "server", "tool" } } } },
                new ToolDefinition { Function = new ToolFunction { Name = "skill_list", Description = "列出所有已启用的 Skills", Parameters = new ToolParameters { Properties = new Dictionary<string, ToolParameterProperty>(), Required = new List<string>() } } },
                new ToolDefinition { Function = new ToolFunction { Name = "skill_use", Description = "激活 Skill 获取提示词模板", Parameters = new ToolParameters { Properties = new Dictionary<string, ToolParameterProperty> { ["name"] = new ToolParameterProperty { Type = "string", Description = "技能名或触发词" } }, Required = new List<string> { "name" } } } },
            };
        }

        public static async Task<ApiMessageWithTools> ExecuteToolAsync(
            ToolCall call, Conversation conv,
            ToolPermissionCallback permissionCallback = null,
            FolderAccessCallback folderAccessCallback = null,
            bool visionEnabled = false,
            CancellationToken ct = default(CancellationToken),
            int depth = 0)
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
                    await _permissionLock.WaitAsync();
                    try
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
                    finally { _permissionLock.Release(); }
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
                        result.Content = await ExecuteReadFile(argsJson, folderAccessCallback);
                        break;
                    case "write_file":
                        result.Content = await ExecuteWriteFile(argsJson, folderAccessCallback);
                        break;
                    case "set_working_directory":
                        result.Content = await ExecuteSetWorkingDirectory(argsJson, folderAccessCallback);
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
                    case "read_image":
                        result.Content = await ExecuteReadImage(argsJson, visionEnabled);
                        break;
                    case "media_control":
                        result.Content = await ExecuteMediaControl(argsJson);
                        break;
                    case "spawn_subagent":
                        result.Content = await ExecuteSpawnSubagent(argsJson, conv, permissionCallback, folderAccessCallback, visionEnabled, ct, depth);
                        break;
                    case "mcp_list":  result.Content = ExecuteMcpList();  break;
                    case "mcp_call":  result.Content = await ExecuteMcpCall(argsJson);  break;
                    case "skill_list": result.Content = ExecuteSkillList(); break;
                    case "skill_use": result.Content = ExecuteSkillUse(argsJson); break;
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
        private static async Task<string> ExecuteSpawnSubagent(
            string argsJson, Conversation conv,
            ToolPermissionCallback permissionCallback,
            FolderAccessCallback folderAccessCallback,
            bool visionEnabled,
            CancellationToken ct = default(CancellationToken),
            int depth = 0)
        {
            if (depth >= MaxSubagentDepth)
                return "错误：已达到子代理最大嵌套深度（" + MaxSubagentDepth + "），无法继续派生。请直接完成当前任务。";

            string task = ExtractJsonString(argsJson, "task");
            string context = ExtractJsonString(argsJson, "context");
            if (string.IsNullOrWhiteSpace(task))
                return "错误：task 不能为空";

            // 获取当前对话使用的 API profile
            var profile = DataManager.GetProfileForConversation(conv);
            if (profile == null)
                return "错误：无法获取 API 配置，子代理无法启动";

            // 构建子代理的独立消息上下文
            var subMessages = new List<ApiMessageWithTools>();

            var sysPrompt = new StringBuilder();
            sysPrompt.AppendLine("你是一个子代理（subagent），被主代理派生来执行一个特定任务。");
            sysPrompt.AppendLine("你拥有与主代理相同的工具访问权限。");
            sysPrompt.AppendLine("完成任务后，请用简洁的文字总结你的发现和结果。不要使用工具调用来回复最终结果。");
            sysPrompt.AppendLine();
            sysPrompt.AppendLine("【当前日期】" + DateTime.Now.ToString("yyyy年M月d日"));
            if (!string.IsNullOrEmpty(context))
            {
                sysPrompt.AppendLine();
                sysPrompt.AppendLine("【背景信息】");
                sysPrompt.AppendLine(context);
            }

            subMessages.Add(new ApiMessageWithTools { Role = "system", Content = sysPrompt.ToString() });
            subMessages.Add(new ApiMessageWithTools { Role = "user", Content = task });

            // 注册到子代理跟踪器（驱动徽章 / 列表 UI）。子代理在后台线程执行，
            // SubagentTracker 内部已把 Records 的增删与 Status/Result 写入 marshal 回 UI 线程，
            // 故此处可安全地从后台线程调用 Start/Complete/Fail。
            var rec = SubagentTracker.Start(task);
            try
            {
                var result = await RunFunctionCallLoopAsync(
                    profile, subMessages, conv,
                    permissionCallback, folderAccessCallback,
                    null, null, visionEnabled, null, ct, depth + 1);

                string output = result.Content ?? "";
                SubagentTracker.Complete(rec, output);
                if (output.Length > 4000)
                    output = output.Substring(0, 4000) + "\n…[子代理输出已截断]";
                return "【子代理完成】\n" + output;
            }
            catch (Exception ex)
            {
                SubagentTracker.Fail(rec, ex.Message);
                return "子代理执行失败: " + ex.Message;
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
                if (c == '\\' && ci < json.Length)
                {
                    char esc = json[ci++];
                    switch (esc)
                    {
                        case '"':  sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
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

            // 带图片的 user 消息 → content 数组格式
            if (!string.IsNullOrEmpty(m.ImageBase64) && m.Role == "user")
            {
                sb.Append(",\"content\":[");
                if (!string.IsNullOrEmpty(m.Content))
                    sb.Append("{\"type\":\"text\",\"text\":\"").Append(EscapeJson(m.Content)).Append("\"},");
                sb.Append("{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:")
                  .Append(m.ImageMimeType ?? "image/png")
                  .Append(";base64,")
                  .Append(m.ImageBase64)
                  .Append("\"}}]");
            }
            else if (m.Content != null)
            {
                sb.Append(",\"content\":\"").Append(EscapeJson(m.Content)).Append("\"");
            }

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

        // ── read_image 工具：读取图片文件并返回 base64（供视觉模型使用）─────
        private static async Task<string> ExecuteReadImage(string argsJson, bool visionEnabled)
        {
            if (!visionEnabled)
                return "当前 API 配置不支持视觉功能，无法读取图片。";
            string path = ExtractJsonString(argsJson, "path");
            if (string.IsNullOrWhiteSpace(path))
                return "错误：路径不能为空";
            // 只允许常见图片格式
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif" && ext != ".webp")
                return "错误：不支持的图片格式，仅支持 png/jpg/jpeg/gif/webp";
            try
            {
                StorageFile file = await ResolveFile(path);
                var props = await file.GetBasicPropertiesAsync();
                if (props.Size > 10 * 1024 * 1024)
                    return "错误：图片文件过大（" + (props.Size / 1024 / 1024) + "MB），最大支持 10MB";
                var buf = await FileIO.ReadBufferAsync(file);
                byte[] bytes = new byte[buf.Length];
                Windows.Storage.Streams.DataReader.FromBuffer(buf).ReadBytes(bytes);
                string mime = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg"
                            : ext == ".gif" ? "image/gif"
                            : ext == ".webp" ? "image/webp"
                            : "image/png";
                string b64 = Convert.ToBase64String(bytes);
                return "__IMAGE__:" + mime + ":" + b64;
            }
            catch (UnauthorizedAccessException uae)
            {
                return "权限不足，请调用 set_working_directory 请求授权。\n详情: " + uae.Message;
            }
            catch (Exception ex)
            {
                return "读取图片失败: " + ex.Message;
            }
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
            string p = ExtractJsonString(args, "path") ?? ExtractJsonString(args, "name") ?? ExtractJsonString(args, "uri_or_name");
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

        // 统一到 Lumina.Core/AI/ChatJson.EscapeJson（含同样的"无需转义直接返回原串"快速路径，
        // 故此最热 payload 构建路径无性能损失；并补上原版缺失的控制字符(<0x20)转义）
        private static string EscapeJson(string s) => ChatJson.EscapeJson(s);

        public static async Task<FunctionCallLoopResult>
            RunFunctionCallLoopAsync(ApiProfile profile, List<ApiMessageWithTools> initialMessages, Conversation conv,
                ToolPermissionCallback permissionCallback = null,
                FolderAccessCallback folderAccessCallback = null,
                ToolProgressCallback progressCallback = null,
                ToolTextContentCallback textContentCallback = null,
                bool visionEnabled = false,
                ToolReasoningCallback reasoningCallback = null,
                CancellationToken ct = default(CancellationToken),
                int depth = 0)
        {
            var allMessages = new List<ApiMessageWithTools>(initialMessages);
            int maxTurns = AppSettings.MaxToolTurns;
            bool isClaude = string.Equals(profile.ProviderType, "claude", StringComparison.OrdinalIgnoreCase);
            var reasoningParts = new StringBuilder();

            for (int turn = 0; turn < maxTurns; turn++)
            {
                // 用户点击「停止」后立即中断工具循环，避免继续调用 API / 执行工具 / 派生子代理
                if (ct.IsCancellationRequested)
                    return new FunctionCallLoopResult
                    {
                        Content = "",
                        Reasoning = reasoningParts.ToString(),
                        AllMessages = allMessages
                    };

                string requestJson = isClaude
                    ? BuildClaudeToolRequestJson(profile.Model, allMessages, GetTools())
                    : BuildToolRequestJsonManual(profile.Model, allMessages, GetTools());

                // 带指数退避重试的 HTTP 请求
                string body = null;
                const int maxRetries = 3;
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
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

                    using (var resp = await _http.SendAsync(req, ct))
                    {
                        body = await resp.Content.ReadAsStringAsync();

                        if (resp.IsSuccessStatusCode)
                            break;

                        int code = (int)resp.StatusCode;
                        bool retryable = code == 429 || code == 500 || code == 502 || code == 503;
                        if (!retryable || attempt == maxRetries)
                        {
                            // 截断原始错误正文，避免把超长/含密钥的 JSON 原样回放给用户
                            string errBody = body ?? "";
                            if (errBody.Length > 500) errBody = errBody.Substring(0, 500) + "…";
                            return new FunctionCallLoopResult
                            {
                                Content = $"HTTP {code}：{errBody}",
                                Reasoning = "",
                                AllMessages = allMessages
                            };
                        }
                        int delayMs = 1000 * (1 << attempt); // 1s, 2s, 4s
                        await Task.Delay(delayMs, ct);
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

                // 累积 reasoning_content（DeepSeek V4 等模型的思考过程）
                if (!string.IsNullOrEmpty(msg.ReasoningContent))
                {
                    reasoningParts.Append(msg.ReasoningContent);
                    // 实时推送到 UI（如果调用方提供了回调）
                    reasoningCallback?.Invoke(msg.ReasoningContent);
                }

                // 实时推送模型的中间文本内容（即使还有 tool_calls 要执行）
                if (!string.IsNullOrEmpty(msg.Content))
                    textContentCallback?.Invoke(msg.Content);

                if (msg.ToolCalls == null || msg.ToolCalls.Count == 0)
                {
                    return new FunctionCallLoopResult
                    {
                        Content = msg.Content ?? "",
                        Reasoning = reasoningParts.ToString(),
                        AllMessages = allMessages
                    };
                }

                // ── 并行执行所有 tool_calls ────────────────────────────────
                var toolCalls = msg.ToolCalls;
                var logEntries = new List<ToolCallLogEntry>();

                // Phase 1: 报告所有工具的 thinking/calling
                foreach (var tc in toolCalls)
                {
                    string tcName = tc.Function?.Name ?? "";
                    string tcArgs = tc.Function?.Arguments ?? "{}";
                    progressCallback?.Invoke("thinking", tcName, SummarizeArgs(tcName, tcArgs), tcArgs, "");
                    progressCallback?.Invoke("calling", tcName, SummarizeArgs(tcName, tcArgs), tcArgs, "");
                }

                // 并行执行所有工具
                var execTasks = new List<Task<ApiMessageWithTools>>();
                var stopwatches = new List<Stopwatch>();
                foreach (var tc in toolCalls)
                {
                    var swItem = Stopwatch.StartNew();
                    stopwatches.Add(swItem);
                    execTasks.Add(Task.Run(async () =>
                    {
                        var r = await ExecuteToolAsync(tc, conv, permissionCallback, folderAccessCallback, visionEnabled, ct, depth);
                        swItem.Stop();
                        return r;
                    }));
                }
                var results = await Task.WhenAll(execTasks);

                // Phase 3: 报告结果并添加到消息列表
                for (int ti = 0; ti < toolCalls.Count; ti++)
                {
                    var tc = toolCalls[ti];
                    var result = results[ti];
                    var sw = stopwatches[ti];
                    string tn = tc.Function?.Name ?? "";
                    string args = tc.Function?.Arguments ?? "{}";

                    string resultBrief = SummarizeToolResult(tn, result.Content ?? "");
                    bool isError = (result.Content ?? "").StartsWith("错误")
                        || (result.Content ?? "").StartsWith("权限不足")
                        || (result.Content ?? "").Contains("失败")
                        || (result.Content ?? "").Contains("用户拒绝");
                    progressCallback?.Invoke(isError ? "error" : "result", tn, resultBrief, args, result.Content ?? "");

                    logEntries.Add(new ToolCallLogEntry
                    {
                        Phase     = isError ? "error" : "done",
                        ToolName  = tn,
                        ArgsJson  = args,
                        Result    = result.Content ?? "",
                        ElapsedMs = sw.ElapsedMilliseconds,
                    });

                    // read_image 返回 __IMAGE__:mime:base64 时，把图片附到 tool result 之后
                    if ((result.Content ?? "").StartsWith("__IMAGE__:"))
                    {
                        var parts = result.Content.Split(':');
                        if (parts.Length >= 3)
                        {
                            string mime = parts[1];
                            string b64 = string.Join(":", parts, 2, parts.Length - 2);
                            result.Content = "[图片已加载，将在下一轮发送给模型]";
                            allMessages.Add(result);
                            allMessages.Add(new ApiMessageWithTools
                            {
                                Role          = "user",
                                Content       = "(已读取图片，请分析上图)",
                                ImageBase64   = b64,
                                ImageMimeType = mime,
                            });
                        }
                        else
                        {
                            allMessages.Add(result);
                        }
                    }
                    else
                    {
                        allMessages.Add(result);
                    }
                }

                ApiLogger.LogToolCalls(profile.Model, logEntries);

                // 裁剪过长的 tool result，避免 context 爆炸
                TrimToolResults(allMessages);
            }

            return new FunctionCallLoopResult
            {
                Content = "已达到最大工具调用轮次，请简化你的回答",
                Reasoning = reasoningParts.ToString(),
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
            bool inString = false;

            for (int i = arrStart; i < body.Length; i++)
            {
                char c = body[i];
                if (inString)
                {
                    if (c == '\\' && i + 1 < body.Length) { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{')
                {
                    if (depth == 0) blockStart = i;
                    depth++;
                }
                else if (c == '}')
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
                                int inputStart = FindMatchingBraceStart(block, inputIdx + 8);
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
                else if (c == ']' && depth == 0) break;
            }

            msg.Content = textParts.ToString();
            if (toolCalls.Count > 0) msg.ToolCalls = toolCalls;
            return msg;
        }

        private static int FindMatchingBraceStart(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
                if (s[i] == '{') return i;
            return -1;
        }

        private static int FindMatchingBrace(string s, int start)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        // ══════════════════════════════════════════════════════════════════════
        // MCP & Skills tools (added for AI exposure)
        // ══════════════════════════════════════════════════════════════════════

        private static async Task<string> RunTavilySearchAsync(string query, string apiKey)
        {
            try
            {
                string payload = "{\"api_key\":\"" + EscapeJson(apiKey) + "\",\"query\":\"" + EscapeJson(query) + "\",\"search_depth\":\"basic\",\"max_results\":5}";
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using (var c = new CancellationTokenSource(15000))
                using (var r = await _http.SendAsync(req, c.Token))
                {
                    if (!r.IsSuccessStatusCode) return null;
                    var b = await r.Content.ReadAsStringAsync();
                    var root = JsonObject.Parse(b);
                    var arr = root.GetNamedArray("results", new JsonArray());
                    if (arr.Count == 0) return null;
                    var sb = new StringBuilder(); sb.AppendLine("【Tavily】");
                    for (int j = 0; j < arr.Count; j++)
                    {
                        var o = arr[j].GetObject();
                        string ti = o.GetNamedString("title", ""), ct = o.GetNamedString("content", ""), u = o.GetNamedString("url", "");
                        if (ct.Length > 300) ct = ct.Substring(0, 300) + "…";
                        sb.AppendLine(ti + "\n" + ct + "\n" + u);
                        if (j < arr.Count - 1) sb.AppendLine();
                    }
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        private static string ExecuteMcpList()
        {
            var s = DataManager.Data?.McpServers?.Where(x => x.Enabled).ToList();
            if (s == null || s.Count == 0) return "无可用的 MCP 服务器。请在设置 → MCP 中添加并启用。";
            var sb = new StringBuilder(); sb.AppendLine("【MCP 服务器 · 共 " + s.Count + " 个】");
            foreach (var x in s) sb.AppendLine("▸ " + x.Name + " (" + x.TransportType + ") " + x.Endpoint + (string.IsNullOrEmpty(x.Description) ? "" : " - " + x.Description));
            sb.AppendLine(); sb.AppendLine("使用 mcp_call(server=\"名称\", tool=\"工具名\", args=\"{...}\") 调用。");
            return sb.ToString();
        }

        private static async Task<string> ExecuteMcpCall(string argsJson)
        {
            string sn = ExtractJsonString(argsJson, "server"), tn = ExtractJsonString(argsJson, "tool"), ta = ExtractJsonString(argsJson, "args") ?? "{}";
            var s = DataManager.Data?.McpServers?.FirstOrDefault(x => x.Enabled && x.Name.Equals(sn, StringComparison.OrdinalIgnoreCase));
            if (s == null) return "未找到 MCP 服务器: " + sn;
            if (s.TransportType != "http") return "仅支持 HTTP 传输模式（当前: " + s.TransportType + "）";
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, s.Endpoint.TrimEnd('/') + "/" + tn.TrimStart('/'));
                req.Content = new StringContent(ta, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(s.AuthToken)) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + s.AuthToken);
                using (var c = new CancellationTokenSource(15000))
                using (var r = await _http.SendAsync(req, c.Token))
                {
                    var b = await r.Content.ReadAsStringAsync();
                    return "【MCP " + sn + "/" + tn + "】" + (r.IsSuccessStatusCode ? "" : " HTTP" + (int)r.StatusCode) + "\n" + (b.Length > 3000 ? b.Substring(0, 3000) + "…" : b);
                }
            }
            catch (Exception ex) { return "MCP 调用异常: " + ex.Message; }
        }

        private static string ExecuteSkillList()
        {
            var s = DataManager.Data?.Skills?.Where(x => x.Enabled).ToList();
            if (s == null || s.Count == 0) return "无可用的 Skill。请在设置 → Skills 中添加并启用。";
            var sb = new StringBuilder(); sb.AppendLine("【Skills · 共 " + s.Count + " 个】");
            foreach (var x in s) sb.AppendLine("▸ " + x.Icon + " " + x.Name + (string.IsNullOrEmpty(x.Triggers) ? "" : " [触发: " + x.Triggers + "]") + (string.IsNullOrEmpty(x.Description) ? "" : " - " + x.Description));
            sb.AppendLine(); sb.AppendLine("使用 skill_use(name=\"名称\") 激活技能。");
            return sb.ToString();
        }

        private static string ExecuteSkillUse(string argsJson)
        {
            string n = ExtractJsonString(argsJson, "name");
            var s = DataManager.Data?.Skills?.FirstOrDefault(x => x.Enabled &&
                (x.Name.Equals(n, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(x.Triggers) && x.Triggers.Split(',').Any(tr => tr.Trim().Equals(n, StringComparison.OrdinalIgnoreCase)))));
            if (s == null) return "未找到 Skill: " + n + "。使用 skill_list 查看可用技能。";
            string tpl = s.PromptTemplate ?? "";
            if (string.IsNullOrWhiteSpace(tpl)) return "Skill「" + s.Name + "」的提示词模板为空。";
            return "【Skill: " + s.Icon + " " + s.Name + "】\n请按以下模板回复：\n" + tpl.Replace("{input}", "").Replace("{query}", "");
        }
    }
}
