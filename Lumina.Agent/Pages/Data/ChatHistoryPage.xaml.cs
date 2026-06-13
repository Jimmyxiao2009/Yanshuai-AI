using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    // ChatHistoryPage has been superseded by ConversationsListPage.
    // This stub immediately redirects.
    public sealed partial class ChatHistoryPage : Page
    {
        public ChatHistoryPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Frame.Navigate(typeof(ConversationsListPage));
        }
    }
}
