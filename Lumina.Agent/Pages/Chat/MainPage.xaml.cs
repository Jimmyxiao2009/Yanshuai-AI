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
        private System.Threading.CancellationTokenSource _pageCts = null;
        private ChatBubble _expandedBubble = null;
        // 用量信息已改为各发送任务的局部变量（避免并发发送相互覆盖），不再使用共享字段。
        private DispatcherTimer _subagentTimer = null;

        // ListView 内部 ScrollViewer（虚拟化启用后用它做滚动到底/上滚分页）
        private ScrollViewer _chatSv = null;

        // 懒加载状态
        private int _displayStart = 0;
        private int _displayEnd = 0;
        private int _batchSize = 20;
        private List<ConversationMessage> _fullMessages = null;
        private bool _loadingOlder = false;

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
            SetStatusBarColor();
        }
    }
}
