using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    // ViewModel 包装，供 DataTemplate 绑定
    internal class SearchApiVm : INotifyPropertyChanged
    {
        public SearchApiEntry Entry { get; }
        public SearchApiVm(SearchApiEntry e) { Entry = e; }

        public string Name      => Entry.Name;
        public string TypeLabel => Entry.TypeLabel;
        public Visibility CanEdit => Entry.BuiltIn ? Visibility.Collapsed : Visibility.Visible;

        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; Entry.Enabled = value; OnProp(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnProp([System.Runtime.CompilerServices.CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed partial class SearchSettingsPage : Page
    {
        private readonly ObservableCollection<SearchApiVm> _items = new ObservableCollection<SearchApiVm>();
        private List<SearchApiEntry> _entries;
        private bool _loading = true;

        public SearchSettingsPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            _loading = true;
            DepthPicker.SelectedIndex = AppSettings.SearchResultDepth;

            _entries = await AppSettings.LoadSearchApisAsync();
            if (_entries == null || _entries.Count == 0)
                _entries = BuildDefaultEntries();

            _items.Clear();
            foreach (var entry in _entries)
                _items.Add(new SearchApiVm(entry) { Enabled = entry.Enabled });
            ApiList.ItemsSource = _items;
            _loading = false;
        }

        // ── 构建默认条目列表 ──────────────────────────────────────────────

        private static List<SearchApiEntry> BuildDefaultEntries()
        {
            var list = new List<SearchApiEntry>();
            // DuckDuckGo
            list.Add(new SearchApiEntry { Type = "ddg", Name = "DuckDuckGo", Value = "", BuiltIn = true, Enabled = true });
            // 内置 SearXNG 公共实例
            foreach (var url in BuiltinSearxng)
                list.Add(new SearchApiEntry { Type = "searxng", Name = url.Replace("https://", ""), Value = url, BuiltIn = true, Enabled = true });
            return list;
        }

        public static List<SearchApiEntry> BuildDefaultEntriesPublic() => BuildDefaultEntries();

        private static readonly string[] BuiltinSearxng = new[]
        {
            "https://search.ononoki.org",
            "https://searx.tiekoetter.com",
            "https://priv.au",
            "https://search.serpensin.com",
            "https://paulgo.io",
            "https://search.catboy.house",
            "https://searx.perennialte.ch",
            "https://searx.tuxcloud.net",
            "https://search.mdosch.de",
            "https://search.hbubli.cc",
            "https://search.unredacted.org",
            "https://searx.oloke.xyz",
            "https://search.einfachzocken.eu",
            "https://search.im-in.space",
            "https://sx.catgirl.cloud",
            "https://search.url4irl.com",
            "https://search.femboy.ad",
            "https://baresearch.org",
            "https://etsi.me",
        };

        // ── 开关 ─────────────────────────────────────────────────────────

        private void EntryToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _ = AppSettings.SaveSearchApisAsync(_entries);
        }

        // ── 深度 ─────────────────────────────────────────────────────────

        private void DepthPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            AppSettings.SearchResultDepth = DepthPicker.SelectedIndex;
        }

        // ── 添加 ─────────────────────────────────────────────────────────

        private async void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            var entry = await ShowEditDialog(null);
            if (entry == null) return;
            _entries.Add(entry);
            _items.Add(new SearchApiVm(entry) { Enabled = entry.Enabled });
            await AppSettings.SaveSearchApisAsync(_entries);
        }

        // ── 编辑 ─────────────────────────────────────────────────────────

        private async void EditBtn_Click(object sender, RoutedEventArgs e)
        {
            var vm = (sender as Button)?.Tag as SearchApiVm;
            if (vm == null) return;
            var updated = await ShowEditDialog(vm.Entry);
            if (updated == null) return;

            // 更新原 entry（同一对象）
            vm.Entry.Type    = updated.Type;
            vm.Entry.Name    = updated.Name;
            vm.Entry.Value   = updated.Value;
            vm.Entry.Enabled = updated.Enabled;

            // 刷新列表项
            int idx = _items.IndexOf(vm);
            if (idx >= 0)
            {
                _items.RemoveAt(idx);
                _items.Insert(idx, new SearchApiVm(vm.Entry) { Enabled = vm.Entry.Enabled });
            }
            await AppSettings.SaveSearchApisAsync(_entries);
        }

        // ── 编辑对话框 ───────────────────────────────────────────────────

        private async Task<SearchApiEntry> ShowEditDialog(SearchApiEntry existing)
        {
            bool isNew = existing == null;
            var entry  = isNew ? new SearchApiEntry { Enabled = true } : existing;

            // 类型选择
            var typePicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
            typePicker.Items.Add(new ComboBoxItem { Content = "SearXNG（自定义实例）", Tag = "searxng" });
            typePicker.Items.Add(new ComboBoxItem { Content = "DuckDuckGo", Tag = "ddg" });
            typePicker.Items.Add(new ComboBoxItem { Content = "Bing Search API", Tag = "bing" });
            typePicker.SelectedIndex = entry.Type == "bing" ? 2 : entry.Type == "ddg" ? 1 : 0;

            var nameBox = new TextBox { PlaceholderText = "名称", Text = entry.Name, Margin = new Thickness(0, 8, 0, 0) };

            var valueLabel = new TextBlock { Text = "URL / API Key", FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 8, 0, 0) };
            var valueBox   = new TextBox { PlaceholderText = "https://... 或 API Key", Text = entry.Value, Margin = new Thickness(0, 4, 0, 0) };

            void UpdateValueLabel(object s, SelectionChangedEventArgs _)
            {
                var tag = (typePicker.SelectedItem as ComboBoxItem)?.Tag as string;
                valueLabel.Text = tag == "bing" ? "Bing Subscription Key (Ocp-Apim-Subscription-Key)" :
                                  tag == "ddg"  ? "（无需配置）" : "SearXNG 实例 URL";
                valueBox.IsEnabled = tag != "ddg";
            }
            typePicker.SelectionChanged += UpdateValueLabel;
            UpdateValueLabel(null, null);

            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(new TextBlock { Text = "类型", FontSize = 12, Opacity = 0.6 });
            panel.Children.Add(typePicker);
            panel.Children.Add(new TextBlock { Text = "名称", FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(nameBox);
            panel.Children.Add(valueLabel);
            panel.Children.Add(valueBox);

            var dialog = new ContentDialog
            {
                Title             = isNew ? "添加搜索 API" : "编辑搜索 API",
                Content           = panel,
                PrimaryButtonText = "保存",
                CloseButtonText   = "取消",
            };
            AppSettings.ApplyTheme(panel);

            var result = await dialog.ShowAsync().AsTask();
            if (result != ContentDialogResult.Primary) return null;

            var type = (typePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "searxng";
            var name = nameBox.Text.Trim();
            var value = valueBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = type == "bing" ? "Bing" : type == "ddg" ? "DuckDuckGo" : value.Replace("https://", "");

            return new SearchApiEntry
            {
                Id      = isNew ? Guid.NewGuid().ToString() : entry.Id,
                Type    = type,
                Name    = name,
                Value   = value,
                Enabled = entry.Enabled,
                BuiltIn = false,
            };
        }

        // ── 返回 ─────────────────────────────────────────────────────────

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(SettingsPage));
        }
    }
}
