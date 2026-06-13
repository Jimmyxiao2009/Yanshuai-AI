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
        private void UpdateTokenDisplay()
        {
            if (TokenBubble == null) return;
            if (_conv == null || _conv.Messages == null || _conv.Messages.Count == 0)
            {
                TokenBubble.Visibility = Visibility.Collapsed;
                return;
            }

            // tokensIn = most recent API call input (= current context window size)
            int tokensIn = 0;
            for (int i = _conv.Messages.Count - 1; i >= 0; i--)
                if (_conv.Messages[i].TokensInput > 0) { tokensIn = _conv.Messages[i].TokensInput; break; }

            // tokensOut = conversation total output (all messages summed)
            int tokensOut = 0;
            foreach (var m in _conv.Messages)
                tokensOut += m.TokensOutput;

            if (tokensIn == 0 && tokensOut == 0)
            {
                TokenBubble.Visibility = Visibility.Collapsed;
                return;
            }

            if (TokenInLabel  != null) TokenInLabel.Text  = ContextCompressor.FormatTokenCount(tokensIn);
            if (TokenOutLabel != null) TokenOutLabel.Text = ContextCompressor.FormatTokenCount(tokensOut);

            // ── 上下文使用进度条（auto-compact 指示器）───────────────────
            if (AppState.ActiveConversation != null)
            {
                var profile = DataManager.GetProfileForConversation(AppState.ActiveConversation);
                if (profile != null)
                {
                    var ctx = ContextCompressor.GetContextUsage(AppState.ActiveConversation, profile);
                    if (ContextUsageBar != null && ContextUsageFill != null)
                    {
                        if (ctx.Percent > 10)
                        {
                            ContextUsageBar.Visibility = Visibility.Visible;
                            var totalWidth = TokenBubble.ActualWidth > 0
                                ? TokenBubble.ActualWidth : 300;
                            ContextUsageFill.Width = totalWidth * ctx.Percent / 100.0;

                            // 窗口信息文字
                            var conv = AppState.ActiveConversation;
                            int sentCount = conv.Messages.Count - conv.SummarizedUpTo;
                            int compacted = conv.SummarizedUpTo;
                            if (ContextWindowInfo != null)
                            {
                                if (compacted > 0)
                                    ContextWindowInfo.Text = string.Format("发送 {0} 条，已压缩 {1} 条 · {2}%",
                                        sentCount, compacted, ctx.Percent);
                                else
                                    ContextWindowInfo.Text = string.Format("发送 {0} 条 · {1}%",
                                        sentCount, ctx.Percent);
                            }
                        }
                        else
                        {
                            ContextUsageBar.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }

            TokenBubble.Visibility = Visibility.Visible;
        }

        // ── New conversation (from top-bar ＋ button) ──────────────────────────

        private void PlaaEvalBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(EvaluationPage));

        private void NewConvBtn_Click(object sender, RoutedEventArgs e)
        {
            AppState.ActiveConversation = null;
            Frame.Navigate(typeof(MainPage));
            if (Frame.BackStack.Count > 0 && Frame.BackStack[Frame.BackStack.Count - 1].SourcePageType == typeof(MainPage))
                Frame.BackStack.RemoveAt(Frame.BackStack.Count - 1);
        }

        // ── Manual compress context ───────────────────────────────────────────

        private async void ManualCompressContext_Click(object sender, RoutedEventArgs e)
        {
            if (_conv == null || _isSending) return;
            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null) { AddSystemBubble("⚠ 没有选择 API 配置"); return; }

            var statusBubble = new ChatBubble
            {
                Role = "assistant", Content = "⏳ 正在压缩上下文…",
                BackgroundColor = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                ReasoningBgColor = _reasoningBrush,
            };
            _bubbles.Add(statusBubble);
            ScrollToBottom();
            _isSending = true;

            try
            {
                var result = await ContextCompressor.ForceCompactAsync(_conv, profile,
                    onProgress: msg =>
                    {
                        var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                            () => statusBubble.Content = msg);
                    });

                await DataManager.SaveAsync();
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (result.Compacted)
                        statusBubble.Content = string.Format("✓ 上下文已压缩（压缩了 {0} 条消息，当前 {1}/{2}）",
                            result.CompactedCount, result.UsedLabel, result.LimitLabel);
                    else
                        statusBubble.Content = "ℹ️ " + result.Note;
                    statusBubble.IsStreaming = false;
                    UpdateTokenDisplay();
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    statusBubble.Content = "❌ 压缩失败：" + ex.Message);
            }
            finally
            {
                _isSending = false;
            }
        }

        // ── Conversation settings / appearance ────────────────────────────────

        private void ConvSettingsBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvSettingsPage));

        private void ConvAppearanceBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(ConvAppearancePage));

    }
}
