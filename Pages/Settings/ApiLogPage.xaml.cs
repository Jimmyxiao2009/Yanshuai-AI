using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    // 列表 ViewModel：绑定颜色边框
    public class ApiLogEntryVm
    {
        public ApiLogEntry Entry { get; set; }
        public string Title       => Entry.Title;
        public string SubTitle    => Entry.SubTitle;
        public Brush  TypeAccentColor
        {
            get
            {
                if (Entry.EntryType == ApiLogEntryType.ToolCall)
                    return Entry.IsError
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 80, 60))
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 170, 100));
                return Entry.IsError
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 80, 60))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212));
            }
        }
    }

    public sealed partial class ApiLogPage : Page
    {
        private readonly ObservableCollection<ApiLogEntryVm> _items = new ObservableCollection<ApiLogEntryVm>();
        private ApiLogEntry _selected;
        private enum DetailTab { Request, Response, Tools }
        private DetailTab _tab = DetailTab.Request;
        private string _filter = "all"; // "all" / "api" / "tool"

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
            {
                if (_filter == "api"  && entry.EntryType != ApiLogEntryType.Api)      continue;
                if (_filter == "tool" && entry.EntryType != ApiLogEntryType.ToolCall) continue;
                _items.Add(new ApiLogEntryVm { Entry = entry });
            }
            LogList.ItemsSource = _items;
            EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LogList.Visibility    = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = LogList.SelectedItem as ApiLogEntryVm;
            _selected = vm?.Entry;
            if (_selected == null) { DetailPanel.Visibility = Visibility.Collapsed; return; }
            DetailPanel.Visibility = Visibility.Visible;

            // 工具调用条目默认显示工具 tab
            _tab = _selected.EntryType == ApiLogEntryType.ToolCall ? DetailTab.Tools : DetailTab.Request;

            // 工具调用按钮只在有工具数据时显示
            ShowToolsBtn.Visibility = _selected.EntryType == ApiLogEntryType.ToolCall || _selected.ToolCalls?.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;

            UpdateDetailTab();
        }

        private void ShowRequestBtn_Click(object sender, RoutedEventArgs e)  { _tab = DetailTab.Request;  UpdateDetailTab(); }
        private void ShowResponseBtn_Click(object sender, RoutedEventArgs e) { _tab = DetailTab.Response; UpdateDetailTab(); }
        private void ShowToolsBtn_Click(object sender, RoutedEventArgs e)    { _tab = DetailTab.Tools;    UpdateDetailTab(); }

        private void UpdateDetailTab()
        {
            if (_selected == null) return;

            switch (_tab)
            {
                case DetailTab.Request:
                    DetailText.Text = _selected.RequestJson ?? "（无）";
                    break;
                case DetailTab.Response:
                    DetailText.Text = _selected.ResponseBody ?? "（无）";
                    break;
                case DetailTab.Tools:
                    DetailText.Text = _selected.ToolCallsText.Length > 0
                        ? _selected.ToolCallsText : "（无工具调用记录）";
                    break;
            }

            var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            var accentBrush = new SolidColorBrush(accent);
            var transBrush  = new SolidColorBrush(Windows.UI.Colors.Transparent);
            var whiteBrush  = new SolidColorBrush(Windows.UI.Colors.White);
            var fgBrush     = (Brush)Application.Current.Resources["ApplicationForegroundThemeBrush"];

            SetTabStyle(ShowRequestBtn,  _tab == DetailTab.Request,  accentBrush, transBrush, whiteBrush, fgBrush);
            SetTabStyle(ShowResponseBtn, _tab == DetailTab.Response, accentBrush, transBrush, whiteBrush, fgBrush);
            SetTabStyle(ShowToolsBtn,    _tab == DetailTab.Tools,    accentBrush, transBrush, whiteBrush, fgBrush);
        }

        private static void SetTabStyle(Button btn, bool active, Brush accentBg, Brush transBg, Brush whiteFg, Brush normalFg)
        {
            btn.Background = active ? accentBg : transBg;
            btn.Foreground = active ? whiteFg  : normalFg;
        }

        private void CopyDetailBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            string text = DetailText.Text;
            if (string.IsNullOrEmpty(text)) return;
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }

        // ── 筛选 ──────────────────────────────────────────────────────────

        private void FilterAll_Click(object sender, RoutedEventArgs e)  { _filter = "all";  UpdateFilterButtons(); Reload(); }
        private void FilterApi_Click(object sender, RoutedEventArgs e)  { _filter = "api";  UpdateFilterButtons(); Reload(); }
        private void FilterTool_Click(object sender, RoutedEventArgs e) { _filter = "tool"; UpdateFilterButtons(); Reload(); }

        private void UpdateFilterButtons()
        {
            var accent  = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            var ab = new SolidColorBrush(accent);
            var tb = new SolidColorBrush(Windows.UI.Colors.Transparent);
            var wb = new SolidColorBrush(Windows.UI.Colors.White);
            var fg = (Brush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
            SetTabStyle(FilterAllBtn,  _filter == "all",  ab, tb, wb, fg);
            SetTabStyle(FilterApiBtn,  _filter == "api",  ab, tb, wb, fg);
            SetTabStyle(FilterToolBtn, _filter == "tool", ab, tb, wb, fg);
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
