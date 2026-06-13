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
        private void CustomAccentToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (CustomAccentToggle.IsOn)
            {
                CustomAccentPanel.Visibility = Visibility.Visible;
                if (AccentSwatchList.Items.Count == 0)
                    BuildAccentSwatches();
            }
            else
            {
                CustomAccentPanel.Visibility = Visibility.Collapsed;
                AppSettings.CustomAccentHex = null;
                App.ApplyTheme();
            }
        }

        private void BuildAccentSwatches()
        {
            string[] presets = { "#A33A2C", "#7DB6B0", "#D4504A", "#C9A86A",
                                 "#8B5A8C", "#5D7BBE", "#6B8E5A", "#B86E47" };
            foreach (var hex in presets)
            {
                var border = new Border
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(HexToColor(hex)),
                    BorderBrush = (SolidColorBrush)Application.Current.Resources["YanshuaiBorderBrush"],
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

        private void ApplyAccentHex_Click(object sender, RoutedEventArgs e)
        {
            string hex = (AccentHexBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(hex)) return;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            ApplyAccent(hex);
        }

        private void ApplyAccent(string hex)
        {
            AppSettings.CustomAccentHex = hex;
            App.ApplyTheme();
            AppSettings.ApplyTheme(RootGrid, this);
            try { CurrentAccentSwatch.Background = new SolidColorBrush(HexToColor(hex)); } catch { }
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

        private static string ThemeDisplayName(string name)
        {
            if (name == "Celadon") return AppSettings.S("青瓷雅韵", "Celadon Elegance");
            if (name == "MidnightVermilion") return AppSettings.S("朱砂深夜", "Midnight Vermilion");
            if (name == "ModernSlate") return AppSettings.S("现代·石板", "Modern Slate");
            if (name == "Custom") return AppSettings.S("自定义主题", "Custom");
            return AppSettings.S("墨韵书卷", "Ink Scroll");
        }

    }
}
