using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeUserPage : Page
    {
        public OobeUserPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            var up = DataManager.Data.UserProfile;
            NameBox.Text = up?.Name ?? "";
            DescBox.Text = up?.Description ?? "";
            if (up?.HasAvatar == true)
            {
                try
                {
                    var bytes = Convert.FromBase64String(up.AvatarBase64);
                    var bmp = AppSettings.LoadBitmapSync(bytes);
                    AvatarImage.Source = bmp;
                    AvatarImage.Visibility = Visibility.Visible;
                    AvatarPlaceholder.Visibility = Visibility.Collapsed;
                }
                catch { }
            }

            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            PageTitle.Text    = AppSettings.S("用户资料", "User Profile");
            PageStepHint.Text = AppSettings.S("步骤 5 / 8  ·  可跳过", "Step 5 / 8  ·  Optional");
            DescText.Text = AppSettings.S("设置你的名字和人设，AI 会更了解你。填写后会注入到每次对话的 System Prompt 中。",
                "Set your name and persona so the AI knows you better. Injected into every conversation's System Prompt.");
            PickAvatarBtn.Content = AppSettings.S("选择头像", "Choose Avatar");
            NameLabel.Text   = AppSettings.S("名字", "Name");
            DescLabel.Text   = AppSettings.S("人设简介", "Bio");
            SkipBtn.Label  = AppSettings.S("跳过", "Skip");
            StepLabel.Text   = AppSettings.S("5/8", "5/8");
        }

        private async void PickAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var buf = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var b64 = Convert.ToBase64String(buf.ToArray());
            DataManager.Data.UserProfile.AvatarBase64   = b64;
            DataManager.Data.UserProfile.AvatarMimeType = file.ContentType;
            var bmp2 = AppSettings.LoadBitmapSync(buf.ToArray());
            AvatarImage.Source = bmp2;
            AvatarImage.Visibility = Visibility.Visible;
            AvatarPlaceholder.Visibility = Visibility.Collapsed;
        }

        private async void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            var up = DataManager.Data.UserProfile;
            up.Name        = NameBox.Text.Trim();
            up.Description = DescBox.Text.Trim();

            // 确保同步写入 UserProfiles 列表并设为激活状态，当次立即生效 (P1-22)
            if (DataManager.Data.UserProfiles == null)
                DataManager.Data.UserProfiles = new List<UserProfile>();
            
            var existing = DataManager.Data.UserProfiles.Find(u => u.Id == up.Id);
            if (existing == null)
            {
                DataManager.Data.UserProfiles.Add(up);
            }
            else
            {
                existing.Name = up.Name;
                existing.Description = up.Description;
                existing.AvatarBase64 = up.AvatarBase64;
                existing.AvatarMimeType = up.AvatarMimeType;
            }
            DataManager.Data.ActiveUserProfileId = up.Id;

            await DataManager.SaveAsync();
            Frame.Navigate(typeof(OobePrefsPage));
        }

        private void SkipBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobePrefsPage));

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
