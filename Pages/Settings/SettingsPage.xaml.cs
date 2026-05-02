using System;
using Windows.UI.Xaml;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class SettingsPage : Page
    {
        private bool _loading = true;

        public SettingsPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            AppSettings.ApplyTheme(RootGrid, this);

            DarkModeToggle.IsOn     = AppSettings.IsDark;
            LangToggle.IsOn         = AppSettings.IsEnglish;
            EnterBehaviorPicker.SelectedIndex   = AppSettings.EnterBehavior;
            StartupBehaviorPicker.SelectedIndex = AppSettings.StartupBehavior;
            ReplySoundToggle.IsOn = AppSettings.ReplySoundEnabled;
            ReasoningExpandedToggle.IsOn = AppSettings.ReasoningExpandedByDefault;

            // RAG
            RagEnabledToggle.IsOn = AppSettings.RagEnabled;
            RagTopKSlider.Value = AppSettings.RagTopK;
            RagThreshSlider.Value = AppSettings.RagSimilarityThreshold;
            RagTopKLabel.Text = AppSettings.S($"{AppSettings.RagTopK} 条", $"{AppSettings.RagTopK} items");
            RagThreshLabel.Text = $"{AppSettings.RagSimilarityThreshold:F2}";
            UpdateRagStatus();

            int uaIdx = AppSettings.FetchUAPreset;
            FetchUAPicker.SelectedIndex = (uaIdx >= 0 && uaIdx <= 5) ? uaIdx : 1;
            FetchUACustomBox.Visibility = (uaIdx == 5) ? Visibility.Visible : Visibility.Collapsed;
            FetchUACustomBox.Text = AppSettings.FetchUACustom;
            UpdateUAPreview();

            RestoreSectionStates();

            _loading = false;
            ApplyLanguage();
        }

        // ── Section collapse/expand ──────────────────────────────────

        private void RestoreSectionStates()
        {
            ApplySectionState(DisplaySection,  DisplaySectionIcon,  AppSettings.GetBool("sec_display",  true));
            ApplySectionState(InputSection,    InputSectionIcon,    AppSettings.GetBool("sec_input",    true));
            ApplySectionState(StartupSection,  StartupSectionIcon,  AppSettings.GetBool("sec_startup",  true));
            ApplySectionState(SearchSection,   SearchSectionIcon,   AppSettings.GetBool("sec_search",   true));
            ApplySectionState(RagSection,      RagSectionIcon,      AppSettings.GetBool("sec_rag",      true));
            ApplySectionState(BackupSection,   BackupSectionIcon,   AppSettings.GetBool("sec_backup",   true));
            ApplySectionState(OobeSection,     OobeSectionIcon,     AppSettings.GetBool("sec_oobe",     true));
            ApplySectionState(AboutSection,    AboutSectionIcon,    AppSettings.GetBool("sec_about",    true));
        }

        private static void ApplySectionState(StackPanel content, FontIcon icon, bool expanded)
        {
            content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            icon.Glyph = expanded ? "\uE76C" : "\uE76E";
        }

        private static void ToggleSection(StackPanel content, FontIcon icon, string key)
        {
            bool nowExpanded = content.Visibility == Visibility.Collapsed;
            ApplySectionState(content, icon, nowExpanded);
            AppSettings.SetBool(key, nowExpanded);
        }

        private void DisplaySection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(DisplaySection, DisplaySectionIcon, "sec_display");

        private void InputSection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(InputSection, InputSectionIcon, "sec_input");

        private void StartupSection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(StartupSection, StartupSectionIcon, "sec_startup");

        private void SearchSection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(SearchSection, SearchSectionIcon, "sec_search");

        private void BackupSection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(BackupSection, BackupSectionIcon, "sec_backup");

        private void OobeSection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(OobeSection, OobeSectionIcon, "sec_oobe");

        private void AboutSection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(AboutSection, AboutSectionIcon, "sec_about");

        // ── Default appearance ───────────────────────────────────────

        private void DefaultAppearanceBtn_Tapped(object sender, TappedRoutedEventArgs e)
            => Frame.Navigate(typeof(DefaultAppearancePage));

        // ── Language ─────────────────────────────────────────────────

        private void ApplyLanguage()
        {
            PageTitle.Text           = AppSettings.S("设置",  "Settings");
            DisplaySectionTitle.Text = AppSettings.S("显示",  "Display");
            DarkModeLabel.Text       = AppSettings.S("深色模式", "Dark Mode");
            DarkModeDesc.Text        = AppSettings.S("切换深色/浅色主题", "Toggle dark/light theme");
            LangLabel.Text           = "Language / 语言";
            LangDesc.Text            = AppSettings.S("切换为英文界面", "Switch to Chinese UI");
            DefaultAppearanceLabel.Text = AppSettings.S("默认对话外观", "Default Chat Appearance");
            DefaultAppearanceDesc.Text  = AppSettings.S("自定义气泡颜色和背景", "Customize bubble colors and background");
            CreditsSectionTitle.Text = AppSettings.S("关于",  "About");
            OrigDevLabel.Text        = AppSettings.S("AI 对话助手 · 适配 Windows 10 Mobile", "AI chat assistant · Windows 10 Mobile");
            AboutBtn.Content         = AppSettings.S("关于言枢 AI", "About 言枢 AI");
        }

        private void DarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.IsDark = DarkModeToggle.IsOn;
            AppSettings.ApplyTheme(RootGrid, this);
        }

        private void LangToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.IsEnglish = LangToggle.IsOn;
            ApplyLanguage();
        }

        private void EnterBehaviorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.EnterBehavior = EnterBehaviorPicker.SelectedIndex;
        }

        private void StartupBehaviorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.StartupBehavior = StartupBehaviorPicker.SelectedIndex;
            // 重置启动flag，下次返回主界面时生效
            AppState.IsFirstLaunch = true;
            StartupRestartHint.Visibility = Visibility.Visible;
        }

        private void ReplySoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.ReplySoundEnabled = ReplySoundToggle.IsOn;
        }

        private void ReasoningExpandedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.ReasoningExpandedByDefault = ReasoningExpandedToggle.IsOn;
        }

        private void SearchProviderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void SearchResultDepthPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void FetchUAPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            int idx = FetchUAPicker.SelectedIndex;
            AppSettings.FetchUAPreset = idx;
            FetchUACustomBox.Visibility = (idx == 5) ? Visibility.Visible : Visibility.Collapsed;
            UpdateUAPreview();
        }

        private void FetchUACustomBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.FetchUACustom = FetchUACustomBox.Text.Trim();
            UpdateUAPreview();
        }

        private void UpdateUAPreview()
        {
            FetchUAPreview.Text = AppSettings.FetchUserAgent;
        }

        private async void ExportZip_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
            picker.SuggestedFileName = $"yanshuai_{System.DateTime.Now:yyyyMMdd_HHmm}";
            picker.FileTypeChoices.Add("ZIP备份", new System.Collections.Generic.List<string> { ".zip" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            bool ok = await ImportExportPage.ExportAllAsZip(file);
            BackupStatus.Text = ok ? "✓ 备份导出成功" : "⚠ 导出失败";
        }

        private async void ImportZip_Click(object sender, RoutedEventArgs e)
        {
            var files = await ImportExportPage.PickFiles(new[] { ".zip" });
            if (files == null || files.Count == 0) return;
            bool ok = await ImportExportPage.ImportFromZip(files[0]);
            BackupStatus.Text = ok ? "✓ 恢复成功，请重启应用" : "⚠ 恢复失败";
        }

        private void ReEnterOobeBtn_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.OobeCompleted = false;
            AppState.IsFirstLaunch = true;
            Frame.Navigate(typeof(OobePage1));
        }

        private void OpenWebsiteButton_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(InfoPage));

        private void RagSection_Tapped(object sender, TappedRoutedEventArgs e)
            => ToggleSection(RagSection, RagSectionIcon, "sec_rag");

        private void RagEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.RagEnabled = RagEnabledToggle.IsOn;
            UpdateRagStatus();
        }

        private void RagTopKSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.RagTopK = (int)RagTopKSlider.Value;
            RagTopKLabel.Text = AppSettings.S($"{RagTopKSlider.Value:F0} 条", $"{RagTopKSlider.Value:F0} items");
        }

        private void RagThreshSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.RagSimilarityThreshold = RagThreshSlider.Value;
            RagThreshLabel.Text = $"{RagThreshSlider.Value:F2}";
        }

        private async void RagReloadModelBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/bge.embmodel"));
                if (App.Embedder != null && App.Embedder.LoadModel(file.Path))
                    RagModelStatus.Text = "模型: 已加载";
                else
                    RagModelStatus.Text = "模型: 加载失败";
            }
            catch { RagModelStatus.Text = "模型: 加载失败"; }
        }

        private void UpdateRagStatus()
        {
            if (App.Embedder != null && AppSettings.RagEnabled)
            {
                RagModelStatus.Text = AppSettings.S("模型: 已就绪", "Model: Ready");
                RagModelDetail.Text = "bge-small-zh-v1.5 (512维)";
            }
            else if (AppSettings.RagEnabled)
            {
                RagModelStatus.Text = AppSettings.S("模型: 未加载", "Model: Not loaded");
            }
            else
            {
                RagModelStatus.Text = AppSettings.S("离线 RAG 已禁用", "Offline RAG disabled");
            }
        }

        private void SearchSettingsBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(SearchSettingsPage));

        private void ApiLogBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ApiLogPage));

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(MainPage));
        }
    }
}
