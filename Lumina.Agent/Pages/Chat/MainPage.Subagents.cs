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
        // ── 子代理状态监控 ────────────────────────────────────────────────

        // 缓存徽章画刷——之前每个 2s tick 都 new 一个 SolidColorBrush
        private static readonly SolidColorBrush _subagentBadgeBrush = new SolidColorBrush(Windows.UI.Colors.DodgerBlue);

        private void SubagentTimer_Tick(object sender, object e)
        {
            int running = SubagentTracker.RunningCount;
            int total   = SubagentTracker.TotalCount;

            if (SubagentsBtn != null)
                SubagentsBtn.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (SubagentsBadge != null && SubagentsCount != null)
            {
                if (running > 0)
                {
                    SubagentsBadge.Visibility = Visibility.Visible;
                    string r = running.ToString();
                    if (SubagentsCount.Text != r) SubagentsCount.Text = r;
                    if (SubagentsBadge.Background != _subagentBadgeBrush)
                        SubagentsBadge.Background = _subagentBadgeBrush;
                }
                else
                {
                    SubagentsBadge.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void SubagentsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SubagentsList != null)
                SubagentsList.ItemsSource = SubagentTracker.Records;
        }

        private async void AskPanelClose_Click(object sender, RoutedEventArgs e)
        {
            AskPanelCancel_Click(sender, e);
        }

        private void AskPanelSubmit_Click(object sender, RoutedEventArgs e)
        {
            _askTcs?.TrySetResult("__submit__");
        }

        private void AskPanelCancel_Click(object sender, RoutedEventArgs e)
        {
            _askTcs?.TrySetResult("__cancel__");
        }

        private TaskCompletionSource<string> _askTcs;
        private List<AskQuestionViewModel> _askViewModels;
        private async Task<string> AskUserPanelCallback(string title, List<AskQuestion> questions)
        {
            _askTcs = new TaskCompletionSource<string>();
            _askViewModels = questions.Select(q => new AskQuestionViewModel(q)).ToList();

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (AskPanelTitle != null)
                    AskPanelTitle.Text = string.IsNullOrEmpty(title) ? "AI 向你提问" : title;
                if (AskQuestionsList != null)
                    AskQuestionsList.ItemsSource = _askViewModels;
                if (BottomAskPanel != null)
                {
                    BottomAskPanel.Visibility = Visibility.Visible;
                    AskPanelTransform.Y = BottomAskPanel.ActualHeight;
                }
            });

            string signal = await _askTcs.Task;

            // 关闭面板（直接隐藏，无需等待动画）
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (BottomAskPanel != null)
                    BottomAskPanel.Visibility = Visibility.Collapsed;
            });

            if (signal == "__cancel__") return null;

            var sb = new StringBuilder();
            foreach (var vm in _askViewModels)
            {
                string answer = vm.GetAnswer();
                if (string.IsNullOrEmpty(answer)) continue;
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(vm.Id + "=" + answer);
            }
            _askViewModels = null;
            _askTcs = null;
            return sb.ToString();
        }

        /// <summary>权限确认底部面板：标题+消息+确认/拒绝</summary>
        private async Task<bool> ShowPermissionPanel(string title, string message)
        {
            _askTcs = new TaskCompletionSource<string>();

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (AskPanelTitle != null)
                    AskPanelTitle.Text = string.IsNullOrEmpty(title) ? "权限请求" : title;
                if (AskQuestionsList != null)
                {
                    // 用单条文本问题模拟权限提示
                    _askViewModels = new List<AskQuestionViewModel>
                    {
                        new AskQuestionViewModel(new AskQuestion { Text = message, Type = "permission" })
                    };
                    AskQuestionsList.ItemsSource = _askViewModels;
                }
                if (AskPanelSubmitBtn != null)
                    AskPanelSubmitBtn.Content = "允许";
                if (AskPanelCancelBtn != null)
                    AskPanelCancelBtn.Content = "拒绝";
                if (BottomAskPanel != null)
                    BottomAskPanel.Visibility = Visibility.Visible;
            });

            string signal = await _askTcs.Task;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (BottomAskPanel != null)
                    BottomAskPanel.Visibility = Visibility.Collapsed;
                if (AskPanelSubmitBtn != null)
                    AskPanelSubmitBtn.Content = "提交";
                if (AskPanelCancelBtn != null)
                    AskPanelCancelBtn.Content = "跳过";
            });

            _askTcs = null;
            _askViewModels = null;
            return signal == "__submit__";
        }

        private async Task AnimatePanelIn()
        {
            if (AskPanelTransform == null || BottomAskPanel == null) return;
            await Task.Delay(50); // wait for layout
            double target = Math.Max(BottomAskPanel.ActualHeight, 300);
            if (target <= 0) target = 300;
            for (int i = 0; i < 5; i++)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    AskPanelTransform.Y = target * (5 - i) / 5);
                await Task.Delay(25);
            }
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                AskPanelTransform.Y = 0);
        }

        private async Task AnimatePanelOut()
        {
            if (AskPanelTransform == null || BottomAskPanel == null)
            {
                if (BottomAskPanel != null) BottomAskPanel.Visibility = Visibility.Collapsed;
                return;
            }
            double target = Math.Max(BottomAskPanel.ActualHeight, 300);
            if (target <= 0) target = 300;
            for (int i = 0; i < 4; i++)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    AskPanelTransform.Y = target * (i + 1) / 4);
                await Task.Delay(16);
            }
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                BottomAskPanel.Visibility = Visibility.Collapsed);
        }

        private async void ChatScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_loadingOlder) return;
            if (_displayStart <= 0 || _fullMessages == null) return;
            var sv = (sender as ScrollViewer) ?? _chatSv;
            if (sv == null) return;

            // 接近顶部（<300px）时加载更早消息
            if (sv.VerticalOffset < 300 && !e.IsIntermediate)
            {
                _loadingOlder = true;

                int newStart = Math.Max(0, _displayStart - _batchSize);

                // 从头部插入（index 0 = 最旧消息）。ItemsStackPanel 的
                // ItemsUpdatingScrollMode="KeepItemsInView" 会在头部插入时自动
                // 保持当前可见项位置，无需再手动补偿滚动偏移。
                for (int i = newStart; i < _displayStart; i++)
                    _bubbles.Insert(i - newStart, BuildBubble(_fullMessages[i], i));
                _displayStart = newStart;

                await Task.Delay(50);
                _loadingOlder = false;
            }
        }

        private async void SubagentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(SubagentsList.SelectedItem is SubagentRecord rec)) return;
            SubagentsList.SelectedIndex = -1;
            var detail = "任务：" + rec.Task + "\n\n" +
                         "状态：" + rec.Status + "\n" +
                         "耗时：" + rec.TimeLabel + "\n" +
                         "结果：\n" + (string.IsNullOrEmpty(rec.Result) ? "（执行中…）" : rec.Result);
            var dialog = new ContentDialog
            {
                Title = "子代理详情",
                Content = new ScrollViewer { Content = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 13, }, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, },
                PrimaryButtonText = "关闭",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            await dialog.ShowAsync();
        }
    }
}
