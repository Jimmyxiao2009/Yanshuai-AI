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
        private void LoadCustomThemeEditors()
        {
            Hex_PageBg.Text = AppSettings.CustomTheme_PageBg;
            Hex_Accent.Text = AppSettings.CustomTheme_Accent;
            Hex_Text.Text = AppSettings.CustomTheme_Text;
            Hex_Surface.Text = AppSettings.CustomTheme_Surface;
            Hex_Border.Text = AppSettings.CustomTheme_Border;
            Hex_UserBubble.Text = AppSettings.CustomTheme_UserBubble;
            Hex_AiBubble.Text = AppSettings.CustomTheme_AiBubble;
            Hex_MutedText.Text = AppSettings.CustomTheme_MutedText;

            UpdateEditorSwatches();
            RefreshPreview();

            // Wire up text changed events
            Hex_PageBg.TextChanged += (s, a) => OnCustomColorChanged("PageBg", Hex_PageBg);
            Hex_Accent.TextChanged += (s, a) => OnCustomColorChanged("Accent", Hex_Accent);
            Hex_Text.TextChanged += (s, a) => OnCustomColorChanged("Text", Hex_Text);
            Hex_Surface.TextChanged += (s, a) => OnCustomColorChanged("Surface", Hex_Surface);
            Hex_Border.TextChanged += (s, a) => OnCustomColorChanged("Border", Hex_Border);
            Hex_UserBubble.TextChanged += (s, a) => OnCustomColorChanged("UserBubble", Hex_UserBubble);
            Hex_AiBubble.TextChanged += (s, a) => OnCustomColorChanged("AiBubble", Hex_AiBubble);
            Hex_MutedText.TextChanged += (s, a) => OnCustomColorChanged("MutedText", Hex_MutedText);
        }

        private void OnCustomColorChanged(string key, TextBox box)
        {
            if (_loading) return;
            var text = (box.Text ?? "").Trim();
            if (text.Length != 7 || !text.StartsWith("#")) return;

            SaveCustomColor(key, text);

            Border swatch = null;
            if (key == "PageBg") swatch = Swatch_PageBg;
            else if (key == "Accent") swatch = Swatch_Accent;
            else if (key == "Text") swatch = Swatch_Text;
            else if (key == "Surface") swatch = Swatch_Surface;
            else if (key == "Border") swatch = Swatch_Border;
            else if (key == "UserBubble") swatch = Swatch_UserBubble;
            else if (key == "AiBubble") swatch = Swatch_AiBubble;
            else if (key == "MutedText") swatch = Swatch_MutedText;
            if (swatch != null)
                try { swatch.Background = new SolidColorBrush(HexToColor(text)); } catch { }

            ApplyVisualTheme();
            RefreshPreview();
        }

        private static void SaveCustomColor(string key, string hex)
        {
            if (key == "PageBg") AppSettings.CustomTheme_PageBg = hex;
            else if (key == "Accent") AppSettings.CustomTheme_Accent = hex;
            else if (key == "Text") AppSettings.CustomTheme_Text = hex;
            else if (key == "Surface") AppSettings.CustomTheme_Surface = hex;
            else if (key == "Border") AppSettings.CustomTheme_Border = hex;
            else if (key == "UserBubble") AppSettings.CustomTheme_UserBubble = hex;
            else if (key == "AiBubble") AppSettings.CustomTheme_AiBubble = hex;
            else if (key == "MutedText") AppSettings.CustomTheme_MutedText = hex;
        }

        private void UpdateEditorSwatches()
        {
            SetSwatchColor(Swatch_PageBg, AppSettings.CustomTheme_PageBg);
            SetSwatchColor(Swatch_Accent, AppSettings.CustomTheme_Accent);
            SetSwatchColor(Swatch_Text, AppSettings.CustomTheme_Text);
            SetSwatchColor(Swatch_Surface, AppSettings.CustomTheme_Surface);
            SetSwatchColor(Swatch_Border, AppSettings.CustomTheme_Border);
            SetSwatchColor(Swatch_UserBubble, AppSettings.CustomTheme_UserBubble);
            SetSwatchColor(Swatch_AiBubble, AppSettings.CustomTheme_AiBubble);
            SetSwatchColor(Swatch_MutedText, AppSettings.CustomTheme_MutedText);
        }

        private static void SetSwatchColor(Border swatch, string hex)
        {
            if (swatch == null || string.IsNullOrEmpty(hex)) return;
            try { swatch.Background = new SolidColorBrush(HexToColor(hex)); } catch { }
        }

        private void BuildCustomThemeSwatches()
        {
            CustomThemeSwatchList.Items.Clear();
            string[] presets = { "#A33A2C", "#7DB6B0", "#D4504A", "#C9A86A",
                                 "#8B5A8C", "#5D7BBE", "#6B8E5A", "#B86E47",
                                 "#2B2620", "#F5EFE0", "#1A1B26", "#E8D5B5" };
            foreach (var h in presets)
            {
                var border = new Border
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(HexToColor(h)),
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["YanshuaiBorderBrush"],
                    BorderThickness = new Thickness(1),
                    Tag = h
                };
                border.Tapped += (s, args) =>
                {
                    var b = s as Border;
                    if (b == null) return;
                    string hex = b.Tag as string;
                    // Apply to all hex boxes that currently have focus — or just accent for simplicity
                    if (FocusManager.GetFocusedElement() is TextBox active)
                        active.Text = hex;
                    else
                        Hex_Accent.Text = hex;
                };
                CustomThemeSwatchList.Items.Add(border);
            }
        }

        private void RefreshPreview()
        {
            try
            {
                // 页面背景
                PreviewCard.Background = GetBrush("YanshuaiPageBrush");
                // 强调色条
                PreviewAccentBar.Background = GetBrush("YanshuaiAccentBrush");
                // 用户气泡
                PreviewUserBubble.Background = GetBrush("YanshuaiUserBubbleBrush");
                // AI 气泡
                PreviewAiBubble.Background = GetBrush("YanshuaiAiBubbleBrush");
                // 文字色
                PreviewAiText.Foreground = GetBrush("YanshuaiTextBrush");
                // 输入框背景
                PreviewInputBg.Background = GetBrush("YanshuaiSubtlePanelBrush");
                PreviewInputBg.BorderBrush = GetBrush("YanshuaiBorderBrush");
                // 发送按钮
                PreviewSendBtn.Background = GetBrush("YanshuaiAccentBrush");
            }
            catch { }
        }

        private static Brush GetBrush(string key)
        {
            var res = Application.Current.Resources;
            return res.ContainsKey(key) ? res[key] as Brush : null;
        }

        // ━━━━━ 字体选择 ━━━━━

    }
}
