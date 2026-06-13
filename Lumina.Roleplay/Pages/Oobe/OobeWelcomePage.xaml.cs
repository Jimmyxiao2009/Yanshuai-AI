using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeWelcomePage : Page
    {
        public OobeWelcomePage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            SubtitleText.Text = AppSettings.S("AI 对话助手", "AI Chat Assistant");
            PlatformText.Text = AppSettings.S("适配 Windows 10 Mobile", "For Windows 10 Mobile");
            DescText.Text = AppSettings.S("接下来将引导你完成基本设置，只需几分钟。",
                "Let's walk through the basic setup — it only takes a few minutes.");
            NextBtn.Label = AppSettings.S("开始", "Start");
            RestoreBtn.Label = AppSettings.S("从备份恢复", "Restore from Backup");
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobeAppearancePage));

        private async void RestoreBackupBtn_Click(object sender, RoutedEventArgs e)
        {
            var files = await ImportExportPage.PickFiles(new[] { ".zip" });
            if (files == null || files.Count == 0) return;
            bool ok = await ImportExportPage.ImportFromZip(files[0]);
            var dialog = new ContentDialog
            {
                Title             = ok ? AppSettings.S("恢复成功", "Restore Complete")
                                       : AppSettings.S("恢复失败", "Restore Failed"),
                Content           = ok ? AppSettings.S("数据已从备份恢复，点击确定继续。",
                    "Data has been restored from backup. Tap OK to continue.")
                                       : AppSettings.S("备份文件无效或读取失败，请重试。",
                    "The backup file is invalid or could not be read. Please try again."),
                PrimaryButtonText = AppSettings.S("确定", "OK"),
                RequestedTheme    = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            await dialog.ShowAsync().AsTask();
            if (ok)
            {
                AppSettings.OobeCompleted = true;
                Frame.Navigate(typeof(ShellPage));
            }
        }
    }
}
