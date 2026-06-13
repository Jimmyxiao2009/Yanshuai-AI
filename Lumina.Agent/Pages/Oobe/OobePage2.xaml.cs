using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobePage2 : Page
    {
        public OobePage2() { InitializeComponent(); }

        private string _editProfileId = null;
        private bool _isOobe = true;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            _editProfileId = e.Parameter as string;
            _isOobe = (_editProfileId == null);

            // 标题
            if (!_isOobe)
            {
                PageTitle.Text    = _editProfileId == "new" ? "新建 API 配置" : "编辑 API 配置";
                PageStepHint.Text = "";
                ProgressHint.Visibility = Visibility.Collapsed;
            }

            // 填充预设
            LoadPresets();

            // 填充数据
            if (_editProfileId == "new" || _editProfileId == null)
            {
                if (_isOobe && DataManager.Data.ApiProfiles.Count > 0)
                {
                    var p = DataManager.Data.ApiProfiles[0];
                    FillFromProfile(p);
                }
            }
            else
            {
                var p = DataManager.Data.ApiProfiles.FirstOrDefault(x => x.Id == _editProfileId);
                if (p != null) FillFromProfile(p);
            }
        }

        private void FillFromProfile(ApiProfile p)
        {
            NameBox.Text        = p.Name   ?? "";
            UrlBox.Text         = p.Url    ?? "";
            KeyBox.Password     = p.ApiKey ?? "";
            ModelBox.Text       = p.Model  ?? "";
            ProviderPicker.SelectedIndex = p.ProviderType == "claude" ? 1 : 0;
            VisionToggle.IsOn   = p.VisionEnabled;
            ContextBox.Text     = p.MaxContextTokens > 0 ? p.MaxContextTokens.ToString() : "";
            JailbreakToggle.IsOn = p.JailbreakEnabled;
            JailbreakBox.Text   = p.JailbreakPrompt ?? "";

            if (p.AvailableModels != null && p.AvailableModels.Count > 0)
            {
                ModelCombo.Items.Clear();
                foreach (var m in p.AvailableModels) ModelCombo.Items.Add(m);
                int idx = p.AvailableModels.IndexOf(p.Model);
                if (idx >= 0) ModelCombo.SelectedIndex = idx;
                ModelCombo.Visibility = Visibility.Visible;
            }
        }

        // ── 预设 ───────────────────────────────────────────────────────────────

        private void LoadPresets()
        {
            PresetPicker.Items.Clear();
            PresetPicker.Items.Add(new ComboBoxItem { Content = "自定义（手动填写）", Tag = null });
            foreach (var preset in ApiProfile.GetPresets())
                PresetPicker.Items.Add(new ComboBoxItem { Content = $"{preset.Name}  ({preset.Model})", Tag = preset });
            PresetPicker.SelectedIndex = 0;
        }

        private void PresetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = (PresetPicker.SelectedItem as ComboBoxItem)?.Tag;
            if (tag == null)
            {
                // "自定义" — 不覆盖已填写内容
                return;
            }
            if (tag is ApiProfile preset)
            {
                NameBox.Text    = preset.Name;
                UrlBox.Text     = preset.Url;
                ModelBox.Text   = preset.Model;
                ProviderPicker.SelectedIndex = preset.ProviderType == "claude" ? 1 : 0;
                VisionToggle.IsOn = preset.VisionEnabled;
                ContextBox.Text = preset.MaxContextTokens > 0 ? preset.MaxContextTokens.ToString() : "";
            }
        }

        // ── 模型检测 ───────────────────────────────────────────────────────────

        private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelCombo.SelectedItem is string sel && !string.IsNullOrEmpty(sel))
                ModelBox.Text = sel;
        }

        private async void FetchModelsBtn_Click(object sender, RoutedEventArgs e)
        {
            FetchModelsBtn.IsEnabled = false;
            StatusLabel.Text = "正在获取模型列表…";
            var tempProfile = new ApiProfile
            {
                Url = UrlBox.Text.Trim(),
                ApiKey = KeyBox.Password,
                ProviderType = ProviderPicker.SelectedIndex == 1 ? "claude" : "openai"
            };
            var models = await ApiModelFetcher.FetchModelsAsync(tempProfile);
            if (models != null && models.Count > 0)
            {
                ModelCombo.Items.Clear();
                foreach (var m in models) ModelCombo.Items.Add(m.DisplayLabel);
                ModelCombo.Visibility = Visibility.Visible;
                StatusLabel.Text = $"找到 {models.Count} 个模型";
            }
            else
            {
                StatusLabel.Text = "获取失败（检查 URL 和 Key）";
            }
            FetchModelsBtn.IsEnabled = true;
        }

        private async void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionBtn.IsEnabled = false;
            StatusLabel.Text = "测试中…";
            var tempProfile = new ApiProfile
            {
                Url = UrlBox.Text.Trim(), ApiKey = KeyBox.Password,
                Model = ModelBox.Text.Trim(),
                ProviderType = ProviderPicker.SelectedIndex == 1 ? "claude" : "openai"
            };
            StatusLabel.Text = await ApiModelFetcher.TestConnectionAsync(tempProfile);
            TestConnectionBtn.IsEnabled = true;
        }

        // ── 保存 ──────────────────────────────────────────────────────────────

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

            string provider  = ProviderPicker.SelectedIndex == 1 ? "claude" : "openai";
            bool   vision    = VisionToggle.IsOn;
            int    ctx       = 0;
            int.TryParse(ContextBox.Text.Trim(), out ctx);
            bool   jailbreak = JailbreakToggle.IsOn;
            string jbPrompt  = JailbreakBox.Text.Trim();

            if (_editProfileId != null && _editProfileId != "new")
            {
                var profile = DataManager.Data.ApiProfiles.Find(x => x.Id == _editProfileId);
                if (profile != null)
                {
                    profile.Name             = string.IsNullOrEmpty(name) ? "默认 API" : name;
                    profile.Url              = url;
                    profile.ApiKey           = key;
                    profile.Model            = model;
                    profile.ProviderType     = provider;
                    profile.VisionEnabled    = vision;
                    profile.MaxContextTokens = ctx;
                    profile.JailbreakEnabled = jailbreak;
                    profile.JailbreakPrompt  = jbPrompt;
                }
            }
            else
            {
                var profile = new ApiProfile
                {
                    Name             = string.IsNullOrEmpty(name) ? "默认 API" : name,
                    Url              = url,
                    ApiKey           = key,
                    Model            = model,
                    ProviderType     = provider,
                    VisionEnabled    = vision,
                    MaxContextTokens = ctx,
                    JailbreakEnabled = jailbreak,
                    JailbreakPrompt  = jbPrompt,
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
                AppSettings.OobeCompleted = true;
                Frame.Navigate(typeof(MainPage));
            }
            else
            {
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
