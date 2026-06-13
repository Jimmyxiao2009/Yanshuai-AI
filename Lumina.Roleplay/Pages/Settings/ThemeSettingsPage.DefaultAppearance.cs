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
        private void LoadDefaultAppearance()
        {
            switch (DefBgType)
            {
                case "solid": DefaultBgType.SelectedIndex = 1; break;
                default: DefaultBgType.SelectedIndex = 0; break;
            }
            DefaultSolidPanel.Visibility = DefBgType == "solid" ? Visibility.Visible : Visibility.Collapsed;
            DefaultBgHex.Text = DefBgType == "solid" ? DefBgValue : "";
            RefreshPreviewSwatch(DefaultBgPreview, DefaultBgLabel, DefBgType == "solid" ? DefBgValue : "", "");

            DefaultUserHex.Text = DefUserBubble;
            RefreshPreviewSwatch(DefaultUserPreview, DefaultUserLabel, DefUserBubble,
                AppSettings.S("（使用强调色）", "(Uses accent color)"));

            DefaultAiHex.Text = DefAiBubble;
            RefreshPreviewSwatch(DefaultAiPreview, DefaultAiLabel, DefAiBubble,
                AppSettings.S("（使用强调色暗色）", "(Uses accent dark)"));

            DefaultQuoteHex.Text = DefQuoteColor;
            RefreshPreviewSwatch(DefaultQuotePreview, DefaultQuoteLabel, DefQuoteColor,
                AppSettings.S("（默认橘黄）", "(Default orange)"));

            DefaultBracketHex.Text = DefBracketColor;
            RefreshPreviewSwatch(DefaultBracketPreview, DefaultBracketLabel, DefBracketColor,
                AppSettings.S("（默认灰色）", "(Default gray)"));
        }

        private static void RefreshPreviewSwatch(Border preview, TextBlock label, string hex, string emptyText)
        {
            if (preview == null) return;
            if (!string.IsNullOrEmpty(hex) && TryParseColorHex(hex, out Color c))
            {
                preview.Background = new SolidColorBrush(c);
                if (label != null)
                {
                    label.Text = hex.StartsWith("#") ? hex.ToUpper() : "#" + hex.ToUpper();
                    label.Foreground = new SolidColorBrush(c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 128 ? Colors.Black : Colors.White);
                }
            }
            else
            {
                preview.Background = null;
                if (label != null)
                {
                    label.Text = emptyText;
                    label.Foreground = new SolidColorBrush(Colors.Gray);
                }
            }
        }

        private static bool TryParseColorHex(string hex, out Color color)
        {
            color = Colors.Transparent;
            hex = (hex ?? "").TrimStart('#');
            if (hex.Length != 6) return false;
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                color = Color.FromArgb(255, r, g, b);
                return true;
            }
            catch { return false; }
        }

        private void DefaultBgType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var tag = (DefaultBgType.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
            DefaultSolidPanel.Visibility = tag == "solid" ? Visibility.Visible : Visibility.Collapsed;
            DefBgType = tag;
            if (tag == "none") { DefBgValue = ""; RefreshPreviewSwatch(DefaultBgPreview, DefaultBgLabel, "", ""); }
        }

        private void DefaultBgHex_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = (DefaultBgHex.Text ?? "").Trim();
            if (TryParseColorHex(hex, out _))
            {
                DefaultBgHex.BorderBrush = null;
                var n = hex.StartsWith("#") ? hex : "#" + hex;
                DefBgValue = n; DefBgType = "solid";
                if (DefaultBgType.SelectedIndex != 1) { _loading = true; DefaultBgType.SelectedIndex = 1; _loading = false; }
                DefaultSolidPanel.Visibility = Visibility.Visible;
                RefreshPreviewSwatch(DefaultBgPreview, DefaultBgLabel, n, "");
            }
            else
            {
                DefaultBgHex.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void DefaultBgPreset_Click(object sender, string hex)
        {
            _loading = true; DefaultBgHex.Text = hex; _loading = false;
            DefBgValue = hex; DefBgType = "solid";
            if (DefaultBgType.SelectedIndex != 1) DefaultBgType.SelectedIndex = 1;
            DefaultSolidPanel.Visibility = Visibility.Visible;
            RefreshPreviewSwatch(DefaultBgPreview, DefaultBgLabel, hex, "");
        }

        private void DefaultUserHex_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = (DefaultUserHex.Text ?? "").Trim();
            if (TryParseColorHex(hex, out _))
            {
                DefaultUserHex.BorderBrush = null;
                DefUserBubble = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreviewSwatch(DefaultUserPreview, DefaultUserLabel, DefUserBubble, "（使用强调色）");
            }
            else
            {
                DefaultUserHex.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void DefaultUserPreset_Click(object sender, string hex)
        {
            _loading = true; DefaultUserHex.Text = hex; _loading = false;
            DefUserBubble = hex;
            RefreshPreviewSwatch(DefaultUserPreview, DefaultUserLabel, hex, "（使用强调色）");
        }

        private void DefaultUserClear_Click(object sender, RoutedEventArgs e)
        {
            DefUserBubble = "";
            _loading = true; DefaultUserHex.Text = ""; _loading = false;
            RefreshPreviewSwatch(DefaultUserPreview, DefaultUserLabel, "", "（使用强调色）");
        }

        private void DefaultAiHex_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = (DefaultAiHex.Text ?? "").Trim();
            if (TryParseColorHex(hex, out _))
            {
                DefaultAiHex.BorderBrush = null;
                DefAiBubble = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreviewSwatch(DefaultAiPreview, DefaultAiLabel, DefAiBubble, "（使用强调色暗色）");
            }
            else
            {
                DefaultAiHex.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void DefaultAiPreset_Click(object sender, string hex)
        {
            _loading = true; DefaultAiHex.Text = hex; _loading = false;
            DefAiBubble = hex;
            RefreshPreviewSwatch(DefaultAiPreview, DefaultAiLabel, hex, "（使用强调色暗色）");
        }

        private void DefaultAiClear_Click(object sender, RoutedEventArgs e)
        {
            DefAiBubble = "";
            _loading = true; DefaultAiHex.Text = ""; _loading = false;
            RefreshPreviewSwatch(DefaultAiPreview, DefaultAiLabel, "", "（使用强调色暗色）");
        }

        private void DefaultQuoteHex_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = (DefaultQuoteHex.Text ?? "").Trim();
            if (TryParseColorHex(hex, out _))
            {
                DefaultQuoteHex.BorderBrush = null;
                DefQuoteColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreviewSwatch(DefaultQuotePreview, DefaultQuoteLabel, DefQuoteColor, "（默认橘黄）");
            }
            else
            {
                DefaultQuoteHex.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void DefaultQuotePreset_Click(object sender, RoutedEventArgs e)
        {
            string[] presets = { "#E8A040", "#D4A060", "#C08050", "#F0B860" };
            ShowColorPresetFlyout(DefaultQuoteHex, presets);
        }

        private void DefaultQuoteClear_Click(object sender, RoutedEventArgs e)
        {
            DefQuoteColor = "";
            _loading = true; DefaultQuoteHex.Text = ""; _loading = false;
            RefreshPreviewSwatch(DefaultQuotePreview, DefaultQuoteLabel, "", "（默认橘黄）");
        }

        private void DefaultBracketHex_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = (DefaultBracketHex.Text ?? "").Trim();
            if (TryParseColorHex(hex, out _))
            {
                DefaultBracketHex.BorderBrush = null;
                DefBracketColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreviewSwatch(DefaultBracketPreview, DefaultBracketLabel, DefBracketColor, "（默认灰色）");
            }
            else
            {
                DefaultBracketHex.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void DefaultBracketPreset_Click(object sender, RoutedEventArgs e)
        {
            string[] presets = { "#888888", "#666666", "#999999", "#AAAAAA", "#556677" };
            ShowColorPresetFlyout(DefaultBracketHex, presets);
        }

        private void DefaultBracketClear_Click(object sender, RoutedEventArgs e)
        {
            DefBracketColor = "";
            _loading = true; DefaultBracketHex.Text = ""; _loading = false;
            RefreshPreviewSwatch(DefaultBracketPreview, DefaultBracketLabel, "", "（默认灰色）");
        }

        private void DefaultAppearanceReset_Click(object sender, RoutedEventArgs e)
        {
            DefBgType = "none"; DefBgValue = "";
            DefUserBubble = ""; DefAiBubble = "";
            DefQuoteColor = ""; DefBracketColor = "";
            LoadDefaultAppearance();
        }

        private static void ShowColorPresetFlyout(TextBox target, string[] colors)
        {
            var flyout = new Flyout();
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var hex in colors)
            {
                var swatch = new Border
                {
                    Width = 32, Height = 32, Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(HexToColor(hex)),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1),
                    Tag = hex
                };
                swatch.Tapped += (s, e2) =>
                {
                    var b = s as Border;
                    if (b == null) return;
                    target.Text = b.Tag as string;
                    flyout.Hide();
                };
                panel.Children.Add(swatch);
            }
            flyout.Content = panel;
            flyout.Placement = Windows.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top;
            flyout.ShowAt(target);
        }
    }
}
