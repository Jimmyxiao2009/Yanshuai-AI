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
                uint scaleW = newW, scaleH = newH;
                if (decoder.OrientedPixelWidth != decoder.PixelWidth &&
                    decoder.OrientedPixelWidth == decoder.PixelHeight)
                {
                    scaleW = newH;
                    scaleH = newW;
                }
                var transform = new BitmapTransform { ScaledWidth = scaleW, ScaledHeight = scaleH };
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
            UpdateComposerChrome();
        }

        private void ClearImageBtn_Click(object sender, RoutedEventArgs e)
        {
            _pendingImageBase64  = null;
            _pendingImageMimeType = null;
            PendingImageThumb.Source = null;
            ImagePreviewBar.Visibility = Visibility.Collapsed;
            UpdateComposerChrome();
        }

        private void UpdateComposerChrome()
        {
            if (ComposerToolbarBorder == null || ImagePreviewBar == null)
                return;

            ComposerToolbarBorder.CornerRadius =
                ImagePreviewBar.Visibility == Visibility.Visible
                    ? new CornerRadius(0)
                    : new CornerRadius(17, 17, 0, 0);
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
            var result = await dialog.ShowAsync().AsTask();
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
            if (_conv == null)
            {
                AddSystemBubble("⚠ 当前没有可用对话，请先选择或新建对话。");
                return;
            }
            string userInput = InputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(userInput) && string.IsNullOrEmpty(_pendingImageBase64)) return;

            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null)
            {
                AddSystemBubble("⚠ 没有选择 API 配置，请从菜单进入 API 配置页面。");
                return;
            }
            if (string.IsNullOrWhiteSpace(profile.Url))
            {
                AddSystemBubble("⚠ 当前 API 配置的接口 URL 为空，请在 API 配置页面中进行完善。");
                return;
            }
            if (string.IsNullOrWhiteSpace(profile.ApiKey) && profile.ProviderType != "ollama" && profile.ProviderType != "local")
            {
                AddSystemBubble("⚠ 当前 API 配置的 API Key 为空，请在 API 配置页面中进行完善。");
                return;
            }

            // 清除旧的推荐回复
            _suggestedReplies.Clear();
            SuggestPanelOverlay.Visibility = Visibility.Collapsed;

            _isSending = true;
            SubmitButton.IsEnabled = false;

            // 第一次发送消息时才把对话写入持久化列表
            if (_isPendingConv)
            {
                _isPendingConv = false;
                DataManager.Data.Conversations.Add(_conv);
                DialoguePoolManager.EnsureConversationInPool(_conv);
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
                UpdateTopBar();
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
            UpdateComposerChrome();
            ScrollToBottom();

            await SendWithExistingUserMessage(userMsg, userBubble);
        }

        // ── Long memory ───────────────────────────────────────────────────────

        /// <summary>定期触发或手动调用的记忆检查：如有未总结的轮次则触发记忆管线</summary>
        private static DateTime _lastConsolidation = DateTime.MinValue;
    }
}
