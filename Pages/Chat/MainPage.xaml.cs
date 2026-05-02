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
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════════
    // Chat bubble view-model
    // ══════════════════════════════════════════════════════════════════════════

    public class ChatBubble : INotifyPropertyChanged
    {
        private static readonly string[] _thinkingVerbs =
        {
            "Thinking", "Pondering", "Reflecting", "Reasoning", "Analyzing",
            "Considering", "Processing", "Deliberating", "Contemplating", "Mustering",
            "Wondering", "Exploring", "Examining", "Evaluating", "Synthesizing"
        };
        private static readonly Random _rng = new Random();

        private string _content = "";
        private string _reasoningContent = "";
        private bool _reasoningExpanded = false;
        private bool _isStreaming = false;
        private bool _isStreamingReasoning = false;
        private string _streamingVerb = "";

        public string Role { get; set; }

        public string Content
        {
            get => _content;
            set { _content = value; OnProp(); }
        }

        public string ReasoningContent
        {
            get => _reasoningContent;
            set
            {
                _reasoningContent = value;
                OnProp();
                OnProp(nameof(HasReasoning));
                OnProp(nameof(HasReasoningVisibility));
                OnProp(nameof(ReasoningLabel));
            }
        }

        public bool HasReasoning => !string.IsNullOrEmpty(_reasoningContent);

        public bool ReasoningExpanded
        {
            get => _reasoningExpanded;
            set
            {
                _reasoningExpanded = value;
                OnProp();
                OnProp(nameof(ReasoningBodyVisibility));
                OnProp(nameof(ReasoningLabel));
            }
        }

        public Visibility HasReasoningVisibility =>
            HasReasoning ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReasoningBodyVisibility =>
            ReasoningExpanded ? Visibility.Visible : Visibility.Collapsed;
        public string ReasoningLabel
        {
            get
            {
                if (_isStreamingReasoning)
                    return _streamingVerb + "…";
                return ReasoningExpanded ? "思考过程 ▲" : "思考过程 ▼";
            }
        }

        /// <summary>
        /// Call when reasoning stream begins to pick a random verb and show it.
        /// Call with false when reasoning stream ends.
        /// </summary>
        public bool IsStreamingReasoning
        {
            get => _isStreamingReasoning;
            set
            {
                if (value && !_isStreamingReasoning)
                    _streamingVerb = _thinkingVerbs[_rng.Next(_thinkingVerbs.Length)];
                _isStreamingReasoning = value;
                OnProp(nameof(ReasoningLabel));
            }
        }

        public HorizontalAlignment Align =>
            Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        // ── Streaming flag controls which content view is shown ───────────────

        /// <summary>
        /// Set to true while the AI is streaming. The bubble shows a plain TextBlock.
        /// Set to false when the stream finishes; the bubble switches to the
        /// markdown-rendered RichTextBlock.
        /// </summary>
        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                _isStreaming = value;
                OnProp();
                OnProp(nameof(UserVisibility));
                OnProp(nameof(AiStreamingVisibility));
                OnProp(nameof(AiRenderedVisibility));
                OnProp(nameof(RegenerateBtnVisibility)); // show after stream ends
            }
        }

        // User bubbles: always plain text (left column)
        public Visibility UserVisibility =>
            Role == "user" ? Visibility.Visible : Visibility.Collapsed;

        // AI bubble while streaming: plain TextBlock (updates smoothly every token)
        public Visibility AiStreamingVisibility =>
            Role != "user" && _isStreaming ? Visibility.Visible : Visibility.Collapsed;

        // AI bubble after stream completes: RichTextBlock with markdown
        public Visibility AiRenderedVisibility =>
            Role != "user" && !_isStreaming ? Visibility.Visible : Visibility.Collapsed;

        public SolidColorBrush BackgroundColor { get; set; }
        public SolidColorBrush ForegroundColor { get; set; }
        public SolidColorBrush ReasoningBgColor { get; set; }

        // Inline highlight colors passed to MarkdownBlock (hex strings, empty = default)
        public string QuoteColor   { get; set; } = "";
        public string BracketColor { get; set; } = "";

        // ── Attached image (user messages only) ───────────────────────────────
        private Windows.UI.Xaml.Media.ImageSource _imageSource;
        public Windows.UI.Xaml.Media.ImageSource ImageSource
        {
            get => _imageSource;
            set { _imageSource = value; OnProp(); OnProp(nameof(HasImageVisibility)); }
        }
        public Visibility HasImageVisibility =>
            _imageSource != null && Role == "user" ? Visibility.Visible : Visibility.Collapsed;

        // ── Message identity (for actions) ────────────────────────────────────
        public string MessageId { get; set; }   // maps to ConversationMessage.Id
        public int    MessageIndex { get; set; } // index in _conv.Messages at render time

        // ── Branch data (set by MainPage after creating bubble) ───────────────
        private BranchPoint _branchData;
        public BranchPoint BranchData
        {
            get => _branchData;
            set
            {
                _branchData = value;
                OnProp();
                OnProp(nameof(BranchNavVisibility));
                OnProp(nameof(BranchLabel));
            }
        }
        public Visibility BranchNavVisibility =>
            Role == "user" && _branchData != null && _branchData.Count > 1
                ? Visibility.Visible : Visibility.Collapsed;
        public string BranchLabel =>
            _branchData != null ? $"{_branchData.ActiveIndex + 1} / {_branchData.Count}" : "";

        // ── Per-bubble action button visibility ───────────────────────────────
        public Visibility EditBtnVisibility =>
            Role == "user" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RegenerateBtnVisibility =>
            Role != "user" && !_isStreaming ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContinueBtnVisibility =>
            Role != "user" && !_isStreaming ? Visibility.Visible : Visibility.Collapsed;

        // ── Search status (tool use 进行中显示在气泡下方) ─────────────────────
        private string _searchStatusText = "";
        public string SearchStatusText
        {
            get => _searchStatusText;
            set { _searchStatusText = value; OnProp(); OnProp(nameof(SearchStatusVisibility)); }
        }
        public Visibility SearchStatusVisibility =>
            !string.IsNullOrEmpty(_searchStatusText) && Role != "user"
                ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnProp([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // API types
    // ══════════════════════════════════════════════════════════════════════════

    [DataContract] internal class ApiRequestMessage
    {
        [DataMember(Name = "role")]    public string Role    { get; set; }
        [DataMember(Name = "content")] public string Content { get; set; }
        // 多模态附件（不序列化，手动拼JSON）
        public string ImageBase64    { get; set; }
        public string ImageMimeType  { get; set; }
        // 当非空时，直接作为content的JSON值输出（用于Claude content block数组）
        public string RawContentJson { get; set; }
        // OpenAI tool消息的tool_call_id
        public string ToolCallId { get; set; }
    }
    [DataContract] internal class ApiRequest
    {
        [DataMember(Name = "model")]    public string Model    { get; set; }
        [DataMember(Name = "messages")] public List<ApiRequestMessage> Messages { get; set; }
        [DataMember(Name = "stream")]   public bool Stream { get; set; } = true;
    }
    [DataContract] internal class ApiResponseMessage  { [DataMember(Name = "content")] public string Content { get; set; } }
    [DataContract] internal class ApiResponseChoice   { [DataMember(Name = "message")] public ApiResponseMessage Message { get; set; } }
    [DataContract] internal class ApiResponse
    {
        [DataMember(Name = "choices")] public List<ApiResponseChoice> Choices { get; set; }
        [DataMember(Name = "error")]   public ApiErrorDetail Error { get; set; }
    }
    [DataContract] public class ApiErrorDetail { [DataMember(Name = "message")] public string Message { get; set; } }
    [DataContract] internal class StreamDelta
    {
        [DataMember(Name = "content")]           public string Content          { get; set; }
        [DataMember(Name = "reasoning_content")] public string ReasoningContent { get; set; }
    }
    [DataContract] internal class StreamChoice  { [DataMember(Name = "delta")] public StreamDelta Delta { get; set; } }
    [DataContract] internal class StreamChunk   { [DataMember(Name = "choices")] public List<StreamChoice> Choices { get; set; } }

    // ══════════════════════════════════════════════════════════════════════════
    // MainPage
    // ══════════════════════════════════════════════════════════════════════════

    public sealed partial class MainPage : Page
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(300) };
        private readonly ObservableCollection<ChatBubble> _bubbles = new ObservableCollection<ChatBubble>();
        private Conversation _conv;
        private string _loadedConvId;  // 上次渲染的对话 ID
        private bool _isSending = false;
        private bool _isPendingConv = false;  // true = _conv not yet added to DataManager
        private string _pendingImageBase64 = null;
        private string _pendingImageMimeType = null;
        private System.Threading.CancellationTokenSource _streamCts = null;

        // ── Cached brushes (rebuilt once per conversation, not once per bubble) ─
        private static readonly SolidColorBrush _whiteBrush      = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush _reasoningBrush  = new SolidColorBrush(Color.FromArgb(40, 150, 150, 170));
        private SolidColorBrush _cachedUserBg;
        private SolidColorBrush _cachedUserFg;
        private SolidColorBrush _cachedAiBg;
        private SolidColorBrush _cachedAiFg;
        // Accent color: UISettings construction is expensive on W10M — cache it
        private static Color    _accentColor;
        private static bool     _accentCached = false;

        public MainPage()
        {
            InitializeComponent();
            ChatItems.ItemsSource = _bubbles;
            SetStatusBarColor();
        }

        // ── Navigation ────────────────────────────────────────────────────────

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isPendingConv = false;
            if (DataManager.Data.Conversations == null ||
                (DataManager.Data.Conversations.Count == 0 && DataManager.Data.ApiProfiles.Count == 0))
            {
                await DataManager.LoadAsync();
            }
            AppSettings.ApplyTheme(RootGrid, this);
            SetSendButtonColor();

            // 启动行为：每次从外部导航到MainPage（不是返回）时触发
            if (AppState.IsFirstLaunch)
            {
                AppState.IsFirstLaunch = false;
                int startup = AppSettings.StartupBehavior;
                if (startup == 2) // 启动直接进对话列表
                {
                    AppState.ActiveConversation = DataManager.GetOrCreateDefaultConversation();
                    // Calling Frame.Navigate inside OnNavigatedTo corrupts the Frame
                    // journal — defer to the next dispatcher tick so the current
                    // navigation completes first.
                    var f = Frame;
                    _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        f.Navigate(typeof(ConversationsListPage));
                        if (f.BackStack.Count > 0)
                            f.BackStack.RemoveAt(f.BackStack.Count - 1);
                    });
                    return;
                }
                else if (startup == 1) // 启动新建对话（延迟到第一条消息才写入）
                {
                    _conv = new Conversation
                    {
                        Title        = "新对话",
                        ApiProfileId = DataManager.Data.SelectedApiProfileId ?? "",
                    };
                    _isPendingConv = true;
                }
                else // 最近对话（默认）
                {
                    _conv = DataManager.GetOrCreateDefaultConversation();
                }
            }
            else if (AppState.ActiveConversation != null)
            {
                // 从其他页面返回，恢复上次的对话
                _conv = AppState.ActiveConversation;
                // 如果这个对话还未写入列表（pending），保持挂起状态
                _isPendingConv = !DataManager.Data.Conversations.Contains(_conv);
            }
            else
            {
                _conv = DataManager.GetOrCreateDefaultConversation();
            }
            AppState.ActiveConversation = _conv;
            if (!_isPendingConv)
                DataManager.Data.LastActiveConversationId = _conv.Id;

            RebuildBrushCache();
            UpdateTitleLabel();
            ApplyConvAppearance();

            // 恢复 WebSearch 图标状态
            if (WebSearchIcon != null)
            {
                WebSearchIcon.Foreground = _webSearchEnabled
                    ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                    : new SolidColorBrush(AppSettings.IsDark ? Colors.White : Colors.Black);
                WebSearchIcon.Opacity = _webSearchEnabled ? 1.0 : 0.6;
            }

            // 强制设置工具栏图标前景色（防止深色模式下图标不可见）
            var toolbarFg = new SolidColorBrush(AppSettings.IsDark ? Colors.White : Colors.Black);
            if (FullscreenInputIcon != null) FullscreenInputIcon.Foreground = toolbarFg;
            if (AttachImageIcon    != null) AttachImageIcon.Foreground    = toolbarFg;

            var messages = _conv.Messages.ToList();
            bool sameConv = _loadedConvId == _conv.Id;
            _loadedConvId = _conv.Id;

            // ── 增量刷新：同一对话且气泡数量匹配，跳过重建 ──────────────────
            if (sameConv && _bubbles.Count == messages.Count && messages.Count > 0)
            {
                ScrollToBottom();
                // 检查后台任务（streaming 已结束但 UI 未更新的情况）
                if (!AppState.IsRunning(_conv.Id) && _isSending)
                {
                    _isSending = false;
                    SubmitButton.IsEnabled = true;
                    if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
                }
                goto checkBackgroundTask;
            }

            // ── 增量追加：同一对话但末尾有新消息 ────────────────────────────
            if (sameConv && _bubbles.Count < messages.Count && _bubbles.Count > 0)
            {
                int startIdx = _bubbles.Count;
                for (int i = startIdx; i < messages.Count; i++)
                    _bubbles.Add(BuildBubble(messages[i], i));

                if (WelcomePanel != null)
                    WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ScrollToBottom();
                goto checkBackgroundTask;
            }

            // ── 完整重建：对话切换或首次加载 ─────────────────────────────────
            _bubbles.Clear();

            if (messages.Count == 0)
            {
                if (_bubbles.Count == 0 && WelcomePanel != null)
                    WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
                return;
            }

            if (WelcomePanel != null)
                WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            // 显示加载遮罩
            const int chunkSize = 30;
            if (messages.Count > chunkSize && LoadingOverlay != null)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingProgress.Value = 0;
            }

            // 分批正向加载，每批后 yield 避免 UI 无响应
            for (int i = 0; i < messages.Count; i += chunkSize)
            {
                int end = Math.Min(i + chunkSize, messages.Count);
                for (int j = i; j < end; j++)
                    _bubbles.Add(BuildBubble(messages[j], j));

                if (LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
                {
                    LoadingProgress.Value = end * 100 / messages.Count;
                    LoadingText.Text = string.Format("加载中… {0}/{1}", end, messages.Count);
                }

                await Task.Delay(1);
            }

            // 全部加载完：滚到底部，隐藏遮罩
            ScrollToBottom();
            await Task.Delay(50); // 等 ScrollViewer 完成布局
            ScrollToBottom();
            if (LoadingOverlay != null)
                LoadingOverlay.Visibility = Visibility.Collapsed;

            checkBackgroundTask:
            var convId = _conv?.Id;
            if (convId != null && AppState.IsRunning(convId))
            {
                var bt = AppState.GetTask(convId);
                // 用后台已累积的内容创建一个占位气泡，显示实时进度
                var recoverBubble = new ChatBubble
                {
                    Role             = "assistant",
                    Content          = bt?.Content   ?? "",
                    ReasoningContent = bt?.Reasoning ?? "",
                    IsStreaming      = true,
                    BackgroundColor  = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                    ReasoningBgColor = _reasoningBrush,
                };
                _bubbles.Add(recoverBubble);
                _isSending = true;
                SubmitButton.IsEnabled = true;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE711";
                ScrollToBottom();

                // 让恢复气泡持续跟踪 AppState 内容，直到任务完成
                _ = Task.Run(async () =>
                {
                    while (AppState.IsRunning(convId))
                    {
                        await Task.Delay(200);
                        var cur = AppState.GetTask(convId);
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (cur != null)
                            {
                                recoverBubble.Content          = cur.Content;
                                recoverBubble.ReasoningContent = cur.Reasoning;
                            }
                        });
                    }
                    // 任务完成，同步最终状态并恢复 UI
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        recoverBubble.IsStreaming = false;
                        _isSending = false;
                        SubmitButton.IsEnabled = true;
                        if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
                        // 对话内容已由后台任务写入数据库，重新加载最后一条 AI 消息的内容
                        var lastAi = _conv.Messages.LastOrDefault(m => m.Role == "assistant");
                        if (lastAi != null)
                        {
                            recoverBubble.Content          = lastAi.Content;
                            recoverBubble.ReasoningContent = lastAi.ReasoningContent ?? "";
                            recoverBubble.MessageId        = lastAi.Id;
                        }
                        ScrollToBottom();
                    });
                });
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // 不取消 _streamCts：后台任务继续运行，结果会写入数据库
            // Fire-and-forget：不 await，导航立即完成，保存在后台进行
            _ = DataManager.SaveAsync();
        }

        // ── Hamburger ─────────────────────────────────────────────────────────

        private void HamburgerBtn_Click(object sender, RoutedEventArgs e)
            => NavPane.IsPaneOpen = !NavPane.IsPaneOpen;

        private void ClosePane() => NavPane.IsPaneOpen = false;

        private async void NavigateWithSave(Type pageType)
        {
            await DataManager.SaveAsync();
            Frame.Navigate(pageType);
        }

        private void Nav_Conversations_Click(object sender, RoutedEventArgs e)
        { ClosePane(); NavigateWithSave(typeof(ConversationsListPage)); }

        private void Nav_Api_Click(object sender, RoutedEventArgs e)
        { ClosePane(); Frame.Navigate(typeof(ApiProfilesPage)); }

        private void Nav_Settings_Click(object sender, RoutedEventArgs e)
        { ClosePane(); Frame.Navigate(typeof(SettingsPage)); }

        // ── Conversation settings / appearance ────────────────────────────────

        private void ConvSettingsBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvSettingsPage));

        private void ConvAppearanceBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvAppearancePage));

        // ── 图片附加 ──────────────────────────────────────────────────────────

        private async void AttachImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".webp");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            // 压缩图片：最大边 1024px，JPEG 质量 0.85，减少上传体积
            byte[] bytes;
            string mime;
            using (var stream = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                // 用 OrientedPixelWidth/Height，已考虑 EXIF 旋转方向，避免竖拍图片尺寸计算错误导致撕裂
                uint w = decoder.OrientedPixelWidth, h = decoder.OrientedPixelHeight;
                const uint MaxDim = 1024;
                uint newW = w, newH = h;
                if (w > MaxDim || h > MaxDim)
                {
                    if (w > h) { newW = MaxDim; newH = (uint)(h * MaxDim / w); }
                    else        { newH = MaxDim; newW = (uint)(w * MaxDim / h); }
                }
                var transform = new BitmapTransform { ScaledWidth = newW, ScaledHeight = newH };
                var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied, transform,
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ms);
                    encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied,
                        newW, newH, 96, 96, pixels.DetachPixelData());
                    await encoder.FlushAsync();
                    ms.Seek(0);
                    bytes = new byte[ms.Size];
                    await ms.ReadAsync(bytes.AsBuffer(), (uint)ms.Size, Windows.Storage.Streams.InputStreamOptions.None);
                }
                mime = "image/jpeg";
            }
            _pendingImageBase64  = Convert.ToBase64String(bytes);
            _pendingImageMimeType = mime;

            // 显示缩略图（直接用压缩后的bytes，无需重新打开文件）
            var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
            using (var previewMs = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                await previewMs.WriteAsync(bytes.AsBuffer());
                previewMs.Seek(0);
                await bmp.SetSourceAsync(previewMs);
            }
            PendingImageThumb.Source = bmp;
            ImagePreviewBar.Visibility = Visibility.Visible;
        }

        private void ClearImageBtn_Click(object sender, RoutedEventArgs e)
        {
            _pendingImageBase64  = null;
            _pendingImageMimeType = null;
            PendingImageThumb.Source = null;
            ImagePreviewBar.Visibility = Visibility.Collapsed;
        }

        // ── Input toolbar ─────────────────────────────────────────────────────

        private async void FullscreenInputBtn_Click(object sender, RoutedEventArgs e)
        {
            var editBox = new TextBox
            {
                Text = InputTextBox.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                MinHeight = 300,
                MaxHeight = 500,
                FontSize = 16,
                PlaceholderText = "在此输入消息…",
            };
            var dialog = new ContentDialog
            {
                Title = "全屏输入",
                Content = editBox,
                PrimaryButtonText = "发送",
                SecondaryButtonText = "取消",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                InputTextBox.Text = editBox.Text;
                SubmitButton_Click(sender, e);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                InputTextBox.Text = editBox.Text;
            }
        }

        private bool _webSearchEnabled = false;
        private bool _fetchSearchEnabled = false;

        private void WebSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            _webSearchEnabled = !_webSearchEnabled;
            if (WebSearchIcon != null)
            {
                WebSearchIcon.Foreground = _webSearchEnabled
                    ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                    : (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                WebSearchIcon.Opacity = _webSearchEnabled ? 1.0 : 0.6;
            }
            // 搜索关闭时同步关闭 fetch 搜索页
            if (!_webSearchEnabled)
            {
                _fetchSearchEnabled = false;
                if (FetchSearchIcon != null)
                {
                    FetchSearchIcon.Foreground = (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                    FetchSearchIcon.Opacity = 0.6;
                }
            }
            if (FetchSearchBtn != null)
                FetchSearchBtn.Visibility = _webSearchEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FetchSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            _fetchSearchEnabled = !_fetchSearchEnabled;
            if (FetchSearchIcon != null)
            {
                FetchSearchIcon.Foreground = _fetchSearchEnabled
                    ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                    : (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                FetchSearchIcon.Opacity = _fetchSearchEnabled ? 1.0 : 0.6;
            }
        }

        private void InsertBracketBtn_Click(object sender, RoutedEventArgs e)
        {
            int pos = InputTextBox.SelectionStart;
            string selected = InputTextBox.SelectedText;
            string insert = "（" + selected + "）";
            InputTextBox.Text = InputTextBox.Text.Remove(pos, InputTextBox.SelectionLength).Insert(pos, insert);
            // 有选中文字则光标移到右括号后，否则停在两括号之间
            InputTextBox.SelectionStart = selected.Length > 0 ? pos + insert.Length : pos + 1;
            InputTextBox.Focus(FocusState.Programmatic);
        }

        private void InsertQuoteBtn_Click(object sender, RoutedEventArgs e)
        {
            int pos = InputTextBox.SelectionStart;
            string selected = InputTextBox.SelectedText;
            string insert = "「" + selected + "」";
            InputTextBox.Text = InputTextBox.Text.Remove(pos, InputTextBox.SelectionLength).Insert(pos, insert);
            InputTextBox.SelectionStart = selected.Length > 0 ? pos + insert.Length : pos + 1;
            InputTextBox.Focus(FocusState.Programmatic);
        }

        // ── Input: Enter sends, Shift+Enter inserts newline ───────────────────

        private void InputTextBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;

            var shift = Windows.UI.Core.CoreWindow.GetForCurrentThread()
                               .GetKeyState(Windows.System.VirtualKey.Shift);
            bool shiftDown = (shift & Windows.UI.Core.CoreVirtualKeyStates.Down)
                             == Windows.UI.Core.CoreVirtualKeyStates.Down;
            var ctrl = Windows.UI.Core.CoreWindow.GetForCurrentThread()
                              .GetKeyState(Windows.System.VirtualKey.Control);
            bool ctrlDown = (ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down)
                            == Windows.UI.Core.CoreVirtualKeyStates.Down;

            e.Handled = true;

            int enterMode = AppSettings.EnterBehavior;
            if (enterMode == 0)
            {
                // 模式0：Enter发送，Shift+Enter换行
                if (shiftDown)
                {
                    var tb = InputTextBox;
                    int pos = tb.SelectionStart;
                    tb.Text = tb.Text.Insert(pos, "\r\n");
                    tb.SelectionStart = pos + 2;
                }
                else
                {
                    SubmitButton_Click(sender, e);
                }
            }
            else
            {
                // 模式1：Enter换行，Ctrl+Enter发送
                if (ctrlDown)
                {
                    SubmitButton_Click(sender, e);
                }
                else
                {
                    var tb = InputTextBox;
                    int pos = tb.SelectionStart;
                    tb.Text = tb.Text.Insert(pos, "\r\n");
                    tb.SelectionStart = pos + 2;
                }
            }
        }

        // ── Send ──────────────────────────────────────────────────────────────

        private async void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            // 生成中点击 → 停止
            if (_isSending)
            {
                _streamCts?.Cancel();
                return;
            }
            string userInput = InputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(userInput)) return;

            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null)
            {
                AddSystemBubble("⚠ 没有选择 API 配置，请从菜单进入 API 配置页面。");
                return;
            }

            _isSending = true;
            SubmitButton.IsEnabled = false;

            // 第一次发送消息时才把对话写入持久化列表
            if (_isPendingConv)
            {
                _isPendingConv = false;
                DataManager.Data.Conversations.Add(_conv);
                DataManager.Data.LastActiveConversationId = _conv.Id;
            }

            // Create message first so its Id is available for the bubble
            var userMsg = new ConversationMessage
            {
                Role = "user", Content = userInput, Timestamp = DateTime.Now,
                ImageBase64   = _pendingImageBase64,
                ImageMimeType = _pendingImageMimeType,
            };
            _conv.Messages.Add(userMsg);
            _conv.UpdatedAt = DateTime.Now;

            if (_conv.Messages.Count(m => m.Role == "user") == 1)
            {
                _conv.Title = userInput.Length > 20 ? userInput.Substring(0, 20) + "…" : userInput;
                UpdateTitleLabel();
            }

            // User bubble
            var userBubble = new ChatBubble
            {
                Role = "user", Content = userInput,
                MessageId = userMsg.Id,
                BackgroundColor = UserBubbleBg(), ForegroundColor = UserBubbleFg(),
                ReasoningBgColor = _reasoningBrush,
                ImageSource = PendingImageThumb.Source,  // 复用已加载的缩略图
            };
            if (WelcomePanel != null)
                WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            _bubbles.Add(userBubble);
            InputTextBox.Text = "";
            // 清理pending图片
            _pendingImageBase64   = null;
            _pendingImageMimeType = null;
            PendingImageThumb.Source = null;
            ImagePreviewBar.Visibility = Visibility.Collapsed;
            ScrollToBottom();

            await SendWithExistingUserMessage(userMsg, userBubble);
        }

        // ── Long memory ───────────────────────────────────────────────────────

        private string BuildMemoryBlock()
        {
            if (!_conv.MemoryEnabled) return null;
            if (_conv.MemoryItems == null || _conv.MemoryItems.Count == 0) return null;
            if (_conv.ExchangesSinceLastInject < _conv.MemoryInjectInterval) return null;
            _conv.ExchangesSinceLastInject = 0;
            var sb = new StringBuilder();
            sb.AppendLine("[长期记忆]");
            foreach (var item in _conv.MemoryItems)
                sb.AppendLine("- " + item);
            return sb.ToString().Trim();
        }

        private async Task RunMemorySummaryAsync()
        {
            var memApiId = _conv.MemoryApiProfileId;
            var memProfile = string.IsNullOrEmpty(memApiId)
                ? DataManager.GetProfileForConversation(_conv)
                : DataManager.Data.ApiProfiles.Find(p => p.Id == memApiId);
            if (memProfile == null) return;

            // Collect the messages to summarise
            int count = _conv.MemorySummaryInterval * 2; // exchanges × 2 messages
            var recent = _conv.Messages
                .Skip(Math.Max(0, _conv.Messages.Count - count))
                .Where(m => m.Role == "user" || m.Role == "assistant")
                .ToList();
            if (recent.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("以下是一段对话，请提取其中重要的记忆要点，以简洁的条目（每行一条，以「-」开头）输出，不要任何其他内容：");
            sb.AppendLine();
            foreach (var m in recent)
                sb.AppendLine($"{(m.Role == "user" ? "用户" : "AI")}：{m.Content}");

            var payload = new ApiRequest
            {
                Model = memProfile.Model,
                Stream = false,
                Messages = new List<ApiRequestMessage>
                {
                    new ApiRequestMessage { Role = "user", Content = sb.ToString() }
                }
            };

            string requestJson;
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ApiRequest)).WriteObject(ms, payload);
                requestJson = Encoding.UTF8.GetString(ms.ToArray());
            }

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, memProfile.Url);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {memProfile.ApiKey}");
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return;
                    var body = await resp.Content.ReadAsStringAsync();
                    ApiResponse parsed;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);

                    var text = (parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null) ?? "";
                    var newItems = text.Split('\n')
                        .Select(l => l.TrimStart('-', ' ').Trim())
                        .Where(l => l.Length > 0)
                        .ToList();

                    if (_conv.MemoryItems == null) _conv.MemoryItems = new List<string>();
                    _conv.MemoryItems.AddRange(newItems);
                    await DataManager.SaveAsync();

                    // Show a subtle system notification in chat
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        _bubbles.Add(new ChatBubble
                        {
                            Role = "assistant",
                            Content = $"📝 已生成 {newItems.Count} 条新记忆",
                            BackgroundColor = new SolidColorBrush(Color.FromArgb(60, 80, 160, 80)),
                            ForegroundColor = _whiteBrush,
                            ReasoningBgColor = _reasoningBrush,
                        });
                        ScrollToBottom();
                    });
                }
            }
            catch { /* summarisation errors are silent */ }
        }

        // ── System prompt ─────────────────────────────────────────────────────

        private string BuildSystemPrompt(string userInput)
        {
            var sb = new StringBuilder();
            // 优先用当前对话绑定的API的独立破墙，fallback全局设置
            var profile = DataManager.GetProfileForConversation(_conv);
            bool jbEnabled = (profile != null && profile.JailbreakEnabled)
                             ? true
                             : AppSettings.JailbreakEnabled;
            string jbPrompt = (profile != null && profile.JailbreakEnabled && !string.IsNullOrEmpty(profile.JailbreakPrompt))
                              ? profile.JailbreakPrompt
                              : AppSettings.JailbreakPrompt;
            if (jbEnabled && !string.IsNullOrEmpty(jbPrompt))
            { sb.AppendLine(jbPrompt); sb.AppendLine(); }

            // 注入用户人设（活跃资料）
            var up = DataManager.GetActiveUserProfile();
            if (up != null && (!string.IsNullOrEmpty(up.Name) || !string.IsNullOrEmpty(up.Description)))
            {
                sb.AppendLine();
                sb.AppendLine("【用户信息】以下是与你对话的用户（非你自身）的设定：");
                if (!string.IsNullOrEmpty(up.Name)) sb.AppendLine("用户名：" + up.Name);
                if (!string.IsNullOrEmpty(up.Description)) sb.AppendLine("用户描述：" + up.Description);
            }

            if (_webSearchEnabled)
            {
                sb.AppendLine();
                sb.AppendLine($"【当前日期】{DateTime.Now:yyyy年M月d日}");
                if (_fetchSearchEnabled)
                {
                    sb.AppendLine("【搜索规则】你可以先调用 web_search，也可以直接用 fetch_page 读取搜索引擎的结果页面（如 DuckDuckGo、Bing、SearXNG 等）。但严禁 fetch google.com、google.com.hk 或任何 google 子域名（包括 www.google.com），这类请求会被系统拦截。");
                }
                else
                {
                    sb.AppendLine("【搜索规则】需要查询实时信息时，必须调用 web_search 工具获取结果；禁止直接用 fetch_page 抓取任何搜索引擎页面（包括 Google、DuckDuckGo、Bing、百度等）。fetch_page 仅用于读取 web_search 返回的具体页面 URL。");
                }
            }
            return sb.ToString().Trim();
        }

        // ── Streaming ─────────────────────────────────────────────────────────

        private async Task HandleStreamingResponse(HttpResponseMessage resp, ChatBubble bubble, System.Threading.CancellationToken ct)
        {
            bool reasoningPhase = false;
            int tokensSinceScroll = 0;

            await Task.Run(async () =>
            {
                using (var stream = await resp.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (!reader.EndOfStream && !ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;
                        if (!line.StartsWith("data: ")) continue;
                        var data = line.Substring(6).Trim();
                        if (data == "[DONE]") break;

                        string ct2 = null;
                        string rt   = null;

                        // 尝试 OpenAI/DeepSeek/Groq 格式 (choices[].delta)
                        StreamChunk chunk = null;
                        try
                        {
                            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(data)))
                                chunk = (StreamChunk)new DataContractJsonSerializer(typeof(StreamChunk)).ReadObject(ms);
                        }
                        catch { }

                        var delta = chunk?.Choices?.Count > 0 ? chunk.Choices[0]?.Delta : null;
                        if (delta != null)
                        {
                            rt  = delta.ReasoningContent;
                            ct2 = delta.Content;
                        }
                        else
                        {
                            // 尝试 Gemini 格式: candidates[0].content.parts[0].text
                            ct2 = ExtractGeminiText(data);
                        }

                        if (ct2 == null && rt == null) continue;

                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (!string.IsNullOrEmpty(rt))
                            {
                                if (!reasoningPhase)
                                {
                                    reasoningPhase = true;
                                    bubble.IsStreamingReasoning = true;
                                    bubble.ReasoningExpanded = true;
                                }
                                bubble.ReasoningContent += rt;
                                tokensSinceScroll++;
                            }
                            if (!string.IsNullOrEmpty(ct2))
                            {
                                if (reasoningPhase)
                                {
                                    reasoningPhase = false;
                                    bubble.IsStreamingReasoning = false;
                                    bubble.ReasoningExpanded = false;
                                }
                                bubble.Content += ct2;
                                tokensSinceScroll++;
                            }
                            if (tokensSinceScroll >= 8) { tokensSinceScroll = 0; ScrollToBottom(); }
                        });
                    }
                }
            });
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                ScrollToBottom());
        }

        private async Task HandleRegularResponse(HttpResponseMessage resp, ChatBubble bubble)
        {
            var body = await resp.Content.ReadAsStringAsync();
            string newContent;
            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    ApiResponse parsed;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                    string openAiContent = parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null;
                    if (!string.IsNullOrEmpty(openAiContent))
                        newContent = openAiContent;
                    else
                        // 尝试 Gemini 格式: candidates[0].content.parts[0].text
                        newContent = ExtractGeminiText(body) ?? "（无响应）";
                }
                catch { newContent = body; }
            }
            else
            {
                ApiResponse err = null;
                try
                {
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        err = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                }
                catch { }
                newContent = $"错误 {(int)resp.StatusCode}：{err?.Error?.Message ?? body}";
            }
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                bubble.Content = newContent);
        }

        // ── Reasoning toggle ──────────────────────────────────────────────────

        private void ToggleReasoning_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ChatBubble b)
                b.ReasoningExpanded = !b.ReasoningExpanded;
        }

        // ── Message actions ───────────────────────────────────────────────────

        // 点击气泡图片缩略图 → 大图预览
        private async void BubbleImage_Click(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (!((sender as Border)?.Tag is ChatBubble b) || b.ImageSource == null) return;
            var img = new Image
            {
                Source = b.ImageSource,
                Stretch = Windows.UI.Xaml.Media.Stretch.Uniform,
                MaxHeight = 600,
                MaxWidth = 800,
            };
            var dialog = new ContentDialog
            {
                Content = new Border
                {
                    Background = new SolidColorBrush(Colors.Black),
                    Child = img,
                },
                CloseButtonText = "关闭",
            };
            await dialog.ShowAsync();
        }

        // Copy message content to clipboard
        private void CopyMsg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!((sender as Button)?.Tag is ChatBubble b)) return;
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(b.Content ?? "");
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            }
            catch { }
        }

        // Delete this message and everything after it
        private void DeleteMsg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!((sender as Button)?.Tag is ChatBubble b)) return;
                if (string.IsNullOrEmpty(b.MessageId)) return;
                var msg = _conv.Messages.FirstOrDefault(m => m.Id == b.MessageId);
                if (msg == null) return;
                int idx = _conv.Messages.IndexOf(msg);
                if (idx < 0) return;

                _conv.Messages.RemoveRange(idx, _conv.Messages.Count - idx);
                while (_bubbles.Count > idx) _bubbles.RemoveAt(_bubbles.Count - 1);
                _ = DataManager.SaveAsync();
            }
            catch { }
        }

        // Edit user message → creates a new branch
        private async void EditMsg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isSending) return;
                if (!((sender as Button)?.Tag is ChatBubble b)) return;
                if (string.IsNullOrEmpty(b.MessageId)) return;
                var msg = _conv.Messages.FirstOrDefault(m => m.Id == b.MessageId);
                if (msg == null || msg.Role != "user") return;
                int anchorIdx = _conv.Messages.IndexOf(msg);

                var editBox = new TextBox
                {
                    Text = msg.Content, AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap, MinHeight = 100,
                    Width = 320, Header = "编辑消息"
                };
                var dialog = new ContentDialog
                {
                    Title = "编辑并创建分支",
                    Content = editBox,
                    PrimaryButtonText = "发送",
                    SecondaryButtonText = "取消",
                    RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

                string newText = editBox.Text.Trim();
                if (string.IsNullOrEmpty(newText)) return;

                // ── Find or create BranchPoint at this index ──────────────────
                if (_conv.BranchPoints == null) _conv.BranchPoints = new List<BranchPoint>();
                var bp = _conv.BranchPoints.FirstOrDefault(x => x.AnchorIndex == anchorIdx);
                if (bp == null)
                {
                    bp = new BranchPoint { AnchorIndex = anchorIdx, AnchorMessageId = msg.Id };
                    _conv.BranchPoints.Add(bp);
                }

                var snapshot = new ConversationBranch
                {
                    Messages = _conv.Messages.Skip(anchorIdx).Select(m2 => new ConversationMessage
                    {
                        Id = m2.Id, Role = m2.Role, Content = m2.Content,
                        ReasoningContent = m2.ReasoningContent, Timestamp = m2.Timestamp
                    }).ToList()
                };
                if (bp.ActiveIndex < bp.Branches.Count)
                    bp.Branches[bp.ActiveIndex] = snapshot;
                else
                    bp.Branches.Add(snapshot);

                // New branch with the edited message
                var newMsg = new ConversationMessage { Role = "user", Content = newText, Timestamp = DateTime.Now };
                bp.Branches.Add(new ConversationBranch { Messages = new List<ConversationMessage> { newMsg } });
                bp.ActiveIndex = bp.Branches.Count - 1;

                // Truncate conversation and add new message
                _conv.Messages.RemoveRange(anchorIdx, _conv.Messages.Count - anchorIdx);
                _conv.Messages.Add(newMsg);

                // Rebuild all bubbles
                _bubbles.Clear();
                RebuildBrushCache();
                var bMsgs = _conv.Messages.ToList();
                for (int bi = 0; bi < bMsgs.Count; bi++)
                {
                    _bubbles.Add(BuildBubble(bMsgs[bi], bi));
                    if (bi > 0 && bi % 30 == 0) await Task.Yield();
                }
                await DataManager.SaveAsync();

                // Attach branch data to the anchor bubble
                var anchorBubble = _bubbles.FirstOrDefault(bu => bu.MessageId == newMsg.Id);
                if (anchorBubble != null) anchorBubble.BranchData = bp;

                InputTextBox.Text = "";
                await SendWithExistingUserMessage(newMsg, anchorBubble);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EditMsg_Click error: " + ex.Message);
            }
        }

        // Continue: send a hidden "continue" prompt, AI generates another reply
        private async void ContinueMsg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isSending) return;

                var profile = DataManager.GetProfileForConversation(_conv);
                if (profile == null) { AddSystemBubble("⚠ 没有选择 API 配置"); return; }

                _isSending = true;
                _streamCts = new System.Threading.CancellationTokenSource();
                SubmitButton.IsEnabled = true;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE711";

                var apiMessages = new List<ApiRequestMessage>();
                string sysPrompt = BuildSystemPrompt("");
                if (!string.IsNullOrEmpty(sysPrompt))
                    apiMessages.Add(new ApiRequestMessage { Role = "system", Content = sysPrompt });

                int windowSize = _conv.ContextWindow > 0 ? _conv.ContextWindow : int.MaxValue;
                var windowMsgs = _conv.Messages
                    .Skip(Math.Max(0, _conv.Messages.Count - windowSize))
                    .ToList();
                foreach (var m in windowMsgs)
                    apiMessages.Add(new ApiRequestMessage
                    {
                        Role    = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                        Content = m.Content,
                        // 继续功能不重发图片：历史图片已处理过，重发浪费token且部分API会报错
                    });

                // 隐藏的继续指令，不写入对话记录
                apiMessages.Add(new ApiRequestMessage { Role = "user", Content = "继续" });

                var aiBubble = new ChatBubble
                {
                    Role = "assistant", Content = "",
                    IsStreaming = true,
                    BackgroundColor = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                    ReasoningBgColor = _reasoningBrush,
                };
                _bubbles.Add(aiBubble);
                ScrollToBottom();

                string requestJson = BuildRequestJson(profile.Model, apiMessages, true, profile.VisionEnabled, profile.ProviderType == "claude");
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                    if (profile.ProviderType == "claude")
                    {
                        req.Headers.TryAddWithoutValidation("x-api-key", profile.ApiKey);
                        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                    }
                    else
                    {
                        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
                    }
                    req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
                    {
                        string ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                        if (ct.Contains("event-stream") || ct.Contains("stream"))
                            await HandleStreamingResponse(resp, aiBubble, _streamCts.Token);
                        else
                            await HandleRegularResponse(resp, aiBubble);
                        ApiLogger.Log(profile.ProviderType, profile.Model, requestJson, aiBubble.Content, !resp.IsSuccessStatusCode);
                    }
                }
                catch (Exception ex) { aiBubble.Content = $"连接错误：{ex.Message}"; ApiLogger.Log(profile.ProviderType, profile.Model, requestJson, ex.Message, true); }

                aiBubble.IsStreamingReasoning = false;
                aiBubble.IsStreaming = false;
                aiBubble.ReasoningExpanded = AppSettings.ReasoningExpandedByDefault;
                _streamCts?.Dispose();
                _streamCts = null;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";

                var aiMsg = new ConversationMessage
                {
                    Role = "assistant", Content = aiBubble.Content,
                    ReasoningContent = aiBubble.ReasoningContent, Timestamp = DateTime.Now
                };
                aiBubble.MessageId = aiMsg.Id;
                _conv.Messages.Add(aiMsg);
                _conv.UpdatedAt = DateTime.Now;

                await DataManager.SaveAsync();
                ScrollToBottom();
                PlaySound();
                _isSending = false;
                SubmitButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ContinueMsg error: " + ex.Message);
                _isSending = false;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
                SubmitButton.IsEnabled = true;
            }
        }

        // Regenerate last AI response (or any AI message)
        private async void RegenerateMsg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isSending) return;
                if (!((sender as Button)?.Tag is ChatBubble b)) return;
                if (string.IsNullOrEmpty(b.MessageId)) return;
                var msg = _conv.Messages.FirstOrDefault(m => m.Id == b.MessageId);
                if (msg == null) return;
                int idx = _conv.Messages.IndexOf(msg);
                if (idx < 0) return;

                // Remove this AI message and all after
                _conv.Messages.RemoveRange(idx, _conv.Messages.Count - idx);
                while (_bubbles.Count > idx) _bubbles.RemoveAt(_bubbles.Count - 1);

                var userMsg = _conv.Messages.LastOrDefault(m => m.Role == "user");
                if (userMsg == null) return;
                var userBubble = _bubbles.LastOrDefault(bub => bub.Role == "user");
                await SendWithExistingUserMessage(userMsg, userBubble);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RegenerateMsg error: " + ex.Message);
                _isSending = false;
                SubmitButton.IsEnabled = true;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
            }
        }

        // ── Branch navigation ─────────────────────────────────────────────────

        private void PrevBranch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!((sender as Button)?.Tag is ChatBubble b) || b.BranchData == null) return;
                SwitchBranch(b, b.BranchData.ActiveIndex - 1);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("PrevBranch error: " + ex); }
        }
        private void NextBranch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!((sender as Button)?.Tag is ChatBubble b) || b.BranchData == null) return;
                SwitchBranch(b, b.BranchData.ActiveIndex + 1);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("NextBranch error: " + ex); }
        }

        private void SwitchBranch(ChatBubble anchorBubble, int targetIndex)
        {
            var bp = anchorBubble?.BranchData;
            if (bp == null || targetIndex < 0 || targetIndex >= bp.Count) return;

            // Use the stored index — no message-ID search, so it works after edits
            int anchorIdx = bp.AnchorIndex;
            if (anchorIdx < 0 || anchorIdx > _conv.Messages.Count) return;

            // Save current tail into current branch slot
            if (bp.ActiveIndex >= 0 && bp.ActiveIndex < bp.Branches.Count)
            {
                bp.Branches[bp.ActiveIndex] = new ConversationBranch
                {
                    Messages = _conv.Messages.Skip(anchorIdx).Select(m => new ConversationMessage
                    {
                        Id = m.Id, Role = m.Role, Content = m.Content,
                        ReasoningContent = m.ReasoningContent, Timestamp = m.Timestamp
                    }).ToList()
                };
            }

            // Restore target branch
            var target = bp.Branches[targetIndex];
            _conv.Messages.RemoveRange(anchorIdx, _conv.Messages.Count - anchorIdx);
            foreach (var m in target.Messages) _conv.Messages.Add(m);
            bp.ActiveIndex = targetIndex;

            // Rebuild bubbles from anchorIdx onwards
            // BuildBubble will auto-attach BranchData by AnchorIndex
            while (_bubbles.Count > anchorIdx) _bubbles.RemoveAt(_bubbles.Count - 1);
            int rebuildIdx = anchorIdx;
            foreach (var m in _conv.Messages.Skip(anchorIdx))
                _bubbles.Add(BuildBubble(m, rebuildIdx++));

            _ = DataManager.SaveAsync();
            ScrollToBottom();
        }

        // ── Shared send helper (used by Edit + Regenerate) ────────────────────

        private async Task SendWithExistingUserMessage(ConversationMessage userMsg, ChatBubble userBubble)
        {
            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null) { AddSystemBubble("⚠ 没有选择 API 配置"); return; }

            _isSending = true;
            _streamCts = new System.Threading.CancellationTokenSource();
            SubmitButton.IsEnabled = true; // 生成中仍可点击（点击=停止）
            if (SubmitIcon != null) SubmitIcon.Glyph = ""; // X 图标

            var apiMessages = new List<ApiRequestMessage>();
            string sysPrompt = BuildSystemPrompt(userMsg.Content);
            if (!string.IsNullOrEmpty(sysPrompt))
                apiMessages.Add(new ApiRequestMessage { Role = "system", Content = sysPrompt });

            int windowSize = _conv.ContextWindow > 0 ? _conv.ContextWindow : int.MaxValue;
            var windowMsgs = _conv.Messages
                .Skip(Math.Max(0, _conv.Messages.Count - windowSize))
                .ToList();
            foreach (var m in windowMsgs)
                apiMessages.Add(new ApiRequestMessage
                {
                    Role          = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                    Content       = m.Content,
                    ImageBase64   = m.ImageBase64,
                    ImageMimeType = m.ImageMimeType,
                });
            string memoryBlock = BuildMemoryBlock();
            if (!string.IsNullOrEmpty(memoryBlock))
                apiMessages.Add(new ApiRequestMessage { Role = "system", Content = memoryBlock });

            var aiBubble = new ChatBubble
            {
                Role = "assistant", Content = "",
                IsStreaming = true,
                BackgroundColor = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                ReasoningBgColor = _reasoningBrush,
            };
            _bubbles.Add(aiBubble);
            ScrollToBottom();

            // ── 注册后台任务（页面离开后继续运行）──────────────────────────
            var conv       = _conv;
            var cts        = _streamCts;
            var webEnabled = _webSearchEnabled;
            string requestJson = BuildRequestJson(profile.Model, apiMessages, true, profile.VisionEnabled, profile.ProviderType == "claude");

            AppState.BackgroundConvId = conv.Id;
            AppState.RegisterTask(conv.Id, null); // 占位，真正的 task 稍后赋值

            // 实时同步内容到 AppState，页面不在时也能累积
            aiBubble.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(ChatBubble.Content) ||
                    ev.PropertyName == nameof(ChatBubble.ReasoningContent))
                    AppState.UpdateContent(conv.Id, aiBubble.Content, aiBubble.ReasoningContent);
            };

            var sendTask = Task.Run(async () =>
            {
                try
                {
                    if (webEnabled)
                    {
                        string finalContent = await RunToolUseLoopAsync(profile, apiMessages, aiBubble, cts.Token);
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            aiBubble.Content = finalContent);
                    }
                    else
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                        if (profile.ProviderType == "claude")
                        {
                            req.Headers.TryAddWithoutValidation("x-api-key", profile.ApiKey);
                            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                        }
                        else
                        {
                            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
                        }
                        req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                        {
                            string ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                            if (ct.Contains("event-stream") || ct.Contains("stream"))
                                await HandleStreamingResponse(resp, aiBubble, cts.Token);
                            else
                                await HandleRegularResponse(resp, aiBubble);
                            ApiLogger.Log(profile.ProviderType, profile.Model, requestJson, aiBubble.Content, !resp.IsSuccessStatusCode);
                        }
                    }
                }
                catch (System.OperationCanceledException) { /* 用户主动取消 */ }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        aiBubble.Content = $"连接错误：{ex.Message}");
                }

                // ── 写入数据库（无论页面是否存在都执行）─────────────────────
                string finalAiContent = "";
                string finalReasoning = "";
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.IsStreamingReasoning = false;
                    aiBubble.IsStreaming = false;
                    aiBubble.ReasoningExpanded = AppSettings.ReasoningExpandedByDefault;
                    finalAiContent = aiBubble.Content;
                    finalReasoning = aiBubble.ReasoningContent;
                });

                var aiMsg = new ConversationMessage
                {
                    Role             = "assistant",
                    Content          = finalAiContent,
                    ReasoningContent = finalReasoning,
                    Timestamp        = DateTime.Now,
                };
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    aiBubble.MessageId = aiMsg.Id);
                conv.Messages.Add(aiMsg);
                conv.UpdatedAt = DateTime.Now;
                conv.ExchangesSinceLastSummary++;
                conv.ExchangesSinceLastInject++;

                await DataManager.SaveAsync();
                AppState.CompleteTask(conv.Id);

                // ── 通知 UI（页面在时刷新，不在时忽略）───────────────────────
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    cts.Dispose();
                    if (conv.MemoryEnabled && conv.ExchangesSinceLastSummary >= conv.MemorySummaryInterval)
                    {
                        conv.ExchangesSinceLastSummary = 0;
                        _ = RunMemorySummaryAsync();
                    }
                    if (SubmitIcon != null) SubmitIcon.Glyph = "";
                    ScrollToBottom();
                    PlaySound();
                    _isSending = false;
                    SubmitButton.IsEnabled = true;
                });
            });

            AppState.RegisterTask(conv.Id, sendTask);
            await sendTask;
        }


        // ── Web Search / Tool Use ─────────────────────────────────────────────

        // 搜索 API 池（懒加载，首次搜索时从文件读取）
        private List<SearchApiEntry> _searchApiPool = null;
        private readonly Random _rng = new Random();

        private async Task<List<SearchApiEntry>> GetSearchPoolAsync()
        {
            if (_searchApiPool != null) return _searchApiPool;
            var saved = await AppSettings.LoadSearchApisAsync();
            if (saved != null && saved.Count > 0)
                _searchApiPool = saved;
            else
                _searchApiPool = SearchSettingsPage.BuildDefaultEntriesPublic();
            return _searchApiPool;
        }

        // 缓存可用 SearXNG 实例列表（按响应速度排序）
        private List<string> _cachedSearxngUrls = null;
        private DateTime _searxngCacheTime = DateTime.MinValue;

        private async Task<List<string>> GetUsableSearxngAsync(IList<SearchApiEntry> pool)
        {
            if (_cachedSearxngUrls != null && (DateTime.Now - _searxngCacheTime).TotalMinutes < 15)
                return _cachedSearxngUrls;

            var instances = pool.Where(e => e.Type == "searxng" && e.Enabled && !string.IsNullOrEmpty(e.Value))
                                .Select(e => e.Value).ToArray();
            if (instances.Length == 0) return null;

            var results   = new List<(string url, long ms)>();
            var tasks     = new System.Collections.Generic.List<Task>();

            for (int i = 0; i < instances.Length; i++)
            {
                int idx = i;
                string inst = instances[idx];
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var req = new HttpRequestMessage(HttpMethod.Head, inst + "/search?q=test&format=json");
                        req.Headers.TryAddWithoutValidation("Accept", "application/json");
                        using (var cts2 = new System.Threading.CancellationTokenSource(8000))
                        using (var resp = await _http.SendAsync(req, cts2.Token))
                        {
                            sw.Stop();
                            if ((int)resp.StatusCode < 500)
                            {
                                lock (results)
                                    results.Add((inst, sw.ElapsedMilliseconds));
                            }
                        }
                    }
                    catch { }
                }));
            }

            await Task.WhenAll(tasks);

            if (results.Count > 0)
            {
                _cachedSearxngUrls = results.OrderBy(r => r.ms).Select(r => r.url).ToList();
                _searxngCacheTime = DateTime.Now;
            }
            return _cachedSearxngUrls;
        }

        // 执行搜索，带重试和降级
        private async Task<string> RunSearchAsync(string query)
        {
            try
            {
                var pool    = await GetSearchPoolAsync();
                var enabled = pool.Where(e => e.Enabled).ToList();
                if (enabled.Count == 0)
                    return "[搜索失败：未启用任何搜索源，请前往设置开启]";

                // 1. Bing（最稳定，但需要 API Key）
                var bingEntry = enabled.FirstOrDefault(e => e.Type == "bing" && !string.IsNullOrEmpty(e.Value));
                if (bingEntry != null)
                {
                    string result = await RunBingSearchAsync(query, bingEntry.Value);
                    if (!string.IsNullOrEmpty(result)) return result;
                }

                // 2. SearXNG — 逐个尝试可用实例，直到搜到或全部失败
                bool hasSearxng = enabled.Any(e => e.Type == "searxng" && !string.IsNullOrEmpty(e.Value));
                if (hasSearxng)
                {
                    var usable = await GetUsableSearxngAsync(pool);
                    if (usable != null && usable.Count > 0)
                    {
                        foreach (string baseUrl in usable)
                        {
                            try
                            {
                                var url = baseUrl.TrimEnd('/') + "/search?q=" + Uri.EscapeDataString(query) + "&format=json&pageno=1";
                                var req = new HttpRequestMessage(HttpMethod.Get, url);
                                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                                using (var cts = new System.Threading.CancellationTokenSource(10000))
                                using (var resp = await _http.SendAsync(req, cts.Token))
                                {
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        string r = ParseSearxngResults(await resp.Content.ReadAsStringAsync());
                                        if (!string.IsNullOrEmpty(r)) return r;
                                    }
                                }
                            }
                            catch { /* 换下一个实例 */ }
                        }
                    }
                }

                // 3. DuckDuckGo — 用 lite 接口（比 /html/ 稳定）
                if (enabled.Any(e => e.Type == "ddg"))
                {
                    try
                    {
                        var req = new HttpRequestMessage(HttpMethod.Get, "https://lite.duckduckgo.com/lite/?q=" + Uri.EscapeDataString(query));
                        req.Headers.TryAddWithoutValidation("User-Agent", AppSettings.FetchUserAgent);
                        using (var cts = new System.Threading.CancellationTokenSource(10000))
                        using (var resp = await _http.SendAsync(req, cts.Token))
                        {
                            if (resp.IsSuccessStatusCode)
                            {
                                string r = ParseDdgLiteResults(await resp.Content.ReadAsStringAsync());
                                if (!string.IsNullOrEmpty(r)) return r;
                            }
                        }
                    }
                    catch { }
                }

                return "[搜索无结果：所有搜索源均已尝试但未返回有效数据，请检查网络或稍后重试]";
            }
            catch (Exception ex)
            {
                return $"[搜索异常：{ex.Message}]";
            }
        }

        private async Task<string> RunBingSearchAsync(string query, string subscriptionKey)
        {
            try
            {
                var url = "https://api.bing.microsoft.com/v7.0/search?q=" + Uri.EscapeDataString(query) + "&mkt=zh-CN&count=5";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", subscriptionKey);
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    return ParseBingResults(await resp.Content.ReadAsStringAsync());
                }
            }
            catch { return null; }
        }

        private static string ParseSerperResults(string json) => null; // 已移除
        private static string ParseBraveResults(string json)  => null;
        private static string ParseBaiduResults(string json)  => null;
        private static string ParseDdgResults(string json)    => null;

        private static string ParseBingResults(string json)
        {
            // Bing v7: {"webPages":{"value":[{"name":"...","url":"...","snippet":"..."}]}}
            if (string.IsNullOrEmpty(json)) return null;
            var sb = new StringBuilder();
            int idx = json.IndexOf("\"value\":");
            if (idx < 0) return null;
            int searchFrom = idx;
            int count = 0;
            while (count < 5)
            {
                int nameIdx = json.IndexOf("\"name\":", searchFrom);
                if (nameIdx < 0) break;
                string title = ExtractJsonString(json, nameIdx + 7);
                int urlIdx = json.IndexOf("\"url\":", nameIdx);
                if (urlIdx < 0) break;
                string url = ExtractJsonString(json, urlIdx + 6);
                int snipIdx = json.IndexOf("\"snippet\":", nameIdx);
                string snippet = snipIdx >= 0 ? ExtractJsonString(json, snipIdx + 10) : "";
                sb.AppendLine($"- {title}\n  {snippet}\n  {url}");
                searchFrom = urlIdx + 6;
                count++;
                if (sb.Length > 2000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string ParseSearxngResults(string json)
        {
            // 找 "results": [ 数组，逐个提取 title/content/url
            var sb = new StringBuilder();
            int arrStart = json.IndexOf("\"results\":");
            if (arrStart < 0) return null;
            int idx = json.IndexOf('[', arrStart);
            if (idx < 0) return null;

            int count = 0;
            while (count < 8)
            {
                int braceStart = json.IndexOf('{', idx);
                if (braceStart < 0) break;
                int braceEnd   = json.IndexOf('}', braceStart);
                if (braceEnd < 0) break;

                string block = json.Substring(braceStart, braceEnd - braceStart + 1);

                string title   = ExtractJsonValue(block, "title");
                string content = ExtractJsonValue(block, "content");
                string url     = ExtractJsonValue(block, "url");

                if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(content))
                {
                    sb.AppendLine($"- {title}\n  {content}\n  {url}");
                    count++;
                }
                idx = braceEnd + 1;
                if (sb.Length > 3000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        // 从 JSON 键值对中提取引号内的值（只搜 block 范围，更准确）
        private static string ExtractJsonValue(string block, string key)
        {
            int keyIdx = block.IndexOf("\"" + key + "\":");
            if (keyIdx < 0) return "";
            int valStart = keyIdx + key.Length + 3;
            while (valStart < block.Length && block[valStart] == ' ') valStart++;
            if (valStart >= block.Length || block[valStart] != '"') return ExtractJsonRawValue(block, valStart);
            valStart++;
            var sb = new StringBuilder();
            while (valStart < block.Length)
            {
                char c = block[valStart++];
                if (c == '"') break;
                if (c == '\\' && valStart < block.Length)
                {
                    char esc = block[valStart++];
                    switch (esc) { case 'n': sb.Append('\n'); break; case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break; case '\\': sb.Append('\\'); break; case '"': sb.Append('"'); break; default: sb.Append(esc); break; }
                }
                else sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string ExtractJsonRawValue(string block, int start)
        {
            int end = start;
            while (end < block.Length && block[end] != ',' && block[end] != '}' && block[end] != ']')
                end++;
            string val = block.Substring(start, end - start).Trim();
            return val == "null" ? "" : val;
        }

        private static string ParseDdgLiteResults(string html)
        {
            // DDG Lite 返回简单的 HTML 表格，结构比 /html/ 稳定
            var sb = new StringBuilder();
            int idx = 0;
            int count = 0;
            while (count < 5)
            {
                int linkIdx = html.IndexOf("<a rel=\"nofollow\"", idx);
                if (linkIdx < 0) break;
                int hrefStart = html.IndexOf("href=\"", linkIdx) + 6;
                if (hrefStart < 6) break;
                int hrefEnd = html.IndexOf('"', hrefStart);
                string url = hrefEnd > hrefStart ? html.Substring(hrefStart, hrefEnd - hrefStart) : "";

                int titleStart = html.IndexOf('>', hrefEnd) + 1;
                int titleEnd   = html.IndexOf("</a>", titleStart);
                string title = titleStart > 0 && titleEnd > titleStart
                    ? StripHtmlTags(html.Substring(titleStart, titleEnd - titleStart)).Trim()
                    : "";

                int snipIdx = html.IndexOf("result-snippet", titleEnd);
                string snippet = "";
                if (snipIdx >= 0 && snipIdx < titleEnd + 500)
                {
                    int snipStart = html.IndexOf('>', snipIdx) + 1;
                    int snipEnd   = html.IndexOf("</td>", snipStart);
                    if (snipStart > 0 && snipEnd > snipStart)
                        snippet = StripHtmlTags(html.Substring(snipStart, snipEnd - snipStart)).Trim();
                }

                if (!string.IsNullOrEmpty(title))
                {
                    sb.AppendLine($"- {title}\n  {snippet}\n  {url}");
                    count++;
                }
                idx = titleEnd;
                if (sb.Length > 3000) break;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        private static string StripHtmlTags(string html)
        {
            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        // 从 JSON 字符串提取引号包裹的值（简单实现，不依赖完整 JSON 解析）
        private static string ExtractJsonString(string json, int start)
        {
            while (start < json.Length && json[start] != '"') start++;
            if (start >= json.Length) return "";
            start++; // skip opening quote
            var sb = new StringBuilder();
            while (start < json.Length)
            {
                char c = json[start++];
                if (c == '"') break;
                if (c == '\\' && start < json.Length)
                {
                    char esc = json[start++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default:  sb.Append(esc);  break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // Fetch 页面正文（简单提取文本，去掉HTML标签）
        private async Task<string> FetchPageAsync(string url)
        {
            try
            {
                // 禁止 fetch Google 任何域名
                Uri uri;
                if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    string host = uri.Host.ToLower();
                    if (host == "google.com" || host.EndsWith(".google.com") ||
                        host == "google.com.hk" || host.EndsWith(".google.com.hk"))
                        return "错误：禁止访问 Google 域名，请使用其他搜索引擎。";
                }
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", AppSettings.FetchUserAgent);
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return $"无法访问页面（{(int)resp.StatusCode}）";
                    var html = await resp.Content.ReadAsStringAsync();
                    // 去掉 script/style 块
                    html = System.Text.RegularExpressions.Regex.Replace(html, @"<script[\s\S]*?</script>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    html = System.Text.RegularExpressions.Regex.Replace(html, @"<style[\s\S]*?</style>",   "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    // 去掉所有 HTML 标签
                    var text = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");
                    // 折叠空白
                    text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", "\n").Trim();
                    // 按设置截断
                    int depth = AppSettings.SearchResultDepth;
                    int limit = depth == 0 ? 2000 : depth == 1 ? 8000 : int.MaxValue;
                    return text.Length > limit ? text.Substring(0, limit) + "\n…（已截断）" : text;
                }
            }
            catch (Exception ex) { return $"Fetch 失败：{ex.Message}"; }
        }

        // ── Tool use：构建带 tools 定义的请求 JSON ────────────────────────────

        private static string BuildRequestJsonWithTools(string model, List<ApiRequestMessage> messages, bool stream, bool isClaudeProvider, bool supportsVision = false)
        {
            string toolsDef = isClaudeProvider
                ? BuildClaudeToolsDef()
                : BuildOpenAiToolsDef();

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append($"\"stream\":{(stream ? "true" : "false")},");
            if (isClaudeProvider)
            {
                sb.Append("\"max_tokens\":8192,");
                // Claude API要求system作为顶层字段，messages只含user/assistant
                var sysMsgs = messages.Where(m => m.Role == "system").ToList();
                var chatMsgs = messages.Where(m => m.Role != "system").ToList();
                if (sysMsgs.Count > 0)
                {
                    string sysContent = string.Join("\n\n", sysMsgs.Select(m => m.Content));
                    sb.Append($"\"system\":\"{EscapeJson(sysContent)}\",");
                }
                sb.Append($"\"tools\":{toolsDef},");
                sb.Append("\"messages\":[");
                for (int i = 0; i < chatMsgs.Count; i++)
                {
                    var m = chatMsgs[i];
                    sb.Append("{");
                    sb.Append($"\"role\":\"{EscapeJson(m.Role)}\",");
                    if (!string.IsNullOrEmpty(m.RawContentJson))
                        sb.Append($"\"content\":{m.RawContentJson}");
                    else
                        sb.Append($"\"content\":\"{EscapeJson(m.Content)}\"");
                    sb.Append("}");
                    if (i < chatMsgs.Count - 1) sb.Append(",");
                }
                sb.Append("]");
            }
            else
            {
                sb.Append($"\"tools\":{toolsDef},");
                sb.Append("\"tool_choice\":\"auto\",");
                sb.Append("\"messages\":[");
                for (int i = 0; i < messages.Count; i++)
                {
                    var m = messages[i];
                    sb.Append("{");
                    sb.Append($"\"role\":\"{EscapeJson(m.Role)}\",");

                    if (m.Role == "tool" && !string.IsNullOrEmpty(m.ToolCallId))
                    {
                        // tool消息必须带tool_call_id
                        sb.Append($"\"tool_call_id\":\"{EscapeJson(m.ToolCallId)}\",");
                        sb.Append($"\"content\":\"{EscapeJson(m.Content)}\"");
                    }
                    else if (m.Role == "assistant" && m.Content != null && m.Content.StartsWith("\x01TOOLCALLS\x01"))
                    {
                        // assistant消息带tool_calls数组
                        string toolCallsJson = m.Content.Substring(11); // skip marker
                        sb.Append("\"content\":null,");
                        sb.Append($"\"tool_calls\":{toolCallsJson}");
                    }
                    else if (supportsVision && !string.IsNullOrEmpty(m.ImageBase64) && m.Role == "user")
                    {
                        sb.Append("\"content\":[");
                        sb.Append("{\"type\":\"image_url\",\"image_url\":{");
                        sb.Append($"\"url\":\"data:{EscapeJson(m.ImageMimeType)};base64,{m.ImageBase64}\"");
                        sb.Append("}},{");
                        sb.Append($"\"type\":\"text\",\"text\":\"{EscapeJson(m.Content)}\"");
                        sb.Append("}]");
                    }
                    else
                    {
                        sb.Append($"\"content\":\"{EscapeJson(m.Content)}\"");
                    }
                    sb.Append("}");
                    if (i < messages.Count - 1) sb.Append(",");
                }
                sb.Append("]");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildOpenAiToolsDef() =>
            "[{\"type\":\"function\",\"function\":{\"name\":\"web_search\",\"description\":\"搜索互联网以获取最新信息。当你需要查询实时数据、新闻或不确定的事实时调用此工具。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"搜索关键词\"}},\"required\":[\"query\"]}}}," +
             "{\"type\":\"function\",\"function\":{\"name\":\"fetch_page\",\"description\":\"获取指定URL页面的正文内容，用于深入阅读搜索结果中的某个页面。\",\"parameters\":{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"要获取的页面URL\"}},\"required\":[\"url\"]}}}]";

        private static string BuildClaudeToolsDef() =>
            "[{\"name\":\"web_search\",\"description\":\"搜索互联网以获取最新信息。\",\"input_schema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"搜索关键词\"}},\"required\":[\"query\"]}}," +
             "{\"name\":\"fetch_page\",\"description\":\"获取指定URL页面的正文内容。\",\"input_schema\":{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"要获取的页面URL\"}},\"required\":[\"url\"]}}]";

        private class OpenAiToolCall  { public string Name; public string Id; public string ArgsJson; public string RawToolCallsJson; }
        private class ClaudeToolCall  { public string Name; public string Id; public string ArgsJson; }

        // 从响应 JSON 中提取 tool_call（OpenAI 格式），返回结果或 null
        private static OpenAiToolCall ExtractOpenAiToolCall(string json)
        {
            int tcIdx = json.IndexOf("\"tool_calls\":");
            if (tcIdx < 0) return null;
            // 提取原始 tool_calls 数组，原样回传避免重建时格式变化
            int arrStart = json.IndexOf('[', tcIdx + 13);
            string rawToolCallsJson = null;
            if (arrStart >= 0)
            {
                int depth2 = 0; int end2 = arrStart;
                for (; end2 < json.Length; end2++)
                {
                    if (json[end2] == '[' || json[end2] == '{') depth2++;
                    else if (json[end2] == ']' || json[end2] == '}') { depth2--; if (depth2 == 0) break; }
                }
                rawToolCallsJson = json.Substring(arrStart, end2 - arrStart + 1);
            }
            int nameIdx = json.IndexOf("\"name\":", tcIdx);
            if (nameIdx < 0) return null;
            string name = ExtractJsonString(json, nameIdx + 7);
            int argIdx = json.IndexOf("\"arguments\":", tcIdx);
            if (argIdx < 0) return null;
            // arguments 可能是字符串或对象
            int afterColon = argIdx + 12;
            while (afterColon < json.Length && json[afterColon] == ' ') afterColon++;
            string args;
            if (afterColon < json.Length && json[afterColon] == '"')
                args = ExtractJsonString(json, afterColon);
            else
            {
                // 找到匹配的 {} 块
                int brace = json.IndexOf('{', afterColon);
                if (brace < 0) return null;
                int depth = 0; int end = brace;
                for (; end < json.Length; end++)
                {
                    if (json[end] == '{') depth++;
                    else if (json[end] == '}') { depth--; if (depth == 0) break; }
                }
                args = json.Substring(brace, end - brace + 1);
            }
            int idIdx = json.IndexOf("\"id\":", tcIdx);
            string id = idIdx >= 0 ? ExtractJsonString(json, idIdx + 5) : "";
            return new OpenAiToolCall { Name = name, Id = id, ArgsJson = args, RawToolCallsJson = rawToolCallsJson };
        }

        // 从响应 JSON 中提取 Claude tool_use block，返回结果或 null
        private static ClaudeToolCall ExtractClaudeToolCall(string json)
        {
            int tuIdx = json.IndexOf("\"type\":\"tool_use\"");
            if (tuIdx < 0) return null;
            int nameIdx = json.IndexOf("\"name\":", tuIdx);
            int idIdx   = json.IndexOf("\"id\":", tuIdx);
            int inputIdx= json.IndexOf("\"input\":", tuIdx);
            if (nameIdx < 0 || inputIdx < 0) return null;
            string name = ExtractJsonString(json, nameIdx + 7);
            string id   = idIdx >= 0 ? ExtractJsonString(json, idIdx + 5) : "";
            int brace = json.IndexOf('{', inputIdx + 8);
            string args = "{}";
            if (brace >= 0)
            {
                int depth = 0; int end = brace;
                for (; end < json.Length; end++)
                {
                    if (json[end] == '{') depth++;
                    else if (json[end] == '}') { depth--; if (depth == 0) break; }
                }
                args = json.Substring(brace, end - brace + 1);
            }
            return new ClaudeToolCall { Name = name, Id = id, ArgsJson = args };
        }

        // 从 tool call 的 args JSON 中取出指定字段的字符串值
        private static string ExtractArgField(string argsJson, string field)
        {
            int idx = argsJson.IndexOf($"\"{field}\":");
            if (idx < 0) return "";
            return ExtractJsonString(argsJson, idx + field.Length + 3);
        }

        // ── Tool use 完整多轮循环 ─────────────────────────────────────────────

        private async Task<string> RunToolUseLoopAsync(
            ApiProfile profile,
            List<ApiRequestMessage> apiMessages,
            ChatBubble aiBubble,
            System.Threading.CancellationToken ct)
        {
            // 将 ApiRequestMessage 转换为 ApiMessageWithTools
            var toolMessages = new List<ApiMessageWithTools>();
            foreach (var m in apiMessages)
            {
                toolMessages.Add(new ApiMessageWithTools
                {
                    Role = m.Role,
                    Content = m.Content,
                });
            }

            // 用 FunctionCallEngine 跑完整工具循环
            FunctionCallLoopResult result = await FunctionCallEngine.RunFunctionCallLoopAsync(profile, toolMessages, _conv);
            string content = result.Content;
            List<ApiMessageWithTools> allMessages = result.AllMessages ?? new List<ApiMessageWithTools>();

            // 显示搜索状态
            int toolCount = allMessages.Count(m => m.Role == "tool");
            if (toolCount > 0)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.SearchStatusText = "已使用 " + toolCount + " 个工具调用";
                });
            }

            return content;
        }

        private static string ExtractContentFromResponse(string responseBody, bool isClaudeProvider)
        {
            if (isClaudeProvider)
            {
                // Claude: find "type":"text" block then its "text" value
                int textIdx = responseBody.IndexOf("\"type\":\"text\"");
                if (textIdx >= 0)
                {
                    int textValIdx = responseBody.IndexOf("\"text\":", textIdx);
                    if (textValIdx >= 0)
                        return ExtractJsonString(responseBody, textValIdx + 7);
                }
            }
            else
            {
                // OpenAI format: choices[0].message.content
                // Skip over any "content": null occurrences from tool_calls
                int searchFrom = 0;
                while (true)
                {
                    int contentIdx = responseBody.IndexOf("\"content\":", searchFrom);
                    if (contentIdx < 0) break;
                    int valueStart = contentIdx + 10;
                    // skip whitespace
                    while (valueStart < responseBody.Length && responseBody[valueStart] == ' ') valueStart++;
                    if (valueStart < responseBody.Length && responseBody[valueStart] == '"')
                    {
                        string val = ExtractJsonString(responseBody, valueStart);
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                    searchFrom = contentIdx + 10;
                }
                // DeepSeek-R1 fallback: reasoning_content
                int rcIdx = responseBody.IndexOf("\"reasoning_content\":");
                if (rcIdx >= 0)
                {
                    string rc = ExtractJsonString(responseBody, rcIdx + 20);
                    if (!string.IsNullOrEmpty(rc)) return rc;
                }
                // Gemini fallback
                string gemini = ExtractGeminiText(responseBody);
                if (!string.IsNullOrEmpty(gemini)) return gemini;
            }
            return "";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
            if (ChatScrollViewer == null) return;
            // 在 WP 上，ChangeView 会让 ScrollViewer 短暂获得焦点，触发虚拟键盘。
            // 滚动前把焦点转到 RootGrid（非文本控件），避免键盘弹出。
            // 只在 InputTextBox 当前没有焦点时才转移，不打断用户正在输入的状态。
            var focused = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as Control;
            if (focused != InputTextBox)
                this.Focus(FocusState.Programmatic);
            ChatScrollViewer.ChangeView(null, double.MaxValue, null);
        }

        // ── 手动构建请求JSON（支持多模态content array）────────────────────────

        // 从 Gemini API 响应中提取文本：candidates[0].content.parts[0].text
        private static string ExtractGeminiText(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int candIdx = json.IndexOf("\"candidates\"");
            if (candIdx < 0) return null;
            int partsIdx = json.IndexOf("\"parts\"", candIdx);
            if (partsIdx < 0) return null;
            int textIdx = json.IndexOf("\"text\":", partsIdx);
            if (textIdx < 0) return null;
            return ExtractJsonString(json, textIdx + 7);
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return string.Empty;
            var r = new System.Text.StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if      (c == '\\') r.Append("\\\\");
                else if (c == '"')  r.Append("\\\"");
                else if (c == '\n') r.Append("\\n");
                else if (c == '\r') r.Append("\\r");
                else if (c == '\t') r.Append("\\t");
                else if (c < 0x20) r.Append($"\\u{(int)c:x4}"); // 其他控制字符
                else                r.Append(c);
            }
            return r.ToString();
        }

        private static string BuildRequestJson(string model, List<ApiRequestMessage> messages, bool stream, bool supportsVision = false, bool isClaudeProvider = false)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{EscapeJson(model)}\",");
            sb.Append($"\"stream\":{(stream ? "true" : "false")},");

            if (isClaudeProvider)
            {
                sb.Append("\"max_tokens\":8192,");
                // Claude API: system 提取为顶层字段，messages 只含 user/assistant
                var sysMsgs  = messages.Where(m => m.Role == "system").ToList();
                var chatMsgs = messages.Where(m => m.Role != "system").ToList();
                if (sysMsgs.Count > 0)
                {
                    string sysContent = string.Join("\n\n", sysMsgs.Select(m => m.Content));
                    sb.Append($"\"system\":\"{EscapeJson(sysContent)}\",");
                }
                sb.Append("\"messages\":[");
                for (int i = 0; i < chatMsgs.Count; i++)
                {
                    var m = chatMsgs[i];
                    sb.Append("{");
                    sb.Append($"\"role\":\"{EscapeJson(m.Role)}\",");
                    if (supportsVision && !string.IsNullOrEmpty(m.ImageBase64) && m.Role == "user")
                    {
                        // Claude multimodal: content block array
                        sb.Append("\"content\":[");
                        sb.Append("{\"type\":\"image\",\"source\":{");
                        sb.Append($"\"type\":\"base64\",\"media_type\":\"{EscapeJson(m.ImageMimeType)}\",");
                        sb.Append($"\"data\":\"{m.ImageBase64}\"");
                        sb.Append("}},");
                        sb.Append("{\"type\":\"text\",");
                        sb.Append($"\"text\":\"{EscapeJson(m.Content)}\"");
                        sb.Append("}]");
                    }
                    else
                    {
                        sb.Append($"\"content\":\"{EscapeJson(m.Content)}\"");
                    }
                    sb.Append("}");
                    if (i < chatMsgs.Count - 1) sb.Append(",");
                }
                sb.Append("]");
            }
            else
            {
                sb.Append("\"messages\":[");
                for (int i = 0; i < messages.Count; i++)
                {
                    var m = messages[i];
                    sb.Append("{");
                    sb.Append($"\"role\":\"{EscapeJson(m.Role)}\",");
                    if (supportsVision && !string.IsNullOrEmpty(m.ImageBase64) && m.Role == "user")
                    {
                        // OpenAI multimodal: content is array
                        sb.Append("\"content\":[");
                        sb.Append("{");
                        sb.Append("\"type\":\"image_url\",");
                        sb.Append("\"image_url\":{");
                        sb.Append($"\"url\":\"data:{EscapeJson(m.ImageMimeType)};base64,{m.ImageBase64}\"");
                        sb.Append("}");
                        sb.Append("},");
                        sb.Append("{");
                        sb.Append("\"type\":\"text\",");
                        sb.Append($"\"text\":\"{EscapeJson(m.Content)}\"");
                        sb.Append("}");
                        sb.Append("]");
                    }
                    else
                    {
                        sb.Append($"\"content\":\"{EscapeJson(m.Content)}\"");
                    }
                    sb.Append("}");
                    if (i < messages.Count - 1) sb.Append(",");
                }
                sb.Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private void PlaySound()
        {
            try { if (AppSettings.ReplySoundEnabled && ReplySound != null) { ReplySound.Source = new Uri("ms-appx:///Assets/replySound.mp3"); ReplySound.AutoPlay = true; ReplySound.Play(); } }
            catch { }
        }

        // ── Colour helpers ────────────────────────────────────────────────────

        private static Color GetAccentColor()
        {
            if (!_accentCached)
            {
                _accentColor  = new UISettings().GetColorValue(UIColorType.Accent);
                _accentCached = true;
            }
            return _accentColor;
        }

        // Call once when loading a new conversation to pre-build all bubble brushes.
        private void RebuildBrushCache()
        {
            var app = _conv?.Appearance;
            var ac  = GetAccentColor();

            // User background
            if (app != null && !string.IsNullOrEmpty(app.UserBubbleColor)
                && ConvAppearancePage.TryParseHex(app.UserBubbleColor, out Color uc))
                _cachedUserBg = new SolidColorBrush(uc);
            else
                _cachedUserBg = new SolidColorBrush(ac);

            // User foreground (auto contrast)
            {
                var c = (app != null && !string.IsNullOrEmpty(app.UserBubbleColor)
                         && ConvAppearancePage.TryParseHex(app.UserBubbleColor, out Color cc)) ? cc : ac;
                _cachedUserFg = new SolidColorBrush(
                    c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 186 ? Colors.Black : Colors.White);
            }

            // AI background
            if (app != null && !string.IsNullOrEmpty(app.AiBubbleColor)
                && ConvAppearancePage.TryParseHex(app.AiBubbleColor, out Color aiBc))
                _cachedAiBg = new SolidColorBrush(aiBc);
            else
            {
                double f = 0.65;
                _cachedAiBg = new SolidColorBrush(Color.FromArgb(ac.A,
                    (byte)(ac.R * f), (byte)(ac.G * f), (byte)(ac.B * f)));
            }

            // AI foreground (auto contrast based on AI bubble background)
            {
                var c = (app != null && !string.IsNullOrEmpty(app.AiBubbleColor)
                         && ConvAppearancePage.TryParseHex(app.AiBubbleColor, out Color cc)) ? cc
                         : Color.FromArgb(ac.A, (byte)(ac.R * 0.65), (byte)(ac.G * 0.65), (byte)(ac.B * 0.65));
                _cachedAiFg = new SolidColorBrush(
                    c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 186 ? Colors.Black : Colors.White);
            }
        }

        private SolidColorBrush UserBubbleBg() => _cachedUserBg  ?? (_cachedUserBg  = new SolidColorBrush(GetAccentColor()));
        private SolidColorBrush UserBubbleFg() => _cachedUserFg  ?? (_cachedUserFg  = _whiteBrush);
        private SolidColorBrush AiBubbleBg()   => _cachedAiBg   ?? (_cachedAiBg    = new SolidColorBrush(GetAccentColor()));
        private SolidColorBrush AiBubbleFg()   => _cachedAiFg   ?? (_cachedAiFg    = _whiteBrush);

        private void SetSendButtonColor()
            => SubmitButton.Background = _cachedUserBg ?? new SolidColorBrush(GetAccentColor());

        private void ApplyTheme() => AppSettings.ApplyTheme(RootGrid, this);

        /// <summary>Apply per-conversation background and refresh all bubble colors.</summary>
        private async void ApplyConvAppearance()
        {
            if (_conv == null) return;
            var app = _conv.Appearance ?? new ConvAppearance();

            // Background
            switch (app.BackgroundType)
            {
                case "solid":
                    ChatBgImage.Visibility  = Visibility.Collapsed;
                    if (ConvAppearancePage.TryParseHex(app.BackgroundValue, out Color bgc))
                    {
                        ChatBgSolid.Background = new SolidColorBrush(bgc);
                        ChatBgSolid.Visibility = Visibility.Visible;
                    }
                    else ChatBgSolid.Visibility = Visibility.Collapsed;
                    break;

                case "image":
                    ChatBgSolid.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(app.BackgroundValue))
                    {
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(app.BackgroundValue);
                            var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                            {
                                await ms.WriteAsync(bytes.AsBuffer());
                                ms.Seek(0);
                                await bmp.SetSourceAsync(ms);
                            }
                            ChatBgImage.Source     = bmp;
                            ChatBgImage.Visibility = Visibility.Visible;
                            // 暗度遮罩：DimOpacity 0=透明 100=全黑
                            byte alpha = (byte)(app.DimOpacity * 255 / 100);
                            ChatBgDimOverlay.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                            ChatBgDimOverlay.Visibility = alpha > 0 ? Visibility.Visible : Visibility.Collapsed;
                        }
                        catch { ChatBgImage.Visibility = Visibility.Collapsed; ChatBgDimOverlay.Visibility = Visibility.Collapsed; }
                    }
                    else { ChatBgImage.Visibility = Visibility.Collapsed; ChatBgDimOverlay.Visibility = Visibility.Collapsed; }
                    break;

                default:
                    ChatBgSolid.Visibility = Visibility.Collapsed;
                    ChatBgImage.Visibility = Visibility.Collapsed;
                    ChatBgDimOverlay.Visibility = Visibility.Collapsed;
                    break;
            }

            // Refresh bubble colors on existing bubbles
            foreach (var b in _bubbles)
            {
                bool isUser = b.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
                if (isUser)
                {
                    b.BackgroundColor = UserBubbleBg();
                    b.ForegroundColor = UserBubbleFg();
                }
                else
                {
                    b.BackgroundColor = AiBubbleBg();
                    b.ForegroundColor = AiBubbleFg();
                }
            }
        }

        private async void SetStatusBarColor()
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar"))
            {
                var sb = StatusBar.GetForCurrentView();
                sb.BackgroundColor = Colors.Black;
                sb.BackgroundOpacity = 1;
                sb.ForegroundColor = Colors.White;
                await sb.ShowAsync();
}
}

}
}

