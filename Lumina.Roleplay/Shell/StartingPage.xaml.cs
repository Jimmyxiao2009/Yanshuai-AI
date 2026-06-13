using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml;

namespace yanshuai
{
    // Startup page removed. This stub redirects immediately.
    public sealed partial class StartingPage : Page
    {
        public StartingPage() { InitializeComponent(); }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Frame.Navigate(typeof(ShellPage));
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ShellPage));
        }
    }
}
