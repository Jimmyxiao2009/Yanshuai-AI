using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MainPage : Page
    {
        // ── Long memory ───────────────────────────────────────────────────────

        private string BuildMemoryBlock()
        {
            if (!_conv.MemoryEnabled) return null;
            var allMemories = new List<MemoryItem>();

            // 1. 全局共享记忆
            if (DataManager.Data.GlobalMemories != null)
                allMemories.AddRange(DataManager.Data.GlobalMemories);

            // 2. 项目共享记忆
            if (!string.IsNullOrEmpty(_conv.ProjectId))
            {
                var project = DataManager.Data.Projects?.Find(p => p.Id == _conv.ProjectId);
                if (project?.ProjectMemories != null)
                    allMemories.AddRange(project.ProjectMemories);
            }

            // 3. 旧格式兼容（Conversation.MemoryItems 列表）
            if (_conv.MemoryItems != null && _conv.MemoryItems.Count > 0)
            {
                foreach (var s in _conv.MemoryItems)
                    allMemories.Add(new MemoryItem { Text = s, Category = "general", Importance = 0.5 });
            }

            if (allMemories.Count == 0) return null;
            if (_conv.ExchangesSinceLastInject < _conv.MemoryInjectInterval) return null;
            _conv.ExchangesSinceLastInject = 0;

            allMemories.Sort((a, b) => b.Importance.CompareTo(a.Importance));
            int take = Math.Min(allMemories.Count, 20);

            var sb = new StringBuilder();
            sb.AppendLine("[共享长期记忆] 以下是跨对话保存的用户信息和偏好，请在回复中适当利用这些信息：");
            sb.AppendLine();
            for (int i = 0; i < take; i++)
            {
                var m = allMemories[i];
                string cat = m.Category == "fact" ? "事实" :
                             m.Category == "preference" ? "偏好" :
                             m.Category == "event" ? "事件" :
                             m.Category == "instruction" ? "指令" : "通用";
                sb.AppendLine($"- [{cat}] {m.Text}");
            }
            return sb.ToString().Trim();
        }

        private async Task RunMemorySummaryAsync()
        {
            var memApiId = _conv.MemoryApiProfileId;
            var memProfile = string.IsNullOrEmpty(memApiId)
                ? DataManager.GetProfileForConversation(_conv)
                : DataManager.Data.ApiProfiles.Find(p => p.Id == memApiId);
            if (memProfile == null) return;

            // Collect the messages to summarise
            int count = _conv.MemorySummaryInterval * 2; // exchanges × 2 messages
            var recent = _conv.Messages
                .Skip(Math.Max(0, _conv.Messages.Count - count))
                .Where(m => m.Role == "user" || m.Role == "assistant")
                .ToList();
            if (recent.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("以下是一段对话，请提取其中重要的记忆要点，以简洁的条目（每行一条，以「-」开头）输出，不要任何其他内容：");
            sb.AppendLine();
            foreach (var m in recent)
                sb.AppendLine($"{(m.Role == "user" ? "用户" : "AI")}：{m.Content}");

            var payload = new ApiRequest
            {
                Model = memProfile.Model,
                Stream = false,
                Messages = new List<ApiRequestMessage>
                {
                    new ApiRequestMessage { Role = "user", Content = sb.ToString() }
                }
            };

            string requestJson;
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ApiRequest)).WriteObject(ms, payload);
                requestJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, memProfile.Url);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {memProfile.ApiKey}");
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return;
                    var body = await resp.Content.ReadAsStringAsync();
                    ApiResponse parsed;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = (ApiResponse)_apiRespSer.ReadObject(ms);

                    var text = (parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null) ?? "";
                    var newItems = text.Split('\n')
                        .Select(l => l.TrimStart('-', ' ').Trim())
                        .Where(l => l.Length > 0)
                        .ToList();

                    if (_conv.MemoryItems == null) _conv.MemoryItems = new List<string>();
                    _conv.MemoryItems.AddRange(newItems);
                    await DataManager.SaveAsync();

                    // Show a subtle system notification in chat
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        _bubbles.Add(new ChatBubble
                        {
                            Role = "assistant",
                            Content = $"📝 已生成 {newItems.Count} 条新记忆",
                            BackgroundColor = new SolidColorBrush(Color.FromArgb(60, 80, 160, 80)),
                            ForegroundColor = _whiteBrush,
                            ReasoningBgColor = _reasoningBrush,
                        });
                        ScrollToBottom();
                    });
                }
            }
            catch { /* summarisation errors are silent */ }
        }

        // ── System prompt ─────────────────────────────────────────────────────

        private string BuildSystemPrompt(string userInput)
        {
            var sb = new StringBuilder();
            // 优先用当前对话绑定的API的独立破墙，fallback全局设置
            var profile = DataManager.GetProfileForConversation(_conv);
            bool jbEnabled = (profile != null && profile.JailbreakEnabled)
                             ? true
                             : AppSettings.JailbreakEnabled;
            string jbPrompt = (profile != null && profile.JailbreakEnabled && !string.IsNullOrEmpty(profile.JailbreakPrompt))
                              ? profile.JailbreakPrompt
                              : AppSettings.JailbreakPrompt;
            if (jbEnabled && !string.IsNullOrEmpty(jbPrompt))
            { sb.AppendLine(jbPrompt); sb.AppendLine(); }

            // 注入用户人设（活跃资料）
            var up = DataManager.GetActiveUserProfile();
            if (up != null && (!string.IsNullOrEmpty(up.Name) || !string.IsNullOrEmpty(up.Description)))
            {
                sb.AppendLine();
                sb.AppendLine("【用户信息】以下是与你对话的用户（非你自身）的设定：");
                if (!string.IsNullOrEmpty(up.Name)) sb.AppendLine("用户名：" + up.Name);
                if (!string.IsNullOrEmpty(up.Description)) sb.AppendLine("用户描述：" + up.Description);
            }

            // ── 新对话附加上下文：最近 M 个对话各取 N 条消息 ────────────
            if (_conv.Messages.Count <= 1)
            {
                int m = AppSettings.NewConvContextCount;
                int n = AppSettings.NewConvMessageCount;
                if (m > 0 && n > 0)
                {
                    var recentConvs = DataManager.Data.Conversations
                        .Where(c => c.Id != _conv.Id && c.Messages.Count > 0)
                        .OrderByDescending(c => c.UpdatedAt)
                        .Take(m)
                        .ToList();

                    if (recentConvs.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("【参考：最近对话上下文】以下是从你最近的对话中提取的消息片段（仅提供背景参考，本对话是全新开始的）：");
                        foreach (var rc in recentConvs)
                        {
                            sb.AppendLine();
                            string title = string.IsNullOrEmpty(rc.Title) ? "（无标题）" : rc.Title;
                            sb.AppendLine("▸ " + title + " · " + rc.UpdatedAt.ToString("MM-dd HH:mm"));
                            var msgs = rc.Messages.Skip(Math.Max(0, rc.Messages.Count - n)).ToList();
                            foreach (var m2 in msgs)
                            {
                                string role = m2.Role == "user" ? "用户" : "AI";
                                string preview = m2.Content ?? "";
                                if (preview.Length > 200)
                                    preview = preview.Substring(0, 200) + "…";
                                sb.AppendLine("  " + role + "：" + preview);
                            }
                        }
                    }
                }
            }

            if (_toolsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine($"【当前日期】{DateTime.Now:yyyy年M月d日}");
                sb.AppendLine("【重要：工具调用规则】你现在配备了以下工具，遇到对应场景时必须主动调用，不得依靠训练知识直接回答：");
                sb.AppendLine("- web_search：查询实时信息、新闻、天气、当前价格、最新事件等一切需要联网的内容");
                sb.AppendLine("- fetch_page：精读某个具体 URL 的完整正文（不能用于任何搜索引擎页面）");
                sb.AppendLine("- read_file / write_file：在「工作目录」中读写文件。路径由 App 管理，你只需提供文件名（write_file 还可给后缀 ext）和内容，不要提供任何目录或盘符");
                sb.AppendLine("- list_files：留空 path 即列出工作目录内容");
                sb.AppendLine("- set_working_directory：当还没有工作目录、或需要更换目录时调用，让用户通过系统选择器授权一个目录");
                sb.AppendLine("- calendar_list / calendar_create：查看或创建日历事件");
                sb.AppendLine("- contacts_search：搜索联系人");
                sb.AppendLine("- make_call / send_sms：拨打电话或发送短信（仅手机端）");
                sb.AppendLine("- open_app：打开系统应用或设置页面");
                sb.AppendLine("- media_control：控制媒体播放（play/pause/next/previous/stop）或打开音量设置");
                sb.AppendLine("- spawn_subagent：派生子代理执行独立子任务（深度研究、批量处理等），子代理有独立上下文和工具权限");

                // 告知 AI 当前工作目录（若已设置），否则提示先调用 set_working_directory
                try
                {
                    string workdir = AppSettings.WorkingDirPath;
                    if (!string.IsNullOrEmpty(workdir))
                        sb.AppendLine("【当前工作目录】" + workdir + "（read_file/write_file/list_files 均在此目录内，直接用文件名即可）");
                    else
                        sb.AppendLine("【工作目录】尚未设置。首次需要读写文件时，请先调用 set_working_directory 让用户选择一个目录。");
                }
                catch { }

                if (_fetchSearchEnabled)
                {
                    sb.AppendLine("【搜索策略】可先调用 web_search 获取摘要，再用 fetch_page 精读具体页面。严禁 fetch google.com、google.com.hk 或任何 Google 子域名，此类请求会被系统拦截。");
                }
                else
                {
                    sb.AppendLine("【搜索策略】需要实时信息时必须调用 web_search；fetch_page 仅用于读取 web_search 返回结果中的具体页面 URL，禁止用于抓取任何搜索引擎页面。");
                }

                sb.AppendLine();
                sb.AppendLine("## 推理与工具调用规则");
                sb.AppendLine();
                sb.AppendLine("你在思考和行动时遵循严格的循环，不允许跳步：");
                sb.AppendLine();
                sb.AppendLine("1. Thought：分析当前情况，说明为什么需要调用下一个工具，预期得到什么。");
                sb.AppendLine("2. Action：调用一个工具。每轮只调用一个。");
                sb.AppendLine("3. Observation：读取工具返回结果，在下一个 Thought 中处理——评估结果是否充分，还缺少什么。");
                sb.AppendLine("4. 重复，直到信息充分，再输出面向用户的最终回复。");
                sb.AppendLine();
                sb.AppendLine("约束：");
                sb.AppendLine("- 禁止无 Thought 直接调用工具。");
                sb.AppendLine("- 禁止连续调用多个工具而不分析中间结果。");
                sb.AppendLine("- 工具返回错误或空结果时，在 Thought 中分析原因并调整参数或换用其他工具，不要用相同参数重试。");
                sb.AppendLine("- 只有在确认信息充分后，才输出最终回复。");

                sb.AppendLine();
                sb.AppendLine("## 行为准则");
                sb.AppendLine();
                sb.AppendLine("- 回复语言跟随用户输入语言。");
                sb.AppendLine("- 上下文中已有搜索结果或文件内容时，优先使用这些信息，不重复搜索已知内容。");
                sb.AppendLine("- 写文件、执行命令等不可逆操作前，在 Thought 中说明操作内容和影响范围，通过权限确认回调等待用户确认后再执行（Full Trust 模式除外）。");
                sb.AppendLine("- 回答完整，不主动截断；如内容较长，完整输出后再询问用户是否需要展开说明。");
            }

            // ── 注入项目级指令 ────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(_conv.ProjectId))
            {
                var project = DataManager.Data.Projects?.FirstOrDefault(p => p.Id == _conv.ProjectId);
                if (project != null && !string.IsNullOrEmpty(project.SystemPrompt))
                {
                    sb.AppendLine();
                    sb.AppendLine("【项目指令】" + project.SystemPrompt);
                }
                // 注入项目知识库摘要（前 3000 字符）
                if (project != null && project.KnowledgeFiles != null && project.KnowledgeFiles.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("【项目知识库】");
                    int totalLen = 0;
                    foreach (var kf in project.KnowledgeFiles)
                    {
                        int sep = kf.IndexOf('\x01');
                        string fname = sep > 0 ? kf.Substring(0, sep) : "file";
                        string content = sep > 0 ? kf.Substring(sep + 1) : kf;
                        if (content.Length > 1000) content = content.Substring(0, 1000) + "…";
                        sb.AppendLine($"[{fname}] {content}");
                        totalLen += content.Length;
                        if (totalLen > 3000) { sb.AppendLine("…(更多文件已省略)"); break; }
                    }
                }
            }

            // ── 注入长期记忆（全局+项目） ──────────────────────────────────────
            string memoryContext = MemoryManager.BuildMemoryContext(_conv.ProjectId, userInput);
            if (!string.IsNullOrEmpty(memoryContext))
            {
                sb.AppendLine();
                sb.AppendLine(memoryContext);
            }

            // ── 注入上下文压缩摘要 ────────────────────────────────────────────
            string summaryBlock = ContextCompressor.BuildSummarySystemBlock(_conv);
            if (!string.IsNullOrEmpty(summaryBlock))
            {
                sb.AppendLine();
                sb.AppendLine(summaryBlock);
            }

            return sb.ToString().Trim();
        }

    }
}
