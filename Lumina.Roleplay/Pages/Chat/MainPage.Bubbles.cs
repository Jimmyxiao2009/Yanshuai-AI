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
            await dialog.ShowAsync().AsTask();
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
                while (_bubbles.Count > idx) _bubbles.RemoveAt(_bubbles.Count - 1);
                _ = DataManager.SaveAsync();
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
                if (await dialog.ShowAsync().AsTask() != ContentDialogResult.Primary) return;

                string newText = editBox.Text.Trim();
                if (string.IsNullOrEmpty(newText)) return;

                var bp = GetOrCreateBranchPoint(anchorIdx, msg.Id);
                SaveCurrentTailToBranch(bp);

                // New branch with the edited message
                var newMsg = CloneMessage(msg);
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
                // 角色卡初始消息不持久化，重建时始终补回
                MaybeShowFirstMessage();
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
                string sysPrompt = BuildSystemPrompt();
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

                var character2 = DataManager.GetCharacterForConversation(_conv);
                if (character2 != null && !string.IsNullOrEmpty(character2.PostHistoryInstructions))
                    apiMessages.Add(new ApiRequestMessage { Role = "system", Content = character2.PostHistoryInstructions });

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
                    ApplyAuthHeaders(req, profile);
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
                catch (Exception ex) { aiBubble.Content = $"连接错误：{ex.Message}"; aiBubble.HasError = true; ApiLogger.Log(profile.ProviderType, profile.Model, requestJson, ex.Message, true); }

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
                _ = GenerateSuggestedRepliesAsync();
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
                if (anchorIdx < 0) return;

                var userMsg = _conv.Messages[anchorIdx];
                var bp = GetOrCreateBranchPoint(anchorIdx, userMsg.Id);
                SaveCurrentTailToBranch(bp);

                // Remove this AI message and all after
                PruneBranchPointsFrom(anchorIdx + 1);
                _conv.Messages.RemoveRange(idx, _conv.Messages.Count - idx);
                while (_bubbles.Count > idx) _bubbles.RemoveAt(_bubbles.Count - 1);

                var regeneratedStart = new ConversationBranch { Messages = new List<ConversationMessage> { CloneMessage(userMsg) } };
                bp.Branches.Add(regeneratedStart);
                bp.ActiveIndex = bp.Branches.Count - 1;
                var userBubble = _bubbles.FirstOrDefault(bub => bub.MessageId == userMsg.Id);
                if (userBubble != null) userBubble.BranchData = bp;

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

        // Retry last failed AI response
        private async void RetryMsg_Click(object sender, RoutedEventArgs e)
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
                if (anchorIdx < 0) return;

                var userMsg = _conv.Messages[anchorIdx];
                var bp = GetOrCreateBranchPoint(anchorIdx, userMsg.Id);
                SaveCurrentTailToBranch(bp);

                // Remove this failed AI message and all after
                PruneBranchPointsFrom(anchorIdx + 1);
                _conv.Messages.RemoveRange(idx, _conv.Messages.Count - idx);
                while (_bubbles.Count > idx) _bubbles.RemoveAt(_bubbles.Count - 1);

                var retryStart = new ConversationBranch { Messages = new List<ConversationMessage> { CloneMessage(userMsg) } };
                bp.Branches.Add(retryStart);
                bp.ActiveIndex = bp.Branches.Count - 1;
                var userBubble = _bubbles.FirstOrDefault(bub => bub.MessageId == userMsg.Id);
                if (userBubble != null) userBubble.BranchData = bp;

                await SendWithExistingUserMessage(userMsg, userBubble);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RetryMsg error: " + ex.Message);
                _isSending = false;
                SubmitButton.IsEnabled = true;
                if (SubmitIcon != null) SubmitIcon.Glyph = "";
                AddSystemBubble("⚠ 重试失败: " + ex.Message);
            }
        }

        // ── Branch navigation ─────────────────────────────────────────────────

        private static ConversationMessage CloneMessage(ConversationMessage m)
        {
            if (m == null) return null;
            return new ConversationMessage
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                ReasoningContent = m.ReasoningContent,
                ImageBase64 = m.ImageBase64,
                ImageMimeType = m.ImageMimeType,
                Timestamp = m.Timestamp
            };
        }

        private ConversationBranch SnapshotBranchTail(int anchorIdx)
        {
            var branch = new ConversationBranch { Messages = new List<ConversationMessage>() };
            if (_conv?.Messages == null || anchorIdx < 0 || anchorIdx >= _conv.Messages.Count)
                return branch;

            foreach (var m in _conv.Messages.Skip(anchorIdx))
                branch.Messages.Add(CloneMessage(m));
            return branch;
        }

        private BranchPoint GetOrCreateBranchPoint(int anchorIdx, string anchorMessageId)
        {
            if (_conv.BranchPoints == null) _conv.BranchPoints = new List<BranchPoint>();
            var bp = _conv.BranchPoints.FirstOrDefault(x => x.AnchorIndex == anchorIdx);
            if (bp == null)
            {
                bp = new BranchPoint
                {
                    AnchorIndex = anchorIdx,
                    AnchorMessageId = anchorMessageId,
                    Branches = new List<ConversationBranch>(),
                    ActiveIndex = 0
                };
                _conv.BranchPoints.Add(bp);
            }
            if (bp.Branches == null) bp.Branches = new List<ConversationBranch>();
            if (bp.ActiveIndex < 0 || bp.ActiveIndex >= bp.Branches.Count)
                bp.ActiveIndex = bp.Branches.Count > 0 ? 0 : 0;
            return bp;
        }

        private void SaveCurrentTailToBranch(BranchPoint bp)
        {
            if (bp == null || bp.Branches == null) return;
            if (bp.AnchorIndex < 0 || bp.AnchorIndex >= _conv.Messages.Count) return;

            var snapshot = SnapshotBranchTail(bp.AnchorIndex);
            if (bp.ActiveIndex >= 0 && bp.ActiveIndex < bp.Branches.Count)
                bp.Branches[bp.ActiveIndex] = snapshot;
            else
            {
                bp.Branches.Add(snapshot);
                bp.ActiveIndex = bp.Branches.Count - 1;
            }
        }

        private void PruneBranchPointsFrom(int startIndex)
        {
            if (_conv?.BranchPoints == null) return;
            _conv.BranchPoints.RemoveAll(bp => bp == null || bp.AnchorIndex >= startIndex || bp.AnchorIndex < 0);
        }

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
                if (anchorIdx < 0 || anchorIdx >= _conv.Messages.Count) return;

                // Save current tail into current branch slot
                SaveCurrentTailToBranch(bp);

                // Restore target branch
                var target = bp.Branches[targetIndex];
                _conv.Messages.RemoveRange(anchorIdx, _conv.Messages.Count - anchorIdx);
                foreach (var m in target.Messages) _conv.Messages.Add(CloneMessage(m));
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SwitchBranch error: " + ex.Message);
                AddSystemBubble("⚠ 切换分支失败: " + ex.Message);
            }
        }

        // ── Shared send helper (used by Edit + Regenerate) ────────────────────

    }
}
