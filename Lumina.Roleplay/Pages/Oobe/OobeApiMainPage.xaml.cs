using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeApiMainPage : Page
    {
        private string _editProfileId = null;
        private bool _isOobe = true;
        private bool _loading = true;

        public OobeApiMainPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            _loading = true;

            _editProfileId = e.Parameter as string;
            _isOobe = (_editProfileId == null);

            // 非 OOBE 模式：显示 Role/SubModel/Jailbreak，隐藏进度点
            RolePanel.Visibility = _isOobe ? Visibility.Collapsed : Visibility.Visible;
            ProgressDots.Visibility = _isOobe ? Visibility.Visible : Visibility.Collapsed;

            if (_isOobe)
            {
                // OOBE 新建：预填第一个已有配置（如果有）
                if (DataManager.Data.ApiProfiles.Count > 0)
                {
                    var p = DataManager.Data.ApiProfiles[0];
                    NameBox.Text    = p.Name   ?? "";
                    UrlBox.Text     = p.Url    ?? "";
                    KeyBox.Password = p.ApiKey ?? "";
                    ModelBox.Text   = p.Model  ?? "";
                    ProviderBox.SelectedIndex = p.ProviderType == "claude" ? 1 : 0;
                    VisionToggle.IsOn = p.VisionEnabled;
                }
                else
                {
                    ProviderBox.SelectedIndex = 0;
                    RoleBox.SelectedIndex = 0;
                }
            }
            else if (_editProfileId == "new")
            {
                ProviderBox.SelectedIndex = 0;
                RoleBox.SelectedIndex = 0;
                JbBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                var p = DataManager.Data.ApiProfiles.Find(x => x.Id == _editProfileId);
                if (p != null)
                {
                    NameBox.Text    = p.Name   ?? "";
                    UrlBox.Text     = p.Url    ?? "";
                    KeyBox.Password = p.ApiKey ?? "";
                    ModelBox.Text   = p.Model  ?? "";
                    ProviderBox.SelectedIndex = p.ProviderType == "claude" ? 1 : 0;
                    VisionToggle.IsOn = p.VisionEnabled;
                    RoleBox.SelectedIndex = p.Role == "sub" ? 1 : p.Role == "both" ? 2 : 0;
                    SubModelBox.Text = p.SubModel ?? "";
                    JbToggle.IsOn = p.JailbreakEnabled;
                    JbBox.Text = p.JailbreakPrompt ?? "";
                    JbBox.Visibility = p.JailbreakEnabled ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    ProviderBox.SelectedIndex = 0;
                    RoleBox.SelectedIndex = 0;
                    JbBox.Visibility = Visibility.Collapsed;
                }
            }

            _loading = false;
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            if (_isOobe)
            {
                PageTitle.Text    = AppSettings.S("主智能体 API", "Main Agent API");
                PageStepHint.Text = AppSettings.S("步骤 2 / 8  ·  必须完成", "Step 2 / 8  ·  Required");
                NextBtn.Label   = AppSettings.S("下一步", "Next");
                StepLabel.Text    = AppSettings.S("2/8", "2/8");
            }
            else
            {
                PageTitle.Text    = _editProfileId == "new"
                    ? AppSettings.S("新增 API 配置", "Add API Profile")
                    : AppSettings.S("编辑 API 配置", "Edit API Profile");
                PageStepHint.Text = AppSettings.S("设置 API 连接信息", "Configure API connection");
                NextBtn.Label   = AppSettings.S("保存", "Save");
            }

            DescText.Text = AppSettings.S(
                "言枢通过兼容 OpenAI 格式的 API 与 AI 对话。需要填写 API 地址、密钥和模型名称。",
                "Yanshuai uses OpenAI-compatible APIs. Fill in the URL, API key, and model name.");
            NameLabel.Text    = AppSettings.S("配置名称", "Profile Name");
            UrlLabel.Text     = AppSettings.S("API 地址", "API URL");
            KeyLabel.Text     = AppSettings.S("API Key", "API Key");
            ModelLabel.Text   = AppSettings.S("模型", "Model");
            ProviderLabel.Text = AppSettings.S("提供商类型", "Provider Type");
            ProviderOpenai.Content = AppSettings.S("OpenAI 兼容", "OpenAI Compatible");
            ProviderClaude.Content = "Anthropic Claude";
            VisionLabel.Text  = AppSettings.S("视觉能力（多模态）", "Vision (Multi-modal)");
            RoleLabel.Text    = AppSettings.S("用途", "Role");
            ((ComboBoxItem)RoleBox.Items[0]).Content = AppSettings.S("主模型（对话）", "Main (chat)");
            ((ComboBoxItem)RoleBox.Items[1]).Content = AppSettings.S("副模型（记忆/RAG）", "Sub (memory/RAG)");
            ((ComboBoxItem)RoleBox.Items[2]).Content = AppSettings.S("通用（主+副）", "Both");
            SubModelLabel.Text = AppSettings.S("副模型（可选）", "Sub-Model (optional)");
            SubModelBox.PlaceholderText = AppSettings.S("例：gpt-4o-mini / llama-3.1-8b", "e.g. gpt-4o-mini");
            SubModelHint.Text = AppSettings.S(
                "用于记忆总结、RAG 嵌入等辅助任务，留空则复用主模型",
                "Used for memory summarization and RAG. Leave blank to reuse the main model.");
            JbLabel.Text      = AppSettings.S("破墙（可选）", "Jailbreak");
            JbEnableLabel.Text = AppSettings.S("启用", "Enable");
            JbBox.PlaceholderText = AppSettings.S("破墙提示词", "Jailbreak Prompt");
            TestBtn.Content   = AppSettings.S("测试连接", "Test Connection");
        }

        private void JbToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            JbBox.Visibility = JbToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            string name  = NameBox.Text.Trim();
            string url   = UrlBox.Text.Trim();
            string key   = KeyBox.Password;
            string model = ModelBox.Text.Trim();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
            {
                ErrorText.Text = AppSettings.S("请填写 API 地址、密钥和模型名称",
                    "Please fill in API URL, Key, and Model");
                return;
            }
            ErrorText.Text = "";

            string provider = ProviderBox.SelectedIndex == 1 ? "claude" : "openai";
            string role = RoleBox.SelectedIndex == 1 ? "sub" : RoleBox.SelectedIndex == 2 ? "both" : "main";

            if (!_isOobe && _editProfileId != "new")
            {
                // 编辑已有配置
                var profile = DataManager.Data.ApiProfiles.Find(x => x.Id == _editProfileId);
                if (profile != null)
                {
                    profile.Name   = string.IsNullOrEmpty(name) ? AppSettings.S("默认 API", "Default API") : name;
                    profile.Url    = url;
                    profile.ApiKey = key;
                    profile.Model  = model;
                    profile.ProviderType  = provider;
                    profile.VisionEnabled = VisionToggle.IsOn;
                    profile.Role          = role;
                    profile.SubModel      = SubModelBox.Text.Trim();
                    profile.JailbreakEnabled = JbToggle.IsOn;
                    profile.JailbreakPrompt  = JbBox.Text.Trim();
                }
            }
            else
            {
                // 新建配置（OOBE 或 "new"）
                var profile = new ApiProfile
                {
                    Name   = string.IsNullOrEmpty(name) ? AppSettings.S("默认 API", "Default API") : name,
                    Url    = url,
                    ApiKey = key,
                    Model  = model,
                    ProviderType  = provider,
                    VisionEnabled = VisionToggle.IsOn,
                    Role          = _isOobe ? "main" : role,
                    SubModel      = _isOobe ? "" : SubModelBox.Text.Trim(),
                    JailbreakEnabled = _isOobe ? false : JbToggle.IsOn,
                    JailbreakPrompt  = _isOobe ? "" : JbBox.Text.Trim(),
                };
                DataManager.Data.ApiProfiles.Add(profile);
                if (string.IsNullOrEmpty(AppSettings.DefaultApiProfileId))
                    AppSettings.DefaultApiProfileId = profile.Id;
            }

            if (DataManager.Data.ApiProfiles.Count > 0 && string.IsNullOrEmpty(AppSettings.DefaultApiProfileId))
                AppSettings.DefaultApiProfileId = DataManager.Data.ApiProfiles[0].Id;

            await DataManager.SaveAsync();

            if (_isOobe)
                Frame.Navigate(typeof(OobeApiSubPage));
            else
            {
                if (Frame.CanGoBack) Frame.GoBack();
                else Frame.Navigate(typeof(ApiProfilesPage));
            }
        }

        private async void TestBtn_Click(object sender, RoutedEventArgs e)
        {
            string url   = UrlBox.Text.Trim();
            string key   = KeyBox.Password;
            string model = ModelBox.Text.Trim();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
            {
                TestResultText.Text = AppSettings.S("请先填写 API 地址、密钥和模型", "Fill in URL, Key, and Model first");
                TestResultText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Red);
                return;
            }

            TestBtn.IsEnabled = false;
            TestProgress.Visibility = Visibility.Visible;
            TestResultText.Text = "";

            bool ok = await TestConnectionAsync(url, key, model);

            TestProgress.Visibility = Visibility.Collapsed;
            TestBtn.IsEnabled = true;

            if (ok)
            {
                TestResultText.Text = AppSettings.S("✓ 连接成功", "✓ Connection successful");
                TestResultText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Green);
            }
            else
            {
                TestResultText.Text = AppSettings.S("✗ 连接失败，请检查地址和密钥",
                    "✗ Connection failed — check URL and API key");
                TestResultText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Red);
            }
        }

        private async Task<bool> TestConnectionAsync(string url, string key, string model)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    string json = $"{{\"model\":\"{EscapeJson(model)}\",\"stream\":false,\"max_tokens\":1,\"messages\":[{{\"role\":\"user\",\"content\":\"hi\"}}]}}";
                    var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using (var resp = await http.SendAsync(req))
                        return resp.IsSuccessStatusCode;
                }
            }
            catch { return false; }
        }

        // 统一到 Lumina.Core/AI/ChatJson（原本地副本漏 \t 与控制字符转义——已修复）
        private static string EscapeJson(string s) => ChatJson.EscapeJson(s);

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
