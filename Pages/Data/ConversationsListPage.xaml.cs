using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    // ── ViewModel：包装 Conversation，提供绑定属性 ─────────────────────────────
    public class ConvViewModel
    {
        public Conversation Conv { get; }
        private bool _isSelected;
        private bool _selectMode;

        public ConvViewModel(Conversation conv) { Conv = conv; }

        public string Title              => Conv.Title;
        public string UpdatedAtDisplay   => Conv.UpdatedAtDisplay;
        public string LastMessagePreview => Conv.LastMessagePreview;
        public Visibility PinIconVisibility => Conv.IsPinned ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DeleteBtnVisibility  => _selectMode ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CheckBoxVisibility   => _selectMode ? Visibility.Visible   : Visibility.Collapsed;
        public Visibility CheckedVisibility    => _isSelected ? Visibility.Visible   : Visibility.Collapsed;
        public Brush      CheckedBrush         => _isSelected
            ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
            : new SolidColorBrush(Colors.Transparent);

        public Brush SelectedBrush => _isSelected
            ? new SolidColorBrush(Color.FromArgb(40, 0, 120, 215))
            : new SolidColorBrush(Colors.Transparent);

        public bool IsSelected
        {
            get => _isSelected;
            set => _isSelected = value;
        }

        public void SetSelectMode(bool value) { _selectMode = value; }
    }

    // ── 分组：纯数据，不做绑定刷新（通过 RebuildFlatList 重建触发刷新）────────
    public class ConvGroup
    {
        public string Key           { get; }
        public Visibility IsPinnedGroup { get; }
        public bool IsExpanded      { get; set; } = true;
        public bool IsAllSelected   { get; set; } = false;
        public bool SelectMode      { get; set; } = false;
        public List<ConvViewModel> Items { get; }

        public string CountText   => $"{Items.Count}";
        public string ExpandGlyph => IsExpanded ? "\uE76C" : "\uE76E";
        public Visibility CheckBoxVisibility  => SelectMode ? Visibility.Visible   : Visibility.Collapsed;
        public Visibility CheckedVisibility   => IsAllSelected ? Visibility.Visible : Visibility.Collapsed;
        public Brush      CheckedBrush        => IsAllSelected
            ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
            : new SolidColorBrush(Colors.Transparent);

        public ConvGroup(string key, bool isPinned, IEnumerable<ConvViewModel> items)
        {
            Key           = key;
            IsPinnedGroup = isPinned ? Visibility.Visible : Visibility.Collapsed;
            Items         = items.ToList();
        }
    }

    public class ConvTemplateSelector : DataTemplateSelector
    {
        public DataTemplate GroupHeaderTemplate { get; set; }
        public DataTemplate ConvItemTemplate    { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
            => item is ConvGroup ? GroupHeaderTemplate : ConvItemTemplate;
    }

    public sealed partial class ConversationsListPage : Page
    {
        private bool _selectMode = false;
        private readonly ObservableCollection<object> _flatList = new ObservableCollection<object>();
        private readonly List<ConvGroup> _groups = new List<ConvGroup>();

        public ConversationsListPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ConvList.ItemsSource = _flatList;
            RefreshList();
        }

        // ── Search ────────────────────────────────────────────────────────────

        private bool _searchOpen = false;

        private void SearchToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _searchOpen = !_searchOpen;
            SearchBar.Visibility = _searchOpen ? Visibility.Visible : Visibility.Collapsed;
            if (_searchOpen)
            {
                SearchBox.Focus(FocusState.Programmatic);
            }
            else
            {
                SearchBox.Text = "";
                ClearSearchBtn.Visibility = Visibility.Collapsed;
                RefreshList();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = SearchBox.Text;
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
            RefreshList(q);
        }

        private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            ClearSearchBtn.Visibility = Visibility.Collapsed;
            RefreshList();
        }

        // ── List refresh ──────────────────────────────────────────────────────

        private void RefreshList(string filter = null)
        {
            bool isFiltering = !string.IsNullOrWhiteSpace(filter);
            IEnumerable<Conversation> all = DataManager.Data.Conversations;

            if (isFiltering)
            {
                var f = filter.ToLower();
                all = all.Where(c =>
                    (c.Title?.ToLower().Contains(f) == true) ||
                    (c.LastMessagePreview?.ToLower().Contains(f) == true));
            }

            var list = all.ToList();
            var newGroups = new List<ConvGroup>();

            var pinned = list.Where(c => c.IsPinned).OrderByDescending(c => c.UpdatedAt)
                             .Select(c => new ConvViewModel(c)).ToList();
            if (pinned.Count > 0)
                newGroups.Add(new ConvGroup("置顶", true, pinned));

            var grouped = list
                .Where(c => !c.IsPinned && !string.IsNullOrEmpty(c.GroupName))
                .GroupBy(c => c.GroupName)
                .OrderBy(g => AppSettings.GetGroupOrder(g.Key));
            foreach (var g in grouped)
                newGroups.Add(new ConvGroup(g.Key, false,
                    g.OrderByDescending(c => c.UpdatedAt).Select(c => new ConvViewModel(c))));

            var ungrouped = list
                .Where(c => !c.IsPinned && string.IsNullOrEmpty(c.GroupName))
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ConvViewModel(c))
                .ToList();
            if (ungrouped.Count > 0)
                newGroups.Add(new ConvGroup("对话", false, ungrouped));

            // 恢复折叠状态
            foreach (var ng in newGroups)
            {
                var old = _groups.FirstOrDefault(g => g.Key == ng.Key);
                if (old != null) ng.IsExpanded = old.IsExpanded;
            }

            // 多选模式下恢复选中状态（按 Conv.Id 匹配）
            if (_selectMode)
            {
                var selectedIds = new HashSet<string>(
                    _groups.SelectMany(g => g.Items).Where(vm => vm.IsSelected).Select(vm => vm.Conv.Id));
                foreach (var ng in newGroups)
                {
                    ng.SelectMode = true;
                    foreach (var vm in ng.Items)
                        vm.IsSelected = selectedIds.Contains(vm.Conv.Id);
                    ng.IsAllSelected = ng.Items.Count > 0 && ng.Items.All(vm => vm.IsSelected);
                }
            }

            _groups.Clear();
            _groups.AddRange(newGroups);
            RebuildFlatList();

            bool empty = list.Count == 0;
            EmptyState.Visibility = empty ? Visibility.Visible  : Visibility.Collapsed;
            ConvList.Visibility   = empty ? Visibility.Collapsed : Visibility.Visible;

            EmptyTitle.Text = isFiltering ? "没有匹配的对话" : "还没有对话";
            EmptyHint.Text  = isFiltering ? "尝试其他关键词" : "点击下方 ＋ 开始新对话";
        }

        private void RebuildFlatList()
        {
            _flatList.Clear();
            foreach (var g in _groups)
            {
                _flatList.Add(g);
                if (g.IsExpanded)
                    foreach (var vm in g.Items)
                        _flatList.Add(vm);
            }
        }

        // 多选模式下只需要刷新视觉状态，不重建列表（避免闪烁）
        // ConvViewModel/ConvGroup 没有 INotifyPropertyChanged，改为用替换同位置对象触发刷新
        private void RefreshItemInPlace(object item)
        {
            int idx = _flatList.IndexOf(item);
            if (idx >= 0)
            {
                _flatList.RemoveAt(idx);
                _flatList.Insert(idx, item);
            }
        }

        // ── 点击组头：展开/收纳 ───────────────────────────────────────────────

        private void GroupHeader_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ConvGroup group)
            {
                group.IsExpanded = !group.IsExpanded;
                RebuildFlatList();
            }
        }

        // ── 右键/长按组头：组别管理菜单 ──────────────────────────────────────

        private void GroupHeader_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            ShowGroupMenu(sender as FrameworkElement);
        }

        private void GroupHeader_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            e.Handled = true;
            ShowGroupMenu(sender as FrameworkElement);
        }

        private void ShowGroupMenu(FrameworkElement element)
        {
            if (!(element?.DataContext is ConvGroup group)) return;
            // 置顶组不允许管理
            if (group.IsPinnedGroup == Visibility.Visible) return;

            // 获取当前所有自定义组的有序列表
            var orderedGroupKeys = _groups
                .Where(g => g.IsPinnedGroup == Visibility.Collapsed && g.Key != "对话")
                .Select(g => g.Key)
                .ToList();
            int idx = orderedGroupKeys.IndexOf(group.Key);

            var menu = new MenuFlyout();

            var renameItem = new MenuFlyoutItem { Text = "重命名分组" };
            renameItem.Click += (s, ev) => RenameGroup(group);
            menu.Items.Add(renameItem);

            var deleteItem = new MenuFlyoutItem { Text = "解散分组（对话保留）" };
            deleteItem.Click += (s, ev) => DisbandGroup(group);
            menu.Items.Add(deleteItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var upItem = new MenuFlyoutItem { Text = "上移" };
            upItem.IsEnabled = idx > 0;
            upItem.Click += (s, ev) => MoveGroup(orderedGroupKeys, idx, -1);
            menu.Items.Add(upItem);

            var downItem = new MenuFlyoutItem { Text = "下移" };
            downItem.IsEnabled = idx >= 0 && idx < orderedGroupKeys.Count - 1;
            downItem.Click += (s, ev) => MoveGroup(orderedGroupKeys, idx, +1);
            menu.Items.Add(downItem);

            menu.ShowAt(element);
        }

        private void MoveGroup(List<string> orderedKeys, int idx, int delta)
        {
            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= orderedKeys.Count) return;
            string tmp = orderedKeys[idx];
            orderedKeys[idx] = orderedKeys[newIdx];
            orderedKeys[newIdx] = tmp;
            AppSettings.RenumberGroupOrders(orderedKeys);
            RefreshList(SearchBox.Text);
        }

        private async void RenameGroup(ConvGroup group)
        {
            var inputBox = new TextBox
            {
                Text = group.Key,
                Margin = new Thickness(0, 8, 0, 0),
                SelectionStart = group.Key.Length,
            };
            var dialog = new ContentDialog
            {
                Title               = "重命名分组",
                Content             = inputBox,
                PrimaryButtonText   = "确定",
                SecondaryButtonText = "取消",
                RequestedTheme      = DialogTheme,
            };
            if (await dialog.ShowAsync().AsTask() != ContentDialogResult.Primary) return;

            string newName = inputBox.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == group.Key) return;

            // 同步排序表里的旧组名
            List<string> okeys; List<int> ovals;
            AppSettings.GetGroupOrders(out okeys, out ovals);
            int oi = okeys.IndexOf(group.Key);
            if (oi >= 0) { okeys[oi] = newName; AppSettings.SaveGroupOrders(okeys, ovals); }

            foreach (var conv in DataManager.Data.Conversations.Where(c => c.GroupName == group.Key))
                conv.GroupName = newName;

            await DataManager.SaveAsync();
            RefreshList(SearchBox.Text);
        }

        private async void DisbandGroup(ConvGroup group)
        {
            var dialog = new ContentDialog
            {
                Title               = "解散分组",
                Content             = $"解散「{group.Key}」？该分组内的 {group.Items.Count} 条对话将移入未分组，不会被删除。",
                PrimaryButtonText   = "解散",
                SecondaryButtonText = "取消",
                RequestedTheme      = DialogTheme,
            };
            if (await dialog.ShowAsync().AsTask() != ContentDialogResult.Primary) return;

            foreach (var conv in DataManager.Data.Conversations.Where(c => c.GroupName == group.Key))
                conv.GroupName = "";

            await DataManager.SaveAsync();
            RefreshList(SearchBox.Text);
        }

        // ── 点击组头复选框区：整组选/取消选 ──────────────────────────────────

        private void GroupCheckBox_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true; // 阻止冒泡到 GroupHeader_Tapped
            if (!_selectMode) return;
            if (!((sender as FrameworkElement)?.DataContext is ConvGroup group)) return;

            bool newState = !group.IsAllSelected;
            foreach (var vm in group.Items)
            {
                vm.IsSelected = newState;
                RefreshItemInPlace(vm);
            }
            group.IsAllSelected = newState;
            RefreshItemInPlace(group);
            UpdateSelectCount();
        }

        // ── 点击对话条目 ──────────────────────────────────────────────────────

        private void ConvItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ConvViewModel vm)) return;

            if (_selectMode)
            {
                vm.IsSelected = !vm.IsSelected;
                // 更新所属组的 IsAllSelected
                var group = _groups.FirstOrDefault(g => g.Items.Contains(vm));
                if (group != null)
                {
                    group.IsAllSelected = group.Items.Count > 0 && group.Items.All(v => v.IsSelected);
                    RefreshItemInPlace(group);
                }
                RefreshItemInPlace(vm);
                UpdateSelectCount();
            }
            else
            {
                OpenConversation(vm.Conv);
            }
        }

        private void OpenConversation(Conversation conv)
        {
            AppState.ActiveConversation = conv;
            DataManager.Data.LastActiveConversationId = conv.Id;
            Frame.Navigate(typeof(MainPage));
        }

        // ── 右键/长按：进入多选并选中该项 ────────────────────────────────────

        private void ConvItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (!_selectMode) EnterSelectMode();
            SelectVmFromSender(sender);
        }

        private void ConvItem_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            e.Handled = true;
            if (!_selectMode) EnterSelectMode();
            SelectVmFromSender(sender);
        }

        private void SelectVmFromSender(object sender)
        {
            if (!((sender as FrameworkElement)?.DataContext is ConvViewModel vm)) return;
            if (!vm.IsSelected)
            {
                vm.IsSelected = true;
                var group = _groups.FirstOrDefault(g => g.Items.Contains(vm));
                if (group != null)
                {
                    group.IsAllSelected = group.Items.All(v => v.IsSelected);
                    RefreshItemInPlace(group);
                }
                RefreshItemInPlace(vm);
                UpdateSelectCount();
            }
        }

        // ── 选中计数更新 ──────────────────────────────────────────────────────

        private void UpdateSelectCount()
        {
            int count = _groups.SelectMany(g => g.Items).Count(vm => vm.IsSelected);
            SelectHint.Text = $"已选 {count} 条";
            bool any = count > 0;
            PinSelectBtn.IsEnabled    = any;
            GroupSelectBtn.IsEnabled  = any;
            ExportSelectBtn.IsEnabled = any;
            DeleteSelectBtn.IsEnabled = any;

            int total = _flatList.OfType<ConvViewModel>().Count();
            ToggleAllBtn.Label = (count == total && total > 0) ? "全不选" : "全选";
        }

        // ── Select mode ───────────────────────────────────────────────────────

        private void SelectBtn_Click(object sender, RoutedEventArgs e) => EnterSelectMode();

        private void EnterSelectMode()
        {
            _selectMode = true;
            foreach (var g in _groups)
            {
                g.SelectMode = true;
                g.IsAllSelected = false;
                foreach (var vm in g.Items) { vm.IsSelected = false; vm.SetSelectMode(true); }
            }
            RebuildFlatList();

            NewConvBtn.Visibility      = Visibility.Collapsed;
            SearchBtn2.Visibility      = Visibility.Collapsed;
            ImportBtn.Visibility       = Visibility.Collapsed;
            SelectBtn.Visibility       = Visibility.Collapsed;
            ToggleAllBtn.Visibility    = Visibility.Visible;
            PinSelectBtn.Visibility    = Visibility.Visible;
            GroupSelectBtn.Visibility  = Visibility.Visible;
            ExportSelectBtn.Visibility = Visibility.Visible;
            DeleteSelectBtn.Visibility = Visibility.Visible;
            CancelSelectBtn.Visibility = Visibility.Visible;
            PinSelectBtn.IsEnabled     = false;
            GroupSelectBtn.IsEnabled   = false;
            ExportSelectBtn.IsEnabled  = false;
            DeleteSelectBtn.IsEnabled  = false;
            ToggleAllBtn.Label         = "全选";

            SelectHint.Text       = "已选 0 条";
            SelectHint.Visibility = Visibility.Visible;
        }

        private void ExitSelectMode()
        {
            _selectMode = false;
            foreach (var g in _groups)
            {
                g.SelectMode = false;
                g.IsAllSelected = false;
                foreach (var vm in g.Items) { vm.IsSelected = false; vm.SetSelectMode(false); }
            }
            RebuildFlatList();

            NewConvBtn.Visibility      = Visibility.Visible;
            SearchBtn2.Visibility      = Visibility.Visible;
            ImportBtn.Visibility       = Visibility.Visible;
            SelectBtn.Visibility       = Visibility.Visible;
            ToggleAllBtn.Visibility    = Visibility.Collapsed;
            PinSelectBtn.Visibility    = Visibility.Collapsed;
            GroupSelectBtn.Visibility  = Visibility.Collapsed;
            ExportSelectBtn.Visibility = Visibility.Collapsed;
            DeleteSelectBtn.Visibility = Visibility.Collapsed;
            CancelSelectBtn.Visibility = Visibility.Collapsed;

            SelectHint.Visibility = Visibility.Collapsed;
        }

        private void ToggleAllBtn_Click(object sender, RoutedEventArgs e)
        {
            var allVms = _groups.SelectMany(g => g.Items).ToList();
            bool allSelected = allVms.All(vm => vm.IsSelected);
            bool newState = !allSelected;
            foreach (var vm in allVms) vm.IsSelected = newState;
            foreach (var g in _groups)
            {
                g.IsAllSelected = newState;
                RefreshItemInPlace(g);
            }
            foreach (var vm in allVms) RefreshItemInPlace(vm);
            UpdateSelectCount();
        }

        private void CancelSelectBtn_Click(object sender, RoutedEventArgs e) => ExitSelectMode();

        // ── 获取当前选中的对话列表 ────────────────────────────────────────────

        private List<Conversation> GetSelectedConversations()
            => _groups.SelectMany(g => g.Items).Where(vm => vm.IsSelected).Select(vm => vm.Conv).ToList();

        // ── Pin selected ──────────────────────────────────────────────────────

        private async void PinSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedConversations();
            if (selected.Count == 0) return;

            bool allPinned = selected.All(c => c.IsPinned);
            foreach (var conv in selected)
                conv.IsPinned = !allPinned;

            await DataManager.SaveAsync();
            ExitSelectMode();
            RefreshList(SearchBox.Text);
        }

        // ── Group selected ────────────────────────────────────────────────────

        private async void GroupSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedConversations();
            if (selected.Count == 0) return;

            var existingGroups = DataManager.Data.Conversations
                .Select(c => c.GroupName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            var inputBox = new TextBox
            {
                PlaceholderText = "输入新分组名，留空则移出分组",
                Margin = new Thickness(0, 8, 0, 0),
            };

            var panel = new StackPanel();

            if (existingGroups.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "选择已有分组：", FontSize = 13, Opacity = 0.7 });
                foreach (var gname in existingGroups)
                {
                    var btn = new Button
                    {
                        Content = gname,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = new SolidColorBrush(Colors.Transparent),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0, 6, 0, 6),
                        Margin = new Thickness(0, 2, 0, 0),
                    };
                    var captured = gname;
                    btn.Click += (s, ev) => inputBox.Text = captured;
                    panel.Children.Add(btn);
                }
                panel.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 8, 0, 0),
                });
                panel.Children.Add(new TextBlock { Text = "或输入新分组名：", FontSize = 13, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) });
            }
            else
            {
                panel.Children.Add(new TextBlock { Text = "输入分组名（留空则移出分组）：", FontSize = 13, Opacity = 0.7 });
            }
            panel.Children.Add(inputBox);

            var dialog = new ContentDialog
            {
                Title               = $"设置分组（{selected.Count} 条对话）",
                Content             = new ScrollViewer { Content = panel, MaxHeight = 400 },
                PrimaryButtonText   = "确定",
                SecondaryButtonText = "取消",
                RequestedTheme      = DialogTheme,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            string newGroup = inputBox.Text.Trim();
            foreach (var conv in selected)
                conv.GroupName = newGroup;

            await DataManager.SaveAsync();
            ExitSelectMode();
            RefreshList(SearchBox.Text);
        }

        // ── Delete single (normal mode) ───────────────────────────────────────

        private void DeleteConvBtn_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // 阻止冒泡到 Border.Tapped (ConvItem_Tapped)，避免触发进入对话
            e.Handled = true;
        }

        private async void DeleteConvBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectMode) return;
            if (!((sender as Button)?.Tag is ConvViewModel vm)) return;

            var dialog = new ContentDialog
            {
                Title               = "删除对话",
                Content             = $"确定删除「{vm.Conv.Title}」？此操作不可撤销。",
                PrimaryButtonText   = "删除",
                SecondaryButtonText = "取消",
                RequestedTheme      = DialogTheme
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                DataManager.Data.Conversations.Remove(vm.Conv);
                if (AppState.ActiveConversation?.Id == vm.Conv.Id)
                    AppState.ActiveConversation = null;
                await DataManager.SaveAsync();
                RefreshList(SearchBox.Text);
            }
        }

        // ── Export selected ───────────────────────────────────────────────────

        private async void ExportSelectedConv_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedConversations();
            if (selected.Count == 0) return;

            var folder = await ImportExportPage.PickFolder();
            if (folder == null) return;

            int ok = 0;
            foreach (var conv in selected)
            {
                try
                {
                    string content = ImportExportPage.BuildConvJsonlPublic(conv);
                    string safe = ImportExportPage.MakeSafeFilename(conv.Title) + ".jsonl";
                    var file = await folder.CreateFileAsync(safe, Windows.Storage.CreationCollisionOption.GenerateUniqueName);
                    await Windows.Storage.FileIO.WriteTextAsync(file, content);
                    ok++;
                }
                catch { }
            }
            ExitSelectMode();
            var dlg = new ContentDialog
            {
                Title             = "导出完成",
                Content           = $"已导出 {ok}/{selected.Count} 条对话",
                PrimaryButtonText = "好",
                RequestedTheme    = DialogTheme
            };
            await dlg.ShowAsync();
        }

        // ── Delete selected ───────────────────────────────────────────────────

        private async void DeleteSelectedConv_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedConversations();
            if (selected.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title               = "删除对话",
                Content             = $"确定删除选中的 {selected.Count} 条对话？此操作不可撤销。",
                PrimaryButtonText   = "删除",
                SecondaryButtonText = "取消",
                RequestedTheme      = DialogTheme
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            foreach (var conv in selected)
            {
                DataManager.Data.Conversations.Remove(conv);
                if (AppState.ActiveConversation?.Id == conv.Id)
                    AppState.ActiveConversation = null;
            }
            await DataManager.SaveAsync();
            ExitSelectMode();
        }

        // ── Import ────────────────────────────────────────────────────────────

        private async void ImportConv_Click(object sender, RoutedEventArgs e)
        {
            var files = await ImportExportPage.PickFiles(new[] { ".jsonl" });
            if (files == null || files.Count == 0) return;
            int ok = 0;
            foreach (var file in files)
            {
                try
                {
                    string text = await Windows.Storage.FileIO.ReadTextAsync(file);
                    bool imported = await ImportExportPage.ImportConvFromText(text, file.Name);
                    if (imported) ok++;
                }
                catch { }
            }
            await DataManager.SaveAsync();
            RefreshList(SearchBox.Text);
        }

        // ── New conversation ──────────────────────────────────────────────────

        private void NewConvBtn_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectMode) { ExitSelectMode(); return; }
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }

        private static ElementTheme DialogTheme =>
            AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light;
    }
}
