using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobePage2 : Page
    {
        public OobePage2() { InitializeComponent(); }

        private string _editProfileId = null;  // null=OOBE新建, "new"=页面新建, id=编辑
        private bool _isOobe = true;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            _editProfileId = e.Parameter as string;
            _isOobe = (_editProfileId == null);
            if (!_isOobe)
            {
                PageTitle.Text    = _editProfileId == "new" ? "新建 API 配置" : "编辑 API 配置";
                PageStepHint.Text = "";
            }

            if (_editProfileId == "new" || _editProfileId == null)
            {
                // 新建模式
                if (_isOobe && DataManager.Data.ApiProfiles.Count > 0)
                {
                    // OOBE且已有配置则预填
                    var p = DataManager.Data.ApiProfiles[0];
                    NameBox.Text    = p.Name   ?? "";
                    UrlBox.Text     = p.Url    ?? "";
                    KeyBox.Password = p.ApiKey ?? "";
                    ModelBox.Text   = p.Model  ?? "";
                }
            }
            else
            {
                // 编辑已有配置
                var p = DataManager.Data.ApiProfiles.Find(x => x.Id == _editProfileId);
                if (p != null)
                {
                    NameBox.Text    = p.Name   ?? "";
                    UrlBox.Text     = p.Url    ?? "";
                    KeyBox.Password = p.ApiKey ?? "";
                    ModelBox.Text   = p.Model  ?? "";
                }
            }
        }

        private async void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            string name  = NameBox.Text.Trim();
            string url   = UrlBox.Text.Trim();
            string key   = KeyBox.Password;
            string model = ModelBox.Text.Trim();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
            {
                ErrorText.Text = "请填写 API 地址、密钥和模型名称";
                return;
            }
            ErrorText.Text = "";

            // 保存API配置
            if (_editProfileId != null && _editProfileId != "new")
            {
                // 编辑已有
                var profile = DataManager.Data.ApiProfiles.Find(x => x.Id == _editProfileId);
                if (profile != null)
                {
                    profile.Name   = string.IsNullOrEmpty(name) ? "默认 API" : name;
                    profile.Url    = url; profile.ApiKey = key; profile.Model = model;
                }
            }
            else
            {
                // 新建
                var profile = new ApiProfile
                {
                    Name   = string.IsNullOrEmpty(name) ? "默认 API" : name,
                    Url    = url, ApiKey = key, Model  = model,
                };
                DataManager.Data.ApiProfiles.Add(profile);
                if (string.IsNullOrEmpty(AppSettings.DefaultApiProfileId))
                    AppSettings.DefaultApiProfileId = profile.Id;
            }
            if (DataManager.Data.ApiProfiles.Count > 0 && string.IsNullOrEmpty(AppSettings.DefaultApiProfileId))
                AppSettings.DefaultApiProfileId = DataManager.Data.ApiProfiles[0].Id;
            await DataManager.SaveAsync();

            if (_isOobe)
            {
                // OOBE 完成，进入主界面
                AppSettings.OobeCompleted = true;
                Frame.Navigate(typeof(MainPage));
            }
            else
            {
                // 从ApiProfilesPage进来的，返回ApiProfilesPage
                if (Frame.CanGoBack) Frame.GoBack();
                else Frame.Navigate(typeof(ApiProfilesPage));
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
