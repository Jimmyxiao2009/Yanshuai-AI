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
        private async Task SendWithExistingUserMessage(ConversationMessage userMsg, ChatBubble userBubble)
        {
            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null) { AddSystemBubble("⚠ 没有选择 API 配置"); return; }

            _isSending = true;
            _streamCts = new System.Threading.CancellationTokenSource();
            SubmitButton.IsEnabled = true; // 生成中仍可点击（点击=停止）
            if (SubmitIcon != null) SubmitIcon.Glyph = ""; // X 图标
            if (SubmitBtnBorder != null) SubmitBtnBorder.Background = new SolidColorBrush(Color.FromArgb(220, 200, 60, 60));

            var apiMessages = new List<ApiRequestMessage>();

            // Static system prompt (KV-cache friendly: changes only on character/profile switch)
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
                    Role          = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                    Content       = m.Content,
                    ImageBase64   = m.ImageBase64,
                    ImageMimeType = m.ImageMimeType,
                });

            // Dynamic context → prepend to last user message (keeps static prompt cacheable)
            await PrefetchCloudRagAsync(userMsg.Content);
            string dynCtx = BuildDynamicContext(userMsg.Content);
            if (AppSettings.RagDebugEnabled && !string.IsNullOrEmpty(_ragDebugText))
            {
                AddDebugBubble(_ragDebugText + "\n动态上下文长度：" + dynCtx.Length);
            }
            if (!string.IsNullOrEmpty(dynCtx))
            {
                // Prepend to the last user message in apiMessages
                for (int i = apiMessages.Count - 1; i >= 0; i--)
                {
                    if (apiMessages[i].Role == "user")
                    {
                        apiMessages[i].Content = dynCtx + "\n\n" + apiMessages[i].Content;
                        break;
                    }
                }
            }

            string memoryBlock = BuildMemoryBlock();
            if (!string.IsNullOrEmpty(memoryBlock))
                apiMessages.Add(new ApiRequestMessage { Role = "system", Content = memoryBlock });

            var character2 = DataManager.GetCharacterForConversation(_conv);
            if (character2 != null && !string.IsNullOrEmpty(character2.PostHistoryInstructions))
                apiMessages.Add(new ApiRequestMessage { Role = "system", Content = character2.PostHistoryInstructions });

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
                    var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                    ApplyAuthHeaders(req, profile);
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
                catch (System.OperationCanceledException) { /* 用户主动取消 */ }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    { aiBubble.Content = $"连接错误：{ex.Message}"; aiBubble.HasError = true; });
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
                    _streamCts = null;
                    if (conv.MemoryEnabled && conv.ExchangesSinceLastSummary >= conv.MemorySummaryInterval)
                    {
                        conv.ExchangesSinceLastSummary = 0;
                        _ = RunMemorySummaryAsync();
                        _ = RunDeepMemoryExtractionAsync();
                    }
                    if (SubmitIcon != null) SubmitIcon.Glyph = "";
                    if (SubmitBtnBorder != null) SubmitBtnBorder.Background = (Brush)Application.Current.Resources["YanshuaiAccentBrush"];
                    ScrollToBottom();
                    PlaySound();
                    _isSending = false;
                    SubmitButton.IsEnabled = true;
                });
            });

            AppState.RegisterTask(conv.Id, sendTask);
            await sendTask;
        }


    }
}
