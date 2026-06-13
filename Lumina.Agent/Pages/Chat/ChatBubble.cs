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

    public enum ThinkChainKind { Reasoning, Tool }

    public class ThinkChainEntry : INotifyPropertyChanged
    {
        public ThinkChainKind Kind { get; set; }

        // Reasoning fields
        private readonly StringBuilder _reasoningSb = new StringBuilder();
        private string _reasoningText = "";
        public string ReasoningText
        {
            get => _reasoningText;
            set { _reasoningSb.Clear(); if (value != null) _reasoningSb.Append(value); _reasoningText = value; OnProp(); }
        }

        /// <summary>流式追加推理增量。用 StringBuilder 累积，避免 string += 每次重新分配整串（O(n²)）。</summary>
        public void AppendReasoning(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return;
            _reasoningSb.Append(chunk);
            _reasoningText = _reasoningSb.ToString(); // 绑定到 UI 需要 string，这一次 ToString 不可避免
            OnProp(nameof(ReasoningText));
        }

        private bool _isStreamingReasoning;
        public bool IsStreamingReasoning
        {
            get => _isStreamingReasoning;
            set { _isStreamingReasoning = value; OnProp(); OnProp(nameof(ReasoningLabel)); }
        }

        private static readonly string[] _verbs = { "思考中", "推敲中", "分析中", "推理中", "探索中" };
        private static readonly Random _rng = new Random();
        private string _streamingVerb = "";
        public string ReasoningLabel => _isStreamingReasoning
            ? _streamingVerb + "…"
            : (IsExpanded ? "思考过程 ▲" : "思考过程 ▼");

        // Tool fields
        private string _icon = "⏳";
        public string Icon
        {
            get => _icon;
            set { _icon = value; OnProp(); }
        }
        public string ToolName { get; set; }
        private string _detail = "";
        public string Detail
        {
            get => _detail;
            set { _detail = value; OnProp(); }
        }

        // Shared
        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnProp(); OnProp(nameof(BodyVisibility)); OnProp(nameof(ReasoningLabel)); OnProp(nameof(ChevronText)); }
        }
        public Visibility BodyVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        public string ChevronText => IsExpanded ? "▴" : "▾";

        public Visibility ReasoningVisibility => Kind == ThinkChainKind.Reasoning ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ToolVisibility => Kind == ThinkChainKind.Tool ? Visibility.Visible : Visibility.Collapsed;

        public void StartStreaming()
        {
            _streamingVerb = _verbs[_rng.Next(_verbs.Length)];
            IsStreamingReasoning = true;
        }
        public void StopStreaming() { IsStreamingReasoning = false; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnProp([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

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

        // ── Streaming performance: batch token appends ───────────────────
        private StringBuilder _streamBuf = new StringBuilder(4096);
        private StringBuilder _reasoningBuf = new StringBuilder(2048);
        private int _pendingTokens = 0;
        private const int FlushThreshold = 4; // flush every N tokens

        public string Role { get; set; }

        public string Content
        {
            get => _content;
            set { _content = value; OnProp(); OnProp(nameof(RenderedContent)); }
        }

        /// <summary>
        /// 供 markdown 渲染视图（RichTextBlock + MarkdownBlock）绑定的正文：流式进行中
        /// 恒为空，仅在流式结束（IsStreaming=false）后才返回完整文本。
        ///
        /// 渲染视图与流式视图（StreamFade）是同一 DataTemplate 里的两个并列元素，靠
        /// Visibility 切换；但折叠元素的绑定依旧有效。若 MarkdownBlock 直接绑 Content，
        /// 流式期间每来一批 token（≈4 次/秒）就会把「到目前为止的全部内容」从头重新
        /// 解析 + 排版一次——这是 O(n²)，且发生在 await Dispatcher 的 UI 工作里，会反压
        /// 阻塞 SSE reader，导致已收到的 token 在管线里大量积压（取消时一次性涌出）。
        /// 用本属性让整条消息只在收尾时解析一次；流式期间正文由 StreamFade 绑 Content 实时显示。
        /// </summary>
        public string RenderedContent => _isStreaming ? "" : _content;

        /// <summary>
        /// Append a streaming token efficiently. Batches updates to reduce
        /// PropertyChanged notifications and avoid O(n²) string concat.
        /// </summary>
        public void AppendStreamToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            _streamBuf.Append(token);
            _pendingTokens++;
            if (_pendingTokens >= FlushThreshold)
                FlushStream();
        }

        /// <summary>Append reasoning token with batching.</summary>
        public void AppendReasoningToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            _reasoningBuf.Append(token);
            _pendingTokens++;
            if (_pendingTokens >= FlushThreshold)
                FlushStream();
        }

        /// <summary>Flush buffered tokens to Content/ReasoningContent.</summary>
        public void FlushStream()
        {
            if (_streamBuf.Length > 0 && _streamBuf.Length != _content.Length)
            {
                _content = _streamBuf.ToString();
                OnProp(nameof(Content));
            }
            if (_reasoningBuf.Length > 0 && _reasoningBuf.Length != _reasoningContent.Length)
            {
                _reasoningContent = _reasoningBuf.ToString();
                OnProp(nameof(ReasoningContent));
                OnProp(nameof(HasReasoning));
                OnProp(nameof(HasReasoningVisibility));
                OnProp(nameof(ReasoningLabel));
            }
            _pendingTokens = 0;
        }

        /// <summary>Call when streaming ends to sync buffers and reset.</summary>
        public void FinalizeStream()
        {
            FlushStream();
            // Sync Content from buffer (in case FlushStream skipped due to length equality)
            _content = _streamBuf.ToString();
            _reasoningContent = _reasoningBuf.Length > 0 ? _reasoningBuf.ToString() : _reasoningContent;
            _streamBuf.Clear();
            _reasoningBuf.Clear();
            _pendingTokens = 0;
            OnProp(nameof(Content));
            OnProp(nameof(RenderedContent));
            OnProp(nameof(ReasoningContent));
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
                OnProp(nameof(RenderedContent)); // 流式结束→渲染视图此刻才解析整段（仅一次）
                OnProp(nameof(RegenerateBtnVisibility)); // show after stream ends
            }
        }

        // User bubbles: always plain text (left column)
        public Visibility UserVisibility =>
            Role == "user" ? Visibility.Visible : Visibility.Collapsed;

        // AI bubble while streaming: only shown when content is actually being typed
        // (hide during tool use when SearchStatusText is active to avoid empty bubble)
        public Visibility AiStreamingVisibility =>
            Role != "user" && _isStreaming && string.IsNullOrEmpty(_searchStatusText)
                ? Visibility.Visible : Visibility.Collapsed;

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
        /// <summary>外置图片引用 id（仅 ~32 字符，不含 base64）。点击看大图时按它从
        /// ImageStore 读取全分辨率，避免把整张 base64 常驻在气泡上。</summary>
        public string ImageRefId { get; set; }
        /// <summary>旧数据回退用的内联 base64（仅当没有外置引用时才设置）。</summary>
        public string FullImageBase64 { get; set; }
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
            set { _searchStatusText = value; OnProp(); OnProp(nameof(SearchStatusVisibility)); OnProp(nameof(AiStreamingVisibility)); }
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

        // ── ThinkChain：思考+工具交替统一列表 ──────────────────────────────
        private readonly ObservableCollection<ThinkChainEntry> _thinkChain
            = new ObservableCollection<ThinkChainEntry>();
        public ObservableCollection<ThinkChainEntry> ThinkChain => _thinkChain;
        public Visibility ThinkChainVisibility =>
            _thinkChain.Count > 0 && Role != "user" ? Visibility.Visible : Visibility.Collapsed;

        private int _prevReasoningLen = 0;

        /// <summary>追加推理文本增量到最后一个 Reasoning 条目（或新建）。</summary>
        public void AppendReasoningChunk(string fullText)
        {
            if (string.IsNullOrEmpty(fullText)) return;
            string chunk = fullText.Length > _prevReasoningLen
                ? fullText.Substring(_prevReasoningLen) : "";
            _prevReasoningLen = fullText.Length;
            if (string.IsNullOrEmpty(chunk)) return;

            var last = _thinkChain.Count > 0 ? _thinkChain[_thinkChain.Count - 1] : null;
            if (last != null && last.Kind == ThinkChainKind.Reasoning)
            {
                last.AppendReasoning(chunk);
            }
            else
            {
                var entry = new ThinkChainEntry { Kind = ThinkChainKind.Reasoning, ReasoningText = chunk, IsExpanded = true };
                entry.StartStreaming();
                _thinkChain.Add(entry);
            }
            OnProp(nameof(ThinkChainVisibility));
        }

        /// <summary>停止最后一个 Reasoning 条目的流式动画。</summary>
        public void StopReasoningStreaming()
        {
            for (int i = _thinkChain.Count - 1; i >= 0; i--)
                if (_thinkChain[i].Kind == ThinkChainKind.Reasoning)
                { _thinkChain[i].StopStreaming(); break; }
        }

        /// <summary>新建或更新工具步骤条目。</summary>
        public void AddOrUpdateToolStep(string icon, string toolName, string detail)
        {
            // 从后往前找同名工具条目更新
            for (int i = _thinkChain.Count - 1; i >= 0; i--)
            {
                var e = _thinkChain[i];
                if (e.Kind == ThinkChainKind.Tool && e.ToolName == toolName && e.Icon != "✅" && e.Icon != "❌")
                {
                    e.Icon = icon;
                    e.Detail = detail;
                    OnProp(nameof(ThinkChainVisibility));
                    return;
                }
            }
            // 新建
            _thinkChain.Add(new ThinkChainEntry { Kind = ThinkChainKind.Tool, Icon = icon, ToolName = toolName, Detail = detail });
            OnProp(nameof(ThinkChainVisibility));
        }

        /// <summary>导出 ThinkChain 为可持久化的步骤列表（推理+工具，保序）。无内容返回 null。</summary>
        public List<ThinkStep> ExportThinkSteps()
        {
            if (_thinkChain.Count == 0) return null;
            var list = new List<ThinkStep>(_thinkChain.Count);
            foreach (var e in _thinkChain)
            {
                if (e.Kind == ThinkChainKind.Reasoning)
                {
                    if (string.IsNullOrEmpty(e.ReasoningText)) continue;
                    list.Add(new ThinkStep { Kind = "reasoning", Text = e.ReasoningText });
                }
                else
                {
                    list.Add(new ThinkStep { Kind = "tool", ToolName = e.ToolName, Detail = e.Detail, Icon = e.Icon });
                }
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>从持久化步骤列表重建 ThinkChain（保序）。用于切换/重开对话后还原展示。</summary>
        public void RestoreThinkSteps(List<ThinkStep> steps)
        {
            _thinkChain.Clear();
            _prevReasoningLen = 0;
            if (steps != null)
            {
                bool expanded = AppSettings.ReasoningExpandedByDefault;
                foreach (var s in steps)
                {
                    if (s == null) continue;
                    if (s.Kind == "tool")
                    {
                        _thinkChain.Add(new ThinkChainEntry
                        {
                            Kind = ThinkChainKind.Tool,
                            Icon = string.IsNullOrEmpty(s.Icon) ? "✅" : s.Icon,
                            ToolName = s.ToolName,
                            Detail = s.Detail,
                            IsExpanded = expanded,
                        });
                    }
                    else if (!string.IsNullOrEmpty(s.Text))
                    {
                        _thinkChain.Add(new ThinkChainEntry
                        {
                            Kind = ThinkChainKind.Reasoning,
                            ReasoningText = s.Text,
                            IsExpanded = expanded,
                        });
                    }
                }
            }
            OnProp(nameof(ThinkChainVisibility));
        }

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
        [DataMember(Name = "usage")]   public ApiUsageInfo Usage { get; set; }
    }
    [DataContract] public class ApiErrorDetail { [DataMember(Name = "message")] public string Message { get; set; } }
    [DataContract] internal class StreamDelta
    {
        [DataMember(Name = "content")]           public string Content          { get; set; }
        [DataMember(Name = "reasoning_content")] public string ReasoningContent { get; set; }
    }
    [DataContract] internal class StreamChoice  { [DataMember(Name = "delta")] public StreamDelta Delta { get; set; } }
    [DataContract] internal class StreamChunk
    {
        [DataMember(Name = "choices")] public List<StreamChoice> Choices { get; set; }
        [DataMember(Name = "usage")]   public ApiUsageInfo Usage { get; set; }
    }
    /// <summary>
    /// OpenAI 兼容 usage 块（Anthropic 风格字段也会填充到这里）。
    /// 注意：DataContractJsonSerializer 不支持动态 key 解析，所以 cached_tokens
    /// 暂时在 ChatCompletionUsage 之后用 JsonObject 二次扫描补全。
    /// </summary>
    [DataContract] internal class ApiUsageInfo
    {
        [DataMember(Name = "prompt_tokens")]     public int PromptTokens     { get; set; }
        [DataMember(Name = "completion_tokens")] public int CompletionTokens { get; set; }
        [DataMember(Name = "total_tokens")]      public int TotalTokens      { get; set; }
        [DataMember(Name = "input_tokens")]      public int InputTokens      { get; set; }
        [DataMember(Name = "output_tokens")]     public int OutputTokens     { get; set; }
        [DataMember(Name = "cache_read_input_tokens")] public int CacheReadInputTokens { get; set; }
    }

}
