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
        private void ChineseFontPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var values = new[] { "ThemeDefault", "LXGW", "SourceHanSerif", "ZCOOL", "NotoSansSC", "MaShanZheng", "System" };
            if (ChineseFontPicker.SelectedIndex >= 0 &&
                ChineseFontPicker.SelectedIndex < values.Length)
            {
                AppSettings.ChineseFontFamily = values[ChineseFontPicker.SelectedIndex];
                ApplyFontChange();
            }
        }

        private void EnglishFontPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var values = new[] { "ThemeDefault", "EBGaramond", "Cormorant", "Lora", "PlayfairDisplay", "System" };
            if (EnglishFontPicker.SelectedIndex >= 0 &&
                EnglishFontPicker.SelectedIndex < values.Length)
            {
                AppSettings.EnglishFontFamily = values[EnglishFontPicker.SelectedIndex];
                ApplyFontChange();
            }
        }

        private void ApplyFontChange()
        {
            ApplyVisualTheme();
        }

        private void ApplyVisualTheme()
        {
            App.ApplyTheme();
            AppSettings.ApplyThemeForced(RootGrid, this);
            App.ApplyTypography(RootGrid);
        }

        private void RefreshFontPickers()
        {
            _loading = true;
            ChineseFontPicker.SelectedIndex = 0;
            EnglishFontPicker.SelectedIndex = 0;
            _loading = false;
        }

        // ━━━━━ 自定义主色 ━━━━━

    }
}
