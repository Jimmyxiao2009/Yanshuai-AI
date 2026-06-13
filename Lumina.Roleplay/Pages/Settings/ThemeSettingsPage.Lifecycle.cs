using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;


namespace yanshuai
{
    public sealed partial class ThemeSettingsPage : Page
    {

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();

            // 主题预设
            ThemeSelectedLabel.Text = AppSettings.S("当前：", "Current: ") + ThemeDisplayName(AppSettings.ThemeName);

            // 字体下拉
            var cnFonts = new[] { "ThemeDefault", "LXGW", "SourceHanSerif", "ZCOOL", "NotoSansSC", "MaShanZheng", "System" };
            ChineseFontPicker.SelectedIndex = Array.IndexOf(cnFonts, AppSettings.ChineseFontFamily);
            if (ChineseFontPicker.SelectedIndex < 0) ChineseFontPicker.SelectedIndex = 0;

            var enFonts = new[] { "ThemeDefault", "EBGaramond", "Cormorant", "Lora", "PlayfairDisplay", "System" };
            EnglishFontPicker.SelectedIndex = Array.IndexOf(enFonts, AppSettings.EnglishFontFamily);
            if (EnglishFontPicker.SelectedIndex < 0) EnglishFontPicker.SelectedIndex = 0;

            // 自定义主色
            var hex = AppSettings.CustomAccentHex;
            if (!string.IsNullOrEmpty(hex))
            {
                CustomAccentToggle.IsOn = true;
                CustomAccentPanel.Visibility = Visibility.Visible;
                AccentHexBox.Text = hex;
                if (AccentSwatchList.Items.Count == 0) BuildAccentSwatches();
            }

            // 自定义主题
            if (AppSettings.ThemeName == "Custom")
            {
                CustomEditorPanel.Visibility = Visibility.Visible;
                LoadCustomThemeEditors();
                BuildCustomThemeSwatches();
            }

            LoadDefaultAppearance();

            _loading = false;
        }

        // ━━━━━ 本地化 ━━━━━

        private void ApplyLanguage()
        {
            PageTitle.Text = AppSettings.S("主题与字体", "Theme & Fonts");
            ThemeStyleLabel.Text = AppSettings.S("主题风格", "Theme Style");
            ThemeStyleDesc.Text = AppSettings.S("选择整体配色和字体风格，重启应用后部分元素生效",
                "Choose overall colors and fonts. Some elements need an app restart.");

            ChineseFontLabel.Text = AppSettings.S("中文字体", "Chinese Font");
            ChineseFontDesc.Text = AppSettings.S("选择正文使用的字体", "Choose the font for Chinese text");
            EnglishFontLabel.Text = AppSettings.S("英文字体", "English Font");
            EnglishFontDesc.Text = AppSettings.S("选择英文/数字使用的字体", "Choose the font for English and numbers");

            CustomAccentLabel.Text = AppSettings.S("自定义主色调", "Custom Accent Color");
            CustomAccentDesc.Text = AppSettings.S("覆盖当前主题的强调色", "Override the current theme's accent color");
            AccentSwatchLabel.Text = AppSettings.S("预设色板", "Preset Swatches");
            AccentHexLabel.Text = AppSettings.S("或输入十六进制值（#RRGGBB）", "Or enter a hex value (#RRGGBB)");
            ApplyAccentBtn.Content = AppSettings.S("应用", "Apply");
            CurrentAccentLabel.Text = AppSettings.S("当前主色", "Current Accent");

            DefaultAppearanceLabel.Text = AppSettings.S("默认对话外观", "Default Chat Appearance");
            DefaultAppearanceDesc.Text = AppSettings.S("新建对话时使用的默认气泡和背景颜色",
                "Default bubble and background colors for new conversations");
            BgSectionLabel.Text = AppSettings.S("对话背景", "Background");
            UserBubbleSectionLabel.Text = AppSettings.S("用户气泡颜色", "User Bubble Color");
            AiBubbleSectionLabel.Text = AppSettings.S("AI 气泡颜色", "AI Bubble Color");
            HighlightSectionLabel.Text = AppSettings.S("文本高亮颜色", "Text Highlight Color");
            QuoteSectionLabel.Text = AppSettings.S("引号颜色", "Quote Color");
            BracketSectionLabel.Text = AppSettings.S("括号颜色", "Bracket Color");

            DefaultUserClearBtn.Content = AppSettings.S("清除", "Clear");
            DefaultAiClearBtn.Content = AppSettings.S("清除", "Clear");
            QuotePresetBtn.Content = AppSettings.S("预设", "Presets");
            QuoteClearBtn.Content = AppSettings.S("清除", "Clear");
            BracketPresetBtn.Content = AppSettings.S("预设", "Presets");
            BracketClearBtn.Content = AppSettings.S("清除", "Clear");
            ResetDefaultBtn.Content = AppSettings.S("恢复默认", "Reset to Default");

            ColorPageBgLabel.Text = AppSettings.S("页面背景", "Page Background");
            ColorAccentLabel.Text = AppSettings.S("强调色", "Accent");
            ColorTextLabel.Text = AppSettings.S("文字色", "Text Color");
            ColorSurfaceLabel.Text = AppSettings.S("面板色", "Surface");
            ColorBorderLabel.Text = AppSettings.S("边框色", "Border");
            ColorUserBubbleLabel.Text = AppSettings.S("用户气泡", "User Bubble");
            ColorAiBubbleLabel.Text = AppSettings.S("AI 气泡", "AI Bubble");
            ColorMutedTextLabel.Text = AppSettings.S("辅助文字", "Muted Text");
            CustomSwatchLabel.Text = AppSettings.S("预设主题色板", "Preset Theme Colors");
            AutoApplyLabel.Text = AppSettings.S("修改后自动应用", "Applies automatically on change");

            PreviewTitle.Text = AppSettings.S("实时预览", "Live Preview");
            CustomColorsLabel.Text = AppSettings.S("自定义配色", "Custom Colors");
            PreviewSendBtn.Content = AppSettings.S("发送", "Send");

            InkThemeName.Text = AppSettings.S("墨韵书卷", "Ink Scroll");
            InkThemeSubtitle.Text = AppSettings.S("宣纸·朱砂", "Rice Paper · Vermilion");
            CeladonThemeName.Text = AppSettings.S("青瓷雅韵", "Celadon Elegance");
            CeladonThemeSubtitle.Text = AppSettings.S("天青·月白", "Sky Blue · Moon White");
            MidnightThemeName.Text = AppSettings.S("朱砂深夜", "Midnight Vermilion");
            MidnightThemeSubtitle.Text = AppSettings.S("墨蓝·鎏金", "Ink Blue · Gold");
            CustomThemeName.Text = AppSettings.S("自定义", "Custom");
            CustomThemeSubtitle.Text = AppSettings.S("自由调色", "Free Palette");
            ModernThemeName.Text = AppSettings.S("现代·石板", "Modern Slate");
            ModernThemeSubtitle.Text = AppSettings.S("石板·靛蓝", "Slate · Indigo");

            RebuildFontPickers();
            RebuildBgTypePicker();
        }

