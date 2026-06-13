using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════════
    // Chat bubble view-model（从 MainPage.xaml.cs 迁出，类名保持 ChatBubble 以零改动复用）
    // 自包含：实现 INotifyPropertyChanged，不引用任何 MainPage / 静态单例。
    // ══════════════════════════════════════════════════════════════════════════
    public class ChatBubble : INotifyPropertyChanged
    {
        private static readonly string[] _thinkingVerbs =
        {
            "思考中", "分析中", "推理中", "斟酌中", "酝酿中",
            "梳理中", "处理中", "沉思中", "推敲中", "构思中",
            "探寻中", "探索中", "审视中", "评估中", "整合中"
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
                OnProp(nameof(ContinueBtnVisibility));
            }
        }

        // User bubbles: always plain text (left column)
        public Visibility UserVisibility =>
            Role == "user" && !_isSystemMessage ? Visibility.Visible : Visibility.Collapsed;

        // AI bubble while streaming: plain TextBlock (updates smoothly every token)
        public Visibility AiStreamingVisibility =>
            Role != "user" && _isStreaming && !_isSystemMessage ? Visibility.Visible : Visibility.Collapsed;

        // AI bubble after stream completes: RichTextBlock with markdown
        public Visibility AiRenderedVisibility =>
            Role != "user" && !_isStreaming && !_isSystemMessage ? Visibility.Visible : Visibility.Collapsed;

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
            Role != "user" && !_isStreaming && !_hasError ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ContinueBtnVisibility =>
            Role != "user" && !_isStreaming ? Visibility.Visible : Visibility.Collapsed;

        // ── Error state (API error → show retry button) ──────────────────────
        private bool _hasError = false;
        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnProp(); OnProp(nameof(RetryBtnVisibility)); OnProp(nameof(RegenerateBtnVisibility)); }
        }
        public Visibility RetryBtnVisibility =>
            HasError ? Visibility.Visible : Visibility.Collapsed;

        // ── First message (character card opening line) ──────────────────────
        private bool _isFirstMessage = false;
        public bool IsFirstMessage
        {
            get => _isFirstMessage;
            set { _isFirstMessage = value; OnProp(); OnProp(nameof(FirstMessageTagVisibility)); }
        }
        public Visibility FirstMessageTagVisibility =>
            _isFirstMessage ? Visibility.Visible : Visibility.Collapsed;

        // ── System message (info/error notifications, not a chat message) ──────
        private bool _isSystemMessage = false;
        public bool IsSystemMessage
        {
            get => _isSystemMessage;
            set
            {
                _isSystemMessage = value;
                OnProp();
                OnProp(nameof(SystemMessageVisibility));
                OnProp(nameof(ActionStripVisibility));
            }
        }
        public Visibility SystemMessageVisibility =>
            IsSystemMessage ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActionStripVisibility =>
            IsSystemMessage ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnProp([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
