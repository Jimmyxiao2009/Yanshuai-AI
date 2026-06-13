using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class CharaWizardPage3 : Page
    {
        public CharaWizardPage3() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
            if (!string.IsNullOrEmpty(CharaWizardData.EditingCardId))
                PageTitleBlock.Text = AppSettings.S("编辑角色卡", "Edit Character Card");
            SystemPromptBox.Text = CharaWizardData.SystemPrompt;
            PostHistoryBox.Text  = CharaWizardData.PostHistoryInstructions;
            MesExampleBox.Text   = CharaWizardData.MesExample;
            CreatorNotesBox.Text = CharaWizardData.CreatorNotes;
            TagsBox.Text         = CharaWizardData.Tags;
            CreatorBox.Text      = CharaWizardData.Creator;
            VersionBox.Text      = CharaWizardData.CharacterVersion;
        }

        private void NextBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            CharaWizardData.SystemPrompt            = SystemPromptBox.Text.Trim();
            CharaWizardData.PostHistoryInstructions = PostHistoryBox.Text.Trim();
            CharaWizardData.MesExample              = MesExampleBox.Text.Trim();
            CharaWizardData.CreatorNotes            = CreatorNotesBox.Text.Trim();
            CharaWizardData.Tags                    = TagsBox.Text.Trim();
            CharaWizardData.Creator                 = CreatorBox.Text.Trim();
            CharaWizardData.CharacterVersion        = VersionBox.Text.Trim();
            Frame.Navigate(typeof(CharaWizardPage4));
        }

        private void ApplyLanguage()
        {
            PageTitleBlock.Text = AppSettings.S("新建角色卡", "New Character Card");
            StepText.Text = AppSettings.S("第 3 步 / 共 4 步  ·  提示词与附加信息", "Step 3/4 · Prompt & Extras");
            SysPromptLabel.Text = AppSettings.S("系统提示词（可选）", "System Prompt (optional)");
            SysPromptHint.Text = AppSettings.S("角色专属系统级指令，注入到对话开头。可留空。", "Character-specific system instructions, injected at the start. Can be left empty.");
            SystemPromptBox.PlaceholderText = AppSettings.S("例：始终以第一人称回答，不要跳出角色…", "e.g. Always answer in first person, stay in character…");
            PostHistoryLabel.Text = AppSettings.S("历史后注入（可选）", "Post-History Injection (optional)");
            PostHistoryHint.Text = AppSettings.S("每次发送时追加到消息列表末尾的指令，用于强化角色扮演。", "Appended after each message to reinforce roleplay behavior.");
            PostHistoryBox.PlaceholderText = AppSettings.S("例：[以上是对话记录，请继续扮演角色]", "e.g. [Continue roleplaying as the character]");
            MesExampleLabel.Text = AppSettings.S("示例对话（可选）", "Example Dialogue (optional)");
            MesExampleHint.Text = AppSettings.S("用于展示角色说话风格的对话示例，格式参考 SillyTavern 规范。", "Example dialogues showing speech style, following SillyTavern format.");
            MesExampleBox.PlaceholderText = AppSettings.S("例：\n<START>\n{{user}}: 你好\n{{char}}: （微微颔首）有何贵干。", "e.g.\n<START>\n{{user}}: Hello\n{{char}}: (nods) What brings you here?");
            ExtraLabel.Text = AppSettings.S("作者备注 / 标签 / 版本（可选）", "Creator Notes / Tags / Version (optional)");
            CreatorNotesBox.Header = AppSettings.S("作者备注", "Creator Notes");
            TagsBox.Header = AppSettings.S("标签（逗号分隔）", "Tags (comma-separated)");
            TagsBox.PlaceholderText = AppSettings.S("例：古风, 武侠, 恋爱", "e.g. historical, wuxia, romance");
            CreatorBox.Header = AppSettings.S("作者", "Creator");
            VersionBox.Header = AppSettings.S("版本", "Version");
            VersionBox.PlaceholderText = AppSettings.S("例：1.0", "e.g. 1.0");
            NextBtn.Label = AppSettings.S("下一步", "Next");
        }

        private void BackButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
