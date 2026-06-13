using System;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using yanshuai.Services;
using yanshuai.ViewModels;

namespace yanshuai
{
    public sealed partial class ShellPage : Page
    {
        public static ShellPage Current { get; private set; }

        public Frame ContentFrame => ShellFrame;

        private readonly ShellViewModel _vm = new ShellViewModel();

        public ShellPage()
        {
            InitializeComponent();
            DataContext = _vm;

            // 注册内容 Frame，供 NavigationService 使用（取代散落的静态导航调用）
            NavigationService.Instance.Register(ShellFrame);

            ShellFrame.Navigated += OnFrameNavigated;
        }

        private void OnFrameNavigated(object sender, NavigationEventArgs e)
        {
            // 窄屏导航后收起浮层侧栏；宽屏常驻不动
            if (ShellSplitView.DisplayMode == SplitViewDisplayMode.Overlay)
                ShellSplitView.IsPaneOpen = false;

            _vm.UpdateSelection(e.SourcePageType);
            // 刷新侧栏“当前角色对话”（活动对话在目标页 OnNavigatedTo 中已确定）
            _vm.RefreshCurrentCharacter();

            // MainPage 自己通过 SetTopContent 注入顶栏；其余页面显示标题
            if (e.SourcePageType != typeof(MainPage))
                SetTitle(PageTitle(e.SourcePageType));
        }

        private static string PageTitle(Type t)
        {
            if (t == typeof(ApiProfilesPage))        return AppSettings.S("API 连接", "API Profiles");
            if (t == typeof(CharacterCardsPage))     return AppSettings.S("角色卡", "Characters");
            if (t == typeof(NetworkCharaPage))       return AppSettings.S("角色广场", "Character Plaza");
            if (t == typeof(ConversationsListPage))  return AppSettings.S("对话列表", "Conversations");
            if (t == typeof(ThemeSettingsPage))      return AppSettings.S("主题与字体", "Theme & Fonts");
            if (t == typeof(SettingsPage))           return AppSettings.S("设置", "Settings");
            if (t == typeof(InfoPage))               return AppSettings.S("关于", "About");
            if (t == typeof(ConvSettingsPage))       return AppSettings.S("对话设置", "Chat Settings");
            if (t == typeof(ConvAppearancePage))     return AppSettings.S("外观", "Appearance");
            if (t == typeof(ImportExportPage))       return AppSettings.S("导入 / 导出", "Import / Export");
            if (t == typeof(UserProfilesPage))       return AppSettings.S("用户档案", "User Profiles");
            if (t == typeof(UserProfilePage))        return AppSettings.S("用户档案", "User Profile");
            if (t == typeof(CharaWizardPage1) || t == typeof(CharaWizardPage2) ||
                t == typeof(CharaWizardPage3) || t == typeof(CharaWizardPage4))
                                                     return AppSettings.S("创建角色卡", "New Character");
            return AppSettings.S("言枢", "Yanshuai");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Current = this;
            NavigationService.Instance.Register(ShellFrame);

            if (ShellFrame.Content == null)
                ShellFrame.Navigate(typeof(MainPage));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (Current == this)
                Current = null;
        }

        // ── 公共 API（其他页面依赖，保持不变） ─────────────────────────────
        public void SetTitle(string title)
        {
            TopBarContent.Content = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 16,
                FontWeight = FontWeights.Light,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public void SetTopContent(UIElement content)
        {
            TopBarContent.Content = content;
        }

        public void ClosePane()
        {
            ShellSplitView.IsPaneOpen = false;
        }

        // ── 交互 ───────────────────────────────────────────────────────────
        private void Hamburger_Click(object sender, RoutedEventArgs e)
        {
            ShellSplitView.IsPaneOpen = !ShellSplitView.IsPaneOpen;
        }

        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.Tag as NavItem;
            if (item == null || item.PageType == null) return;

            if (ShellFrame.CurrentSourcePageType != item.PageType)
                ShellFrame.Navigate(item.PageType);

            if (ShellSplitView.DisplayMode == SplitViewDisplayMode.Overlay)
                ShellSplitView.IsPaneOpen = false;

            _ = DataManager.SaveAsync();
        }

        // 侧栏点击某条“当前角色对话” → 切换到该对话并重载聊天页
        private void ConvItem_Click(object sender, RoutedEventArgs e)
        {
            var conv = (sender as FrameworkElement)?.Tag as Conversation;
            if (conv == null) return;

            AppState.ActiveConversation = conv;
            if (DataManager.Data != null)
                DataManager.Data.LastActiveConversationId = conv.Id;

            // 即便已在 MainPage 也重新导航以重载该对话
            ShellFrame.Navigate(typeof(MainPage));

            if (ShellSplitView.DisplayMode == SplitViewDisplayMode.Overlay)
                ShellSplitView.IsPaneOpen = false;
        }
    }
}
