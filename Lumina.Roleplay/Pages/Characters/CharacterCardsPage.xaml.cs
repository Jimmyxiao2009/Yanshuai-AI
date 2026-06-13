// CharacterCardsPage.xaml.cs — 角色卡画廊（MVVM 重写）
// 绑定 GridView + 可复用 CharacterCardView；单击新对话、右键/长按看详情、
// GridView 原生多选 + 批量操作（导出/分组/删除/导入）。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using yanshuai.ViewModels;

namespace yanshuai
{
    public sealed partial class CharacterCardsPage : Page
    {
        private readonly CharacterCardsViewModel _vm = new CharacterCardsViewModel();
        private readonly CollectionViewSource _cvs = new CollectionViewSource { IsSourceGrouped = true };

        public CharacterCardsPage()
        {
            InitializeComponent();
            DataContext = _vm;
            _cvs.Source = _vm.Groups;
            CardsGrid.ItemsSource = _cvs.View;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            PageTitle.Text = AppSettings.S("角色卡", "Characters");
            EmptyLabel.Text = AppSettings.S("还没有角色卡，点下方新建或导入", "No character cards yet — create or import below");
            ApplyLanguage();
            if (_vm.IsSelectMode) ExitSelectMode();
            Refresh();
        }

        private void Refresh()
        {
            _vm.Load();
            // 重新挂载分组源，确保分组视图刷新
            _cvs.Source = null;
            _cvs.Source = _vm.Groups;
        }

        private void ApplyLanguage()
        {
            AddBtn.Label = AppSettings.S("新建角色", "New Character");
            ImportBtn.Label = AppSettings.S("导入", "Import");
            SelectBtn.Label = AppSettings.S("多选", "Multi-Select");
            ToggleAllBtn.Label = AppSettings.S("全选", "Select All");
            MoveGroupBtn.Label = AppSettings.S("分组", "Group");
            ExportSelectBtn.Label = AppSettings.S("导出", "Export");
            DeleteSelectBtn.Label = AppSettings.S("删除", "Delete");
            CancelSelectBtn.Label = AppSettings.S("取消", "Cancel");
        }

        // ── 卡片交互 ────────────────────────────────────────────────────────
        private void Card_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (_vm.IsSelectMode) return;
            var item = e.ClickedItem as CardItemViewModel;
            if (item != null) OpenChat(item.Card);
        }

        private void OpenChat(CharacterCard card)
        {
            DataManager.Data.SelectedCharacterCardId = card.Id;
            var conv = DataManager.CreateNewConversation();
            AppState.ActiveConversation = conv;
            var p = DialoguePoolManager.GetOrCreatePool(card);
            if (p != null && !p.ConversationIds.Contains(conv.Id))
                p.AddConversation(conv);
            _ = DataManager.SaveAsync();
            Frame.Navigate(typeof(MainPage));
        }

