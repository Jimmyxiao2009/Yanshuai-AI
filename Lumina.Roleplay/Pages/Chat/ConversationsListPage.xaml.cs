using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using yanshuai.ViewModels;

namespace yanshuai
{
    /// <summary>对话列表页（master）。点选打开对话，新建按钮创建对话。</summary>
    public sealed partial class ConversationsListPage : Page
    {
        private readonly ConversationsViewModel _vm = new ConversationsViewModel();

        public ConversationsListPage()
        {
            InitializeComponent();
            DataContext = _vm;
            NewChatLabel.Text = AppSettings.S("新建对话", "New Chat");
            EmptyLabel.Text = AppSettings.S("还没有对话，点上方新建一个吧", "No conversations yet — start one above");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _vm.Load();
        }

        private void Conv_ItemClick(object sender, ItemClickEventArgs e)
        {
            var conv = e.ClickedItem as Conversation;
            if (conv == null) return;
            AppState.ActiveConversation = conv;
            if (DataManager.Data != null)
                DataManager.Data.LastActiveConversationId = conv.Id;
            Frame.Navigate(typeof(MainPage));
        }

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            var conv = DataManager.CreateNewConversation();
            AppState.ActiveConversation = conv;
            _ = DataManager.SaveAsync();
            Frame.Navigate(typeof(MainPage));
        }
    }
}
