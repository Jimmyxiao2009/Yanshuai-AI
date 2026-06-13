using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class CharaWizardPage1 : Page
    {
        public CharaWizardPage1() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();

            // 恢复已填内容（返回时）
            NameBox.Text = CharaWizardData.Name;
            if (!string.IsNullOrEmpty(CharaWizardData.AvatarBase64))
                LoadImageToControl(CharaWizardData.AvatarBase64, AvatarImage, AvatarPlaceholder);
            if (!string.IsNullOrEmpty(CharaWizardData.IllustBase64))
                LoadImageToControl(CharaWizardData.IllustBase64, IllustImage, IllustPlaceholder);
        }

        private void LoadImageToControl(string b64, Image img, FrameworkElement placeholder)
        {
            try
            {
                var bytes = Convert.FromBase64String(b64);
                    var bmp = AppSettings.LoadBitmapSync(bytes);
                img.Source = bmp;
                img.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private async void PickAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".webp");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var buf = await Windows.Storage.FileIO.ReadBufferAsync(file);
            CharaWizardData.AvatarBase64   = Convert.ToBase64String(buf.ToArray());
            CharaWizardData.AvatarMimeType = file.ContentType;
            LoadImageToControl(CharaWizardData.AvatarBase64, AvatarImage, AvatarPlaceholder);
        }

        private void ClearAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.AvatarBase64 = null; CharaWizardData.AvatarMimeType = null;
            AvatarImage.Source = null; AvatarImage.Visibility = Visibility.Collapsed;
            AvatarPlaceholder.Visibility = Visibility.Visible;
        }

        private async void PickIllustBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".webp");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var buf = await Windows.Storage.FileIO.ReadBufferAsync(file);
            CharaWizardData.IllustBase64   = Convert.ToBase64String(buf.ToArray());
            CharaWizardData.IllustMimeType = file.ContentType;
            LoadImageToControl(CharaWizardData.IllustBase64, IllustImage, IllustPlaceholder);
        }

        private void ClearIllustBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.IllustBase64 = null; CharaWizardData.IllustMimeType = null;
            IllustImage.Source = null; IllustImage.Visibility = Visibility.Collapsed;
            IllustPlaceholder.Visibility = Visibility.Visible;
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.Name = NameBox.Text.Trim();
            Frame.Navigate(typeof(CharaWizardPage2));
        }

        private void ApplyLanguage()
        {
            PageTitle.Text = AppSettings.S("新建角色卡", "New Character Card");
            StepText.Text = AppSettings.S("第 1 步 / 共 4 步  ·  头像与名称", "Step 1/4 · Avatar & Name");
            AvatarLabel.Text = AppSettings.S("头像", "Avatar");
            PickAvatarBtn.Content = AppSettings.S("选择头像图片", "Choose Avatar Image");
            ClearAvatarBtn.Content = AppSettings.S("清除", "Clear");
            NameLabel.Text = AppSettings.S("角色名称", "Character Name");
            NameBox.PlaceholderText = AppSettings.S("输入角色名称…", "Enter character name…");
            IllustLabel.Text = AppSettings.S("立绘图片（可选）", "Character Art (optional)");
            IllustHint.Text = AppSettings.S("用于对话背景，建议竖版图片", "Used as chat background, portrait recommended");
            PickIllustBtn.Content = AppSettings.S("选择立绘", "Choose Art");
            ClearIllustBtn.Content = AppSettings.S("清除", "Clear");
            NextBtn.Label = AppSettings.S("下一步", "Next");
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
