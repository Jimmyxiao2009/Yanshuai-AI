using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    // ── View-model wrapper so the list can show the ★ default marker ─────────
    internal class ApiProfileItem
    {
        public ApiProfile Profile { get; }
        private readonly bool _isDefault;

        public ApiProfileItem(ApiProfile p, bool isDefault)
        {
            Profile   = p;
            _isDefault = isDefault;
        }

        // Bound in XAML
        public string NameWithDefault => _isDefault ? $"{Profile.Name}  ★" : Profile.Name;
        public string Model => Profile.Model;
        public string Url   => Profile.Url;
    }

    public sealed partial class ApiProfilesPage : Page
    {
        public ApiProfilesPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            RefreshList();
        }

        // ── List refresh ──────────────────────────────────────────────────────

        private void RefreshList(string reSelectId = null)
        {
            var defaultId = AppSettings.DefaultApiProfileId;
            var items = DataManager.Data.ApiProfiles
                .Select(p => new ApiProfileItem(p, p.Id == defaultId))
                .ToList();

            ProfilesList.ItemsSource = null;
            ProfilesList.ItemsSource = items;

            // Re-select the same profile (if requested)
            if (reSelectId != null)
            {
                var item = items.FirstOrDefault(i => i.Profile.Id == reSelectId);
                if (item != null) ProfilesList.SelectedItem = item;
            }
        }

        private ApiProfile SelectedProfile =>
            (ProfilesList.SelectedItem as ApiProfileItem)?.Profile;

        // ── Selection changed ─────────────────────────────────────────────────

        private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var p = SelectedProfile;
            bool has = p != null;
            EditBtn.IsEnabled       = has;
            DeleteBtn.IsEnabled     = has;
            SetDefaultBtn.IsEnabled = has;

            if (has)
                SetDefaultBtn.Label = AppSettings.DefaultApiProfileId == p.Id
                    ? AppSettings.S("已是默认", "Is Default")
                    : AppSettings.S("设为默认", "Set Default");
        }

        // ── Set default ───────────────────────────────────────────────────────

        private void SetDefaultBtn_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProfile;
            if (p == null) return;
            AppSettings.DefaultApiProfileId    = p.Id;
            DataManager.Data.DefaultApiProfileId = p.Id;
            // Also update SelectedApiProfileId so GetProfileForConversation fallback works
            DataManager.Data.SelectedApiProfileId = p.Id;
            _ = DataManager.SaveAsync();
            RefreshList(reSelectId: p.Id);
        }

        // ── Add / Edit / Delete ───────────────────────────────────────────────

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(OobePage2), "new");
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProfile;
            if (p == null) return;
            if (await ShowEditDialog(p, isNew: false))
            {
                await DataManager.SaveAsync();
                RefreshList(reSelectId: p.Id);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProfile;
            if (p == null) return;

            var confirm = new ContentDialog
            {
                Title               = AppSettings.S("删除配置",               "Delete Profile"),
                Content             = AppSettings.S($"确定删除「{p.Name}」？", $"Delete \"{p.Name}\"?"),
                PrimaryButtonText   = AppSettings.S("删除",  "Delete"),
                SecondaryButtonText = AppSettings.S("取消",  "Cancel"),
                RequestedTheme      = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                DataManager.Data.ApiProfiles.Remove(p);
                if (AppSettings.DefaultApiProfileId == p.Id)
                {
                    AppSettings.DefaultApiProfileId = "";
                    DataManager.Data.DefaultApiProfileId = "";
                    DataManager.Data.SelectedApiProfileId = "";
                }
                await DataManager.SaveAsync();
                RefreshList();
            }
        }

        // ── Edit dialog ───────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task<bool> ShowEditDialog(ApiProfile profile, bool isNew)
        {
            var nameBox  = new TextBox  { Header = AppSettings.S("配置名称", "Profile Name"), Text = profile.Name,     Margin = new Thickness(0, 0, 0, 8) };
            var urlBox   = new TextBox  { Header = AppSettings.S("API 地址", "API URL"),      Text = profile.Url,      Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            var keyBox   = new PasswordBox { Header = "API Key", Password = profile.ApiKey ?? "", Margin = new Thickness(0, 0, 0, 8), PasswordRevealMode = PasswordRevealMode.Peek };
            var modelBox = new TextBox  { Header = AppSettings.S("模型", "Model"),             Text = profile.Model,   Margin = new Thickness(0, 0, 0, 8) };
            var hint     = new TextBlock
            {
                Text = "Groq: api.groq.com/openai/v1/chat/completions\nOpenAI: api.openai.com/v1/chat/completions\nDeepSeek: api.deepseek.com/v1/chat/completions",
                FontSize = 12, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12)
            };
            // 提供商类型
            var providerLabel = new TextBlock { Text = AppSettings.S("提供商类型", "Provider Type"), FontSize = 12, Opacity = 0.55, Margin = new Thickness(0, 0, 0, 4) };
            var providerPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
            providerPicker.Items.Add("OpenAI 兼容（DeepSeek、Groq、OpenAI 等）");
            providerPicker.Items.Add("Claude 原生（Anthropic API）");
            providerPicker.SelectedIndex = profile.ProviderType == "claude" ? 1 : 0;
            // 视觉/多模态
            var visionToggle = new ToggleSwitch { Header = AppSettings.S("支持图片输入（多模态）", "Vision / Image Input"), IsOn = profile.VisionEnabled, Margin = new Thickness(0, 0, 0, 8) };
            // 独立破墙
            var jbSep = new TextBlock { Text = AppSettings.S("独立破墙（可选）", "Per-API Jailbreak"), FontSize = 12, FontWeight = Windows.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) };
            var jbToggle = new ToggleSwitch { Header = AppSettings.S("启用此 API 的独立破墙", "Enable for this API"), IsOn = profile.JailbreakEnabled, Margin = new Thickness(0, 0, 0, 4) };
            var jbBox = new TextBox { Header = AppSettings.S("破墙提示词", "Jailbreak Prompt"), Text = profile.JailbreakPrompt ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, Margin = new Thickness(0, 0, 0, 8) };

            var panel = new StackPanel { Width = 300 };
            panel.Children.Add(nameBox);
            panel.Children.Add(urlBox);
            panel.Children.Add(keyBox);
            panel.Children.Add(modelBox);
            panel.Children.Add(hint);
            panel.Children.Add(providerLabel);
            panel.Children.Add(providerPicker);
            panel.Children.Add(visionToggle);
            panel.Children.Add(jbSep);
            panel.Children.Add(jbToggle);
            panel.Children.Add(jbBox);

            var dialog = new ContentDialog
            {
                Title               = isNew ? AppSettings.S("新增 API 配置", "Add API Profile")
                                            : AppSettings.S("编辑 API 配置", "Edit API Profile"),
                Content             = new ScrollViewer { Content = panel, MaxHeight = 520 },
                PrimaryButtonText   = AppSettings.S("保存", "Save"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme      = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                profile.Name             = nameBox.Text.Trim();
                profile.Url              = urlBox.Text.Trim();
                profile.ApiKey           = keyBox.Password;
                profile.Model            = modelBox.Text.Trim();
                profile.ProviderType     = providerPicker.SelectedIndex == 1 ? "claude" : "openai";
                profile.VisionEnabled    = visionToggle.IsOn;
                profile.JailbreakEnabled = jbToggle.IsOn;
                profile.JailbreakPrompt  = jbBox.Text.Trim();
                return true;
            }
            return false;
        }

        // ── Back ──────────────────────────────────────────────────────────────

        private async void ImportApi_Click(object sender, RoutedEventArgs e)
        {
            var files = await ImportExportPage.PickFiles(new[] { ".json" });
            if (files == null || files.Count == 0) return;
            foreach (var file in files)
            {
                try
                {
                    string json = await Windows.Storage.FileIO.ReadTextAsync(file);
                    var profiles = ImportExportPage.FromJson<System.Collections.Generic.List<ApiProfile>>(json);
                    if (profiles != null)
                        foreach (var p in profiles)
                            DataManager.Data.ApiProfiles.Add(p);
                }
                catch { }
            }
            await DataManager.SaveAsync();
            RefreshList();
        }

        private async void ExportApi_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
            picker.SuggestedFileName = "api_profiles";
            picker.FileTypeChoices.Add("JSON", new System.Collections.Generic.List<string> { ".json" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            await Windows.Storage.FileIO.WriteTextAsync(file,
                ImportExportPage.ToJson(DataManager.Data.ApiProfiles));
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }

        // ── 多选模式 ──────────────────────────────────────────────────────────

        private bool _selectMode = false;

        private void ProfileItem_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (!_selectMode) EnterSelectMode();
            SelectItemFromSender(sender);
        }

        private void ProfileItem_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            e.Handled = true;
            if (!_selectMode) EnterSelectMode();
            SelectItemFromSender(sender);
        }

        private void SelectItemFromSender(object sender)
        {
            if ((sender as FrameworkElement)?.DataContext is ApiProfileItem item &&
                !ProfilesList.SelectedItems.Contains(item))
                ProfilesList.SelectedItems.Add(item);
        }

        private void EnterSelectMode()
        {
            _selectMode = true;
            ProfilesList.SelectionMode      = ListViewSelectionMode.Multiple;
            ProfilesList.IsItemClickEnabled = false;
            ProfilesList.SelectionChanged  += ProfilesList_MultiSelectionChanged;

            AddBtn.Visibility        = Visibility.Collapsed;
            EditBtn.Visibility       = Visibility.Collapsed;
            DeleteBtn.Visibility     = Visibility.Collapsed;
            ToggleAllBtn.Visibility  = Visibility.Visible;
            DeleteSelectBtn.Visibility = Visibility.Visible;
            CancelSelectBtn.Visibility = Visibility.Visible;
            DeleteSelectBtn.IsEnabled  = false;

            SelectHint.Text       = AppSettings.S("已选 0 项", "0 selected");
            SelectHint.Visibility = Visibility.Visible;
        }

        private void ExitSelectMode()
        {
            _selectMode = false;
            ProfilesList.SelectionChanged -= ProfilesList_MultiSelectionChanged;
            ProfilesList.SelectedItems.Clear();
            ProfilesList.SelectionMode      = ListViewSelectionMode.Single;
            ProfilesList.IsItemClickEnabled = false;

            AddBtn.Visibility        = Visibility.Visible;
            EditBtn.Visibility       = Visibility.Visible;
            DeleteBtn.Visibility     = Visibility.Visible;
            ToggleAllBtn.Visibility  = Visibility.Collapsed;
            DeleteSelectBtn.Visibility = Visibility.Collapsed;
            CancelSelectBtn.Visibility = Visibility.Collapsed;
            SelectHint.Visibility    = Visibility.Collapsed;

            RefreshList();
        }

        private void ProfilesList_MultiSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int count = ProfilesList.SelectedItems.Count;
            DeleteSelectBtn.IsEnabled = count > 0;
            SelectHint.Text = AppSettings.S($"已选 {count} 项", $"{count} selected");
            ToggleAllBtn.Label = count == ProfilesList.Items.Count
                ? AppSettings.S("取消全选", "Deselect all")
                : AppSettings.S("全选", "Select all");
        }

        private void ToggleAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesList.SelectedItems.Count == ProfilesList.Items.Count)
                ProfilesList.SelectedItems.Clear();
            else
                ProfilesList.SelectAll();
        }

        private void CancelSelectBtn_Click(object sender, RoutedEventArgs e) => ExitSelectMode();

        private async void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProfilesList.SelectedItems.OfType<ApiProfileItem>().ToList();
            if (selected.Count == 0) return;

            var confirm = new ContentDialog
            {
                Title               = AppSettings.S("删除配置", "Delete Profiles"),
                Content             = AppSettings.S($"确定删除选中的 {selected.Count} 个配置？", $"Delete {selected.Count} selected profiles?"),
                PrimaryButtonText   = AppSettings.S("删除", "Delete"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme      = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            foreach (var item in selected)
            {
                DataManager.Data.ApiProfiles.Remove(item.Profile);
                if (AppSettings.DefaultApiProfileId == item.Profile.Id)
                {
                    AppSettings.DefaultApiProfileId = "";
                    DataManager.Data.DefaultApiProfileId = "";
                    DataManager.Data.SelectedApiProfileId = "";
                }
            }
            await DataManager.SaveAsync();
            ExitSelectMode();
        }
    }
}