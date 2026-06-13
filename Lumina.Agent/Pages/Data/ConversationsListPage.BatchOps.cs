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
    public sealed partial class ConversationsListPage : Page
    {
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

    }
}
