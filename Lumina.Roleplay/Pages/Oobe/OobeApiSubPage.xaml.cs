using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeApiSubPage : Page
    {
        private bool _loading = true;

        public OobeApiSubPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            AppSettings.ApplyTheme(RootGrid, this);

            UseSubToggle.IsOn = AppSettings.UseSubAgent;
            SubUrlBox.Text    = AppSettings.SubAgentUrl;
            SubKeyBox.Password = AppSettings.SubAgentApiKey;
            SubModelBox.Text  = AppSettings.SubAgentModel;
            SubProviderBox.SelectedIndex = AppSettings.SubAgentProviderType == "claude" ? 1 : 0;
            SubConfigPanel.Visibility = UseSubToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

            ApplyLanguage();
            _loading = false;
        }

        private void ApplyLanguage()
        {
            PageTitle.Text    = AppSettings.S("子智能体 API", "Sub-Agent API");
            PageStepHint.Text = AppSettings.S("步骤 3 / 8  ·  可跳过", "Step 3 / 8  ·  Optional");
            DescText.Text = AppSettings.S("子智能体用于记忆嵌入、对话摘要、角色卡补全等辅助任务，可使用更轻量的模型。",
                "The sub-agent handles embedding, summarization, and card completion. A lightweight model works well here.");
            UseSubLabel.Text = AppSettings.S("使用独立子智能体", "Use Separate Sub-Agent");
            UseSubDesc.Text  = AppSettings.S("关闭则复用主智能体 API 配置", "Off = reuse the main API profile");
            SubUrlLabel.Text = AppSettings.S("API 地址", "API URL");
            SubKeyLabel.Text = AppSettings.S("API Key", "API Key");
            SubModelLabel.Text = AppSettings.S("模型", "Model");
            SubProviderLabel.Text = AppSettings.S("提供商类型", "Provider Type");
            SubProviderOpenai.Content = AppSettings.S("OpenAI 兼容", "OpenAI Compatible");
            SubProviderClaude.Content = "Anthropic Claude";
            NextBtn.Label  = AppSettings.S("下一步", "Next");
            StepLabel.Text   = AppSettings.S("3/8", "3/8");
        }

        private void UseSubToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SubConfigPanel.Visibility = UseSubToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.UseSubAgent = UseSubToggle.IsOn;
            if (UseSubToggle.IsOn)
            {
                AppSettings.SubAgentUrl  = SubUrlBox.Text.Trim();
                AppSettings.SubAgentApiKey = SubKeyBox.Password;
                AppSettings.SubAgentModel  = SubModelBox.Text.Trim();
                AppSettings.SubAgentProviderType =
                    SubProviderBox.SelectedIndex == 1 ? "claude" : "openai";
            }
            Frame.Navigate(typeof(OobeCharaPage));
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
