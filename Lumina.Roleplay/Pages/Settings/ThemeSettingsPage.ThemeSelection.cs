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
        private void ThemeCardInk_Tapped(object sender, TappedRoutedEventArgs e)
            => SwitchTheme("InkScroll", "墨韵书卷");

        private void ThemeCardCeladon_Tapped(object sender, TappedRoutedEventArgs e)
            => SwitchTheme("Celadon", "青瓷雅韵");

        private void ThemeCardMidnight_Tapped(object sender, TappedRoutedEventArgs e)
            => SwitchTheme("MidnightVermilion", "朱砂深夜");

        private void ThemeCardModern_Tapped(object sender, TappedRoutedEventArgs e)
            => SwitchTheme("ModernSlate", AppSettings.S("现代·石板", "Modern Slate"));

        private void ThemeCardCustom_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_loading) return;
            // 如果当前已经是自定义主题，只展开/收起编辑器
            if (AppSettings.ThemeName == "Custom")
            {
                CustomEditorPanel.Visibility =
                    CustomEditorPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                return;
            }
            SwitchTheme("Custom", "自定义");
        }

        private async void SwitchTheme(string name, string displayName)
        {
            AppSettings.ThemeName = name;

            if (name != "Custom")
            {
                // 预设主题：重置字体为随主题
                AppSettings.ChineseFontFamily = "ThemeDefault";
                AppSettings.EnglishFontFamily = "ThemeDefault";
                CustomEditorPanel.Visibility = Visibility.Collapsed;
            }

            ApplyVisualTheme();
            RefreshFontPickers();
            ThemeSelectedLabel.Text = AppSettings.S("当前：", "Current: ") + displayName;

            if (name == "Custom")
            {
                CustomEditorPanel.Visibility = Visibility.Visible;
                LoadCustomThemeEditors();
                BuildCustomThemeSwatches();
                RefreshPreview();

                var dialog = new ContentDialog
                {
                    Title = AppSettings.S("自定义主题已激活", "Custom Theme Activated"),
                    Content = AppSettings.S("点击下方的色块或输入 Hex 值来调整各个颜色，预览卡片会实时更新。",
                        "Tap a swatch or enter a hex value to adjust colors. The preview updates live."),
                    PrimaryButtonText = AppSettings.S("好", "OK")
                };
                await dialog.ShowAsync().AsTask();
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = AppSettings.S("主题已切换", "Theme Switched"),
                    Content = AppSettings.S("部分元素需重启应用后才能完全更新。",
                        "Some elements require an app restart to fully update."),
                    PrimaryButtonText = AppSettings.S("好", "OK")
                };
                await dialog.ShowAsync().AsTask();
            }
        }

        // ━━━━━ 自定义主题编辑 ━━━━━

    }
}
