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
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private void ResetTheme_Click(object sender, RoutedEventArgs e)
        {
            SwitchTheme("InkScroll", AppSettings.S("墨韵书卷", "Ink Scroll"));
        }

        // ━━━━━ 默认对话外观 ━━━━━

        private static string DefBgType
        {
            get => AppSettings.GetString("default_BgType", "none");
            set => AppSettings.SetString("default_BgType", value);
        }
        private static string DefBgValue
        {
            get => AppSettings.GetString("default_BgValue", "");
            set => AppSettings.SetString("default_BgValue", value);
        }
        private static string DefUserBubble
        {
            get => AppSettings.GetString("default_UserBubble", "");
            set => AppSettings.SetString("default_UserBubble", value);
        }
        private static string DefAiBubble
        {
            get => AppSettings.GetString("default_AiBubble", "");
            set => AppSettings.SetString("default_AiBubble", value);
        }
        private static string DefQuoteColor
        {
            get => AppSettings.GetString("default_QuoteColor", "");
            set => AppSettings.SetString("default_QuoteColor", value);
        }
        private static string DefBracketColor
        {
            get => AppSettings.GetString("default_BracketColor", "");
            set => AppSettings.SetString("default_BracketColor", value);
        }

    }
}
