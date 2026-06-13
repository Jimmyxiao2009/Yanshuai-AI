using System.Linq;
using System;
using System.Collections.Generic;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class ConvSettingsPage : Page
    {
        private Conversation _conv;
        private bool _loading = true;

        public ConvSettingsPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            _conv = AppState.ActiveConversation;
            if (_conv == null) { Frame.GoBack(); return; }

            _loading = true;

            // API picker
            var profiles = DataManager.Data.ApiProfiles;
            ApiProfilePicker.ItemsSource = profiles;
            ApiProfilePicker.SelectedItem = profiles.Find(p => p.Id == _conv.ApiProfileId);

            // Title
            TitleBox.Text = _conv.Title ?? "";

            // Per-conversation memory settings
            MemEnabledToggle.IsOn = _conv.MemoryEnabled;

            var memProfiles = DataManager.Data.ApiProfiles;
            MemApiPicker.ItemsSource = memProfiles;
            MemApiPicker.SelectedItem = string.IsNullOrEmpty(_conv.MemoryApiProfileId)
                ? null
                : memProfiles.Find(p => p.Id == _conv.MemoryApiProfileId);

            MemSumSlider.Value = _conv.MemorySummaryInterval;
            MemInjSlider.Value = _conv.MemoryInjectInterval;
            ContextWindowSlider.Value = _conv.ContextWindow;
            UpdateSliderLabels();
            UpdateContextWindowLabel(_conv.ContextWindow);

            RefreshMemoryList();
            RefreshCompactInfo();
            _loading = false;
        }

        private void UpdateSliderLabels()
        {
            MemSumLabel.Text = $"每 {(int)MemSumSlider.Value} 轮总结一次";
            MemInjLabel.Text = $"每 {(int)MemInjSlider.Value} 轮注入一次记忆";
        }

        private void UpdateContextWindowLabel(double value)
        {
            int v = (int)value;
            ContextWindowLabel.Text = v == 0 ? "不限" : $"{v} 条";
        }

        private void ContextWindowSlider_ValueChanged(object sender,
            Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            UpdateContextWindowLabel(e.NewValue);
            if (_loading || _conv == null) return;
            _conv.ContextWindow = (int)e.NewValue;
            _ = DataManager.SaveAsync();
        }

        private void RefreshMemoryList()
        {
            if (_conv == null) { MemoryList.ItemsSource = null; return; }
            var items = new List<MemItemVm>();

            // 1. 全局共享记忆（全部，不限来源对话）
            if (DataManager.Data.GlobalMemories != null)
            {
                foreach (var m in DataManager.Data.GlobalMemories)
                    items.Add(new MemItemVm(m));
            }

            // 2. 项目共享记忆（如果对话属于某项目）
            if (!string.IsNullOrEmpty(_conv.ProjectId))
            {
                var project = DataManager.Data.Projects?.Find(p => p.Id == _conv.ProjectId);
                if (project?.ProjectMemories != null)
                {
                    foreach (var m in project.ProjectMemories)
                    {
                        var vm = new MemItemVm(m);
                        vm.Source = "project";
                        items.Add(vm);
                    }
                }
            }

            items.Sort((a, b) => b.Importance.CompareTo(a.Importance));
            MemoryList.ItemsSource = items;
            MemItemHint.Text = items.Count > 0
                ? $"共 {items.Count} 条记忆（跨对话共享，按重要性排序）"
                : "暂无记忆（AI 调用 save_memory 工具后自动添加）";
        }

        private void DeleteMemoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is MemItemVm vm)) return;
            // 从对应来源删除
            if (vm.Source == "global" && DataManager.Data.GlobalMemories != null)
                DataManager.Data.GlobalMemories.RemoveAll(m => m.Id == vm.Id);
            else if (vm.Source == "project" && !string.IsNullOrEmpty(_conv.ProjectId))
            {
                var project = DataManager.Data.Projects?.Find(p => p.Id == _conv.ProjectId);
                project?.ProjectMemories?.RemoveAll(m => m.Id == vm.Id);
            }
            RefreshMemoryList();
            _ = DataManager.SaveAsync();
        }

        private void ClearMemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_conv == null) return;
            var convId = _conv.Id;
            DataManager.Data.GlobalMemories?.RemoveAll(m => m.SourceConversationId == convId);
            if (!string.IsNullOrEmpty(_conv.ProjectId))
            {
                var project = DataManager.Data.Projects?.Find(p => p.Id == _conv.ProjectId);
                project?.ProjectMemories?.RemoveAll(m => m.SourceConversationId == convId);
            }
            RefreshMemoryList();
            _ = DataManager.SaveAsync();
        }

        /// <summary>MemoryItem 显示包装（带格式化标签）</summary>
        public class MemItemVm
        {
            public string Id { get; set; }
            public string Text { get; set; }
            public string CategoryLabel { get; set; }
            public string ImportanceLabel { get; set; }
            public string TimeLabel { get; set; }
            public DateTime Timestamp { get; set; }
            public double Importance { get; set; }
            public string Source { get; set; }

            public MemItemVm(MemoryItem m)
            {
                Id = m.Id;
                Text = m.Text;
                Timestamp = m.Timestamp;
                Importance = m.Importance;
                CategoryLabel = GetCategoryLabel(m.Category);
                ImportanceLabel = m.Importance >= 0.8 ? "★ 重要" : m.Importance >= 0.5 ? "一般" : "次要";
                TimeLabel = FormatTime(m.Timestamp);
                Source = "global";
            }

            private static string GetCategoryLabel(string cat)
            {
                switch (cat)
                {
                    case "fact":        return "事实";
                    case "preference":  return "偏好";
                    case "event":       return "事件";
                    case "instruction": return "指令";
                    default:            return "通用";
                }
            }

            private static string FormatTime(DateTime dt)
            {
                if (dt == DateTime.MinValue) return "";
                var now = DateTime.Now;
                if (dt.Date == now.Date) return dt.ToString("HH:mm");
                if ((now - dt).TotalDays < 7) return dt.ToString("ddd HH:mm");
                return dt.ToString("MM-dd");
            }
        }

        // ── Conv pickers ──────────────────────────────────────────────────────

        private void ApiProfilePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _conv == null) return;
            _conv.ApiProfileId = (ApiProfilePicker.SelectedItem as ApiProfile)?.Id ?? "";
            _ = DataManager.SaveAsync();
        }

        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            var text = TitleBox.Text.Trim();
            if (!string.IsNullOrEmpty(text) && _conv != null)
            {
                _conv.Title = text;
                _ = DataManager.SaveAsync();
            }
        }

        // ── Per-conv memory settings ──────────────────────────────────────────

        private void MemEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading || _conv == null) return;
            _conv.MemoryEnabled = MemEnabledToggle.IsOn;
            _ = DataManager.SaveAsync();
        }

        private void MemApiPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _conv == null) return;
            _conv.MemoryApiProfileId = (MemApiPicker.SelectedItem as ApiProfile)?.Id ?? "";
            _ = DataManager.SaveAsync();
        }

        private void MemSumSlider_ValueChanged(object sender,
            Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading || _conv == null) return;
            _conv.MemorySummaryInterval = (int)e.NewValue;
            UpdateSliderLabels();
        }

        private void MemInjSlider_ValueChanged(object sender,
            Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading || _conv == null) return;
            _conv.MemoryInjectInterval = (int)e.NewValue;
            UpdateSliderLabels();
        }

        // ── Delete conversation ───────────────────────────────────────────────

        private async void DeleteConvBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "删除对话",
                Content = $"确定删除「{_conv?.Title}」？此操作不可撤销。",
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                DataManager.Data.Conversations.Remove(_conv);
                AppState.ActiveConversation = null;
                await DataManager.SaveAsync();
                Frame.Navigate(typeof(ConversationsListPage));
            }
        }

        // ── Back ──────────────────────────────────────────────────────────────

        private async void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            await DataManager.SaveAsync();
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }

        // ── Auto-Compact ───────────────────────────────────────────────────────

        private void RefreshCompactInfo()
        {
            if (_conv == null) return;
            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null)
            {
                CompactUsageLabel.Text = "当前使用率：无法获取 API 配置";
                return;
            }
            var usage = ContextCompressor.GetContextUsage(_conv, profile);
            int compactedCount = _conv.SummarizedUpTo;
            CompactUsageLabel.Text = string.Format("当前使用率：{0} / {1}（{2}%）",
                usage.UsedLabel, usage.LimitLabel, usage.Percent);
            CompactSummaryLabel.Text = compactedCount > 0
                ? $"已压缩 {compactedCount} 条消息，摘要 {ContextCompressor.FormatTokenCount(ContextCompressor.EstimateTokens(_conv.ContextSummary))}"
                : "尚未压缩";
            CompactProgressBar.Value = usage.Percent;
            if (_conv.LastCompactAtTicks > 0)
                CompactHint.Text = "上次压缩：" + new DateTime(_conv.LastCompactAtTicks, DateTimeKind.Local).ToString("MM-dd HH:mm");
            else
                CompactHint.Text = "";
        }

        private async void ForceCompactBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_conv == null) return;
            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null) return;

            CompactUsageLabel.Text = "⏳ 正在压缩…";
            var result = await ContextCompressor.ForceCompactAsync(_conv, profile,
                onProgress: msg =>
                {
                    var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                        () => CompactUsageLabel.Text = msg);
                });
            if (result.Compacted)
                CompactUsageLabel.Text = string.Format("✓ 已压缩 {0} 条（{1}% → {2}%）",
                    result.CompactedCount, result.Percent >= 100 ? 100 : 
                    (int)(result.Used * 100.0 / Math.Max(1, result.Limit)),
                    result.Percent);
            else
                CompactUsageLabel.Text = result.Note;

            await DataManager.SaveAsync();
            RefreshCompactInfo();
        }
    }
}
