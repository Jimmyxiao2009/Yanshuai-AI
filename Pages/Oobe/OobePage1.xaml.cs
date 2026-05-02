using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobePage1 : Page
    {
        public OobePage1() { InitializeComponent(); }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
        }
        private void NextBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobeBasicPage));

        private async void RestoreBackupBtn_Click(object sender, RoutedEventArgs e)
        {
            var files = await ImportExportPage.PickFiles(new[] { ".zip" });
            if (files == null || files.Count == 0) return;
            bool ok = await ImportExportPage.ImportFromZip(files[0]);
            var dialog = new ContentDialog
            {
                Title             = ok ? "恢复成功" : "恢复失败",
                Content           = ok ? "数据已从备份恢复，点击确定继续。" : "备份文件无效或读取失败，请重试。",
                PrimaryButtonText = "确定",
                RequestedTheme    = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            await dialog.ShowAsync().AsTask();
            if (ok)
            {
                AppSettings.OobeCompleted = true;
                Frame.Navigate(typeof(MainPage));
            }
        }
    }
}
