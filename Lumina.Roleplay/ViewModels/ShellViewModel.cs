using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;

namespace yanshuai.ViewModels
{
    /// <summary>侧栏“当前角色对话”按对话池分组的一组。</summary>
    public class ConvPoolGroup
    {
        public string PoolName { get; set; }
        public ObservableCollection<Conversation> Conversations { get; set; } = new ObservableCollection<Conversation>();
    }

    /// <summary>侧栏一个导航项。数据驱动，取代原先 6 个硬编码 Click 处理器。</summary>
    public class NavItem : ViewModelBase
    {
        /// <summary>Segoe MDL2 Assets 码位（如 0xE8F1）。源码用 ASCII 十六进制，避免直接嵌 PUA 字符。</summary>
        public int GlyphCode { get; set; }

        /// <summary>FontIcon 绑定用：由码位转出的字形字符串。</summary>
        public string Glyph
        {
            get { return char.ConvertFromUtf32(GlyphCode); }
        }

        public string Label { get; set; }
        public Type PageType { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (Set(ref _isSelected, value))
                    OnPropertyChanged(nameof(SelectionBarVisibility));
            }
        }

        /// <summary>选中指示条可见性（无 converter，沿用项目既有的 Visibility 计算属性风格）。</summary>
        public Visibility SelectionBarVisibility
        {
            get { return _isSelected ? Visibility.Visible : Visibility.Collapsed; }
        }
    }

    /// <summary>
    /// Shell 的 ViewModel：提供导航项集合 + 选中同步。
    /// 导航动作由 ShellPage 统一处理（NavigationService + 关闭侧栏），保持轻量。
    /// </summary>
    public class ShellViewModel : ViewModelBase
    {
        public ObservableCollection<NavItem> PrimaryItems { get; } = new ObservableCollection<NavItem>();
        public ObservableCollection<NavItem> BottomItems { get; } = new ObservableCollection<NavItem>();

        public string Header { get { return AppSettings.S("言枢 · 角色", "言枢 · Characters"); } }

        public ShellViewModel()
        {
            PrimaryItems.Add(new NavItem { GlyphCode = 0xE8F1, Label = AppSettings.S("当前对话", "Current Chat"),  PageType = typeof(MainPage) });
            PrimaryItems.Add(new NavItem { GlyphCode = 0xE8FD, Label = AppSettings.S("对话列表", "Conversations"), PageType = typeof(ConversationsListPage) });
            PrimaryItems.Add(new NavItem { GlyphCode = 0xE77B, Label = AppSettings.S("角色", "Characters"),        PageType = typeof(CharacterCardsPage) });
            PrimaryItems.Add(new NavItem { GlyphCode = 0xE8B9, Label = AppSettings.S("角色广场", "Character Hub"),  PageType = typeof(NetworkCharaPage) });
            PrimaryItems.Add(new NavItem { GlyphCode = 0xE774, Label = AppSettings.S("API 连接", "API Connection"), PageType = typeof(ApiProfilesPage) });

            BottomItems.Add(new NavItem { GlyphCode = 0xE716, Label = AppSettings.S("用户资料", "User Profile"), PageType = typeof(UserProfilesPage) });
            BottomItems.Add(new NavItem { GlyphCode = 0xE713, Label = AppSettings.S("设置", "Settings"),        PageType = typeof(SettingsPage) });
        }

        /// <summary>根据当前页类型同步选中态（左侧高亮条）。</summary>
        public void UpdateSelection(Type pageType)
        {
            foreach (var it in PrimaryItems) it.IsSelected = it.PageType == pageType;
            foreach (var it in BottomItems)  it.IsSelected = it.PageType == pageType;
        }

        // ── 当前角色的对话（按对话池分类，显示在侧栏） ──────────────────────
        public ObservableCollection<ConvPoolGroup> CurrentCharacterPools { get; } = new ObservableCollection<ConvPoolGroup>();

        private string _currentCharacterHeader = "";
        public string CurrentCharacterHeader
        {
            get { return _currentCharacterHeader; }
            private set { Set(ref _currentCharacterHeader, value); }
        }

        private bool _hasCurrentCharacter;
        public bool HasCurrentCharacter
        {
            get { return _hasCurrentCharacter; }
            private set { if (Set(ref _hasCurrentCharacter, value)) OnPropertyChanged(nameof(CurrentCharacterVisibility)); }
        }
        public Visibility CurrentCharacterVisibility =>
            _hasCurrentCharacter ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>刷新“当前角色对话”侧栏：取活动对话的角色，列出该角色全部对话并按对话池分组。</summary>
        public void RefreshCurrentCharacter()
        {
            CurrentCharacterPools.Clear();

            var active = AppState.ActiveConversation;
            var charId = active?.CharacterCardId;
            if (string.IsNullOrEmpty(charId) || DataManager.Data == null)
            {
                HasCurrentCharacter = false;
                return;
            }

            var card = DataManager.Data.CharacterCards?.Find(c => c.Id == charId);
            CurrentCharacterHeader = AppSettings.S("当前角色 · ", "Current · ")
                                     + (card != null ? card.Name : AppSettings.S("角色", "Character"));

            var allConvs = DataManager.Data.Conversations ?? new List<Conversation>();
            var assigned = new HashSet<string>();

            var pools = (DataManager.Data.DialoguePools ?? new List<DialoguePool>())
                        .Where(p => p.CharacterCardId == charId).ToList();
            foreach (var pool in pools)
            {
                var convs = (pool.ConversationIds ?? new List<string>())
                    .Select(id => allConvs.Find(c => c.Id == id))
                    .Where(c => c != null)
                    .OrderByDescending(c => c.UpdatedAt)
                    .ToList();
                if (convs.Count == 0) continue;

                var g = new ConvPoolGroup
                {
                    PoolName = string.IsNullOrEmpty(pool.PoolName) ? AppSettings.S("对话池", "Pool") : pool.PoolName
                };
                foreach (var c in convs) { g.Conversations.Add(c); assigned.Add(c.Id); }
                CurrentCharacterPools.Add(g);
            }

            // 未归入任何池的该角色对话
            var orphans = allConvs
                .Where(c => c.CharacterCardId == charId && !assigned.Contains(c.Id))
                .OrderByDescending(c => c.UpdatedAt)
                .ToList();
            if (orphans.Count > 0)
            {
                var g = new ConvPoolGroup { PoolName = AppSettings.S("其他", "Other") };
                foreach (var c in orphans) g.Conversations.Add(c);
                CurrentCharacterPools.Add(g);
            }

            HasCurrentCharacter = CurrentCharacterPools.Count > 0;
        }
    }
}
