using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MainPage : Page
    {

        // ── Navigation ────────────────────────────────────────────────────────

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pageCts = new System.Threading.CancellationTokenSource();
            RegisterKeyboardNotifications();
            if (ShellPage.Current == null)
            {
                Frame.Navigate(typeof(ShellPage));
                return;
            }

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
                if (startup == 1) // 启动新建对话（延迟到第一条消息才写入）
                {
                    _conv = new Conversation
                    {
                        Title           = "新对话",
                        ApiProfileId    = DataManager.Data.SelectedApiProfileId    ?? "",
                        CharacterCardId = DataManager.Data.SelectedCharacterCardId ?? "",
                        UserProfileId   = DataManager.GetActiveUserProfile()?.Id ?? "",
                        MemoryEnabled   = true,
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
                _conv = DataManager.GetOrCreateDefaultConversation();
            }
            AppState.ActiveConversation = _conv;
            if (!_isPendingConv)
                DataManager.Data.LastActiveConversationId = _conv.Id;

            RebuildBrushCache();
            UpdateTopBar();
            ApplyConvAppearance();
            UpdateApiInfoLabel();

            // 强制设置工具栏图标前景色（防止深色模式下图标不可见）
            var toolbarFg = new SolidColorBrush(AppSettings.IsDark ? Colors.White : Colors.Black);
            if (FullscreenInputIcon != null) FullscreenInputIcon.Foreground = toolbarFg;

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

            if (messages.Count == 0)
            {
                MaybeShowFirstMessage();
                if (_bubbles.Count == 0 && WelcomePanel != null)
                    WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
                return;
            }

            if (WelcomePanel != null)
                WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // 显示加载遮罩
            const int chunkSize = 30;
            if (messages.Count > chunkSize && LoadingOverlay != null)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingProgress.Value = 0;
            }

            // 分批正向加载，每批后 yield 避免 UI 无响应
            for (int i = 0; i < messages.Count; i += chunkSize)
            {
                int end = Math.Min(i + chunkSize, messages.Count);
                for (int j = i; j < end; j++)
                    _bubbles.Add(BuildBubble(messages[j], j));

                if (LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
                {
                    LoadingProgress.Value = end * 100 / messages.Count;
                    LoadingText.Text = string.Format("加载中… {0}/{1}", end, messages.Count);
                }

                await Task.Delay(1);
            }

            // 全部加载完：滚到底部，隐藏遮罩
            ScrollToBottom();
            await Task.Delay(50); // 等 ScrollViewer 完成布局
            ScrollToBottom();
            if (LoadingOverlay != null)
                LoadingOverlay.Visibility = Visibility.Collapsed;

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
                    ReasoningContent = bt?.Reasoning ?? "",
                    IsStreaming      = true,
                    BackgroundColor  = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                    ReasoningBgColor = _reasoningBrush,
                };
                _bubbles.Add(recoverBubble);
                _isSending = true;
                SubmitButton.IsEnabled = true;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE711";
                ScrollToBottom();

                // 让恢复气泡持续跟踪 AppState 内容，直到任务完成
                _ = Task.Run(async () =>
                {
                    var pageToken = _pageCts?.Token ?? default;
                    while (AppState.IsRunning(convId) && !pageToken.IsCancellationRequested)
                    {
                        await Task.Delay(200);
                        var cur = AppState.GetTask(convId);
                        if (pageToken.IsCancellationRequested) break;
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (pageToken.IsCancellationRequested) return;
                            if (cur != null)
                            {
                                recoverBubble.Content          = cur.Content;
                                recoverBubble.ReasoningContent = cur.Reasoning;
                            }
                        });
                    }
                    if (pageToken.IsCancellationRequested) return;
                    // 任务完成，同步最终状态并恢复 UI
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        if (pageToken.IsCancellationRequested) return;
                        recoverBubble.IsStreaming = false;
                        _isSending = false;
                        SubmitButton.IsEnabled = true;
                        if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
                        // 对话内容已由后台任务写入数据库，重新加载最后一条 AI 消息的内容
                        var lastAi = _conv.Messages.LastOrDefault(m => m.Role == "assistant");
                        if (lastAi != null)
                        {
                            recoverBubble.Content          = lastAi.Content;
                            recoverBubble.ReasoningContent = lastAi.ReasoningContent ?? "";
                            recoverBubble.MessageId        = lastAi.Id;
                        }
                        ScrollToBottom();
                    });
                });
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _pageCts?.Cancel();
            _pageCts?.Dispose();
            _pageCts = null;
            UnregisterKeyboardNotifications();
            _memoryTimer?.Stop();
            // 不取消 _streamCts：后台任务继续运行，结果会写入数据库
            // Fire-and-forget：不 await，导航立即完成，保存在后台进行
            _ = DataManager.SaveAsync();

            // 离开时如有未总结的对话轮次，触发记忆总结和深层记忆提取
            if (_conv != null && _conv.ExchangesSinceLastSummary > 0 && _conv.MemoryEnabled)
            {
                _conv.ExchangesSinceLastSummary = 0;
                _ = RunMemorySummaryAsync();
                _ = RunDeepMemoryExtractionAsync();
            }
        }

        private void NavigateWithSave(Type page)
        {
            var shellFrame = ShellPage.Current?.ContentFrame;
            if (shellFrame == null) return;
            shellFrame.Navigate(page);
            _ = DataManager.SaveAsync();
        }

        // ── Conversation settings / appearance ────────────────────────────────

        private void ConvSettingsBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvSettingsPage));

        private void ConvAppearanceBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvAppearancePage));

        // ── 图片附加 ──────────────────────────────────────────────────────────

        private void RegisterKeyboardNotifications()
        {
            try
            {
                var inputPane = Windows.UI.ViewManagement.InputPane.GetForCurrentView();
                inputPane.Showing += InputPane_Showing;
                inputPane.Hiding += InputPane_Hiding;
            }
            catch { }
        }

        private void UnregisterKeyboardNotifications()
        {
            try
            {
                var inputPane = Windows.UI.ViewManagement.InputPane.GetForCurrentView();
                inputPane.Showing -= InputPane_Showing;
                inputPane.Hiding -= InputPane_Hiding;
            }
            catch { }
        }

        private void InputPane_Showing(Windows.UI.ViewManagement.InputPane sender, Windows.UI.ViewManagement.InputPaneVisibilityEventArgs args)
        {
            args.EnsuredFocusedElementInView = true;
            RootGrid.Margin = new Thickness(0, 0, 0, args.OccludedRect.Height);
        }

        private void InputPane_Hiding(Windows.UI.ViewManagement.InputPane sender, Windows.UI.ViewManagement.InputPaneVisibilityEventArgs args)
        {
            args.EnsuredFocusedElementInView = true;
            RootGrid.Margin = new Thickness(0);
        }
    }
}