        private void Cards_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (_vm.IsSelectMode) return;
            var card = CardFromSource(e.OriginalSource);
            if (card != null) Frame.Navigate(typeof(CharacterDetailPage), card.Id);
        }

        private void Cards_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (_vm.IsSelectMode || e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            var card = CardFromSource(e.OriginalSource);
            if (card != null) Frame.Navigate(typeof(CharacterDetailPage), card.Id);
        }

        private static CharacterCard CardFromSource(object originalSource)
        {
            var fe = originalSource as FrameworkElement;
            return (fe?.DataContext as CardItemViewModel)?.Card;
        }

        // ── 多选 ───────────────────────────────────────────────────────────
        private List<CharacterCard> GetSelectedCards()
        {
            return CardsGrid.SelectedItems.OfType<CardItemViewModel>().Select(i => i.Card).ToList();
        }

        private void Cards_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_vm.IsSelectMode) return;
            int n = CardsGrid.SelectedItems.Count;
            DeleteSelectBtn.IsEnabled = n > 0;
            ExportSelectBtn.IsEnabled = n > 0;
            MoveGroupBtn.IsEnabled = n > 0;
            DeleteSelectBtn.Label = n > 0
                ? string.Format(AppSettings.S("删除 ({0})", "Delete ({0})"), n)
                : AppSettings.S("删除", "Delete");
        }

        private void EnterSelectMode()
        {
            _vm.IsSelectMode = true;
            CardsGrid.SelectionMode = ListViewSelectionMode.Multiple;
            CardsGrid.IsItemClickEnabled = false;

            AddBtn.Visibility = Visibility.Collapsed;
            ImportBtn.Visibility = Visibility.Collapsed;
            SelectBtn.Visibility = Visibility.Collapsed;
            ToggleAllBtn.Visibility = Visibility.Visible;
            MoveGroupBtn.Visibility = Visibility.Visible;
            ExportSelectBtn.Visibility = Visibility.Visible;
            DeleteSelectBtn.Visibility = Visibility.Visible;
            CancelSelectBtn.Visibility = Visibility.Visible;
            MoveGroupBtn.IsEnabled = false;
            ExportSelectBtn.IsEnabled = false;
            DeleteSelectBtn.IsEnabled = false;
        }

        private void ExitSelectMode()
        {
            _vm.IsSelectMode = false;
            CardsGrid.SelectedItems.Clear();
            CardsGrid.SelectionMode = ListViewSelectionMode.None;
            CardsGrid.IsItemClickEnabled = true;

            AddBtn.Visibility = Visibility.Visible;
            ImportBtn.Visibility = Visibility.Visible;
            SelectBtn.Visibility = Visibility.Visible;
            ToggleAllBtn.Visibility = Visibility.Collapsed;
            MoveGroupBtn.Visibility = Visibility.Collapsed;
            ExportSelectBtn.Visibility = Visibility.Collapsed;
            DeleteSelectBtn.Visibility = Visibility.Collapsed;
            CancelSelectBtn.Visibility = Visibility.Collapsed;
            DeleteSelectBtn.Label = AppSettings.S("删除", "Delete");
        }

        private void ToggleSelectMode_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.IsSelectMode) ExitSelectMode();
            else EnterSelectMode();
        }

        private void ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            int total = _vm.Groups.Sum(g => g.Count);
            if (CardsGrid.SelectedItems.Count >= total)
                CardsGrid.SelectedItems.Clear();
            else
                CardsGrid.SelectAll();
        }

        // ── 批量操作 ───────────────────────────────────────────────────────
        private async void ExportSelected_Click(object sender, RoutedEventArgs e)
        {
            var cards = GetSelectedCards();
            if (cards.Count == 0) return;
            var folder = await ImportExportPage.PickFolder();
            if (folder == null) return;

            int ok = 0;
            foreach (var card in cards)
            {
                try
                {
                    string json = ImportExportPage.BuildStCharaJson(card);
                    string safe = ImportExportPage.MakeSafeFilename(card.Name);
                    if (card.HasAvatar)
                    {
                        byte[] pngBytes = Convert.FromBase64String(card.AvatarBase64);
                        byte[] output = ImportExportPage.InjectPngTextChunk(pngBytes, "chara",
                                            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
                        var file = await folder.CreateFileAsync(safe + ".png", CreationCollisionOption.GenerateUniqueName);
                        await FileIO.WriteBytesAsync(file, output);
                    }
                    else
                    {
                        var file = await folder.CreateFileAsync(safe + ".json", CreationCollisionOption.GenerateUniqueName);
                        await FileIO.WriteTextAsync(file, json, Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    }
                    ok++;
                }
                catch { }
            }

            ExitSelectMode();
            var dialog = new ContentDialog
            {
                Title = AppSettings.S("导出完成", "Export Done"),
                Content = string.Format(AppSettings.S("已导出 {0}/{1} 张角色卡", "Exported {0}/{1} cards"), ok, cards.Count),
                PrimaryButtonText = "OK",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            await dialog.ShowAsync().AsTask();
        }

        private async void MoveGroup_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedCards();
            if (selected.Count == 0) return;

            var existingGroups = DataManager.Data.CharacterCards
                .Select(c => c.GroupName)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .OrderBy(g => g)
                .ToList();

            string chosen = null;
            var panel = new StackPanel();

            if (existingGroups.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = AppSettings.S("选择已有分组", "Choose existing group"),
                    FontSize = 13, Opacity = 0.6,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                foreach (var gn in existingGroups)
                {
                    var btn = new Button
                    {
                        Content = gn,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Background = new Windows.UI.Xaml.Media.SolidColorBrush(AppSettings.IsDark
                            ? Windows.UI.Color.FromArgb(20, 255, 255, 255)
                            : Windows.UI.Color.FromArgb(12, 0, 0, 0)),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(12, 8, 12, 8),
                        Tag = gn
                    };
                    panel.Children.Add(btn);
                }
                panel.Children.Add(new Border
                {
                    Height = 1, Opacity = 0.15,
                    Background = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["YanshuaiBorderBrush"],
                    Margin = new Thickness(0, 6, 0, 6)
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = AppSettings.S("或输入新分组名", "Or enter a new group name"),
                FontSize = 13, Opacity = 0.6,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var newGroupBox = new TextBox
            {
                PlaceholderText = AppSettings.S("新分组名称", "New group name"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            panel.Children.Add(newGroupBox);

            var dlg = new ContentDialog
            {
                Title = AppSettings.S("移动到分组", "Move to Group"),
                Content = panel,
                PrimaryButtonText = AppSettings.S("确定", "OK"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };

            foreach (var child in panel.Children)
            {
                if (child is Button groupBtn && groupBtn.Tag is string)
                {
                    groupBtn.Click += (s2, e2) =>
                    {
                        chosen = groupBtn.Tag as string;
                        dlg.Hide();
                    };
                }
            }

            var result = await dlg.ShowAsync().AsTask();

            if (chosen == null)
            {
                if (result != ContentDialogResult.Primary) return;
                chosen = newGroupBox.Text.Trim();
                if (string.IsNullOrEmpty(chosen)) return;
            }

            var ids = new HashSet<string>(selected.Select(c => c.Id));
            foreach (var card in DataManager.Data.CharacterCards)
                if (ids.Contains(card.Id)) card.GroupName = chosen;
            await DataManager.SaveAsync();

            ExitSelectMode();
            Refresh();
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedCards();
            if (selected.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = AppSettings.S("确认删除", "Confirm Delete"),
                Content = string.Format(AppSettings.S("确定要删除 {0} 个角色卡吗？", "Delete {0} character cards?"), selected.Count),
                PrimaryButtonText = AppSettings.S("删除", "Delete"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel")
            };
            var result = await dialog.ShowAsync().AsTask();
            if (result != ContentDialogResult.Primary) return;

            var ids = new HashSet<string>(selected.Select(c => c.Id));
            DataManager.Data.CharacterCards.RemoveAll(c => ids.Contains(c.Id));
            await DataManager.SaveAsync();

            ExitSelectMode();
            Refresh();
        }

        // ── 新建 / 导入 ─────────────────────────────────────────────────────
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.Reset();
            CharaWizardData.ReturnTarget = "CharacterCardsPage";
            Frame.Navigate(typeof(CharaWizardPage1));
        }

        private async void ImportChara_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".json");

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            int ok = 0;
            foreach (var file in files)
            {
                try
                {
                    CharacterCard card = null;
                    if (file.FileType.ToLower() == ".png")
                    {
                        var bytes = (await FileIO.ReadBufferAsync(file)).ToArray();
                        card = ImportExportPage.ParseCharaPng(bytes);
                    }
                    else
                    {
                        string json = await FileIO.ReadTextAsync(file);
                        card = ImportExportPage.ParseCharaJson(json);
                    }
                    if (card != null)
                    {
                        DataManager.Data.CharacterCards.Add(card);
                        ok++;
                    }
                }
                catch { }
            }

            await DataManager.SaveAsync();

            if (ok > 0)
            {
                var dlg = new ContentDialog
                {
                    Title = AppSettings.S("导入完成", "Import Done"),
                    Content = string.Format(AppSettings.S("已导入 {0} 张角色卡", "Imported {0} character card(s)"), ok),
                    PrimaryButtonText = "OK",
                    RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
                };
                await dlg.ShowAsync().AsTask();
            }
            Refresh();
        }
    }
}
