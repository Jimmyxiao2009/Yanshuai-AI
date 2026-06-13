using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class ProjectSettingsPage : Page
    {
        private Project _project;

        public ProjectSettingsPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            string projectId = e.Parameter as string;
            if (string.IsNullOrEmpty(projectId)) { GoBack(); return; }

            _project = DataManager.Data.Projects?.FirstOrDefault(p => p.Id == projectId);
            if (_project == null) { GoBack(); return; }

            LoadProjectData();
            RefreshConvList();
            RefreshMemoryList();
        }

        // ── Load project ────────────────────────────────────────────────────

        private void LoadProjectData()
        {
            ProjectTitle.Text = _project.Name ?? "项目";
            NameBox.Text = _project.Name ?? "";
            DescBox.Text = _project.Description ?? "";
            SysPromptBox.Text = _project.SystemPrompt ?? "";
            IconPreview.Text = string.IsNullOrEmpty(_project.IconGlyph) ? "\uE8B7" : _project.IconGlyph;

            // 常用 MDL2 图标
            IconPickerList.ItemsSource = new List<string>
            {
                "\uE8B7", "\uE8F1", "\uE8BD", "\uE8A5", "\uE7C3",
                "\uE783", "\uE716", "\uE74E", "\uE774", "\uE736",
                "\uE80F", "\uE896", "\uE7BA", "\uE817", "\uE7C8",
                "\uE738", "\uE818", "\uE7C4", "\uE82D", "\uE8A1",
                "\uE785", "\uE730", "\uE8A4", "\uE790", "\uE71A",
                "\uE7F4", "\uE814", "\uE802", "\uE8B2", "\uE7C1",
            };
        }

        // ── Conversations tab ────────────────────────────────────────────────

        private void RefreshConvList()
        {
            var convIds = _project.ConversationIds ?? new List<string>();
            var convs = DataManager.Data.Conversations
                .Where(c => convIds.Contains(c.Id))
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ConvItemVm(c))
                .ToList();

            ConvList.ItemsSource = convs;
            ConvEmptyState.Visibility = convs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ConvList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = e.ClickedItem as ConvItemVm;
            if (vm == null) return;
            var conv = DataManager.Data.Conversations.FirstOrDefault(c => c.Id == vm.Id);
            if (conv == null) return;
            AppState.ActiveConversation = conv;
            Frame.Navigate(typeof(MainPage));
        }

        private async void ConvDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as ConvItemVm;
            if (vm == null) return;

            var dlg = new ContentDialog
            {
                Title = "移除对话",
                Content = $"确定从项目中移除「{vm.Title}」？对话本身不会被删除。",
                PrimaryButtonText = "移除",
                SecondaryButtonText = "取消",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            _project.ConversationIds?.Remove(vm.Id);
            var conv = DataManager.Data.Conversations.FirstOrDefault(c => c.Id == vm.Id);
            if (conv != null) conv.ProjectId = "";
            _project.UpdatedAt = DateTime.Now;
            await DataManager.SaveAsync();
            RefreshConvList();
        }

        // ── Settings tab: basic fields ───────────────────────────────────────

        private void IconBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var glyph = btn?.Tag as string;
            if (string.IsNullOrEmpty(glyph) || _project == null) return;
            _project.IconGlyph = glyph;
            IconPreview.Text = glyph;
            ProjectTitle.Text = _project.Name ?? "项目";
            _project.UpdatedAt = DateTime.Now;
            _ = DataManager.SaveAsync();
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_project == null) return;
            _project.Name = NameBox.Text;
            ProjectTitle.Text = _project.Name ?? "项目";
            _project.UpdatedAt = DateTime.Now;
            _ = DataManager.SaveAsync();
        }

        private void DescBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_project == null) return;
            _project.Description = DescBox.Text;
            _project.UpdatedAt = DateTime.Now;
            _ = DataManager.SaveAsync();
        }

        private void SysPromptBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_project == null) return;
            _project.SystemPrompt = SysPromptBox.Text;
            _project.UpdatedAt = DateTime.Now;
            _ = DataManager.SaveAsync();
        }

        // ── Knowledge base ──────────────────────────────────────────────────

        private void RefreshKbList()
        {
            if (_project.KnowledgeFiles == null)
                _project.KnowledgeFiles = new List<string>();

            var items = _project.KnowledgeFiles.Select(kf =>
            {
                int sep = kf.IndexOf('\x01');
                string name = sep > 0 ? kf.Substring(0, sep) : kf;
                return new KbFileVm { Name = name, RawEntry = kf };
            }).ToList();
            KbFileList.ItemsSource = items;
        }

        private async void AddKbFileBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            string[] exts = { ".txt", ".md", ".csv", ".json", ".xml", ".html",
                              ".cs", ".py", ".js", ".ts", ".yaml", ".yml", ".toml" };
            foreach (var ext in exts) picker.FileTypeFilter.Add(ext);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            foreach (var file in files)
            {
                try
                {
                    string content = await Windows.Storage.FileIO.ReadTextAsync(file);
                    if (content.Length > 30000)
                        content = content.Substring(0, 30000) + "\n...[已截断]";
                    _project.KnowledgeFiles.Add(file.Name + "\x01" + content);
                }
                catch { }
            }
            _project.UpdatedAt = DateTime.Now;
            _ = DataManager.SaveAsync();
            RefreshKbList();
        }

        private void RemoveKbFile_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as KbFileVm;
            if (vm == null || _project == null) return;
            _project.KnowledgeFiles.Remove(vm.RawEntry);
            _project.UpdatedAt = DateTime.Now;
            _ = DataManager.SaveAsync();
            RefreshKbList();
        }

        // ── Memory tab ─────────────────────────────────────────────────────

        private void RefreshMemoryList()
        {
            if (_project.ProjectMemories == null)
                _project.ProjectMemories = new List<MemoryItem>();

            var items = _project.ProjectMemories
                .OrderByDescending(m => m.Timestamp)
                .Select(m => new MemoryItemVm(m))
                .ToList();
            MemoryList.ItemsSource = items;
            MemoryCountLabel.Text = $"{items.Count} 条";
        }

        private async void AddMemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            string category = "general";
            double importance = 0.5;
            string text = "";

            var categoryPicker = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
            };
            string[] cats = { "general", "fact", "preference", "event", "instruction" };
            string[] catLabels = { "通用", "事实", "偏好", "事件", "指令" };
            for (int i = 0; i < cats.Length; i++)
                categoryPicker.Items.Add(new ComboBoxItem { Content = catLabels[i], Tag = cats[i] });
            categoryPicker.SelectedIndex = 0;

            var importanceSlider = new Slider
            {
                Minimum = 0, Maximum = 1, StepFrequency = 0.1,
                Value = 0.5, Header = "重要性",
                Margin = new Thickness(0, 0, 0, 8),
            };

            var textBox = new TextBox
            {
                Header = "记忆内容",
                PlaceholderText = "例：用户偏好使用深色主题",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
                MaxHeight = 200,
            };

            var panel = new StackPanel { Width = 340 };
            panel.Children.Add(categoryPicker);
            panel.Children.Add(importanceSlider);
            panel.Children.Add(textBox);

            var dlg = new ContentDialog
            {
                Title = "添加记忆",
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                PrimaryButtonText = "添加",
                SecondaryButtonText = "取消",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            text = textBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;
            var selCat = categoryPicker.SelectedItem as ComboBoxItem;
            category = selCat?.Tag as string ?? "general";
            importance = importanceSlider.Value;

            var item = new MemoryItem
            {
                Text = text,
                Category = category,
                Importance = importance,
                Timestamp = DateTime.Now,
            };
            _project.ProjectMemories.Add(item);
            _project.UpdatedAt = DateTime.Now;
            await DataManager.SaveAsync();
            RefreshMemoryList();
        }

        private async void EditMemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as MemoryItemVm;
            if (vm == null) return;

            var item = _project.ProjectMemories.FirstOrDefault(m => m.Id == vm.Id);
            if (item == null) return;

            var categoryPicker = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
            };
            string[] cats = { "general", "fact", "preference", "event", "instruction" };
            string[] catLabels = { "通用", "事实", "偏好", "事件", "指令" };
            for (int i = 0; i < cats.Length; i++)
                categoryPicker.Items.Add(new ComboBoxItem { Content = catLabels[i], Tag = cats[i] });
            // 选中当前分类
            for (int i = 0; i < cats.Length; i++)
                if (cats[i] == item.Category) { categoryPicker.SelectedIndex = i; break; }
            if (categoryPicker.SelectedIndex < 0) categoryPicker.SelectedIndex = 0;

            var importanceSlider = new Slider
            {
                Minimum = 0, Maximum = 1, StepFrequency = 0.1,
                Value = item.Importance, Header = "重要性",
                Margin = new Thickness(0, 0, 0, 8),
            };

            var textBox = new TextBox
            {
                Header = "记忆内容",
                Text = item.Text ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
                MaxHeight = 200,
            };

            var panel = new StackPanel { Width = 340 };
            panel.Children.Add(categoryPicker);
            panel.Children.Add(importanceSlider);
            panel.Children.Add(textBox);

            var dlg = new ContentDialog
            {
                Title = "编辑记忆",
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                PrimaryButtonText = "保存",
                SecondaryButtonText = "取消",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            string newText = textBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(newText)) return;

            var selCat = categoryPicker.SelectedItem as ComboBoxItem;
            item.Text = newText;
            item.Category = selCat?.Tag as string ?? "general";
            item.Importance = importanceSlider.Value;
            item.Timestamp = DateTime.Now; // 更新时间戳表示刚被编辑
            _project.UpdatedAt = DateTime.Now;
            await DataManager.SaveAsync();
            RefreshMemoryList();
        }

        private async void RemoveMemoryBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var vm = btn?.Tag as MemoryItemVm;
            if (vm == null) return;

            var item = _project.ProjectMemories.FirstOrDefault(m => m.Id == vm.Id);
            if (item == null) return;

            _project.ProjectMemories.Remove(item);
            _project.UpdatedAt = DateTime.Now;
            await DataManager.SaveAsync();
            RefreshMemoryList();
        }

        // ── New conversation in project ──────────────────────────────────────

        private void NewConvInProject_Click(object sender, RoutedEventArgs e)
        {
            var conv = new Conversation
            {
                Title = "新对话",
                ProjectId = _project.Id,
                ApiProfileId = !string.IsNullOrEmpty(_project.ApiProfileId)
                    ? _project.ApiProfileId
                    : DataManager.Data.SelectedApiProfileId ?? "",
            };
            DataManager.Data.Conversations.Add(conv);
            if (_project.ConversationIds == null)
                _project.ConversationIds = new List<string>();
            _project.ConversationIds.Add(conv.Id);
            _ = DataManager.SaveAsync();

            AppState.ActiveConversation = conv;
            Frame.Navigate(typeof(MainPage));
        }

        // ── Delete project ───────────────────────────────────────────────────

        private async void DeleteProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ContentDialog
            {
                Title = "删除项目",
                Content = "确定要删除项目「" + (_project.Name ?? "") + "」吗？关联的对话不会被删除。",
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            DataManager.Data.Projects.Remove(_project);
            foreach (var conv in DataManager.Data.Conversations)
            {
                if (conv.ProjectId == _project.Id)
                    conv.ProjectId = "";
            }
            await DataManager.SaveAsync();
            GoBack();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) => GoBack();

        private void GoBack()
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(ProjectsListPage));
        }
    }

    // ── View models ──────────────────────────────────────────────────────────

    internal class ConvItemVm
    {
        private readonly Conversation _c;
        public ConvItemVm(Conversation c) { _c = c; }

        public string Id => _c.Id;
        public string Title => _c.Title ?? "对话";
        public string Preview
        {
            get
            {
                if (_c.Messages == null || _c.Messages.Count == 0) return "空对话";
                var text = _c.Messages[_c.Messages.Count - 1].Content ?? "";
                text = System.Text.RegularExpressions.Regex.Replace(text, @"[*_~`#>\[\]()!]", "");
                return text.Length > 50 ? text.Substring(0, 50) + "\u2026" : text;
            }
        }
        public string DateLabel
        {
            get
            {
                var now = DateTime.Now;
                if (_c.UpdatedAt.Date == now.Date) return _c.UpdatedAt.ToString("HH:mm");
                if ((now - _c.UpdatedAt).TotalDays < 7) return _c.UpdatedAt.ToString("M/d");
                return _c.UpdatedAt.ToString("MM/dd");
            }
        }
    }

    internal class MemoryItemVm
    {
        private readonly MemoryItem _m;
        public MemoryItemVm(MemoryItem m) { _m = m; }

        public string Id => _m.Id;
        public string Text => _m.Text ?? "";
        public string CategoryLabel
        {
            get
            {
                switch (_m.Category)
                {
                    case "fact":        return "事实";
                    case "preference":  return "偏好";
                    case "event":       return "事件";
                    case "instruction": return "指令";
                    default:            return "通用";
                }
            }
        }
        public string ImportanceLabel
        {
            get
            {
                if (_m.Importance >= 0.8) return "重要";
                if (_m.Importance >= 0.5) return "一般";
                return "次要";
            }
        }
    }

    internal class KbFileVm
    {
        public string Name { get; set; }
        public string RawEntry { get; set; }
    }
}
