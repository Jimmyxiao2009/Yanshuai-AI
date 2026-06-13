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

    }
}
