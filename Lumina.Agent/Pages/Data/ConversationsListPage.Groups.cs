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

    }
}
