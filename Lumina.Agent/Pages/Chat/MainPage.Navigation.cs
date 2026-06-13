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

        // ── Navigation ────────────────────────────────────────────────────────

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isPendingConv = false;
            if (DataManager.Data.Conversations == null ||
                (DataManager.Data.Conversations.Count == 0 && DataManager.Data.ApiProfiles.Count == 0))
            {
                await DataManager.LoadAsync();
            }
            AppSettings.ApplyTheme(RootGrid, this);
            SetSendButtonColor();

            // 启动行为：每次从外部导航到MainPage（不是返回）时触发
            if (AppState.IsFirstLaunch)
            {
                AppState.IsFirstLaunch = false;
                int startup = AppSettings.StartupBehavior;
                if (startup == 2) // 启动直接进对话列表
                {
                    AppState.ActiveConversation = DataManager.GetOrCreateDefaultConversation();
                    // Calling Frame.Navigate inside OnNavigatedTo corrupts the Frame
                    // journal — defer to the next dispatcher tick so the current
                    // navigation completes first.
                    var f = Frame;
                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        f.Navigate(typeof(ConversationsListPage));
                        if (f.BackStack.Count > 0)
                            f.BackStack.RemoveAt(f.BackStack.Count - 1);
                    });
                    return;
                }
                else if (startup == 1) // 启动新建对话（延迟到第一条消息才写入）
                {
                    _conv = new Conversation
                    {
                        Title        = "新对话",
                        ApiProfileId = DataManager.Data.SelectedApiProfileId ?? "",
                    };
                    _isPendingConv = true;
                }
                else // 最近对话（默认）
                {
                    _conv = DataManager.GetOrCreateDefaultConversation();
                }
            }
            else if (AppState.ActiveConversation != null)
            {
                // 从其他页面返回，恢复上次的对话
                _conv = AppState.ActiveConversation;
                // 如果这个对话还未写入列表（pending），保持挂起状态
                _isPendingConv = !DataManager.Data.Conversations.Contains(_conv);
            }
            else
            {
                // ActiveConversation == null：新建对话（从对话列表点「新建」进来）
                _conv = new Conversation
                {
                    Title        = "新对话",
                    ApiProfileId = DataManager.Data.SelectedApiProfileId ?? "",
                };
                _isPendingConv = true;
                // 不再重置工具权限 — 已授权的权限永久有效
                if (FunctionCallEngine.FullTrust)
                    FunctionCallEngine.FullTrust = false;
            }
            AppState.ActiveConversation = _conv;
            if (!_isPendingConv)
                DataManager.Data.LastActiveConversationId = _conv.Id;

            RebuildBrushCache();
            UpdateTitleLabel();
            UpdateTokenDisplay();
            LoadRecentConversations();
            ApplyConvAppearance();

            // 启动子代理状态轮询
            _subagentTimer?.Stop();
            _subagentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _subagentTimer.Tick += SubagentTimer_Tick;
            _subagentTimer.Start();
            SubagentTimer_Tick(null, null);

            // 注册上滚加载更早消息（绑定到 ListView 自身的内部 ScrollViewer）
            EnsureChatScrollViewer();

            // 恢复 Flyout toggle 状态
            if (ToolsToggle != null) ToolsToggle.IsChecked = _toolsEnabled;
            if (FullTrustToggle != null) FullTrustToggle.IsChecked = FunctionCallEngine.FullTrust;
            if (FetchToggle != null) FetchToggle.IsChecked = _fetchSearchEnabled;

            // 强制设置附件图标前景色（防止深色模式下图标不可见）
            var toolbarFg = new SolidColorBrush(AppSettings.IsDark ? Colors.White : Colors.Black);
            if (AttachImageIcon != null) AttachImageIcon.Foreground = toolbarFg;

            // 输入框占位符提示
            if (InputTextBox != null)
            {
                InputTextBox.PlaceholderText = AppSettings.EnterBehavior == 1
                    ? "说点什么… (Ctrl+Enter 发送)"
                    : "说点什么…";
            }

            var messages = _conv.Messages.ToList();
            bool sameConv = _loadedConvId == _conv.Id;
            _loadedConvId = _conv.Id;

            // ── 增量刷新：同一对话且气泡数量匹配，跳过重建 ──────────────────
            if (sameConv && _bubbles.Count == messages.Count && messages.Count > 0)
            {
                ScrollToBottom();
                // 检查后台任务（streaming 已结束但 UI 未更新的情况）
                if (!AppState.IsRunning(_conv.Id) && _isSending)
                {
                    _isSending = false;
                    SubmitButton.IsEnabled = true;
                    if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
                }
                goto checkBackgroundTask;
            }

            // ── 增量追加：同一对话但末尾有新消息 ────────────────────────────
            if (sameConv && _bubbles.Count < messages.Count && _bubbles.Count > 0)
            {
                int startIdx = _bubbles.Count;
                for (int i = startIdx; i < messages.Count; i++)
                    _bubbles.Add(BuildBubble(messages[i], i));

                if (WelcomePanel != null)
                    WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ScrollToBottom();
                goto checkBackgroundTask;
            }

            // ── 完整重建：对话切换或首次加载 ─────────────────────────────────
            _bubbles.Clear();
            _displayStart = 0;
            _displayEnd = 0;

            if (messages.Count == 0)
            {
                if (_bubbles.Count == 0 && WelcomePanel != null)
                    WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
                return;
            }

            if (WelcomePanel != null)
                WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // 懒加载：初始只加载最近 N 条，向上滚动时按需加载更早的消息
            const int initialLoad = 20;
            const int batchSize = 20;

            _displayEnd = messages.Count;
            _displayStart = Math.Max(0, messages.Count - initialLoad);
            _batchSize = batchSize;
            _fullMessages = messages;

            for (int i = _displayStart; i < _displayEnd; i++)
                _bubbles.Add(BuildBubble(messages[i], i));

            // 等 ListView 完成布局后滚到底部
            await Task.Delay(50);
            ScrollToBottom();
            await Task.Delay(30);
            ScrollToBottom();

            checkBackgroundTask:
            var convId = _conv?.Id;
            if (convId != null && AppState.IsRunning(convId))
            {
                var bt = AppState.GetTask(convId);
                // 用后台已累积的内容创建一个占位气泡，显示实时进度
                var recoverBubble = new ChatBubble
                {
                    Role             = "assistant",
                    Content          = bt?.Content   ?? "",
                    IsStreaming      = true,
                    BackgroundColor  = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                    ReasoningBgColor = _reasoningBrush,
                };
                if (!string.IsNullOrEmpty(bt?.Reasoning))
                    recoverBubble.AppendReasoningChunk(bt.Reasoning);
                _bubbles.Add(recoverBubble);
                _isSending = true;
                SubmitButton.IsEnabled = true;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE711";
                ScrollToBottom();

                // 让恢复气泡持续跟踪 AppState 内容，直到任务完成
                _ = Task.Run(async () =>
                {
                    while (AppState.IsRunning(convId))
                    {
                        await Task.Delay(200);
                        var cur = AppState.GetTask(convId);
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (cur != null)
                            {
                                recoverBubble.Content = cur.Content;
                                if (!string.IsNullOrEmpty(cur.Reasoning))
                                    recoverBubble.AppendReasoningChunk(cur.Reasoning);
                            }
                        });
                    }
                    // 任务完成，同步最终状态并恢复 UI
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        recoverBubble.IsStreaming = false;
                        _isSending = false;
                        SubmitButton.IsEnabled = true;
                        if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
                        // 对话内容已由后台任务写入数据库，重新加载最后一条 AI 消息的内容
                        var lastAi = _conv.Messages.LastOrDefault(m => m.Role == "assistant");
                        if (lastAi != null)
                        {
                            recoverBubble.Content = lastAi.Content;
                            if (!string.IsNullOrEmpty(lastAi.ReasoningContent))
                                recoverBubble.AppendReasoningChunk(lastAi.ReasoningContent);
                            recoverBubble.MessageId = lastAi.Id;
                        }
                        ScrollToBottom();
                    });
                });
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // 不取消 _streamCts：后台任务继续运行，结果会写入数据库
            // Fire-and-forget：不 await，导航立即完成，保存在后台进行
            _ = DataManager.SaveAsync();
        }

        // ── Hamburger ─────────────────────────────────────────────────────────

        private void HamburgerBtn_Click(object sender, RoutedEventArgs e)
            => NavPane.IsPaneOpen = !NavPane.IsPaneOpen;

        private void ClosePane() => NavPane.IsPaneOpen = false;

        private async void NavigateWithSave(Type pageType)
        {
            await DataManager.SaveAsync();
            Frame.Navigate(pageType);
        }

        private void Nav_Conversations_Click(object sender, RoutedEventArgs e)
        { ClosePane(); NavigateWithSave(typeof(ConversationsListPage)); }

        private void Nav_Api_Click(object sender, RoutedEventArgs e)
        { ClosePane(); Frame.Navigate(typeof(ApiProfilesPage)); }

        private void Nav_Settings_Click(object sender, RoutedEventArgs e)
        { ClosePane(); Frame.Navigate(typeof(SettingsPage)); }

        private void Nav_Projects_Click(object sender, RoutedEventArgs e)
        { ClosePane(); Frame.Navigate(typeof(ProjectsListPage)); }

        private async void Nav_McpSkills_Click(object sender, RoutedEventArgs e)
        { ClosePane(); await Task.Delay(100); Frame.Navigate(typeof(McpSkillsPage)); }

        // ── Recent convs + projects in hamburger pane ─────────────────────

        private const int MaxSidebarItems = 15;

        private void LoadRecentConversations()
        {
            if (RecentConvList == null || RecentProjectList == null) return;

            // 1) 收集所有非项目对话，按更新时间倒序
            var standaloneConvs = DataManager.Data.Conversations
                .Where(c => string.IsNullOrEmpty(c.ProjectId))
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ConvSidebarVm(c))
                .ToList();

            // 2) 收集所有项目
            var projects = (DataManager.Data.Projects ?? new List<Project>())
                .OrderByDescending(p => p.UpdatedAt)
                .Select(p => new ProjectSidebarVm(p))
                .ToList();

            // 3) 分配 15 个名额给两者（对话优先，剩余给项目）
            int convQuota   = Math.Min(standaloneConvs.Count, MaxSidebarItems);
            int projectQuota = Math.Min(projects.Count, MaxSidebarItems - convQuota);

            RecentConvList.ItemsSource    = standaloneConvs.Take(convQuota).ToList();
            RecentProjectList.ItemsSource = projects.Take(projectQuota).ToList();
        }

        private void RecentConv_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as ConvSidebarVm;
            if (vm == null) return;
            var conv = DataManager.Data.Conversations.FirstOrDefault(c => c.Id == vm.Id);
            if (conv == null) return;
            ClosePane();
            AppState.ActiveConversation = conv;
            _conv = conv;
            _loadedConvId = null;
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Frame.Navigate(typeof(MainPage));
                if (Frame.BackStack.Count > 0 && Frame.BackStack[Frame.BackStack.Count - 1].SourcePageType == typeof(MainPage))
                    Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
            });
        }

        private void RecentProject_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as ProjectSidebarVm;
            if (vm == null) return;
            ClosePane();
            Frame.Navigate(typeof(ProjectSettingsPage), vm.Id);
        }

        // ── Token display ─────────────────────────────────────────────────────

    }
}
