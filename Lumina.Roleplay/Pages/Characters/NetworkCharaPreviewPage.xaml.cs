using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class NetworkCharaPreviewPage : Page
    {
        private CharacterCard _card;

        public NetworkCharaPreviewPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            _card = e.Parameter as CharacterCard;
            if (_card == null)
            {
                if (Frame.CanGoBack) Frame.GoBack();
                return;
            }

            PopulateFields();
            LoadAvatarAsync();
            PopulateGroupCombo();
        }

        private void PopulateFields()
        {
            NameText.Text = _card.Name ?? "";
            CreatorText.Text = !string.IsNullOrEmpty(_card.Creator)
                ? AppSettings.S("作者：" + _card.Creator, "By " + _card.Creator)
                : "";
            TagsBox.Text = _card.Tags ?? "";
            FirstMsgBox.Text = _card.FirstMessage ?? "";
            DescBox.Text = _card.Description ?? "";
            PersonalityBox.Text = _card.Personality ?? "";
            ScenarioBox.Text = _card.Scenario ?? "";
            SysPromptBox.Text = _card.SystemPrompt ?? "";
        }

        private async void LoadAvatarAsync()
        {
            if (!_card.HasAvatar) return;
            try
            {
                byte[] bytes = Convert.FromBase64String(_card.AvatarBase64);
                var bmp = new BitmapImage();
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    var dw = new Windows.Storage.Streams.DataWriter(ms);
                    dw.WriteBytes(bytes);
                    await dw.StoreAsync();
                    await ms.FlushAsync();
                    ms.Seek(0);
                    await bmp.SetSourceAsync(ms);
                }
                AvatarBorder.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
                AvatarPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void PopulateGroupCombo()
        {
            var existingGroups = DataManager.Data.CharacterCards
                .Select(c => c.GroupName)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct().OrderBy(g => g).ToList();

            GroupCombo.Items.Clear();
            GroupCombo.Items.Add(AppSettings.S("（未分组）", "(No group)"));
            foreach (var g in existingGroups)
                GroupCombo.Items.Add(g);

            if (!string.IsNullOrEmpty(_card.GroupName))
            {
                int idx = existingGroups.IndexOf(_card.GroupName);
                if (idx >= 0) GroupCombo.SelectedIndex = idx + 1;
                else GroupCombo.SelectedIndex = 0;
            }
            else
            {
                GroupCombo.SelectedIndex = 0;
            }
        }

        private void SyncCardFromUI()
        {
            _card.Tags = TagsBox.Text.Trim();
            _card.FirstMessage = FirstMsgBox.Text.Trim();
            _card.Description = DescBox.Text.Trim();
            _card.Personality = PersonalityBox.Text.Trim();
            _card.Scenario = ScenarioBox.Text.Trim();
            _card.SystemPrompt = SysPromptBox.Text.Trim();
        }

        private void SyncGroupFromUI()
        {
            int selIdx = GroupCombo.SelectedIndex;
            var existingGroups = DataManager.Data.CharacterCards
                .Select(c => c.GroupName)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct().OrderBy(g => g).ToList();

            if (selIdx > 0 && selIdx <= existingGroups.Count)
                _card.GroupName = existingGroups[selIdx - 1];
            else
                _card.GroupName = "";
        }

        private async void AIBtn_Click(object sender, RoutedEventArgs e)
        {
            SyncCardFromUI();
            SetButtonsEnabled(false);
            StatusText.Text = AppSettings.S("AI 补全中…", "AI completing…");

            _card = await CardCompleter.CompleteCardAsync(_card);
            PopulateFields();

            StatusText.Text = AppSettings.S("AI 补全完成", "AI completed");
            SetButtonsEnabled(true);
        }

        private async void AIInstructBtn_Click(object sender, RoutedEventArgs e)
        {
            string instruction = InstructionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(instruction))
            {
                StatusText.Text = AppSettings.S("请先输入指令", "Enter an instruction first");
                return;
            }

            SyncCardFromUI();
            SetButtonsEnabled(false);
            StatusText.Text = AppSettings.S("按指令重生成中…", "Regenerating…");

            _card = await CardCompleter.CompleteCardAsync(_card, instruction);
            PopulateFields();

            StatusText.Text = AppSettings.S("重生成完成", "Regenerated");
            InstructionBox.Text = "";
            SetButtonsEnabled(true);
        }

        private async void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            SyncCardFromUI();
            SyncGroupFromUI();

            DataManager.Data.CharacterCards.Add(_card);
            await DataManager.SaveAsync();

            // 清除本页及旧 NetworkCharaPage，导航到新实例确保信息流刷新
            while (Frame.BackStack.Count > 0)
            {
                var entry = Frame.BackStack[Frame.BackStack.Count - 1];
                if (entry.SourcePageType == typeof(NetworkCharaPreviewPage)
                    || entry.SourcePageType == typeof(NetworkCharaPage))
                    Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
                else
                    break;
            }
            Frame.Navigate(typeof(NetworkCharaPage));
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private void SetButtonsEnabled(bool enabled)
        {
            AIBtn.IsEnabled = enabled;
            AIInstructBtn.IsEnabled = enabled;
            ImportBtn.IsEnabled = enabled;
        }
    }
}
