using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;

namespace yanshuai.ViewModels
{
    /// <summary>
    /// 对话列表（master）的 ViewModel。包装 DataManager.Data.Conversations，
    /// 置顶优先、按更新时间倒序。Conversation 自带 Title / LastMessagePreview /
    /// UpdatedAtDisplay / PinIconVisibility，可直接绑定。
    /// </summary>
    public class ConversationsViewModel : ViewModelBase
    {
        public ObservableCollection<Conversation> Items { get; } = new ObservableCollection<Conversation>();

        private bool _isEmpty;
        public bool IsEmpty
        {
            get { return _isEmpty; }
            set
            {
                if (Set(ref _isEmpty, value))
                    OnPropertyChanged(nameof(EmptyVisibility));
            }
        }

        public Visibility EmptyVisibility
        {
            get { return _isEmpty ? Visibility.Visible : Visibility.Collapsed; }
        }

        public void Load()
        {
            Items.Clear();
            var convs = DataManager.Data?.Conversations ?? new List<Conversation>();
            foreach (var c in convs.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.UpdatedAt))
                Items.Add(c);
            IsEmpty = Items.Count == 0;
        }
    }
}