        private void RebuildFontPickers()
        {
            ChineseFontPicker.Items.Clear();
            ChineseFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("跟随当前主题", "Follow Current Theme") });
            ChineseFontPicker.Items.Add(new ComboBoxItem { Content = "霞鹜文楷（楷书·古风）" });
            ChineseFontPicker.Items.Add(new ComboBoxItem { Content = "思源宋体（宋体·典雅）" });
            ChineseFontPicker.Items.Add(new ComboBoxItem { Content = "站酷小薇（书法·飘逸）" });
            ChineseFontPicker.Items.Add(new ComboBoxItem { Content = "思源黑体（黑体·现代）" });
            ChineseFontPicker.Items.Add(new ComboBoxItem { Content = "马山正（手写·活泼）" });
            ChineseFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("系统默认", "System Default") });

            EnglishFontPicker.Items.Clear();
            EnglishFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("跟随当前主题", "Follow Current Theme") });
            EnglishFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("EB Garamond（古典衬线）", "EB Garamond (Classic Serif)") });
            EnglishFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("Cormorant Garamond（纤细衬线）", "Cormorant Garamond (Fine Serif)") });
            EnglishFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("Lora（现代衬线）", "Lora (Modern Serif)") });
            EnglishFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("Playfair Display（优雅展示）", "Playfair Display (Elegant)") });
            EnglishFontPicker.Items.Add(new ComboBoxItem { Content = AppSettings.S("系统默认", "System Default") });
        }

        private void RebuildBgTypePicker()
        {
            int idx = DefaultBgType.SelectedIndex;
            DefaultBgType.Items.Clear();
            DefaultBgType.Items.Add(new ComboBoxItem { Content = AppSettings.S("无背景", "No Background"), Tag = "none" });
            DefaultBgType.Items.Add(new ComboBoxItem { Content = AppSettings.S("纯色", "Solid Color"), Tag = "solid" });
            if (idx >= 0 && idx < DefaultBgType.Items.Count)
                DefaultBgType.SelectedIndex = idx;
        }

        // ━━━━━ 主题预设 ━━━━━

    }
}
