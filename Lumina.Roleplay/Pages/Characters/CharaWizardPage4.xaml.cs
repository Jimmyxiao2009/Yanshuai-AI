using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class CharaWizardPage4 : Page
    {
        public CharaWizardPage4() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
            if (!string.IsNullOrEmpty(CharaWizardData.EditingCardId))
                PageTitleBlock.Text = AppSettings.S("编辑角色卡", "Edit Character Card");
            FirstMsgBox.Text = CharaWizardData.FirstMessage;
            FirstMsgBox.TextChanged += (s, ev) => UpdatePreview();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            string text = FirstMsgBox.Text.Trim();
            PreviewText.Text = text;
            PreviewPanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void FinishBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.FirstMessage = FirstMsgBox.Text.Trim();

            string editId = CharaWizardData.EditingCardId;
            string target = CharaWizardData.ReturnTarget;

            if (!string.IsNullOrEmpty(editId))
            {
                // 编辑已有卡
                var card = DataManager.Data.CharacterCards.Find(c => c.Id == editId);
                if (card != null)
                {
                    CharaWizardData.ApplyToCard(card);
                    await DataManager.SaveAsync();
                }
                CharaWizardData.Reset();
                Frame.Navigate(typeof(CharacterCardsPage));
                CleanWizardBackStack();
            }
            else
            {
                // 新建
                var card = CharaWizardData.ToCard();
                if (string.IsNullOrWhiteSpace(card.Name))
                    card.Name = "新角色";
                DataManager.Data.CharacterCards.Add(card);
                await DataManager.SaveAsync();
                CharaWizardData.Reset();

                if (target == "OobeCharaPage")
                {
                    Frame.Navigate(typeof(OobeUserPage));
                    CleanWizardBackStack(includeOobeChara: true);
                }
                else
                {
                    Frame.Navigate(typeof(CharacterCardsPage));
                    CleanWizardBackStack();
                }
            }
        }

        private void CleanWizardBackStack(bool includeOobeChara = false)
        {
            var wizardTypes = new System.Collections.Generic.HashSet<System.Type>
            {
                typeof(CharaWizardPage1), typeof(CharaWizardPage2),
                typeof(CharaWizardPage3), typeof(CharaWizardPage4),
            };
            if (includeOobeChara) wizardTypes.Add(typeof(OobeCharaPage));

            while (Frame.BackStack.Count > 0 &&
                   wizardTypes.Contains(Frame.BackStack[Frame.BackStack.Count - 1].SourcePageType))
                Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
        }

        private void ApplyLanguage()
        {
            PageTitleBlock.Text = AppSettings.S("新建角色卡", "New Character Card");
            StepText.Text = AppSettings.S("第 4 步 / 共 4 步  ·  开场白", "Step 4/4 · Opening Line");
            FirstMsgLabel.Text = AppSettings.S("开场白", "Opening Line");
            FirstMsgHint.Text = AppSettings.S("对话开始时角色发送的第一条消息", "The first message the character sends when the conversation starts");
            FirstMsgBox.PlaceholderText = AppSettings.S("例：（抬眸看你一眼，随即收回目光）你找我有何要事？", "e.g. (Lifts gaze to look at you, then looks away) What business do you have with me?");
            FirstMsgNote.Text = AppSettings.S("留空则对话开始时不显示开场白，可随时在角色卡编辑页修改", "Leave empty for no opening line. Can be edited later in the character card.");
            PreviewLabel.Text = AppSettings.S("预览", "Preview");
            FinishBtn.Label = AppSettings.S("完成", "Finish");
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
