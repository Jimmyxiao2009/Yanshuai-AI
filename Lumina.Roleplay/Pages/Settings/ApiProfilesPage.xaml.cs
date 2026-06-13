using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class ApiProfilesPage : Page
    {
        public ApiProfilesPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
            RefreshList();
        }

        private void ApplyLanguage()
        {
            PageTitle.Text = AppSettings.S("API 配置", "API Profiles");
            EmptyTitle.Text = AppSettings.S("还没有 API 配置", "No API profiles yet");
            EmptyDesc.Text = AppSettings.S("点击下方「新增」添加 API 连接", "Tap + below to add an API connection");
            AddBtn.Label = AppSettings.S("新增", "Add");
            EditBtn.Label = AppSettings.S("编辑", "Edit");
            DeleteBtn.Label = AppSettings.S("删除", "Delete");
            SetDefaultBtn.Label = AppSettings.S("设为默认", "Set Default");
            ExportBackupBtn.Label = AppSettings.S("导出备份", "Export Backup");
            ImportBackupBtn.Label = AppSettings.S("从备份恢复", "Restore from Backup");
            ToggleAllBtn.Label = AppSettings.S("全选", "Select All");
            DeleteSelectBtn.Label = AppSettings.S("删除选中", "Delete Selected");
            CancelSelectBtn.Label = AppSettings.S("取消", "Cancel");
        }

        // ── Card building ────────────────────────────────────────────────────

        private ApiProfile _selectedProfile;

        private void RefreshList()
        {
            CardContainer.Children.Clear();
            var profiles = DataManager.Data.ApiProfiles;
            var defaultId = AppSettings.DefaultApiProfileId;

            EmptyPanel.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var p in profiles)
            {
                bool isDefault = p.Id == defaultId;
                var card = BuildCard(p, isDefault);
                CardContainer.Children.Add(card);
            }

            UpdateButtons();
        }

        private Border BuildCard(ApiProfile p, bool isDefault)
        {
            bool isSelected = _selectedProfile?.Id == p.Id || _selectedIds.Contains(p.Id);

            var border = new Border
            {
                BorderBrush = (SolidColorBrush)Application.Current.Resources["YanshuaiBorderBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 14, 18, 14),
                Tag = p,
                Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(20, 128, 128, 128))
                    : new SolidColorBrush(Colors.Transparent),
            };

            // Tap → select
            border.Tapped += (s, e) =>
            {
                _selectedProfile = (ApiProfile)((Border)s).Tag;
                RefreshList();
            };

            // Right-tap / hold → multi-select
            border.RightTapped += (s, e) =>
            {
                e.Handled = true;
                if (!_selectMode) EnterSelectMode();
                SelectItemFromSender(s);
            };
            border.Holding += (s, e) =>
            {
                if (e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
                e.Handled = true;
                if (!_selectMode) EnterSelectMode();
                SelectItemFromSender(s);
            };

            // Role badge
            string roleBadge;
            Color roleBadgeColor;
            switch (p.Role)
            {
                case "sub": roleBadge = AppSettings.S("副", "Sub"); roleBadgeColor = Color.FromArgb(255, 140, 180, 80); break;
                case "both": roleBadge = AppSettings.S("通用", "Both"); roleBadgeColor = Color.FromArgb(255, 180, 120, 60); break;
                default: roleBadge = AppSettings.S("主", "Main"); roleBadgeColor = Color.FromArgb(255, 60, 140, 220); break;
            }

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new TextBlock
            {
                Text = isDefault ? $"{p.Name} ★" : p.Name,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)Application.Current.Resources["YanshuaiTextBrush"]
            });
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(roleBadgeColor),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = roleBadge,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.White),
                }
            });

            border.Child = new StackPanel
            {
                Children =
                {
                    nameRow,
                    new TextBlock
                    {
                        Text = p.Model,
                        FontSize = 13, Opacity = 0.6,
                        Foreground = (SolidColorBrush)Application.Current.Resources["YanshuaiTextBrush"],
                        Margin = new Thickness(0, 2, 0, 0)
                    },
                    new TextBlock
                    {
                        Text = p.Url,
                        FontSize = 11, Opacity = 0.45,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = (SolidColorBrush)Application.Current.Resources["YanshuaiTextBrush"],
                        Margin = new Thickness(0, 1, 0, 0)
                    }
                }
            };

            return border;
        }

        private void UpdateButtons()
        {
            bool has = _selectedProfile != null;
            EditBtn.IsEnabled = has;
            DeleteBtn.IsEnabled = has;
            SetDefaultBtn.IsEnabled = has;
            if (has)
                SetDefaultBtn.Label = AppSettings.DefaultApiProfileId == _selectedProfile.Id
                    ? AppSettings.S("已是默认", "Is Default")
                    : AppSettings.S("设为默认", "Set Default");
        }

        // ── Set default ───────────────────────────────────────────────────────

        private void SetDefaultBtn_Click(object sender, RoutedEventArgs e)
        {
            var p = _selectedProfile;
            if (p == null) return;
            AppSettings.DefaultApiProfileId = p.Id;
            DataManager.Data.DefaultApiProfileId = p.Id;
            DataManager.Data.SelectedApiProfileId = p.Id;
            _ = DataManager.SaveAsync();
            RefreshList();
            _selectedProfile = p;
        }

        // ── Add / Edit / Delete ───────────────────────────────────────────────

        private void AddButton_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobeApiMainPage), "new");

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile != null)
                Frame.Navigate(typeof(OobeApiMainPage), _selectedProfile.Id);
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var p = _selectedProfile;
            if (p == null) return;

            var confirm = new ContentDialog
            {
                Title = AppSettings.S("删除配置", "Delete Profile"),
                Content = AppSettings.S($"确定删除「{p.Name}」？", $"Delete \"{p.Name}\"?"),
                PrimaryButtonText = AppSettings.S("删除", "Delete"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await confirm.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                DataManager.Data.ApiProfiles.Remove(p);
                if (AppSettings.DefaultApiProfileId == p.Id)
                {
                    AppSettings.DefaultApiProfileId = "";
                    DataManager.Data.DefaultApiProfileId = "";
                    DataManager.Data.SelectedApiProfileId = "";
                }
                if (_selectedProfile?.Id == p.Id) _selectedProfile = null;
                await DataManager.SaveAsync();
                RefreshList();
            }
        }

        // ── Import / Export ──────────────────────────────────────────────────

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

        // ── Multi-select mode ────────────────────────────────────────────────

        private bool _selectMode = false;
        private readonly HashSet<string> _selectedIds = new HashSet<string>();

        private void SelectItemFromSender(object sender)
        {
            if ((sender as FrameworkElement)?.Tag is ApiProfile item)
            {
                if (_selectedIds.Contains(item.Id))
                    _selectedIds.Remove(item.Id);
                else
                    _selectedIds.Add(item.Id);
                RefreshList();
            }
        }

        private void EnterSelectMode()
        {
            _selectMode = true;
            SetDefaultBtn.Visibility = Visibility.Collapsed;
            EditBtn.Visibility = Visibility.Collapsed;
            DeleteBtn.Visibility = Visibility.Collapsed;
            AddBtn.Visibility = Visibility.Collapsed;
            ToggleAllBtn.Visibility = Visibility.Visible;
            DeleteSelectBtn.Visibility = Visibility.Visible;
            DeleteSelectBtn.IsEnabled = true;
            CancelSelectBtn.Visibility = Visibility.Visible;
            SelectHint.Text = AppSettings.S("选择要删除的配置", "Select profiles to delete");
            SelectHint.Visibility = Visibility.Visible;
        }

        private void ExitSelectMode()
        {
            _selectMode = false;
            _selectedIds.Clear();
            SetDefaultBtn.Visibility = Visibility.Visible;
            EditBtn.Visibility = Visibility.Visible;
            DeleteBtn.Visibility = Visibility.Visible;
            AddBtn.Visibility = Visibility.Visible;
            ToggleAllBtn.Visibility = Visibility.Collapsed;
            DeleteSelectBtn.Visibility = Visibility.Collapsed;
            DeleteSelectBtn.IsEnabled = false;
            CancelSelectBtn.Visibility = Visibility.Collapsed;
            SelectHint.Visibility = Visibility.Collapsed;
            RefreshList();
        }

        private void ToggleAllBtn_Click(object sender, RoutedEventArgs e)
        {
            var all = DataManager.Data.ApiProfiles;
            if (_selectedIds.Count == all.Count)
                _selectedIds.Clear();
            else
                foreach (var p in all)
                    _selectedIds.Add(p.Id);
            RefreshList();
        }

        private void CancelSelectBtn_Click(object sender, RoutedEventArgs e) => ExitSelectMode();

        private async void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedIds.Count == 0) return;
            var confirm = new ContentDialog
            {
                Title = AppSettings.S("删除选中配置", "Delete Selected"),
                Content = AppSettings.S($"确定删除选中的 {_selectedIds.Count} 个配置？", $"Delete {_selectedIds.Count} selected profile(s)?"),
                PrimaryButtonText = AppSettings.S("删除", "Delete"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await confirm.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                DataManager.Data.ApiProfiles.RemoveAll(p => _selectedIds.Contains(p.Id));
                var defId = AppSettings.DefaultApiProfileId;
                if (_selectedIds.Contains(defId))
                {
                    AppSettings.DefaultApiProfileId = "";
                    DataManager.Data.DefaultApiProfileId = "";
                    DataManager.Data.SelectedApiProfileId = "";
                }
                if (_selectedProfile != null && _selectedIds.Contains(_selectedProfile.Id))
                    _selectedProfile = null;
                await DataManager.SaveAsync();
                ExitSelectMode();
            }
        }
    }
}
