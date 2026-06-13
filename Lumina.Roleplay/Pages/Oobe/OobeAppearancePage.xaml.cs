using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeAppearancePage : Page
    {
        private bool _loading = true;

        public OobeAppearancePage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            AppSettings.ApplyTheme(RootGrid, this);

            DarkModeToggle.IsOn = AppSettings.IsDark;

            PopulateLangPicker();
            RefreshThemeLabel();

            var cnFonts = new[] { "ThemeDefault", "LXGW", "SourceHanSerif", "ZCOOL", "NotoSansSC", "MaShanZheng", "System" };
            int cnIdx = Array.IndexOf(cnFonts, AppSettings.ChineseFontFamily);
            ChineseFontPicker.SelectedIndex = cnIdx >= 0 ? cnIdx : 0;

            var enFonts = new[] { "ThemeDefault", "EBGaramond", "Cormorant", "Lora", "PlayfairDisplay", "System" };
            int enIdx = Array.IndexOf(enFonts, AppSettings.EnglishFontFamily);
            EnglishFontPicker.SelectedIndex = enIdx >= 0 ? enIdx : 0;

            ApplyLanguage();

            // Restore custom-accent state
            if (AppSettings.ThemeName == "Custom" && !string.IsNullOrEmpty(AppSettings.CustomAccentHex))
            {
                CustomAccentPanel.Visibility = Visibility.Visible;
                if (AccentSwatchList.Items.Count == 0) BuildAccentSwatches();
            }

            _loading = false;
        }

        private void ApplyLanguage()
        {
            PageTitle.Text = AppSettings.S("外观", "Appearance");
            PageStepHint.Text = AppSettings.S("步骤 1 / 8  ·  可跳过", "Step 1 / 8  ·  Optional");
            DarkModeLabel.Text = AppSettings.S("深色模式", "Dark Mode");
            DarkModeDesc.Text = AppSettings.S("切换深色/浅色界面", "Switch between dark and light theme");
            LangLabel.Text = AppSettings.S("语言 / Language", "Language");
            LangDesc.Text = AppSettings.S("切换界面语言", "Change UI language");
            ThemeLabel.Text = AppSettings.S("主题", "Theme");
            AccentLabel.Text = AppSettings.S("自定义主色", "Custom Accent");
            CnFontLabel.Text = AppSettings.S("中文字体", "Chinese Font");
            EnFontLabel.Text = AppSettings.S("英文字体", "English Font");
            NextBtn.Label = AppSettings.S("下一步", "Next");
            StepLabel.Text = AppSettings.S("1/8", "1/8");
        }

        private void DarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.IsDark = DarkModeToggle.IsOn;
            ApplyVisualTheme();
        }

        /// <summary>换主题/深浅/字体后统一刷新：换字典 + 强制重解析 ThemeResource + 立刻重排字体。</summary>
        private void ApplyVisualTheme()
        {
            App.ApplyTheme();
            AppSettings.ApplyThemeForced(RootGrid, this);
            App.ApplyTypography(RootGrid);
        }

        private void LangPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (LangPicker.SelectedItem is ComboBoxItem sel && sel.Tag is string code && code != AppSettings.Language)
            {
                AppSettings.Language = code;
                AppSettings.LoadTranslations();
                ApplyLanguage();
                // Defer to avoid modifying ComboBox Items during SelectionChanged
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

        private void ThemeCardInk_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
            => SwitchTheme("InkScroll", "墨韵书卷");

        private void ThemeCardCeladon_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
            => SwitchTheme("Celadon", "青瓷雅韵");

        private void ThemeCardMidnight_Tapped(object sender, TappedRoutedEventArgs e)
            => SwitchTheme("MidnightVermilion", "朱砂深夜");

        private void ThemeCardModern_Tapped(object sender, TappedRoutedEventArgs e)
            => SwitchTheme("ModernSlate", "现代·石板");

        private void ThemeCardCustom_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SwitchTheme("Custom", "自定义");
            CustomAccentPanel.Visibility = Visibility.Visible;
            if (AccentSwatchList.Items.Count == 0) BuildAccentSwatches();
        }

        private void BuildAccentSwatches()
        {
            string[] presets = { "#A33A2C", "#7DB6B0", "#D4504A", "#C9A86A",
                                 "#8B5A8C", "#5D7BBE", "#6B8E5A", "#B86E47" };
            foreach (var hex in presets)
            {
                var border = new Border
                {
                    Width = 36, Height = 36,
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(HexToColor(hex)),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1),
                    Tag = hex
                };
                border.Tapped += (s, args) =>
                {
                    var b = s as Border;
                    if (b == null) return;
                    string h = b.Tag as string;
                    ApplyAccent(h);
                    AccentHexBox.Text = h;
                };
                AccentSwatchList.Items.Add(border);
            }
        }

        private void ApplyAccentBtn_Click(object sender, RoutedEventArgs e)
        {
            string hex = (AccentHexBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(hex)) return;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            ApplyAccent(hex);
        }

        private void ApplyAccent(string hex)
        {
            AppSettings.CustomAccentHex = hex;
            ApplyVisualTheme();
        }

        private static Color HexToColor(string hex)
        {
            hex = (hex ?? "").TrimStart('#');
            if (hex.Length != 6) return Colors.Transparent;
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Color.FromArgb(255, r, g, b);
        }

        private void SwitchTheme(string name, string displayName)
        {
            AppSettings.ThemeName = name;

            if (name != "Custom")
            {
                CustomAccentPanel.Visibility = Visibility.Collapsed;
            }

            ApplyVisualTheme();
            RefreshThemeLabel();
        }

        private void RefreshThemeLabel()
        {
            string name = AppSettings.ThemeName;
            string display;
            if (name == "Celadon")           display = "青瓷雅韵";
            else if (name == "MidnightVermilion") display = "朱砂深夜";
            else if (name == "ModernSlate")       display = "现代·石板";
            else if (name == "Custom")            display = "自定义";
            else                              display = "墨韵书卷";
            ThemeSelectedLabel.Text = AppSettings.S("当前：" + display, "Current: " + display);
        }

        private void ChineseFontPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var vals = new[] { "ThemeDefault", "LXGW", "SourceHanSerif", "ZCOOL", "NotoSansSC", "MaShanZheng", "System" };
            if (ChineseFontPicker.SelectedIndex >= 0 && ChineseFontPicker.SelectedIndex < vals.Length)
            {
                AppSettings.ChineseFontFamily = vals[ChineseFontPicker.SelectedIndex];
                ApplyVisualTheme();
            }
        }

        private void EnglishFontPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var vals = new[] { "ThemeDefault", "EBGaramond", "Cormorant", "Lora", "PlayfairDisplay", "System" };
            if (EnglishFontPicker.SelectedIndex >= 0 && EnglishFontPicker.SelectedIndex < vals.Length)
            {
                AppSettings.EnglishFontFamily = vals[EnglishFontPicker.SelectedIndex];
                ApplyVisualTheme();
            }
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobeApiMainPage));

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
