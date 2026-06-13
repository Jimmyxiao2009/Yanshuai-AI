using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class UserProfilePage : Page
    {
        private bool _loading = true;

        public UserProfilePage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
            _loading = true;

            var up = DataManager.Data.UserProfile ?? new UserProfile();
            NameBox.Text        = up.Name        ?? "";
            DescriptionBox.Text = up.Description ?? "";

            if (up.HasAvatar)
                await LoadAvatarFromBase64(up.AvatarBase64);

            _loading = false;
            UpdatePreview();
        }

        private void ApplyLanguage()
        {
            PageTitle.Text         = AppSettings.S("用户资料", "User Profile");
            PageSubtitle.Text      = AppSettings.S("资料会注入到每次对话的 System Prompt 中", "This profile is injected into every conversation's System Prompt");
            PickAvatarBtn.Content  = AppSettings.S("选择头像", "Choose Avatar");
            ClearAvatarBtn.Content = AppSettings.S("清除头像", "Clear Avatar");
            NameLabel.Text         = AppSettings.S("名字", "Name");
            NameBox.PlaceholderText= AppSettings.S("你的名字（可选）", "Your Name (optional)");
            DescLabel.Text         = AppSettings.S("人设简介", "Character Bio");
            DescriptionBox.PlaceholderText = AppSettings.S("介绍一下自己，会注入到 System Prompt 里告知 AI…", "Introduce yourself — this is sent to the AI in the System Prompt…");
            ExampleHint.Text       = AppSettings.S("示例：一个喜欢科幻小说的大学生，性格活泼，说话直接", "e.g. A college student who loves sci-fi, lively and direct");
            PreviewHeader.Text     = AppSettings.S("注入预览", "Injection Preview");
        }

        private async System.Threading.Tasks.Task LoadAvatarFromBase64(string b64)
        {
            try
            {
                var bytes = Convert.FromBase64String(b64);
                var bmp = new BitmapImage();
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0);
                    await bmp.SetSourceAsync(ms);
                }
                AvatarImage.Source = bmp;
                AvatarImage.Visibility       = Visibility.Visible;
                AvatarPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private async void PickAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var buf   = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var bytes = buf.ToArray();
            var b64   = Convert.ToBase64String(bytes);

            var up = DataManager.Data.UserProfile;
            up.AvatarBase64   = b64;
            up.AvatarMimeType = file.ContentType;
            await DataManager.SaveAsync();

            await LoadAvatarFromBase64(b64);
        }

        private async void ClearAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            var up = DataManager.Data.UserProfile;
            up.AvatarBase64   = null;
            up.AvatarMimeType = null;
            await DataManager.SaveAsync();

            AvatarImage.Source           = null;
            AvatarImage.Visibility       = Visibility.Collapsed;
            AvatarPlaceholder.Visibility = Visibility.Visible;
        }

        private async void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            DataManager.Data.UserProfile.Name = NameBox.Text.Trim();
            await DataManager.SaveAsync();
            UpdatePreview();
        }

        private async void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            DataManager.Data.UserProfile.Description = DescriptionBox.Text.Trim();
            await DataManager.SaveAsync();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var up = DataManager.Data.UserProfile;
            if (string.IsNullOrWhiteSpace(up.Name) && string.IsNullOrWhiteSpace(up.Description))
            {
                PreviewText.Text = AppSettings.S("（填写名字或简介后显示）", "(Shown after entering name or bio)");
                return;
            }
            var sb = new System.Text.StringBuilder("[User] ");
            if (!string.IsNullOrEmpty(up.Name)) sb.Append(up.Name);
            if (!string.IsNullOrEmpty(up.Description))
            {
                sb.Append(": ");
                sb.Append(up.Description);
            }
            PreviewText.Text = sb.ToString();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }
    }
}
