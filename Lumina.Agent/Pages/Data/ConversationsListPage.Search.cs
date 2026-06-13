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

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ConvList.ItemsSource = _flatList;
            RefreshList();
        }

        // ── Search ────────────────────────────────────────────────────────────

        private bool _searchOpen = false;

        private void SearchToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _searchOpen = !_searchOpen;
            SearchBar.Visibility = _searchOpen ? Visibility.Visible : Visibility.Collapsed;
            if (_searchOpen)
            {
                SearchBox.Focus(FocusState.Programmatic);
            }
            else
            {
                SearchBox.Text = "";
                ClearSearchBtn.Visibility = Visibility.Collapsed;
                RefreshList();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = SearchBox.Text;
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;
            RefreshList(q);
        }

        private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            ClearSearchBtn.Visibility = Visibility.Collapsed;
            RefreshList();
        }

        // ── List refresh ──────────────────────────────────────────────────────

    }
}
