using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobePermissionPage : Page
    {
        public OobePermissionPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            PageTitle.Text        = AppSettings.S("权限设置", "Permissions");
            PageSubtitle.Text     = AppSettings.S("应用需要使用以下权限", "This app needs the following permissions");
            InfoText.Text         = AppSettings.S("以下权限已在安装时声明，部分可能需要你在系统设置中手动开启。",
                                                  "These permissions were declared during install. Some may need manual approval in Settings.");
            SettingsBtnText.Text  = AppSettings.S("打开系统隐私设置", "Open Privacy Settings");
            NoteText.Text         = AppSettings.S("如果你更改了权限设置，返回后点击下方「继续」即可。",
                                                  "After changing permissions, tap Continue below.");
            NextBtn.Label         = AppSettings.S("继续", "Continue");
            SdStatus.Text         = AppSettings.S("✓ 已授权", "✓ Granted");
            FsStatus.Text         = AppSettings.S("不可用", "N/A");
        }

        private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op = Launcher.LaunchUriAsync(new System.Uri("ms-settings:privacy"));
            op.Completed = (info, status) => tcs.TrySetResult(info.GetResults());
            await tcs.Task;
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobePage2));

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(OobeBasicPage));
        }
    }
}
