using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class McpSkillsPage : Page
    {
        public McpSkillsPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            RefreshLists();
        }

        private void RefreshLists()
        {
            McpList.ItemsSource    = null;
            McpList.ItemsSource    = DataManager.Data?.McpServers?.ToList() ?? new List<McpServer>();
            SkillsList.ItemsSource = null;
            SkillsList.ItemsSource = DataManager.Data?.Skills?.ToList()     ?? new List<Skill>();
            UpdateButtons();
        }

        // ── Selection ─────────────────────────────────────────────────────────

        private void McpList_SelectionChanged(object sender, SelectionChangedEventArgs e)    => UpdateButtons();
        private void SkillsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        private bool IsMcpTab => MainPivot.SelectedIndex == 0;
        private object SelectedItem => IsMcpTab ? McpList.SelectedItem : SkillsList.SelectedItem;

        private void UpdateButtons()
        {
            bool hasSel = SelectedItem != null;
            EditBtn.IsEnabled   = hasSel;
            DeleteBtn.IsEnabled = hasSel;
        }

        // ── Add / Edit / Delete ──────────────────────────────────────────────

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsMcpTab)
            {
                var server = await ShowMcpDialog(null);
                if (server == null) return;
                DataManager.Data.McpServers.Add(server);
            }
            else
            {
                var skill = await ShowSkillDialog(null);
                if (skill == null) return;
                DataManager.Data.Skills.Add(skill);
            }
            await DataManager.SaveAsync();
            RefreshLists();
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsMcpTab)
            {
                var src = McpList.SelectedItem as McpServer;
                if (src == null) return;
                var updated = await ShowMcpDialog(src);
                if (updated == null) return;
                src.Name          = updated.Name;
                src.Description   = updated.Description;
                src.TransportType = updated.TransportType;
                src.Endpoint      = updated.Endpoint;
                src.Args          = updated.Args;
                src.AuthToken     = updated.AuthToken;
                src.Enabled       = updated.Enabled;
            }
            else
            {
                var src = SkillsList.SelectedItem as Skill;
                if (src == null) return;
                var updated = await ShowSkillDialog(src);
                if (updated == null) return;
                src.Name           = updated.Name;
                src.Icon           = updated.Icon;
                src.Description    = updated.Description;
                src.Triggers       = updated.Triggers;
                src.PromptTemplate = updated.PromptTemplate;
                src.InjectMode     = updated.InjectMode;
                src.Enabled        = updated.Enabled;
            }
            await DataManager.SaveAsync();
            RefreshLists();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            string targetName = IsMcpTab ? (McpList.SelectedItem as McpServer)?.Name
                                         : (SkillsList.SelectedItem as Skill)?.Name;
            if (string.IsNullOrEmpty(targetName)) return;

            var confirm = new ContentDialog
            {
                Title               = "删除",
                Content             = $"确定删除「{targetName}」？",
                PrimaryButtonText   = "删除",
                SecondaryButtonText = "取消",
                RequestedTheme      = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            if (IsMcpTab)
                DataManager.Data.McpServers.Remove(McpList.SelectedItem as McpServer);
            else
                DataManager.Data.Skills.Remove(SkillsList.SelectedItem as Skill);

            await DataManager.SaveAsync();
            RefreshLists();
        }

        // ── Toggle handlers ──────────────────────────────────────────────────

        private async void McpToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var sv = sender as ToggleSwitch;
            if (sv?.Tag is McpServer m) m.Enabled = sv.IsOn;
            await DataManager.SaveAsync();
        }

        private async void SkillToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var sv = sender as ToggleSwitch;
            if (sv?.Tag is Skill s) s.Enabled = sv.IsOn;
            await DataManager.SaveAsync();
        }

        // ── MCP edit dialog ──────────────────────────────────────────────────

        private async Task<McpServer> ShowMcpDialog(McpServer source)
        {
            bool isNew = source == null;
            var nameBox = new TextBox { Header = "名称", Text = source?.Name ?? "", PlaceholderText = "例：本地文件 MCP", Margin = new Thickness(0, 0, 0, 8) };
            var descBox = new TextBox { Header = "描述", Text = source?.Description ?? "", PlaceholderText = "可选", Margin = new Thickness(0, 0, 0, 8) };

            var transportPicker = new ComboBox
            {
                Header = "传输方式",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
            };
            transportPicker.Items.Add(new ComboBoxItem { Content = "HTTP / SSE", Tag = "http" });
            transportPicker.Items.Add(new ComboBoxItem { Content = "WebSocket", Tag = "websocket" });
            transportPicker.Items.Add(new ComboBoxItem { Content = "Stdio (本地命令)", Tag = "stdio" });
            string curT = source?.TransportType ?? "http";
            transportPicker.SelectedIndex = curT == "websocket" ? 1 : (curT == "stdio" ? 2 : 0);

            var endpointBox = new TextBox { Header = "端点 / 命令", Text = source?.Endpoint ?? "", PlaceholderText = "https://… 或 命令路径", Margin = new Thickness(0, 0, 0, 8) };
            var argsBox     = new TextBox { Header = "参数 (Stdio) / 请求头 JSON (HTTP)", Text = source?.Args ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, Margin = new Thickness(0, 0, 0, 8) };
            var tokenBox    = new TextBox { Header = "认证令牌（可选）", Text = source?.AuthToken ?? "", PlaceholderText = "Bearer ... 或 X-API-Key", Margin = new Thickness(0, 0, 0, 8) };
            var enabledTog  = new ToggleSwitch { Header = "启用", IsOn = source?.Enabled ?? true, Margin = new Thickness(0, 0, 0, 4) };

            var panel = new StackPanel { Width = 340 };
            panel.Children.Add(nameBox);
            panel.Children.Add(descBox);
            panel.Children.Add(transportPicker);
            panel.Children.Add(endpointBox);
            panel.Children.Add(argsBox);
            panel.Children.Add(tokenBox);
            panel.Children.Add(enabledTog);

            var dialog = new ContentDialog
            {
                Title               = isNew ? "新增 MCP 服务器" : "编辑 MCP 服务器",
                Content             = new ScrollViewer { Content = panel, MaxHeight = 520 },
                PrimaryButtonText   = "保存",
                SecondaryButtonText = "取消",
                RequestedTheme      = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

            return new McpServer
            {
                Id            = source?.Id ?? Guid.NewGuid().ToString(),
                Name          = string.IsNullOrWhiteSpace(nameBox.Text) ? "新 MCP 服务器" : nameBox.Text.Trim(),
                Description   = descBox.Text?.Trim() ?? "",
                TransportType = (transportPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "http",
                Endpoint      = endpointBox.Text?.Trim() ?? "",
                Args          = argsBox.Text?.Trim() ?? "",
                AuthToken     = tokenBox.Text?.Trim() ?? "",
                Enabled       = enabledTog.IsOn,
                CreatedAt     = source?.CreatedAt ?? DateTime.Now,
            };
        }

        // ── Skill edit dialog ────────────────────────────────────────────────

        private async Task<Skill> ShowSkillDialog(Skill source)
        {
            bool isNew = source == null;
            var nameBox = new TextBox { Header = "名称", Text = source?.Name ?? "", PlaceholderText = "例：翻译 / 总结", Margin = new Thickness(0, 0, 0, 8) };
            var iconBox = new TextBox { Header = "图标 (emoji)", Text = source?.Icon ?? "✨", MaxLength = 4, Margin = new Thickness(0, 0, 0, 8) };
            var descBox = new TextBox { Header = "描述", Text = source?.Description ?? "", Margin = new Thickness(0, 0, 0, 8) };
            var trigBox = new TextBox { Header = "触发词（逗号分隔）", Text = source?.Triggers ?? "", PlaceholderText = "翻译,translate", Margin = new Thickness(0, 0, 0, 8) };

            var modePicker = new ComboBox
            {
                Header = "注入方式",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
            };
            modePicker.Items.Add(new ComboBoxItem { Content = "前缀（附加到用户消息前）", Tag = "prefix" });
            modePicker.Items.Add(new ComboBoxItem { Content = "系统提示（注入到 system）", Tag = "system" });
            modePicker.Items.Add(new ComboBoxItem { Content = "替换（替换原消息）", Tag = "replace" });
            string mode = source?.InjectMode ?? "prefix";
            modePicker.SelectedIndex = mode == "system" ? 1 : (mode == "replace" ? 2 : 0);

            var promptBox = new TextBox
            {
                Header = "提示词模板（可使用 {input} 占位用户输入）",
                Text = source?.PromptTemplate ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                Margin = new Thickness(0, 0, 0, 8),
            };

            var enabledTog = new ToggleSwitch { Header = "启用", IsOn = source?.Enabled ?? true, Margin = new Thickness(0, 0, 0, 4) };

            var panel = new StackPanel { Width = 340 };
            panel.Children.Add(nameBox);
            panel.Children.Add(iconBox);
            panel.Children.Add(descBox);
            panel.Children.Add(trigBox);
            panel.Children.Add(modePicker);
            panel.Children.Add(promptBox);
            panel.Children.Add(enabledTog);

            var dialog = new ContentDialog
            {
                Title               = isNew ? "新增技能" : "编辑技能",
                Content             = new ScrollViewer { Content = panel, MaxHeight = 520 },
                PrimaryButtonText   = "保存",
                SecondaryButtonText = "取消",
                RequestedTheme      = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

            return new Skill
            {
                Id             = source?.Id ?? Guid.NewGuid().ToString(),
                Name           = string.IsNullOrWhiteSpace(nameBox.Text) ? "新技能" : nameBox.Text.Trim(),
                Icon           = string.IsNullOrWhiteSpace(iconBox.Text) ? "✨" : iconBox.Text.Trim(),
                Description    = descBox.Text?.Trim() ?? "",
                Triggers       = trigBox.Text?.Trim() ?? "",
                InjectMode     = (modePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "prefix",
                PromptTemplate = promptBox.Text?.Trim() ?? "",
                Enabled        = enabledTog.IsOn,
                CreatedAt      = source?.CreatedAt ?? DateTime.Now,
            };
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }
    }
}
