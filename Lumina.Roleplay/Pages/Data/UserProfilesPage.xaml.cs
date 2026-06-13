using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    internal class UserProfileViewModel
    {
        private readonly UserProfile _p;
        private readonly bool _isActive;
        public UserProfile Profile => _p;

        public UserProfileViewModel(UserProfile p, bool isActive) { _p = p; _isActive = isActive; }

        public string DisplayName  => string.IsNullOrEmpty(_p.Name) ? AppSettings.S("（未命名）", "(Unnamed)") : _p.Name;
        public string Description  => _p.Description ?? "";
        public Visibility AvatarVisibility => _p.HasAvatar ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActiveVisibility => _isActive ? Visibility.Visible : Visibility.Collapsed;
        public string ActiveLabel => _isActive ? AppSettings.S("当前", "Current") : "";

        private Windows.UI.Xaml.Media.ImageSource _src;
        public Windows.UI.Xaml.Media.ImageSource AvatarSource
        {
            get
            {
                if (_src != null) return _src;
                if (!_p.HasAvatar) return null;
                try
                {
                    var bytes = Convert.FromBase64String(_p.AvatarBase64);
                    var bmp = AppSettings.LoadBitmapSync(bytes);
                    _src = bmp;
                }
                catch { }
                return _src;
            }
        }
    }

    public sealed partial class UserProfilesPage : Page
    {
        public UserProfilesPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
            RefreshList();
        }

        private void ApplyLanguage()
        {
            PageTitle.Text   = AppSettings.S("用户资料", "User Profile");
            PageSubtitle.Text = AppSettings.S("选中资料后点击使用，即可切换当前用户身份", "Select a profile and tap Use to switch");
            UseBtn.Label     = AppSettings.S("使用此资料", "Use This Profile");
            EditBtn.Label    = AppSettings.S("编辑", "Edit");
            DeleteBtn.Label  = AppSettings.S("删除", "Delete");
            AddBtn.Label     = AppSettings.S("新增", "Add");
            // ActiveLabel is set dynamically in RefreshList
        }

        private void RefreshList()
        {
            // 确保列表非空
            if (DataManager.Data.UserProfiles == null)
                DataManager.Data.UserProfiles = new System.Collections.Generic.List<UserProfile>();
            if (DataManager.Data.UserProfiles.Count == 0)
            {
                // 把旧的单一UserProfile迁移进来
                var old = DataManager.Data.UserProfile ?? new UserProfile();
                DataManager.Data.UserProfiles.Add(old);
                DataManager.Data.ActiveUserProfileId = old.Id;
            }

            string activeId = DataManager.Data.ActiveUserProfileId;
            ProfilesList.ItemsSource = null;
            ProfilesList.ItemsSource = DataManager.Data.UserProfiles
                .Select(p => new UserProfileViewModel(p, p.Id == activeId))
                .ToList();

            var active = DataManager.GetActiveUserProfile();
            string prefix = AppSettings.S("当前：", "Current: ");
            string fallback = AppSettings.S("（未命名）", "(Unnamed)");
            ActiveLabel.Text = prefix + (string.IsNullOrEmpty(active?.Name) ? fallback : active.Name);

            UseBtn.IsEnabled = DeleteBtn.IsEnabled = EditBtn.IsEnabled = false;
        }

        private UserProfile SelectedProfile =>
            (ProfilesList.SelectedItem as UserProfileViewModel)?.Profile;

        private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool has = SelectedProfile != null;
            UseBtn.IsEnabled   = has;
            EditBtn.IsEnabled  = has;
            DeleteBtn.IsEnabled = has && DataManager.Data.UserProfiles.Count > 1;
        }

        private async void UseBtn_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProfile;
            if (p == null) return;
            DataManager.Data.ActiveUserProfileId = p.Id;
            DataManager.Data.UserProfile = p; // 同步旧字段
            await DataManager.SaveAsync();
            RefreshList();
        }

        private async void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            var profile = new UserProfile();
            if (await ShowEditDialog(profile))
            {
                DataManager.Data.UserProfiles.Add(profile);
                await DataManager.SaveAsync();
                RefreshList();
            }
        }

        private async void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProfile;
            if (p == null) return;
            if (await ShowEditDialog(p))
            {
                // 如果是当前活跃的，同步旧字段
                if (p.Id == DataManager.Data.ActiveUserProfileId)
                    DataManager.Data.UserProfile = p;
                await DataManager.SaveAsync();
                RefreshList();
            }
        }

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProfile;
            if (p == null || DataManager.Data.UserProfiles.Count <= 1) return;
            var dialog = new ContentDialog
            {
                Title = AppSettings.S("删除资料", "Delete Profile"),
                Content = string.Format(AppSettings.S("确定删除「{0}」？", "Delete「{0}」?"), p.Name),
                PrimaryButtonText = AppSettings.S("删除", "Delete"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await dialog.ShowAsync().AsTask() != ContentDialogResult.Primary) return;
            DataManager.Data.UserProfiles.Remove(p);
            if (DataManager.Data.ActiveUserProfileId == p.Id)
            {
                DataManager.Data.ActiveUserProfileId = DataManager.Data.UserProfiles[0].Id;
                DataManager.Data.UserProfile = DataManager.Data.UserProfiles[0];
            }
            await DataManager.SaveAsync();
            RefreshList();
        }

        private async System.Threading.Tasks.Task<bool> ShowEditDialog(UserProfile profile)
        {
            var nameBox = new TextBox { Header = AppSettings.S("名字", "Name"), Text = profile.Name ?? "",
                FontSize = 15, Padding = new Thickness(10, 10, 10, 10), Margin = new Thickness(0,0,0,12) };
            var descBox = new TextBox { Header = AppSettings.S("人设简介", "Character Bio"), Text = profile.Description ?? "",
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontSize = 15, Padding = new Thickness(10, 10, 10, 10), Height = 100,
                Margin = new Thickness(0,0,0,12) };

            // 头像选择
            string pendingB64 = profile.AvatarBase64;
            string pendingMime = profile.AvatarMimeType;
            var avatarImg = new Windows.UI.Xaml.Controls.Image
                { Width=56, Height=56, Stretch=Windows.UI.Xaml.Media.Stretch.UniformToFill, Margin=new Thickness(0,0,12,0) };
            if (profile.HasAvatar) { try { var bytes=Convert.FromBase64String(profile.AvatarBase64); var bmp=new BitmapImage(); using(var ms=new Windows.Storage.Streams.InMemoryRandomAccessStream()){ ms.WriteAsync(bytes.AsBuffer()).AsTask().Wait(); ms.Seek(0); bmp.SetSource(ms); } avatarImg.Source=bmp; } catch{} }
            var pickBtn = new Button { Content=AppSettings.S("选择头像", "Choose Avatar"), Padding=new Thickness(14,8,14,8), VerticalAlignment=VerticalAlignment.Center };
            pickBtn.Click += async (s,ev) => {
                var pk = new Windows.Storage.Pickers.FileOpenPicker();
                pk.SuggestedStartLocation=Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                pk.FileTypeFilter.Add(".jpg"); pk.FileTypeFilter.Add(".jpeg"); pk.FileTypeFilter.Add(".png");
                var f=await pk.PickSingleFileAsync(); if(f==null)return;
                var buf=await Windows.Storage.FileIO.ReadBufferAsync(f);
                pendingB64=Convert.ToBase64String(buf.ToArray()); pendingMime=f.ContentType;
                var bytes2=buf.ToArray(); var bmp2=new BitmapImage();
                using(var ms2=new Windows.Storage.Streams.InMemoryRandomAccessStream()){ await ms2.WriteAsync(bytes2.AsBuffer()); ms2.Seek(0); await bmp2.SetSourceAsync(ms2); }
                avatarImg.Source=bmp2;
            };
            var avatarRow = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,0,0,12) };
            avatarRow.Children.Add(avatarImg); avatarRow.Children.Add(pickBtn);

            var panel = new StackPanel { Width = 300 };
            panel.Children.Add(avatarRow);
            panel.Children.Add(nameBox);
            panel.Children.Add(descBox);

            var dlg = new ContentDialog
            {
                Title = string.IsNullOrEmpty(profile.Name)
                    ? AppSettings.S("新建用户资料", "New Profile")
                    : AppSettings.S("编辑用户资料", "Edit Profile"),
                Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
                PrimaryButtonText = AppSettings.S("保存", "Save"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await dlg.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                profile.Name = nameBox.Text.Trim();
                profile.Description = descBox.Text.Trim();
                profile.AvatarBase64 = pendingB64;
                profile.AvatarMimeType = pendingMime;
                return true;
            }
            return false;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }
    }
}
