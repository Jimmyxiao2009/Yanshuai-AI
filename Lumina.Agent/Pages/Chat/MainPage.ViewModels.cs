using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MainPage : Page
    {
        /// <summary>问�?��的显示包装</summary>
        public class AskQuestionViewModel
        {
            private readonly AskQuestion _q;

            public string Id => _q.Id;
            public string Text => _q.Text;
            public string Placeholder => _q.Placeholder ?? "输入…";

            public string Answer { get; set; }
            public bool IsConfirmed { get; set; } = true;

            public Visibility IsText => _q.Type == "text" ? Visibility.Visible : Visibility.Collapsed;
            public Visibility IsChoice => _q.Type == "choice" ? Visibility.Visible : Visibility.Collapsed;
            public Visibility IsConfirm => _q.Type == "confirm" ? Visibility.Visible : Visibility.Collapsed;
            public Visibility IsMulti => _q.Type == "multi" ? Visibility.Visible : Visibility.Collapsed;

            public List<string> OptionList
            {
                get
                {
                    if (string.IsNullOrEmpty(_q.Options)) return new List<string>();
                    return _q.Options.Split(',').Select(o => o.Trim()).Where(o => !string.IsNullOrEmpty(o)).ToList();
                }
            }

            public List<MultiOptionItem> MultiItems
            {
                get
                {
                    if (string.IsNullOrEmpty(_q.Options)) return new List<MultiOptionItem>();
                    return _q.Options.Split(',').Select(o => new MultiOptionItem { Label = o.Trim() })
                        .Where(o => !string.IsNullOrEmpty(o.Label)).ToList();
                }
            }

            public AskQuestionViewModel(AskQuestion q)
            {
                _q = q;
                if (_q.Type == "text") Answer = _q.Default ?? "";
            }

            public string GetAnswer()
            {
                switch (_q.Type)
                {
                    case "text": return Answer;
                    case "choice": return Answer;
                    case "confirm": return IsConfirmed ? "是" : "否";
                    case "multi":
                        var checkedItems = MultiItems.Where(m => m.Checked).Select(m => m.Label);
                        return string.Join(",", checkedItems);
                    default: return Answer;
                }
            }
        }

        public class MultiOptionItem
        {
            public string Label { get; set; }
            public bool Checked { get; set; }
        }

    }

    // ── Sidebar conversation ViewModel ──────────────────────────────────────

    internal class ConvSidebarVm
    {
        private readonly Conversation _c;
        public ConvSidebarVm(Conversation c) { _c = c; }
        public string Id => _c.Id;
        public string Title => _c.Title ?? "对话";
        public string DateLabel
        {
            get
            {
                var now = DateTime.Now;
                if (_c.UpdatedAt.Date == now.Date) return _c.UpdatedAt.ToString("HH:mm");
                if ((now - _c.UpdatedAt).TotalDays < 7) return _c.UpdatedAt.ToString("ddd HH:mm");
                return _c.UpdatedAt.ToString("MM-dd");
            }
        }
    }

    // ── Sidebar project ViewModel ────────────────────────────────────────────

    internal class ProjectSidebarVm
    {
        private readonly Project _p;
        public ProjectSidebarVm(Project p) { _p = p; }
        public string Id => _p.Id;
        public string Name => _p.Name ?? "项目";
        public string IconGlyph => string.IsNullOrEmpty(_p.IconGlyph) ? "\uE8B7" : _p.IconGlyph;
        public string ConvCount
        {
            get
            {
                int count = _p.ConversationIds?.Count ?? 0;
                return count > 0 ? $"{count}" : "";
            }
        }
    }
}
