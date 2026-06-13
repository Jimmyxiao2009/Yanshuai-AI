using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class ApiLogPage : Page
    {
        private readonly ObservableCollection<ApiLogEntry> _items = new ObservableCollection<ApiLogEntry>();
        private ApiLogEntry _selected;
        private bool _showingRequest = true;

        public ApiLogPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            Reload();
        }

        private void Reload()
        {
            _items.Clear();
            foreach (var entry in ApiLogger.Entries)
                _items.Add(entry);
            LogList.ItemsSource = _items;
            EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LogList.Visibility    = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = LogList.SelectedItem as ApiLogEntry;
            if (_selected == null) { DetailPanel.Visibility = Visibility.Collapsed; return; }
            DetailPanel.Visibility = Visibility.Visible;
            _showingRequest = true;
            UpdateDetailTab();
        }

        private void ShowRequestBtn_Click(object sender, RoutedEventArgs e)
        {
            _showingRequest = true;
            UpdateDetailTab();
        }

        private void ShowResponseBtn_Click(object sender, RoutedEventArgs e)
        {
            _showingRequest = false;
            UpdateDetailTab();
        }

        private void UpdateDetailTab()
        {
            if (_selected == null) return;
            DetailText.Text = _showingRequest
                ? (_selected.RequestJson ?? "（无）")
                : (_selected.ResponseBody ?? "（无）");

            var accent = Application.Current.Resources["SystemAccentColor"];
            ShowRequestBtn.Background  = _showingRequest  ? new Windows.UI.Xaml.Media.SolidColorBrush((Windows.UI.Color)accent) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            ShowResponseBtn.Background = !_showingRequest ? new Windows.UI.Xaml.Media.SolidColorBrush((Windows.UI.Color)accent) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            ShowRequestBtn.Foreground  = _showingRequest  ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White) : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
            ShowResponseBtn.Foreground = !_showingRequest ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White) : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
        }

        private void CopyDetailBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            string text = _showingRequest ? _selected.RequestJson : _selected.ResponseBody;
            if (string.IsNullOrEmpty(text)) return;
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            ApiLogger.Clear();
            _selected = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            Reload();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(SettingsPage));
        }
    }
}
