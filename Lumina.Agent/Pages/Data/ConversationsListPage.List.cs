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

    }
}
