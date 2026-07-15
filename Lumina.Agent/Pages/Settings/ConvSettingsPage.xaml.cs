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

            // Context window (per-conversation)
            ContextWindowSlider.Value = _conv.ContextWindow;
            UpdateContextWindowLabel(_conv.ContextWindow);

            _loading = false;
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
    }
}
