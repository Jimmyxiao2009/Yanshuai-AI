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
        private List<string> RAGForPrompt = null; // 云端重排预取结果
        private string _ragDebugText = "";
        private string _pendingImageBase64 = null;
        private string _pendingImageMimeType = null;
        private System.Threading.CancellationTokenSource _streamCts = null;
        private List<string> _suggestedReplies = new List<string>();
        private DispatcherTimer _memoryTimer;
        // 聊天列表改为虚拟化 ListView 后，其内部 ScrollViewer 在 Loaded 时获取，
        // 用于“回到底部”按钮的显隐判断（滚动到底用 ListView.ScrollIntoView）。
        private ScrollViewer ChatScrollViewer;

        // ── Cached brushes (rebuilt once per conversation, not once per bubble) ─
        private static readonly SolidColorBrush _whiteBrush      = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush _reasoningBrush  = new SolidColorBrush(Color.FromArgb(40, 150, 150, 170));
        private SolidColorBrush _cachedUserBg;
        private SolidColorBrush _cachedUserFg;
        private SolidColorBrush _cachedAiBg;
        private SolidColorBrush _cachedAiFg;
        // Accent color 缓存已下沉到 Lumina.Core/Common/UiHelpers.GetAccentColor()

        public MainPage()
        {
            InitializeComponent();
            ChatItems.ItemsSource = _bubbles;
            UpdateComposerChrome();
            SetStatusBarColor();
            ChatItems.Loaded += ChatItems_Loaded;

            // 定期记忆检查（每2分钟）
            _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _memoryTimer.Tick += (s, e) => _ = CheckMemoryTriggerAsync();
            _memoryTimer.Start();
        }
    }
}
