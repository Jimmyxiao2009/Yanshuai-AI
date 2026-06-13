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
                    // 预览缩略图只显示约 40×40，按缩略图尺寸解码而非全分辨率（1024px），
                    // 每张附件可省下数百 KB 解码内存。发送给 API 的是 _pendingImagesBase64（不受影响）。
                    bmp.DecodePixelType = Windows.UI.Xaml.Media.Imaging.DecodePixelType.Logical;
                    bmp.DecodePixelWidth = 96;
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
            // 工具关闭时同步关闭 fetch 选项
            if (!_toolsEnabled)
                _fetchSearchEnabled = false;
        }

        private void ToolsToggle_Click(object sender, RoutedEventArgs e)
        {
            _toolsEnabled = (sender as Windows.UI.Xaml.Controls.ToggleMenuFlyoutItem)?.IsChecked == true;
            if (!_toolsEnabled)
                _fetchSearchEnabled = false;
        }

        private void FullTrustToggle_Click(object sender, RoutedEventArgs e)
        {
            FunctionCallEngine.FullTrust = (sender as Windows.UI.Xaml.Controls.ToggleMenuFlyoutItem)?.IsChecked == true;
        }

        private void FetchToggle_Click(object sender, RoutedEventArgs e)
        {
            _fetchSearchEnabled = (sender as Windows.UI.Xaml.Controls.ToggleMenuFlyoutItem)?.IsChecked == true;
        }

        private void FetchSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            _fetchSearchEnabled = !_fetchSearchEnabled;
        }

        private void FullTrustBtn_Click(object sender, RoutedEventArgs e)
        {
            FunctionCallEngine.FullTrust = !FunctionCallEngine.FullTrust;
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
                _ = DataManager.SaveAsync(); // 立即持久化，防止崩溃丢对话
            }

            // 把附带图片写入外置 ImageStore，消息里只保留引用 id；
            // base64 不再随 AppData 持久化/常驻内存（低内存设备卡死的诱因之一）。
            List<string> imageRefs = null;
            List<string> imageMimes = null;
            if (_pendingImagesBase64.Count > 0)
            {
                imageRefs  = new List<string>(_pendingImagesBase64.Count);
                imageMimes = new List<string>(_pendingImagesBase64.Count);
                for (int ii = 0; ii < _pendingImagesBase64.Count; ii++)
                {
                    string id = await ImageStore.SaveBase64Async(_pendingImagesBase64[ii]);
                    if (id != null)
                    {
                        imageRefs.Add(id);
                        imageMimes.Add(ii < _pendingImagesMimeType.Count ? _pendingImagesMimeType[ii] : "image/jpeg");
                    }
                }
                if (imageRefs.Count == 0) { imageRefs = null; imageMimes = null; }
            }

            // Create message first so its Id is available for the bubble
            var userMsg = new ConversationMessage
            {
                Role = "user", Content = apiUserInput, Timestamp = DateTime.Now,
                ImageRefs       = imageRefs,
                ImagesMimeType  = imageMimes,
                AttachedFileNames = bubbleFileTags.Count > 0 ? new List<string>(bubbleFileTags) : null,
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
                // 优先用外置引用看大图；若外置失败则回退到本次内存里的 base64
                ImageRefId = (imageRefs != null && imageRefs.Count > 0) ? imageRefs[0] : null,
                FullImageBase64 = (imageRefs != null && imageRefs.Count > 0) ? null : _pendingImageBase64,
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

    }
}
