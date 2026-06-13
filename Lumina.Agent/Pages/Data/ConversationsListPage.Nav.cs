using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;


namespace yanshuai
{
    public sealed partial class ConversationsListPage : Page
    {
        private void NewConvBtn_Click(object sender, RoutedEventArgs e)
        {
            // 清空 ActiveConversation，MainPage.OnNavigatedTo 会新建一个 pending 对话
            AppState.ActiveConversation = null;
            Frame.Navigate(typeof(MainPage));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectMode) { ExitSelectMode(); return; }
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }

        private static ElementTheme DialogTheme =>
            AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light;
    }
}
