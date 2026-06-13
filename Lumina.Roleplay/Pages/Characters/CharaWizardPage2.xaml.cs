using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class CharaWizardPage2 : Page
    {
        public CharaWizardPage2() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
            DescBox.Text        = CharaWizardData.Description;
            PersonalityBox.Text = CharaWizardData.Personality;
            ScenarioBox.Text    = CharaWizardData.Scenario;
        }

        private void ApplyLanguage()
        {
            PageTitle.Text = AppSettings.S("新建角色卡", "New Character Card");
            StepText.Text = AppSettings.S("第 2 步 / 共 4 步  ·  描述与性格", "Step 2/4 · Description & Personality");
            DescLabel.Text = AppSettings.S("描述", "Description");
            DescHint.Text = AppSettings.S("介绍角色的背景、外貌、身份等", "Describe the character's background, appearance, identity, etc.");
            DescBox.PlaceholderText = AppSettings.S("例：一位来自古代的剑客，沉默寡言，却在关键时刻总能挺身而出…", "e.g. A swordsman from ancient times, quiet but always steps up when it matters…");
            PersonalityLabel.Text = AppSettings.S("性格", "Personality");
            PersonalityHint.Text = AppSettings.S("角色的个性特点、说话方式", "Personality traits, speech patterns");
            PersonalityBox.PlaceholderText = AppSettings.S("例：冷静理性，偶尔会流露出温柔，不喜欢拐弯抹角…", "e.g. Calm and rational, occasionally shows tenderness, straightforward…");
            ScenarioLabel.Text = AppSettings.S("场景（可选）", "Scene (optional)");
            ScenarioHint.Text = AppSettings.S("对话发生的背景设定", "The setting where the conversation takes place");
            ScenarioBox.PlaceholderText = AppSettings.S("例：你在一家古朴的茶馆里遇见了他…", "e.g. You meet him in an old-fashioned tea house…");
            NextBtn.Label = AppSettings.S("下一步", "Next");
        }

        private void NextBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            CharaWizardData.Description  = DescBox.Text.Trim();
            CharaWizardData.Personality  = PersonalityBox.Text.Trim();
            CharaWizardData.Scenario     = ScenarioBox.Text.Trim();
            Frame.Navigate(typeof(CharaWizardPage3));
        }

        private void BackButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
