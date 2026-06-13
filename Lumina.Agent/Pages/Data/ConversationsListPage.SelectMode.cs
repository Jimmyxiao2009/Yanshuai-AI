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

    }
}
