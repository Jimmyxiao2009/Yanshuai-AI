using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class ApiProfileEditPage : Page
    {
        private ApiProfile _profile;
        private bool _isNew;
        private bool _loading = true;

        public ApiProfileEditPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            _loading = true;

            string profileId = e.Parameter as string;
            _isNew = string.IsNullOrEmpty(profileId);
            _profile = _isNew ? new ApiProfile() : DataManager.Data.ApiProfiles.FirstOrDefault(p => p.Id == profileId) ?? new ApiProfile();

            PageTitle.Text = AppSettings.S(_isNew ? "新增 API 配置" : "编辑 API 配置",
                                           _isNew ? "Add API Profile" : "Edit API Profile");

            NameBox.Text   = _profile.Name;
            UrlBox.Text    = _profile.Url;
            KeyBox.Password = _profile.ApiKey ?? "";
            ModelBox.Text  = _profile.Model;
            ProviderBox.SelectedIndex = _profile.ProviderType == "claude" ? 1 : 0;
            VisionToggle.IsOn = _profile.VisionEnabled;
            RoleBox.SelectedIndex = _profile.Role == "sub" ? 1 : _profile.Role == "both" ? 2 : 0;
            SubModelBox.Text = _profile.SubModel ?? "";
            JbToggle.IsOn  = _profile.JailbreakEnabled;
            JbBox.Text     = _profile.JailbreakPrompt ?? "";
            JbBox.Visibility = _profile.JailbreakEnabled ? Visibility.Visible : Visibility.Collapsed;

            ApplyLanguage();
            _loading = false;
        }

        private void ApplyLanguage()
        {
            PageStepHint.Text = AppSettings.S("设置 API 连接信息", "Configure API connection");
            NameLabel.Text = AppSettings.S("配置名称", "Profile Name");
            NameBox.PlaceholderText = AppSettings.S("例：我的 API", "e.g. My API");
            UrlLabel.Text = AppSettings.S("API 地址", "API URL");
            KeyLabel.Text = "API Key";
            ModelLabel.Text = AppSettings.S("模型", "Model");
            ProviderLabel.Text = AppSettings.S("提供商类型", "Provider Type");
            ((ComboBoxItem)ProviderBox.Items[0]).Content = AppSettings.S("OpenAI 兼容", "OpenAI Compatible");
            ((ComboBoxItem)ProviderBox.Items[1]).Content = "Anthropic Claude";
            VisionLabel.Text = AppSettings.S("视觉能力（多模态）", "Vision / Image Input");
            RoleLabel.Text = AppSettings.S("用途", "Role");
            ((ComboBoxItem)RoleBox.Items[0]).Content = AppSettings.S("主模型（对话）", "Main (chat)");
            ((ComboBoxItem)RoleBox.Items[1]).Content = AppSettings.S("副模型（记忆/RAG）", "Sub (memory/RAG)");
            ((ComboBoxItem)RoleBox.Items[2]).Content = AppSettings.S("通用（主+副）", "Both");
            SubModelLabel.Text = AppSettings.S("副模型（可选）", "Sub-Model (optional)");
            SubModelBox.PlaceholderText = AppSettings.S("例：gpt-4o-mini / llama-3.1-8b", "e.g. gpt-4o-mini");
            SubModelHint.Text = AppSettings.S("用于记忆总结、RAG 嵌入等辅助任务，留空则复用主模型",
                "Used for memory summarization and RAG. Leave blank to reuse the main model.");
            JbLabel.Text = AppSettings.S("破墙（可选）", "Jailbreak");
            JbEnableLabel.Text = AppSettings.S("启用", "Enable");
            JbBox.PlaceholderText = AppSettings.S("破墙提示词", "Jailbreak Prompt");
            SaveBtn.Label = AppSettings.S("保存", "Save");
        }

        private void JbToggle_Toggled(object sender, RoutedEventArgs e)
        {
            JbBox.Visibility = JbToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string name  = NameBox.Text.Trim();
            string url   = UrlBox.Text.Trim();
            string model = ModelBox.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(model))
            {
                var dlg = new ContentDialog
                {
                    Title = AppSettings.S("无法保存", "Cannot Save"),
                    Content = AppSettings.S("配置名称、API 地址和模型不能为空。", "Name, URL and Model are required."),
                    CloseButtonText = AppSettings.S("确定", "OK"),
                    RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
                };
                await dlg.ShowAsync().AsTask();
                return;
            }

            _profile.Name = name;
            _profile.Url = url;
            _profile.ApiKey = KeyBox.Password;
            _profile.Model = model;
            _profile.ProviderType = ProviderBox.SelectedIndex == 1 ? "claude" : "openai";
            _profile.VisionEnabled = VisionToggle.IsOn;
            _profile.Role = RoleBox.SelectedIndex == 1 ? "sub" : RoleBox.SelectedIndex == 2 ? "both" : "main";
            _profile.SubModel = SubModelBox.Text.Trim();
            _profile.JailbreakEnabled = JbToggle.IsOn;
            _profile.JailbreakPrompt = JbBox.Text.Trim();

            if (_isNew)
                DataManager.Data.ApiProfiles.Add(_profile);

            _ = DataManager.SaveAsync();
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
