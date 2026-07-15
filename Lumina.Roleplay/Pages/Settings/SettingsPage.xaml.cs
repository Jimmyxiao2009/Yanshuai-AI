using System;
using Windows.UI.Xaml;
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
            PopulateLangPicker();
            EnterBehaviorPicker.SelectedIndex   = AppSettings.EnterBehavior;
            int sb = AppSettings.StartupBehavior;
            StartupBehaviorPicker.SelectedIndex = (sb >= 0 && sb < StartupBehaviorPicker.Items.Count) ? sb : 0;
            CharaIllustBgToggle.IsOn = AppSettings.UseCharaIllustrationAsBg;
            ReplySoundToggle.IsOn = AppSettings.ReplySoundEnabled;
            ReasoningExpandedToggle.IsOn = AppSettings.ReasoningExpandedByDefault;

            _loading = false;
            ApplyLanguage();
        }

        // ── Language ─────────────────────────────────────────────────

        private void ApplyLanguage()
        {
            PageTitle.Text             = AppSettings.S("设置",  "Settings");
            DisplaySectionTitle.Text   = AppSettings.S("显示",  "Display");
            DarkModeLabel.Text         = AppSettings.S("深色模式", "Dark Mode");
            DarkModeDesc.Text          = AppSettings.S("切换深色/浅色主题", "Toggle dark/light theme");
            LangLabel.Text             = "Language / 语言";
            LangDesc.Text              = AppSettings.S("切换界面语言", "Change UI language");
            ThemeFontLabel.Text        = AppSettings.S("主题与字体", "Theme & Fonts");
            ThemeFontDesc.Text         = AppSettings.S("切换配色风格、字体和主色调", "Switch color scheme, fonts and accent");
            ReplySoundLabel.Text       = AppSettings.S("回复声音", "Reply Sound");
            ReplySoundDesc.Text        = AppSettings.S("AI 回复完成时播放提示音", "Play a sound when AI finishes replying");
            ReasoningExpandedLabel.Text = AppSettings.S("默认展开思考过程", "Expand Reasoning by Default");
            ReasoningExpandedDesc.Text = AppSettings.S("AI 回复完成后自动展开思维链", "Auto-expand the AI's reasoning chain after reply");
            InputSectionTitle.Text     = AppSettings.S("输入行为", "Input Behavior");
            EnterBehaviorLabel.Text    = AppSettings.S("Enter 键行为", "Enter Key Behavior");
            StartupSectionTitle.Text   = AppSettings.S("启动行为", "Startup");
            StartupBehaviorLabel.Text  = AppSettings.S("启动应用时自动进入", "Auto-enter on launch");
            CharaSectionTitle.Text     = AppSettings.S("角色卡", "Character Card");
            CharaIllustBgLabel.Text    = AppSettings.S("使用角色卡立绘作为对话背景", "Character art as chat background");
            CharaIllustBgDesc.Text     = AppSettings.S("有立绘的角色卡会自动设为对话背景图", "Cards with art will auto-set as chat background");
            BackupSectionTitle.Text    = AppSettings.S("数据备份", "Data Backup");
            BackupDesc.Text            = AppSettings.S("将所有对话、角色卡、世界书、API配置打包导出或从备份恢复",
                "Export all conversations, characters, world books, and API configs as ZIP or restore from backup");
            ExportZipBtn.Content       = AppSettings.S("导出备份(.zip)", "Export (.zip)");
            ImportZipBtn.Content       = AppSettings.S("从备份恢复", "Restore from Backup");
            OobeSectionTitle.Text      = AppSettings.S("引导向导", "Setup Wizard");
            ReEnterOobeBtn.Content     = AppSettings.S("重新进入初始引导向导", "Re-enter Setup Wizard");
            DebugSectionTitle.Text     = AppSettings.S("调试", "Debug");
            ApiLogBtn.Content          = AppSettings.S("查看 API 日志", "View API Logs");
            CreditsSectionTitle.Text   = AppSettings.S("关于",  "About");
            OrigDevLabel.Text          = AppSettings.S("AI 对话助手 · 适配 Windows 10 Mobile", "AI chat assistant · Windows 10 Mobile");
            AboutBtn.Content           = AppSettings.S("关于言枢 AI", "About 言枢 AI");

            RebuildBehaviorPickers();
        }

        private void RebuildBehaviorPickers()
        {
            // Enter behavior picker
            int enterIdx = EnterBehaviorPicker.SelectedIndex;
            EnterBehaviorPicker.Items.Clear();
            EnterBehaviorPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("Enter 发送，Shift+Enter 换行", "Enter sends, Shift+Enter newline") });
            EnterBehaviorPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("Enter 换行，Ctrl+Enter 发送", "Enter newline, Ctrl+Enter sends") });
            if (enterIdx >= 0 && enterIdx < EnterBehaviorPicker.Items.Count)
                EnterBehaviorPicker.SelectedIndex = enterIdx;

            // Startup behavior picker
            int startupIdx = StartupBehaviorPicker.SelectedIndex;
            StartupBehaviorPicker.Items.Clear();
            StartupBehaviorPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("最近的对话", "Last Conversation") });
            StartupBehaviorPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("新对话", "New Conversation") });
            if (startupIdx >= 0 && startupIdx < StartupBehaviorPicker.Items.Count)
                StartupBehaviorPicker.SelectedIndex = startupIdx;
            else
            {
                StartupBehaviorPicker.SelectedIndex = 0;
                AppSettings.StartupBehavior = 0;
            }

            // Startup restart hint
            StartupRestartHint.Text = AppSettings.S("✓ 返回主界面后即生效", "✓ Takes effect after returning to main page");
        }

        private void DarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.IsDark = DarkModeToggle.IsOn;
            AppSettings.ApplyTheme(RootGrid, this);
        }

        private void LangPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (LangPicker.SelectedItem is ComboBoxItem sel && sel.Tag is string code && code != AppSettings.Language)
            {
                AppSettings.Language = code;
                AppSettings.LoadTranslations();
                ApplyLanguage();
                // Defer ComboBox modification to avoid modifying Items during SelectionChanged
                var ignore = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => PopulateLangPicker());
            }
        }

        private void PopulateLangPicker()
        {
            LangPicker.Items.Clear();
            string cur = AppSettings.Language;
            foreach (var code in AppSettings.AvailableLanguages)
            {
                string display = Translations.GetLangName(code, cur);
                var item = new ComboBoxItem { Content = display, Tag = code };
                LangPicker.Items.Add(item);
                if (code == cur)
                    LangPicker.SelectedItem = item;
            }
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

        private void CharaIllustBgToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.UseCharaIllustrationAsBg = CharaIllustBgToggle.IsOn;
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

        private async void ExportZip_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
            picker.SuggestedFileName = $"yanshuai_{System.DateTime.Now:yyyyMMdd_HHmm}";
            picker.FileTypeChoices.Add("ZIP备份", new System.Collections.Generic.List<string> { ".zip" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            bool ok = await ImportExportPage.ExportAllAsZip(file);
            BackupStatus.Text = ok ? AppSettings.S("✓ 备份导出成功", "✓ Backup exported") : AppSettings.S("⚠ 导出失败", "⚠ Export failed");
        }

        private async void ImportZip_Click(object sender, RoutedEventArgs e)
        {
            var files = await ImportExportPage.PickFiles(new[] { ".zip" });
            if (files == null || files.Count == 0) return;
            bool ok = await ImportExportPage.ImportFromZip(files[0]);
            BackupStatus.Text = ok ? AppSettings.S("✓ 恢复成功，请重启应用", "✓ Restored, please restart") : AppSettings.S("⚠ 恢复失败", "⚠ Restore failed");
        }

        private void ReEnterOobeBtn_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.OobeCompleted = false;
            AppState.IsFirstLaunch = true;
            Frame.Navigate(typeof(OobeWelcomePage));
        }

        private void OpenWebsiteButton_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(InfoPage));

        private void ApiLogBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ApiLogPage));

        // ━━━━━ 主题与字体子页面 ━━━━━

        private void ThemeSettingsBtn_Tapped(object sender, TappedRoutedEventArgs e)
            => Frame.Navigate(typeof(ThemeSettingsPage));

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(MainPage));
        }
    }
}
