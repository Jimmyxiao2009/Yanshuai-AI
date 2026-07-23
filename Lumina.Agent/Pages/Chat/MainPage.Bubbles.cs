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

        // ── ThinkChain entry toggle ───────────────────────────────────────────

        private void ToggleThinkEntry_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ThinkChainEntry entry)
                entry.IsExpanded = !entry.IsExpanded;
        }

        private void ToggleReasoning_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ThinkChainEntry entry)
                entry.IsExpanded = !entry.IsExpanded;
        }

        private void ToggleToolSteps_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ThinkChainEntry entry)
                entry.IsExpanded = !entry.IsExpanded;
        }

        // ── Message actions ───────────────────────────────────────────────────

        // 点击气泡图片缩略图 → 大图预览
        private async void BubbleImage_Click(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (!((sender as Border)?.Tag is ChatBubble b)) return;

            // 缩略图按 240px 解码以省内存；看大图时临时解码全分辨率，对话框关闭后随之释放。
            // 优先按外置引用从磁盘读取全分辨率，回退到旧内联 base64。
            Windows.UI.Xaml.Media.ImageSource src = b.ImageSource;
            string fullB64 = null;
            if (!string.IsNullOrEmpty(b.ImageRefId))
                fullB64 = await ImageStore.LoadBase64Async(b.ImageRefId);
            if (string.IsNullOrEmpty(fullB64))
                fullB64 = b.FullImageBase64;
            if (!string.IsNullOrEmpty(fullB64))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(fullB64);
                    var full = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        await ms.WriteAsync(bytes.AsBuffer());
                        ms.Seek(0);
                        await full.SetSourceAsync(ms);
                    }
                    src = full;
                }
                catch { }
            }
            if (src == null) return;

            var img = new Image
            {
                Source = src,
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
            // 释放全分辨率位图引用，便于尽快回收
            img.Source = null;
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

        private void PruneBranchPointsFrom(int startIndex)
        {
            if (_conv?.BranchPoints == null) return;
            _conv.BranchPoints.RemoveAll(bp => bp.AnchorIndex >= startIndex);
        }

        // Delete this message and everything after it
        private void DeleteMsg_Click(object sender, RoutedEventArgs e)
        {
            if (_isSending) return;
            try
            {
                if (!((sender as Button)?.Tag is ChatBubble b)) return;
                if (string.IsNullOrEmpty(b.MessageId)) return;
                var msg = _conv.Messages.FirstOrDefault(m => m.Id == b.MessageId);
                if (msg == null) return;
                int idx = _conv.Messages.IndexOf(msg);
                if (idx < 0) return;

                PruneBranchPointsFrom(idx);
                _conv.Messages.RemoveRange(idx, _conv.Messages.Count - idx);
                _displayEnd = _conv.Messages.Count;
                // 用气泡的实际位置裁剪（窗口懒加载下 _bubbles 与消息下标不一定对齐）
                int bIdx = _bubbles.IndexOf(b);
                if (bIdx >= 0)
                    while (_bubbles.Count > bIdx) _bubbles.RemoveAt(_bubbles.Count - 1);
                _ = DataManager.SaveAsync();
                UpdateTokenDisplay();
            }
            catch (Exception ex)
            {
                AddSystemBubble("⚠ 删除消息失败: " + ex.Message);
            }
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
                    Messages = _conv.Messages.Skip(anchorIdx).Select(m2 => m2.Clone()).ToList()
                };
                if (bp.ActiveIndex < bp.Branches.Count)
                    bp.Branches[bp.ActiveIndex] = snapshot;
                else
                    bp.Branches.Add(snapshot);

                // New branch with the edited message (cloning original to preserve attachments/images)
                var newMsg = msg.Clone();
                newMsg.Id = Guid.NewGuid().ToString();
                newMsg.Content = newText;
                newMsg.Timestamp = DateTime.Now;
                bp.Branches.Add(new ConversationBranch { Messages = new List<ConversationMessage> { newMsg } });
                bp.ActiveIndex = bp.Branches.Count - 1;

                // Truncate conversation and add new message
                PruneBranchPointsFrom(anchorIdx + 1);
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
                AddSystemBubble("⚠ 编辑消息失败: " + ex.Message);
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
                        // 继续功能不重载图片：历史图片已处理过，重发浪费token且部分API会报错
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
                        // 「继续」路径不做 token 记账，故不写共享的 _lastUsage 字段，
                        // 避免与并发的主发送任务相互覆盖用量数据（见 MainPage.Send.cs）。
                        if (ct.Contains("event-stream") || ct.Contains("stream"))
                            await HandleStreamingResponse(resp, aiBubble, _streamCts.Token);
                        else
                            await HandleRegularResponse(resp, aiBubble);
                        ApiLogger.Log(profile.ProviderType, profile.Model, requestJson, aiBubble.Content, !resp.IsSuccessStatusCode);
                    }
                }
                catch (Exception ex) { aiBubble.Content = $"连接错误：{ex.Message}"; ApiLogger.Log(profile.ProviderType, profile.Model, requestJson, ex.Message, true); }

                aiBubble.StopReasoningStreaming();
                aiBubble.IsStreaming = false;
                _streamCts?.Dispose();
                _streamCts = null;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";

                var aiMsg = new ConversationMessage
                {
                    Role = "assistant", Content = aiBubble.Content,
                    ReasoningContent = string.Join("", aiBubble.ThinkChain
                        .Where(tc => tc.Kind == ThinkChainKind.Reasoning)
                        .Select(tc => tc.ReasoningText)),
                    ThinkSteps = aiBubble.ExportThinkSteps(),
                    Timestamp = DateTime.Now
                };
                aiBubble.MessageId = aiMsg.Id;
                _conv.Messages.Add(aiMsg);
                _conv.UpdatedAt = DateTime.Now;

                await DataManager.SaveAsync();
                ScrollToBottom();
                PlaySound();
                UpdateTokenDisplay();
                _isSending = false;
                SubmitButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ContinueMsg error: " + ex.Message);
                _isSending = false;
                if (SubmitIcon != null) SubmitIcon.Glyph = "\uE74A";
                SubmitButton.IsEnabled = true;
                AddSystemBubble("⚠ 继续输入失败: " + ex.Message);
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

                int anchorIdx = -1;
                for (int i = idx - 1; i >= 0; i--)
                {
                    if (_conv.Messages[i].Role == "user")
                    {
                        anchorIdx = i;
                        break;
                    }
                }
                if (anchorIdx >= 0)
                {
                    var userMsg = _conv.Messages[anchorIdx];
                    if (_conv.BranchPoints == null) _conv.BranchPoints = new List<BranchPoint>();
                    var bp = _conv.BranchPoints.FirstOrDefault(x => x.AnchorIndex == anchorIdx);
                    if (bp == null)
                    {
                        bp = new BranchPoint { AnchorIndex = anchorIdx, AnchorMessageId = userMsg.Id };
                        _conv.BranchPoints.Add(bp);
                    }
                    var snapshot = new ConversationBranch
                    {
                        Messages = _conv.Messages.Skip(anchorIdx).Select(m2 => m2.Clone()).ToList()
                    };
                    if (bp.ActiveIndex < bp.Branches.Count)
                        bp.Branches[bp.ActiveIndex] = snapshot;
                    else
                        bp.Branches.Add(snapshot);
                }

                PruneBranchPointsFrom(idx);

                // Remove this AI message and all after (data layer)
                _conv.Messages.RemoveRange(idx, _conv.Messages.Count - idx);
                _displayEnd = _conv.Messages.Count;
                _displayEnd = _conv.Messages.Count;

                // Remove bubbles from the tapped one onward — use the bubble's ACTUAL
                // position. _bubbles 是按窗口懒加载的，下标与 _conv.Messages 不一定对齐，
                // 不能用消息下标 idx 去裁剪气泡（否则窗口滚动后会删错气泡）。
                int bIdx = _bubbles.IndexOf(b);
                if (bIdx >= 0)
                    while (_bubbles.Count > bIdx) _bubbles.RemoveAt(_bubbles.Count - 1);

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
                AddSystemBubble("⚠ 重新生成失败: " + ex.Message);
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
            if (_isSending) return;
            try
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
                        Messages = _conv.Messages.Skip(anchorIdx).Select(m => m.Clone()).ToList()
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
                UpdateTokenDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SwitchBranch error: " + ex.Message);
                AddSystemBubble("⚠ 切换分支失败: " + ex.Message);
            }
        }

        // ── Shared send helper (used by Edit + Regenerate) ────────────────────

    }
}
