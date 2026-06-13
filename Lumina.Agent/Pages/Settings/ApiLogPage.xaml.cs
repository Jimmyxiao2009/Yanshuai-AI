using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public class LogEntryVm : INotifyPropertyChanged
    {
        private bool _expanded;
        private readonly ApiLogEntry _entry;

        public LogEntryVm(ApiLogEntry e) { _entry = e; }

        public string Prefix
        {
            get
            {
                if (_entry.EntryType == ApiLogEntryType.ToolCall) return "⚙";
                return _entry.IsError ? "✗" : "✓";
            }
        }

        public Brush PrefixColor
        {
            get
            {
                if (_entry.EntryType == ApiLogEntryType.ToolCall)
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 180, 255));
                return _entry.IsError
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 80, 80))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 220, 80));
            }
        }

        public string LineColorHex
        {
            get
            {
                if (_entry.IsError) return "#FF7070";
                if (_entry.EntryType == ApiLogEntryType.ToolCall) return "#80B0FF";
                return "#AAA";
            }
        }

        public Brush LineColor => new SolidColorBrush(
            _entry.IsError ? Windows.UI.Color.FromArgb(255, 255, 112, 112) :
            _entry.EntryType == ApiLogEntryType.ToolCall ? Windows.UI.Color.FromArgb(255, 128, 176, 255) :
            Windows.UI.Color.FromArgb(255, 170, 170, 170));

        public string LineText
        {
            get
            {
                if (_entry.EntryType == ApiLogEntryType.ToolCall)
                {
                    int errs = _entry.ToolCalls?.Count(t => t.Phase == "error") ?? 0;
                    string names = _entry.ToolCalls != null
                        ? string.Join(" → ", _entry.ToolCalls.Select(t => t.ToolName))
                        : "";
                    return string.Format("{0}  tool:{1}  ({2} calls{3})",
                        _entry.TimestampDisplay, names, _entry.ToolCalls?.Count ?? 0,
                        errs > 0 ? ", " + errs + " err" : "");
                }
                return string.Format("{0}  [{1}]  {2}",
                    _entry.TimestampDisplay,
                    _entry.Model ?? _entry.Provider ?? "?",
                    _entry.IsError ? "ERR: " + (_entry.ResponseBody ?? "").Trunc(60) : "OK");
            }
        }

        public Brush BgBrush
        {
            get
            {
                if (_expanded)
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 22, 30));
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 12, 12, 12));
            }
        }

        public string DetailText
        {
            get
            {
                if (_entry.EntryType == ApiLogEntryType.ToolCall)
                    return _entry.ToolCallsText;
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(_entry.RequestJson))
                {
                    sb.AppendLine("▸ request:");
                    sb.AppendLine(_entry.RequestJson.Trunc(500));
                    sb.AppendLine();
                }
                if (!string.IsNullOrEmpty(_entry.ResponseBody))
                {
                    sb.AppendLine("▸ response:");
                    sb.AppendLine(_entry.ResponseBody.Trunc(500));
                }
                return sb.ToString().TrimEnd();
            }
        }

        public Visibility DetailVisibility => _expanded ? Visibility.Visible : Visibility.Collapsed;

        public bool Expanded
        {
            get => _expanded;
            set { _expanded = value; OnProp(nameof(Expanded)); OnProp(nameof(BgBrush)); OnProp(nameof(DetailVisibility)); }
        }

        public ApiLogEntry Entry => _entry;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnProp([CallerMemberName] string n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public static class StringExtensions
    {
        public static string Trunc(this string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }

    public sealed partial class ApiLogPage : Page
    {
        private readonly ObservableCollection<LogEntryVm> _items = new ObservableCollection<LogEntryVm>();
        private LogEntryVm _expandedItem;
        private string _filter = "all";

        public ApiLogPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Reload();
        }

        private void Reload()
        {
            _expandedItem = null;
            _items.Clear();
            foreach (var entry in ApiLogger.Entries)
            {
                if (_filter == "api"  && entry.EntryType != ApiLogEntryType.Api)      continue;
                if (_filter == "tool" && entry.EntryType != ApiLogEntryType.ToolCall) continue;
                _items.Add(new LogEntryVm(entry));
            }
            LogList.ItemsSource = _items;
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = LogList.SelectedItem as LogEntryVm;
            if (_expandedItem != null && _expandedItem != selected)
                _expandedItem.Expanded = false;
            _expandedItem = selected;
            if (_expandedItem != null)
                _expandedItem.Expanded = !_expandedItem.Expanded;
            LogList.SelectedIndex = -1;
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            ApiLogger.Clear();
            _expandedItem = null;
            Reload();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(SettingsPage));
        }
    }
}
