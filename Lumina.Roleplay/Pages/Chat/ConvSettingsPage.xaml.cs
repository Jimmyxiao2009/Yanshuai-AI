using System.Linq;
using System;
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
            RagDebugToggle.IsOn = AppSettings.RagDebugEnabled;

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
            MemoryList.ItemsSource = null;
            MemoryList.ItemsSource = _conv.MemoryItems?.ToList()
                ?? new System.Collections.Generic.List<string>();
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

        private void RagDebugToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.RagDebugEnabled = RagDebugToggle.IsOn;
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
            _ = DataManager.SaveAsync();
        }

        private void MemInjSlider_ValueChanged(object sender,
            Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading || _conv == null) return;
            _conv.MemoryInjectInterval = (int)e.NewValue;
            UpdateSliderLabels();
            _ = DataManager.SaveAsync();
        }

        // ── Memory list ───────────────────────────────────────────────────────

        private void DeleteMemoryItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string item && _conv?.MemoryItems != null)
            {
                _conv.MemoryItems.Remove(item);
                RefreshMemoryList();
                _ = DataManager.SaveAsync();
            }
        }

        private void ClearMemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            _conv?.MemoryItems?.Clear();
            RefreshMemoryList();
            _ = DataManager.SaveAsync();
        }

        private async void ManualSummaryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_conv == null) return;

            ManualSummaryBtn.IsEnabled = false;
            MemoryDebugStatus.Text = "正在总结...";
            var result = await MemoryPipeline.SummarizeAndStoreAsync(_conv);
            RefreshMemoryList();

            if (string.IsNullOrEmpty(result.Error))
            {
                MemoryDebugStatus.Text =
                    $"已生成 {result.Items.Count} 条短时记忆；沉淀 {result.PoolItemCount} 条池级 RAG；用时 {result.ElapsedMilliseconds} ms。";
            }
            else
            {
                MemoryDebugStatus.Text =
                    $"总结失败：{result.Error}；用时 {result.ElapsedMilliseconds} ms。";
            }
            ManualSummaryBtn.IsEnabled = true;
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
            if (await dialog.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                DialoguePoolManager.RemoveConversationEverywhere(_conv);
                DataManager.Data.Conversations.Remove(_conv);
                AppState.ActiveConversation = null;
                await DataManager.SaveAsync();
                Frame.Navigate(typeof(MainPage));
            }
        }

        // ── Back ──────────────────────────────────────────────────────────────

        private async void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            await DataManager.SaveAsync();
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }
    }
}
