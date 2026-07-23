using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MainPage : Page
    {
        private void ChatItems_Loaded(object sender, RoutedEventArgs e)
        {
            // ListView 内部 ScrollViewer 在模板应用后才存在；获取它来驱动
            // “回到底部”按钮的显隐。滚动到底改用 ScrollIntoView，不依赖它。
            if (ChatScrollViewer == null)
            {
                ChatScrollViewer = GetScrollViewer(ChatItems);
                if (ChatScrollViewer != null)
                    ChatScrollViewer.ViewChanged += ChatScrollViewer_ViewChanged;
            }
        }

        // 已下沉到 Lumina.Core/Common/UiHelpers.cs（两项目共用）
        private static ScrollViewer GetScrollViewer(DependencyObject root) => UiHelpers.FindScrollViewer(root);

        private void ChatScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ChatScrollViewer == null) return;
            bool atBottom = ChatScrollViewer.VerticalOffset >= ChatScrollViewer.ScrollableHeight - 80;
            ScrollToBottomBtnBorder.Visibility = atBottom ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ScrollToBottomBtn_Click(object sender, RoutedEventArgs e)
        {
            ScrollToBottom();
            ScrollToBottomBtnBorder.Visibility = Visibility.Collapsed;
        }

        // ── Suggest panel toggle ─────────────────────────────────────────────

        private bool _isSuggesting = false;

        private async void SuggestBtn_Click(object sender, RoutedEventArgs e)
        {
            // 已展开 → 收起
            if (SuggestPanelOverlay.Visibility == Visibility.Visible)
            {
                SuggestPanelOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            // 防连点：正在生成时直接展开已有面板
            if (_isSuggesting)
            {
                SuggestPanelOverlay.Visibility = Visibility.Visible;
                return;
            }

            // 已有推荐且数量>0 → 直接展开
            if (_suggestedReplies != null && _suggestedReplies.Count > 0)
            {
                BuildSuggestPanel();
                SuggestPanelOverlay.Visibility = Visibility.Visible;
                return;
            }

            // 首次点击：自动生成
            SuggestPanelOverlay.Visibility = Visibility.Visible;
            _isSuggesting = true;
            try
            {
                await GenerateSuggestedRepliesAsync();
            }
            finally
            {
                _isSuggesting = false;
            }
        }

        private void SuggestPanelOverlay_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // 只在外层背景点击时收起，不拦截子元素事件
            if (e.OriginalSource == sender)
                SuggestPanelOverlay.Visibility = Visibility.Collapsed;
        }

        private ChatBubble BuildBubble(ConversationMessage msg, int idx)
        {
            bool isUser = msg.Role?.Equals("user", StringComparison.OrdinalIgnoreCase) == true;
            var bubble = new ChatBubble
            {
                Role = msg.Role, Content = msg.Content,
                ReasoningContent = msg.ReasoningContent ?? "",
                ReasoningExpanded = AppSettings.ReasoningExpandedByDefault,
                MessageId    = msg.Id,
                MessageIndex = idx,
                BackgroundColor  = isUser ? UserBubbleBg() : AiBubbleBg(),
                ForegroundColor  = isUser ? UserBubbleFg() : AiBubbleFg(),
                ReasoningBgColor = _reasoningBrush,
                QuoteColor   = _conv?.Appearance?.QuoteColor   ?? "",
                BracketColor = _conv?.Appearance?.BracketColor ?? "",
            };
            if (isUser && idx >= 0 && _conv.BranchPoints != null)
                bubble.BranchData = _conv.BranchPoints.FirstOrDefault(bp => bp.AnchorIndex == idx);
            // 异步加载附带图片（如果有）
            if (isUser && !string.IsNullOrEmpty(msg.ImageBase64))
                _ = LoadBubbleImageAsync(bubble, msg.ImageBase64);
            return bubble;
        }

        private static async Task LoadBubbleImageAsync(ChatBubble bubble, string base64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0);
                    await bmp.SetSourceAsync(ms);
                }
                bubble.ImageSource = bmp;
            }
            catch { }
        }

        private static async Task LoadBubbleImageAsync(string base64, Action<BitmapImage> onLoaded)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                var bmp = new BitmapImage();
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0);
                    await bmp.SetSourceAsync(ms);
                }
                onLoaded?.Invoke(bmp);
            }
            catch { }
        }

        private void AddSystemBubble(string text)
        {
            _bubbles.Add(new ChatBubble
            {
                Role = "system", Content = text,
                IsSystemMessage = true,
            });
            ScrollToBottom();
        }

        private void AddDebugBubble(string text)
        {
            _bubbles.Add(new ChatBubble
            {
                Role = "assistant", Content = "RAG 调试\n" + text,
                BackgroundColor = new SolidColorBrush(Color.FromArgb(80, 40, 80, 120)),
                ForegroundColor = _whiteBrush,
                ReasoningBgColor = _reasoningBrush,
            });
            ScrollToBottom();
        }

    }
}
