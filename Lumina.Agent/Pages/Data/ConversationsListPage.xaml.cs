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
    }
}
