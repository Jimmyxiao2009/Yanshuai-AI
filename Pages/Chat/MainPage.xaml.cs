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
    // ══════════════════════════════════════════════════════════════════════════
    // Chat bubble view-model
    // ══════════════════════════════════════════════════════════════════════════

    public class ToolStepEntry
    {
        public string Icon { get; set; }
        public string ToolName { get; set; }
        public string Detail { get; set; }
    }

    public class ChatBubble : INotifyPropertyChanged
    {
        private static readonly string[] _thinkingVerbsEn =
        {
            "Thinking", "Pondering", "Reflecting", "Reasoning", "Analyzing",
            "Considering", "Processing", "Deliberating", "Contemplating", "Mustering",
            "Wondering", "Exploring", "Examining", "Evaluating", "Synthesizing"
        };
        private static readonly string[] _thinkingVerbsZh =
        {
            "思考中", "推敲中", "分析中", "斟酌中", "推理中",
            "探索中", "评估中", "综合中", "权衡中", "审视中"
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
                {
                    var verbs = AppSettings.IsEnglish ? _thinkingVerbsEn : _thinkingVerbsZh;
                    _streamingVerb = verbs[_rng.Next(verbs.Length)];
                }
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

        // ── Action strip visibility (toggle on bubble tap) ────────────────────
        private bool _isActionStripVisible = false;
        public bool IsActionStripVisible
        {
            get => _isActionStripVisible;
            set { _isActionStripVisible = value; OnProp(); OnProp(nameof(ActionStripVisibility)); }
        }
        public Visibility ActionStripVisibility =>
            IsActionStripVisible ? Visibility.Visible : Visibility.Collapsed;

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

        // ── 工具调用步骤展示（思维链） ──────────────────────────────────────
        private string _toolStepsText = "";
        public string ToolStepsText
        {
            get => _toolStepsText;
            set
            {
                _toolStepsText = value;
                // 解析成结构化列表
                var list = new System.Collections.ObjectModel.ObservableCollection<ToolStepEntry>();
                foreach (var line in value.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string icon = "⏳"; string rest = line;
                    if (line.StartsWith("✅ ")) { icon = "✅"; rest = line.Substring(2); }
                    else if (line.StartsWith("❌ ")) { icon = "❌"; rest = line.Substring(2); }
                    else if (line.StartsWith("⏳ ")) { rest = line.Substring(2); }
                    // rest = "toolName  detail"
                    int sep = rest.IndexOf("  ");
                    string toolName = sep > 0 ? rest.Substring(0, sep) : rest;
                    string detail = sep > 0 ? rest.Substring(sep + 2) : "";
                    list.Add(new ToolStepEntry { Icon = icon, ToolName = toolName, Detail = detail });
                }
                _toolStepsList = list;
                OnProp();
                OnProp(nameof(ToolStepsList));
                OnProp(nameof(HasToolSteps));
                OnProp(nameof(ToolStepsVisibility));
            }
        }

        private System.Collections.ObjectModel.ObservableCollection<ToolStepEntry> _toolStepsList
            = new System.Collections.ObjectModel.ObservableCollection<ToolStepEntry>();
        public System.Collections.ObjectModel.ObservableCollection<ToolStepEntry> ToolStepsList => _toolStepsList;

        private bool _toolStepsExpanded = true;
        public bool ToolStepsExpanded
        {
            get => _toolStepsExpanded;
            set
            {
                _toolStepsExpanded = value;
                OnProp();
                OnProp(nameof(ToolStepsLabel));
                OnProp(nameof(ToolStepsChevron));
                OnProp(nameof(ToolStepsBodyVisibility));
            }
        }

        public bool HasToolSteps => _toolStepsList.Count > 0;
        public Visibility ToolStepsVisibility =>
            HasToolSteps && Role != "user" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ToolStepsBodyVisibility =>
            ToolStepsExpanded ? Visibility.Visible : Visibility.Collapsed;
        public string ToolStepsLabel
        {
            get
            {
                int done = _toolStepsList.Count(s => s.Icon == "✅" || s.Icon == "❌");
                int total = _toolStepsList.Count;
                return total > 0 ? string.Format("工具调用  {0}/{1}", done, total) : "工具调用";
            }
        }
        public string ToolStepsChevron => ToolStepsExpanded ? "▴" : "▾"; // small triangles

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
        private bool _isPendingConv = false;
        // 多附件支持
        private List<string> _pendingImagesBase64   = new List<string>();
        private List<string> _pendingImagesMimeType = new List<string>();
        private List<string> _pendingFileNames      = new List<string>(); // 文本文件 "name\x01content"
        // 兼容旧单图字段（供 PendingImageThumb 使用）
        private string _pendingImageBase64   { get => _pendingImagesBase64.Count  > 0 ? _pendingImagesBase64[0]  : null; }
        private string _pendingImageMimeType { get => _pendingImagesMimeType.Count > 0 ? _pendingImagesMimeType[0] : null; }
        private string _pendingFileName      = null; // 保留供单文件兼容
        private System.Threading.CancellationTokenSource _streamCts = null;
        private ChatBubble _expandedBubble = null;

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
                // ActiveConversation == null：新建对话（从对话列表点「新建」进来）
                _conv = new Conversation
                {
                    Title        = "新对话",
                    ApiProfileId = DataManager.Data.SelectedApiProfileId ?? "",
                };
                _isPendingConv = true;
                ResetToolPermissions();
                if (FunctionCallEngine.FullTrust)
                    FunctionCallEngine.FullTrust = false;
                if (FullTrustIcon != null)
                {
                    FullTrustIcon.Foreground = (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                    FullTrustIcon.Opacity = 0.6;
                }
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
                WebSearchIcon.Foreground = _toolsEnabled
                    ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                    : new SolidColorBrush(AppSettings.IsDark ? Colors.White : Colors.Black);
                WebSearchIcon.Opacity = _toolsEnabled ? 1.0 : 0.6;
            }

            // 强制设置工具栏图标前景色（防止深色模式下图标不可见）
            var toolbarFg = new SolidColorBrush(AppSettings.IsDark ? Colors.White : Colors.Black);
            if (FullscreenInputIcon != null) FullscreenInputIcon.Foreground = toolbarFg;
            if (AttachImageIcon    != null) AttachImageIcon.Foreground    = toolbarFg;

            // FullTrust 图标颜色初始化
            if (FullTrustIcon != null)
            {
                FullTrustIcon.Foreground = FunctionCallEngine.FullTrust
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 80, 40))
                    : (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                FullTrustIcon.Opacity = FunctionCallEngine.FullTrust ? 1.0 : 0.6;
            }

            // 输入框占位符提示
            if (InputTextBox != null)
            {
                InputTextBox.PlaceholderText = AppSettings.EnterBehavior == 1
                    ? "说点什么… (Ctrl+Enter 发送)"
                    : "说点什么…";
            }

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

        // ── New conversation (from top-bar ＋ button) ──────────────────────────

        private void NewConvBtn_Click(object sender, RoutedEventArgs e)
        {
            AppState.ActiveConversation = null;
            Frame.Navigate(typeof(MainPage));
            if (Frame.BackStack.Count > 0 && Frame.BackStack[Frame.BackStack.Count - 1].SourcePageType == typeof(MainPage))
                Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
        }

        // ── Conversation settings / appearance ────────────────────────────────

        private void ConvSettingsBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvSettingsPage));

        private void ConvAppearanceBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvAppearancePage));

        // ── 文件附加 ─────────────────────────────────────────────────────────

        private class AttachPreview
        {
            public Windows.UI.Xaml.Media.ImageSource Thumb { get; set; }
            public Visibility ThumbVisibility => Thumb != null ? Visibility.Visible : Visibility.Collapsed;
            public string Icon { get; set; } = "";
            public Visibility IconVisibility => Thumb == null ? Visibility.Visible : Visibility.Collapsed;
            public string Label { get; set; }
        }

        private readonly System.Collections.ObjectModel.ObservableCollection<AttachPreview> _pendingPreviews
            = new System.Collections.ObjectModel.ObservableCollection<AttachPreview>();

        private async System.Threading.Tasks.Task<byte[]> CompressImageAsync(Windows.Storage.StorageFile file)
        {
            using (var stream = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
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
                    var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ms);
                    enc.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied,
                        newW, newH, 96, 96, pixels.DetachPixelData());
                    await enc.FlushAsync();
                    ms.Seek(0);
                    var b = new byte[ms.Size];
                    await ms.ReadAsync(b.AsBuffer(), (uint)ms.Size, Windows.Storage.Streams.InputStreamOptions.None);
                    return b;
                }
            }
        }

        private async System.Threading.Tasks.Task<string> ReadFileAsTextAsync(Windows.Storage.StorageFile file)
        {
            try
            {
                string t = await Windows.Storage.FileIO.ReadTextAsync(file);
                return t.Length > 20000 ? t.Substring(0, 20000) + "\n...[已截断]" : t;
            }
            catch
            {
                try
                {
                    var buf = await Windows.Storage.FileIO.ReadBufferAsync(file);
                    byte[] raw = new byte[buf.Length];
                    Windows.Storage.Streams.DataReader.FromBuffer(buf).ReadBytes(raw);
                    string t = System.Text.Encoding.UTF8.GetString(raw);
                    return t.Length > 20000 ? t.Substring(0, 20000) + "\n...[已截断]" : t;

                }
                catch (Exception ex) { return "(无法读取: " + ex.Message + ")"; }
            }
        }

        private string GetFileIcon(string ext)
        {
            switch (ext)
            {
                case ".pdf":   return "";
                case ".doc": case ".docx": return "";
                case ".xls": case ".xlsx": return "";
                case ".ppt": case ".pptx": return "";
                default: return "";
            }
        }

        private async void AttachImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            string[] imgExts  = { ".jpg",".jpeg",".png",".gif",".webp" };
            string[] docExts  = { ".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx" };
            string[] txtExts  = { ".txt",".md",".csv",".json",".xml",".html",".htm",
                                  ".cs",".py",".js",".ts",".cpp",".c",".h",".java",
                                  ".log",".yaml",".yml",".toml",".ini",".cfg",".sh",".bat" };
            foreach (var t in imgExts) picker.FileTypeFilter.Add(t);
            foreach (var t in docExts) picker.FileTypeFilter.Add(t);
            foreach (var t in txtExts) picker.FileTypeFilter.Add(t);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            foreach (var file in files)
            {
                string ext = file.FileType.ToLowerInvariant();
                bool isImg = System.Array.IndexOf(imgExts, ext) >= 0;
                if (isImg)
                {
                    byte[] bytes = await CompressImageAsync(file);
                    _pendingImagesBase64.Add(Convert.ToBase64String(bytes));
                    _pendingImagesMimeType.Add("image/jpeg");
                    var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        await ms.WriteAsync(bytes.AsBuffer());
                        ms.Seek(0);
                        await bmp.SetSourceAsync(ms);
                    }
                    _pendingPreviews.Add(new AttachPreview { Thumb = bmp, Label = file.Name });
                }
                else
                {
                    string text = await ReadFileAsTextAsync(file);
                    _pendingFileNames.Add(file.Name + "" + text);
                    _pendingPreviews.Add(new AttachPreview { Icon = "", Label = file.Name });
                }
            }
            PendingAttachList.ItemsSource = _pendingPreviews;
            ImagePreviewBar.Visibility = _pendingPreviews.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearImageBtn_Click(object sender, RoutedEventArgs e)
        {
            _pendingImagesBase64.Clear();
            _pendingImagesMimeType.Clear();
            _pendingFileNames.Clear();
            _pendingPreviews.Clear();
            _pendingFileName = null;
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

        private bool _toolsEnabled = true;
        private bool _fetchSearchEnabled = false;
        // 本次对话已授权的敏感操作 key（由 FunctionCallEngine._grantedPermissions 管理）
        // 新建对话时重置
        private void ResetToolPermissions() => FunctionCallEngine.ResetGrantedPermissions();

        private void WebSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            _toolsEnabled = !_toolsEnabled;
            if (WebSearchIcon != null)
            {
                WebSearchIcon.Foreground = _toolsEnabled
                    ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                    : (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                WebSearchIcon.Opacity = _toolsEnabled ? 1.0 : 0.6;
            }
            // 工具关闭时同步关闭 fetch 选项
            if (!_toolsEnabled)
            {
                _fetchSearchEnabled = false;
                if (FetchSearchIcon != null)
                {
                    FetchSearchIcon.Foreground = (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                    FetchSearchIcon.Opacity = 0.6;
                }
            }
            if (FetchSearchBtn != null)
                FetchSearchBtn.Visibility = _toolsEnabled ? Visibility.Visible : Visibility.Collapsed;
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

        private void FullTrustBtn_Click(object sender, RoutedEventArgs e)
        {
            FunctionCallEngine.FullTrust = !FunctionCallEngine.FullTrust;
            if (FullTrustIcon != null)
            {
                FullTrustIcon.Foreground = FunctionCallEngine.FullTrust
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 80, 40))
                    : (SolidColorBrush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
                FullTrustIcon.Opacity = FunctionCallEngine.FullTrust ? 1.0 : 0.6;
            }
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
            // 构建 API 消息（文件内容内联）和气泡文字（只显示文件名）
            string apiUserInput = userInput;
            var bubbleFileTags = new List<string>();

            // 新多文件系统
            foreach (var pf in _pendingFileNames)
            {
                int sep = pf.IndexOf('\x01');
                if (sep < 0) continue;
                string fname   = pf.Substring(0, sep);
                string fcontent = pf.Substring(sep + 1);
                apiUserInput = "【附件：" + fname + "】\n```\n" + fcontent + "\n```\n\n" + apiUserInput;
                bubbleFileTags.Add(fname);
            }
            // 兼容旧单文件字段
            if (!string.IsNullOrEmpty(_pendingFileName) && _pendingFileName.Contains("\x01"))
            {
                int sep = _pendingFileName.IndexOf('\x01');
                string fname    = _pendingFileName.Substring(0, sep);
                string fcontent = _pendingFileName.Substring(sep + 1);
                apiUserInput = "【附件：" + fname + "】\n```\n" + fcontent + "\n```\n\n" + apiUserInput;
                bubbleFileTags.Add(fname);
            }

            bool hasAttachment = _pendingImagesBase64.Count > 0 || bubbleFileTags.Count > 0;
            if (string.IsNullOrWhiteSpace(apiUserInput) && !hasAttachment) return;

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
                Role = "user", Content = apiUserInput, Timestamp = DateTime.Now,
                ImagesBase64    = _pendingImagesBase64.Count > 0 ? new List<string>(_pendingImagesBase64) : null,
                ImagesMimeType  = _pendingImagesBase64.Count > 0 ? new List<string>(_pendingImagesMimeType) : null,
                AttachedFileNames = bubbleFileTags.Count > 0 ? new List<string>(bubbleFileTags) : null,
                // 兼容旧字段
                ImageBase64     = _pendingImagesBase64.Count > 0 ? _pendingImagesBase64[0] : null,
                ImageMimeType   = _pendingImagesMimeType.Count > 0 ? _pendingImagesMimeType[0] : null,
            };
            _conv.Messages.Add(userMsg);
            _conv.UpdatedAt = DateTime.Now;

            // 气泡只显示用户输入 + 文件名标签
            string bubbleContent = userInput;
            if (bubbleFileTags.Count > 0)
                bubbleContent = (string.IsNullOrEmpty(userInput) ? "" : userInput + "\n")
                    + string.Join("  ", bubbleFileTags.Select(n => "📎 " + n));

            if (_conv.Messages.Count(m => m.Role == "user") == 1)
            {
                string titleText = string.IsNullOrEmpty(userInput)
                    ? (bubbleFileTags.Count > 0 ? bubbleFileTags[0] : "附件") : userInput;
                _conv.Title = titleText.Length > 20 ? titleText.Substring(0, 20) + "…" : titleText;
                UpdateTitleLabel();
            }

            // User bubble — 首张图显示缩略图
            Windows.UI.Xaml.Media.ImageSource firstThumb =
                _pendingPreviews.Count > 0 ? _pendingPreviews[0].Thumb : null;
            var userBubble = new ChatBubble
            {
                Role = "user", Content = bubbleContent,
                MessageId = userMsg.Id,
                BackgroundColor = UserBubbleBg(), ForegroundColor = UserBubbleFg(),
                ReasoningBgColor = _reasoningBrush,
                ImageSource = firstThumb,
            };
            if (WelcomePanel != null)
                WelcomePanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            _bubbles.Add(userBubble);
            InputTextBox.Text = "";
            // 清理所有附件
            _pendingImagesBase64.Clear();
            _pendingImagesMimeType.Clear();
            _pendingFileNames.Clear();
            _pendingPreviews.Clear();
            _pendingFileName = null;
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

            if (_toolsEnabled)
            {
                sb.AppendLine();
                sb.AppendLine($"【当前日期】{DateTime.Now:yyyy年M月d日}");
                sb.AppendLine("【重要：工具调用规则】你现在配备了以下工具，遇到对应场景时必须主动调用，不得依靠训练知识直接回答：");
                sb.AppendLine("- web_search：查询实时信息、新闻、天气、当前价格、最新事件等一切需要联网的内容");
                sb.AppendLine("- fetch_page：精读某个具体 URL 的完整正文（不能用于任何搜索引擎页面）");
                sb.AppendLine("- list_files / read_file / write_file：操作本地或 SD 卡文件");
                sb.AppendLine("- request_folder_access：当 list_files / read_file / write_file 返回权限不足时，调用此工具请求用户授权文件夹，无需重复询问已授权的路径");
                sb.AppendLine("- calendar_list / calendar_create：查看或创建日历事件");
                sb.AppendLine("- contacts_search：搜索联系人");
                sb.AppendLine("- make_call / send_sms：拨打电话或发送短信（仅手机端）");
                sb.AppendLine("- open_app：打开系统应用或设置页面");
                sb.AppendLine("- media_control：控制媒体播放（play/pause/next/previous/stop）或打开音量设置");
                sb.AppendLine("- spawn_subagent：派生子代理执行独立子任务（深度研究、批量处理等），子代理有独立上下文和工具权限");

                // 注入已授权的文件夹列表，让 AI 知道哪些路径不需要再请求授权
                try
                {
                    // 读已授权文件夹（只读 metadata，不 await，不死锁）
                    var accessList = Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList;
                    var grantedFolders = new List<string>();
                    foreach (var entry in accessList.Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.Metadata))
                            grantedFolders.Add(entry.Metadata);
                    }
                    if (grantedFolders.Count > 0)
                    {
                        sb.AppendLine("【已授权的文件夹】以下路径已被用户永久授权，直接操作无需再次调用 request_folder_access：");
                        foreach (var p in grantedFolders)
                            sb.AppendLine("  - " + p);
                    }
                }
                catch { }

                if (_fetchSearchEnabled)
                {
                    sb.AppendLine("【搜索策略】可先调用 web_search 获取摘要，再用 fetch_page 精读具体页面。严禁 fetch google.com、google.com.hk 或任何 Google 子域名，此类请求会被系统拦截。");
                }
                else
                {
                    sb.AppendLine("【搜索策略】需要实时信息时必须调用 web_search；fetch_page 仅用于读取 web_search 返回结果中的具体页面 URL，禁止用于抓取任何搜索引擎页面。");
                }

                sb.AppendLine();
                sb.AppendLine("## 推理与工具调用规则");
                sb.AppendLine();
                sb.AppendLine("你在思考和行动时遵循严格的循环，不允许跳步：");
                sb.AppendLine();
                sb.AppendLine("1. Thought：分析当前情况，说明为什么需要调用下一个工具，预期得到什么。");
                sb.AppendLine("2. Action：调用一个工具。每轮只调用一个。");
                sb.AppendLine("3. Observation：读取工具返回结果，在下一个 Thought 中处理——评估结果是否充分，还缺少什么。");
                sb.AppendLine("4. 重复，直到信息充分，再输出面向用户的最终回复。");
                sb.AppendLine();
                sb.AppendLine("约束：");
                sb.AppendLine("- 禁止无 Thought 直接调用工具。");
                sb.AppendLine("- 禁止连续调用多个工具而不分析中间结果。");
                sb.AppendLine("- 工具返回错误或空结果时，在 Thought 中分析原因并调整参数或换用其他工具，不要用相同参数重试。");
                sb.AppendLine("- 只有在确认信息充分后，才输出最终回复。");

                sb.AppendLine();
                sb.AppendLine("## 行为准则");
                sb.AppendLine();
                sb.AppendLine("- 回复语言跟随用户输入语言。");
                sb.AppendLine("- 上下文中已有搜索结果或文件内容时，优先使用这些信息，不重复搜索已知内容。");
                sb.AppendLine("- 写文件、执行命令等不可逆操作前，在 Thought 中说明操作内容和影响范围，通过权限确认回调等待用户确认后再执行（Full Trust 模式除外）。");
                sb.AppendLine("- 回答完整，不主动截断；如内容较长，完整输出后再询问用户是否需要展开说明。");
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

        // ── Bubble tap toggles action strip ──────────────────────────────────

        private void BubbleTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is ChatBubble b)) return;
            if (_expandedBubble == b)
            {
                b.IsActionStripVisible = false;
                _expandedBubble = null;
                return;
            }
            if (_expandedBubble != null)
                _expandedBubble.IsActionStripVisible = false;
            b.IsActionStripVisible = true;
            _expandedBubble = b;
        }

        // ── Reasoning toggle ──────────────────────────────────────────────────

        private void ToggleReasoning_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ChatBubble b)
                b.ReasoningExpanded = !b.ReasoningExpanded;
        }

        private void ToggleToolSteps_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ChatBubble b)
                b.ToolStepsExpanded = !b.ToolStepsExpanded;
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
            {
                var images = m.GetAllImages();
                if (images.Count > 1)
                {
                    apiMessages.Add(new ApiRequestMessage
                    {
                        Role          = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                        Content       = m.Content,
                        ImageBase64   = images[0].Base64,
                        ImageMimeType = images[0].Mime,
                    });
                    for (int ii = 1; ii < images.Count; ii++)
                        apiMessages.Add(new ApiRequestMessage
                        {
                            Role          = "user",
                            Content       = "",
                            ImageBase64   = images[ii].Base64,
                            ImageMimeType = images[ii].Mime,
                        });
                }
                else
                {
                    apiMessages.Add(new ApiRequestMessage
                    {
                        Role          = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                        Content       = m.Content,
                        ImageBase64   = images.Count == 1 ? images[0].Base64 : null,
                        ImageMimeType = images.Count == 1 ? images[0].Mime   : null,
                    });
                }
            }
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
            var webEnabled = _toolsEnabled;
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
                        // 差距二修复：工具循环开始前先显示"思考中..."，循环内容实时推送到气泡
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            aiBubble.SearchStatusText = "⏳ 思考中…");
                        string finalContent = await RunToolUseLoopAsync(profile, apiMessages, aiBubble, cts.Token);
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            aiBubble.Content = finalContent;
                            aiBubble.SearchStatusText = "";
                        });
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
                    ImageBase64 = m.ImageBase64,
                    ImageMimeType = m.ImageMimeType,
                });
            }

            // 权限确认回调：write_file / calendar_create 需要用户弹窗确认
            ToolPermissionCallback permCb = (toolName, desc) =>
            {
                var tcs = new TaskCompletionSource<bool>();
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var dlg = new ContentDialog
                        {
                            Title = "AI 请求敏感操作",
                            Content = desc,
                            PrimaryButtonText = "允许",
                            SecondaryButtonText = "拒绝",
                            RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
                        };
                        var dlgResult = await dlg.ShowAsync().AsTask();
                        tcs.TrySetResult(dlgResult == ContentDialogResult.Primary);
                    }
                    catch { tcs.TrySetResult(false); }
                });
                return tcs.Task;
            };

            // 文件夹访问回调：request_folder_access 需要用户通过系统选择器授权
            FolderAccessCallback folderCb = (requestedPath) =>
            {
                var tcs = new TaskCompletionSource<string>();
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var dlg = new ContentDialog
                        {
                            Title = "AI 请求文件夹访问权限",
                            Content = "AI 想要访问以下文件夹：\n\n" + requestedPath + "\n\n是否授权？点击「授权」后请在系统文件夹选择器中选中该文件夹。",
                            PrimaryButtonText = "授权",
                            SecondaryButtonText = "拒绝",
                            RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
                        };
                        var dlgResult = await dlg.ShowAsync().AsTask();
                        if (dlgResult != ContentDialogResult.Primary)
                        {
                            tcs.TrySetResult(null);
                            return;
                        }
                        var picker = new FolderPicker
                        {
                            SuggestedStartLocation = PickerLocationId.Desktop,
                            ViewMode = PickerViewMode.List,
                        };
                        picker.FileTypeFilter.Add("*");
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder != null)
                        {
                            // token 用路径哈希，确保唯一且跨重启持久化
                            string token = "fa_" + Math.Abs(folder.Path.ToLowerInvariant().GetHashCode()).ToString();
                            StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder, folder.Path);
                            tcs.TrySetResult(folder.Path);
                        }
                        else
                        {
                            tcs.TrySetResult(null);
                        }
                    }
                    catch { tcs.TrySetResult(null); }
                });
                return tcs.Task;
            };

            // 工具步骤进度回调：实时显示思维链
            var stepsLog = new List<string>();
            ToolProgressCallback progCb = (phase, toolName, detail) =>
            {
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (phase == "thinking")
                    {
                        // Thinking step: show a 💭 entry with the tool about to be called
                        stepsLog.Add("💭 " + toolName + "  " + detail);
                    }
                    else if (phase == "calling")
                    {
                        // Calling step: replace last 💭 with ⏳, or add new ⏳
                        string thoughtPrefix = "💭 " + toolName;
                        bool replaced = false;
                        for (int i = stepsLog.Count - 1; i >= 0; i--)
                        {
                            if (stepsLog[i].StartsWith(thoughtPrefix))
                            {
                                stepsLog[i] = "⏳ " + toolName + "  " + detail;
                                replaced = true;
                                break;
                            }
                        }
                        if (!replaced)
                            stepsLog.Add("⏳ " + toolName + "  " + detail);
                    }
                    else if (phase == "result" || phase == "error")
                    {
                        string prefix = phase == "error" ? "❌ " : "✅ ";
                        string pending = "⏳ " + toolName;
                        for (int i = stepsLog.Count - 1; i >= 0; i--)
                        {
                            if (stepsLog[i].StartsWith(pending))
                            {
                                stepsLog[i] = prefix + toolName + "  " + detail;
                                break;
                            }
                        }
                    }
                    aiBubble.ToolStepsText = string.Join("\n", stepsLog);
                    aiBubble.ToolStepsExpanded = true;
                    int running = stepsLog.Count(s => s.StartsWith("⏳"));
                    aiBubble.SearchStatusText = running > 0 ? "⏳ 执行中 (" + running + " 个工具)…" : "";
                });
            };

            // 中间文本实时推送回调
            ToolTextContentCallback textCb = (intermediateText) =>
            {
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.Content = intermediateText;
                });
            };

            // 用 FunctionCallEngine 跑完整工具循环
            FunctionCallLoopResult result = await FunctionCallEngine.RunFunctionCallLoopAsync(
                profile, toolMessages, _conv, permCb, folderCb, progCb, textCb, profile.VisionEnabled);
            string content = result.Content;
            string reasoning = result.Reasoning ?? "";
            List<ApiMessageWithTools> allMessages = result.AllMessages ?? new List<ApiMessageWithTools>();

            // 推送 reasoning 到气泡（DeepSeek V4 等模型的思考过程）
            if (!string.IsNullOrEmpty(reasoning))
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.ReasoningContent = reasoning;
                });
            }

            // 显示工具调用状态
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

        // 按偏移量从 JSON 字符串中读取引号包裹的值
        private static string ExtractJsonString(string json, int start)
        {
            while (start < json.Length && json[start] != '"') start++;
            if (start >= json.Length) return "";
            start++;
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

