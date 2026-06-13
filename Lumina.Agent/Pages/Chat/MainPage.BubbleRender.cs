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
            // 还原思考过程 + 工具调用展示（切换/重开对话后不丢失）
            if (!isUser)
            {
                if (msg.ThinkSteps != null && msg.ThinkSteps.Count > 0)
                    bubble.RestoreThinkSteps(msg.ThinkSteps);
                else if (!string.IsNullOrEmpty(msg.ReasoningContent))
                    bubble.RestoreThinkSteps(new List<ThinkStep>
                        { new ThinkStep { Kind = "reasoning", Text = msg.ReasoningContent } });
            }
            if (isUser && idx >= 0 && _conv.BranchPoints != null)
                bubble.BranchData = _conv.BranchPoints.FirstOrDefault(bp => bp.AnchorIndex == idx);
            // 异步加载附带图片（如果有）：优先外置引用，回退旧内联 base64
            if (isUser && msg.HasImages)
                _ = LoadBubbleImageAsync(bubble, msg);
            return bubble;
        }

        private static async Task LoadBubbleImageAsync(ChatBubble bubble, ConversationMessage msg)
        {
            try
            {
                string base64 = null;
                if (msg.ImageRefs != null && msg.ImageRefs.Count > 0)
                {
                    bubble.ImageRefId = msg.ImageRefs[0]; // 看大图时按引用从磁盘加载（不常驻 base64）
                    base64 = await ImageStore.LoadBase64Async(msg.ImageRefs[0]);
                }
                if (string.IsNullOrEmpty(base64))
                {
                    base64 = msg.ImageBase64;            // 旧数据回退
                    if (!string.IsNullOrEmpty(base64)) bubble.FullImageBase64 = base64;
                }
                if (string.IsNullOrEmpty(base64)) return;

                byte[] bytes = Convert.FromBase64String(base64);
                var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                // 内联气泡只显示 120×120 缩略图——按缩略图尺寸解码，避免每张图常驻
                // 整张全分辨率位图（1024px≈4MB）。点击看大图时另行解码全分辨率。
                bmp.DecodePixelType = Windows.UI.Xaml.Media.Imaging.DecodePixelType.Logical;
                bmp.DecodePixelWidth = 240;
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

        private void AddSystemBubble(string text)
        {
            _bubbles.Add(new ChatBubble
            {
                Role = "assistant", Content = text,
                BackgroundColor = new SolidColorBrush(Color.FromArgb(255, 80, 40, 40)),
                ForegroundColor = _whiteBrush,
                ReasoningBgColor = _reasoningBrush,
            });
            ScrollToBottom();
        }

        private void UpdateTitleLabel()
        {
            ConvTitleLabel.Text = _conv?.Title ?? "新对话";
        }

        private void ScrollToBottom()
        {
            EnsureChatScrollViewer();
            if (_chatSv == null) return;
            // 在 WP 上，ChangeView 会让 ScrollViewer 短暂获得焦点，触发虚拟键盘。
            // 滚动前把焦点转到 RootGrid（非文本控件），避免键盘弹出。
            // 只在 InputTextBox 当前没有焦点时才转移，不打断用户正在输入的状态。
            var focused = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as Control;
            if (focused != InputTextBox)
                this.Focus(FocusState.Programmatic);
            _chatSv.ChangeView(null, double.MaxValue, null);
        }

        // ── ListView 内部 ScrollViewer 获取（启用虚拟化后列表自身滚动）────────
        private void ChatItems_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureChatScrollViewer();
        }

        /// <summary>懒获取 ChatItems 的内部 ScrollViewer 并挂上滚分页处理。</summary>
        private void EnsureChatScrollViewer()
        {
            if (_chatSv != null || ChatItems == null) return;
            _chatSv = FindScrollViewer(ChatItems);
            if (_chatSv != null)
            {
                _chatSv.ViewChanged -= ChatScrollViewer_ViewChanged;
                _chatSv.ViewChanged += ChatScrollViewer_ViewChanged;
            }
        }

        // 已下沉到 Lumina.Core/Common/UiHelpers.cs（两项目共用）
        private static ScrollViewer FindScrollViewer(DependencyObject root) => UiHelpers.FindScrollViewer(root);

        // ── 手动构建请求JSON（支持多模态content array）────────────────────────

        // 从 Gemini API 响应中提取文本：candidates[0].content.parts[0].text
    }
}
