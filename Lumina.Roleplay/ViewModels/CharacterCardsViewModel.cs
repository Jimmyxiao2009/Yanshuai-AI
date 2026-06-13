using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace yanshuai.ViewModels
{
    /// <summary>单张角色卡的视图模型：包装 CharacterCard，提供绑定属性 + 惰性头像加载。</summary>
    public class CardItemViewModel : ViewModelBase
    {
        public CharacterCard Card { get; }

        public CardItemViewModel(CharacterCard card) { Card = card; }

        public string Name => Card.Name;

        public string Subtitle
        {
            get
            {
                var d = !string.IsNullOrEmpty(Card.Description) ? Card.Description : Card.Personality;
                return d ?? "";
            }
        }

        public Visibility PinVisibility   => Card.IsPinned ? Visibility.Visible : Visibility.Collapsed;
        public Visibility R18GVisibility  => Card.IsR18G   ? Visibility.Visible : Visibility.Collapsed;

        public int ConvCount =>
            DataManager.Data?.Conversations?.Count(c => c.CharacterCardId == Card.Id) ?? 0;
        public string ConvCountText =>
            ConvCount > 0 ? string.Format(AppSettings.S("{0} 段对话", "{0} chats"), ConvCount) : "";
        public Visibility ConvCountVisibility =>
            ConvCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        private ImageBrush _avatar;
        /// <summary>头像画刷（ImageBrush）。绑到圆角 Border 背景即自动裁成圆形。</summary>
        public ImageBrush Avatar
        {
            get { return _avatar; }
            private set
            {
                if (Set(ref _avatar, value))
                    OnPropertyChanged(nameof(PlaceholderVisibility));
            }
        }
        public Visibility PlaceholderVisibility => _avatar == null ? Visibility.Visible : Visibility.Collapsed;

        private bool _avatarRequested;
        /// <summary>惰性加载头像（卡片实现时由 CharacterCardView 调用，配合虚拟化）。</summary>
        public async void EnsureAvatar()
        {
            if (_avatarRequested) return;
            _avatarRequested = true;
            if (!Card.HasAvatar) return;
            try
            {
                byte[] bytes = Convert.FromBase64String(Card.AvatarBase64);
                var bmp = new BitmapImage();
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    var dw = new Windows.Storage.Streams.DataWriter(ms);
                    dw.WriteBytes(bytes);
                    await dw.StoreAsync();
                    await ms.FlushAsync();
                    ms.Seek(0);
                    await bmp.SetSourceAsync(ms);
                }
                Avatar = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
            }
            catch { }
        }
    }

    /// <summary>分组（CollectionViewSource 分组用）：本身就是其卡片的集合，Key 作为组头上下文。</summary>
    public class CardGroup : ObservableCollection<CardItemViewModel>
    {
        public string Key { get; }
        public CardGroup(string key) { Key = key; }
        public int CardCount => Count;
    }

    /// <summary>角色卡画廊 ViewModel：按 GroupName 分组，未分组排最后。</summary>
    public class CharacterCardsViewModel : ViewModelBase
    {
        public ObservableCollection<CardGroup> Groups { get; } = new ObservableCollection<CardGroup>();

        private bool _isSelectMode;
        public bool IsSelectMode { get { return _isSelectMode; } set { Set(ref _isSelectMode, value); } }

        private bool _isEmpty;
        public bool IsEmpty
        {
            get { return _isEmpty; }
            set { if (Set(ref _isEmpty, value)) OnPropertyChanged(nameof(EmptyVisibility)); }
        }
        public Visibility EmptyVisibility => _isEmpty ? Visibility.Visible : Visibility.Collapsed;

        public void Load()
        {
            Groups.Clear();
            var cards = DataManager.Data?.CharacterCards ?? new List<CharacterCard>();
            IsEmpty = cards.Count == 0;
            if (IsEmpty) return;

            string ungrouped = AppSettings.S("未分组", "Ungrouped");
            var grouped = cards
                .GroupBy(c => string.IsNullOrEmpty(c.GroupName) ? ungrouped : c.GroupName)
                // 未分组排最后；其余按名称；组内置顶优先再按名称
                .OrderBy(g => g.Key == ungrouped ? "￿" : g.Key, StringComparer.CurrentCulture);

            foreach (var g in grouped)
            {
                var grp = new CardGroup(g.Key);
                foreach (var card in g.OrderByDescending(c => c.IsPinned).ThenBy(c => c.Name, StringComparer.CurrentCulture))
                    grp.Add(new CardItemViewModel(card));
                Groups.Add(grp);
            }
        }
    }
}
