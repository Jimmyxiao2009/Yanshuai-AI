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
        private async Task SendWithExistingUserMessage(ConversationMessage userMsg, ChatBubble userBubble)
        {
            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile == null) { AddSystemBubble("⚠ 没有选择 API 配置"); return; }

            _isSending = true;
            _streamCts = new System.Threading.CancellationTokenSource();
            SubmitButton.IsEnabled = true; // 生成中仍可点击（点击=停止）
            if (SubmitIcon != null) SubmitIcon.Glyph = ""; // X 图标

            var aiBubble = new ChatBubble
            {
                Role = "assistant", Content = "",
                IsStreaming = true,
                BackgroundColor = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                ReasoningBgColor = _reasoningBrush,
            };
            _bubbles.Add(aiBubble);
            ScrollToBottom();

            var apiMessages = new List<ApiRequestMessage>();
            string sysPrompt = BuildSystemPrompt(userMsg.Content);
            if (!string.IsNullOrEmpty(sysPrompt))
                apiMessages.Add(new ApiRequestMessage { Role = "system", Content = sysPrompt });

            // ── 上下文自动压缩（auto-compact，超过阈值时触发）────────────────
            // OpenCode / Claude Code 风格：先 compact 再发送，过程中显示状态提示
            try
            {
                aiBubble.SearchStatusText = "⏳ 分析上下文…";
                var result = await ContextCompressor.CompressIfNeededAsync(_conv, profile,
                    onProgress: msg =>
                    {
                        var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                            () => aiBubble.SearchStatusText = msg);
                    });
                if (result.Compacted)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        aiBubble.SearchStatusText = $"✓ 上下文已压缩（{result.Percent}%，还剩 {result.UsedLabel}/{result.LimitLabel}）");
                }
                else
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        aiBubble.SearchStatusText = "");
                }
            }
            catch { }

            // ── RAG 检索（如果启用）──────────────────────────────────────
            if (AppSettings.RagEnabled)
            {
                try
                {
                    string ragContext = await RagRetriever.BuildRagContextAsync(userMsg.Content, profile, _conv.Id);
                    if (!string.IsNullOrEmpty(ragContext))
                        apiMessages.Add(new ApiRequestMessage { Role = "system", Content = ragContext });
                }
                catch { }
            }

            // ── 发送消息窗口：auto-compact 后跳过 SummarizedUpTo 之前的消息 ──
            int skipIdx = Math.Max(0, _conv.SummarizedUpTo);
            if (_conv.ContextWindow > 0)
            {
                int windowSkip = Math.Max(0, _conv.Messages.Count - _conv.ContextWindow);
                skipIdx = Math.Max(skipIdx, windowSkip); // 取更保守的跳过
            }
            // 已压缩过的消息不再发送（摘要已在 system prompt 中）
            var windowMsgs = _conv.Messages.Skip(skipIdx).ToList();
            foreach (var m in windowMsgs)
            {
                // 按需从外置 ImageStore 读取（仅本次请求期间存在，发送后随 apiMessages 释放）
                var images = m.HasImages ? await m.GetAllImagesAsync() : new List<ImageEntry>();
                if (images.Count > 1)
                {
                    apiMessages.Add(new ApiRequestMessage
                    {
                        Role          = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                        Content       = m.Content,
                        ImageBase64   = images[0].Base64,
                        ImageMimeType = images[0].Mime,
                    });
                    for (int ii = 1; ii < images.Count; ii++)
                        apiMessages.Add(new ApiRequestMessage
                        {
                            Role          = "user",
                            Content       = "",
                            ImageBase64   = images[ii].Base64,
                            ImageMimeType = images[ii].Mime,
                        });
                }
                else
                {
                    apiMessages.Add(new ApiRequestMessage
                    {
                        Role          = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                        Content       = m.Content,
                        ImageBase64   = images.Count == 1 ? images[0].Base64 : null,
                        ImageMimeType = images.Count == 1 ? images[0].Mime   : null,
                    });
                }
            }
            string memoryBlock = BuildMemoryBlock();
            if (!string.IsNullOrEmpty(memoryBlock))
                apiMessages.Add(new ApiRequestMessage { Role = "system", Content = memoryBlock });

            // ── 注册后台任务（页面离开后继续运行）──────────────────────────
            var conv       = _conv;
            var cts        = _streamCts;
            var webEnabled = _toolsEnabled;
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
                    if (webEnabled)
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            aiBubble.SearchStatusText = "⏳ 思考中…");
                        string finalContent = await RunToolUseLoopAsync(profile, apiMessages, aiBubble, cts.Token);

                        // 清除工具状态文字，让 AiStreamingVisibility 变 Visible，
                        // 然后模拟打字机效果逐段推送最终回复内容
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            aiBubble.Content = "";
                            aiBubble.SearchStatusText = "";
                        });
                        if (!string.IsNullOrEmpty(finalContent) && !cts.Token.IsCancellationRequested)
                        {
                            // 此刻内容已全部到手（工具循环为非流式请求），打字机只是视觉回放。
                            // 步长按总长度缩放，使整段在 ~1 秒内播完：固定 4 字符/帧在长文本下
                            // 帧数随长度线性增长、且每帧都让整个 TextBlock 重排（越长每帧越慢），
                            // 回放远落后于实际已完成的传输，事件在 Dispatcher 上堆积，
                            // 表现为「取消后瞬间崩出全部内容」。
                            int len   = finalContent.Length;
                            int chunk = Math.Max(6, len / 60); // ≤60 帧播完，短文本仍保留打字感
                            int i = 0;
                            while (i < len && !cts.Token.IsCancellationRequested)
                            {
                                i = Math.Min(i + chunk, len);
                                string partial = finalContent.Substring(0, i);
                                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                                    () => aiBubble.Content = partial);
                                await Task.Delay(16);
                            }
                            // 确保最终内容完整（取消时也写入）
                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                                () => aiBubble.Content = finalContent);
                        }
                    }
                    else
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
                        using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                        {
                            string ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                            ApiUsageInfo usage;
                            if (ct.Contains("event-stream") || ct.Contains("stream"))
                                usage = await HandleStreamingResponse(resp, aiBubble, cts.Token);
                            else
                                usage = await HandleRegularResponse(resp, aiBubble);
                            _lastUsage = usage;
                            ApiLogger.Log(profile.ProviderType, profile.Model, requestJson, aiBubble.Content, !resp.IsSuccessStatusCode);
                        }
                    }
                }
                catch (System.OperationCanceledException) { /* 用户主动取消 */ }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        aiBubble.Content = $"连接错误：{ex.Message}");
                }

                // ── 写入数据库（无论页面是否存在都执行）─────────────────────
                string finalAiContent = "";
                string finalReasoning = "";
                List<ThinkStep> finalSteps = null;
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.IsStreamingReasoning = false;
                    aiBubble.IsStreaming = false;
                    aiBubble.ReasoningExpanded = AppSettings.ReasoningExpandedByDefault;
                    finalAiContent = aiBubble.Content;
                    finalReasoning = aiBubble.ReasoningContent;
                    finalSteps     = aiBubble.ExportThinkSteps();
                });

                var aiMsg = new ConversationMessage
                {
                    Role             = "assistant",
                    Content          = finalAiContent,
                    ReasoningContent = finalReasoning,
                    ThinkSteps       = finalSteps,
                    Timestamp        = DateTime.Now,
                };
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    aiBubble.MessageId = aiMsg.Id);
                conv.Messages.Add(aiMsg);
                conv.UpdatedAt = DateTime.Now;
                conv.ExchangesSinceLastSummary++;
                conv.ExchangesSinceLastInject++;

                // ── Token 累计（优先使用 API 上报值，否则本地估算）──────────
                int estInput = 0, estOutput = 0, cachedTokens = 0;
                if (_lastUsage != null)
                {
                    estInput     = _lastUsage.PromptTokens   != 0 ? _lastUsage.PromptTokens   : _lastUsage.InputTokens;
                    estOutput    = _lastUsage.CompletionTokens!= 0 ? _lastUsage.CompletionTokens : _lastUsage.OutputTokens;
                    cachedTokens = _lastUsage.CacheReadInputTokens;
                }
                if (estInput  == 0) estInput  = ContextCompressor.EstimateTokens(userMsg.Content);
                if (estOutput == 0) estOutput = ContextCompressor.EstimateTokens(finalAiContent);
                aiMsg.TokensInput  = estInput;
                aiMsg.TokensOutput = estOutput;
                aiMsg.CachedTokens = cachedTokens;
                conv.TotalTokensUsed += estInput + estOutput;
                _lastUsage = null;

                // ── 自动记忆提取（每 5 轮触发一次）──────────────────────────
                if (conv.Messages.Count(m => m.Role == "user") % 5 == 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await MemoryManager.AutoExtractMemoryAsync(conv, profile); } catch { }
                    });
                }

                // ── RAG 存储（将用户消息存入记忆向量库）──────────────────────
                if (AppSettings.RagEnabled && !string.IsNullOrEmpty(userMsg.Content))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var embedding = await RagRetriever.GetEmbeddingAsync(userMsg.Content, profile);
                            MemoryStore.Add(new MemoryItem
                            {
                                Text = userMsg.Content.Length > 500 ? userMsg.Content.Substring(0, 500) : userMsg.Content,
                                Embedding = embedding,
                                SourceConversationId = conv.Id,
                                Importance = 0.5,
                                Category = "conversation",
                            });
                            await MemoryStore.SaveAsync();
                        }
                        catch { }
                    });
                }

                await DataManager.SaveAsync();
                AppState.CompleteTask(conv.Id);

                // ── 通知 UI（页面在时刷新，不在时忽略）───────────────────────
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    cts.Dispose();
                    if (conv.MemoryEnabled && conv.ExchangesSinceLastSummary >= conv.MemorySummaryInterval)
                    {
                        conv.ExchangesSinceLastSummary = 0;
                        _ = RunMemorySummaryAsync();
                    }
                    if (SubmitIcon != null) SubmitIcon.Glyph = "";
                    ScrollToBottom();
                    PlaySound();
                    UpdateTokenDisplay();
                    _isSending = false;
                    SubmitButton.IsEnabled = true;
                });
            });

            AppState.RegisterTask(conv.Id, sendTask);
            await sendTask;
        }

        // ── Tool use 完整多轮循环 ─────────────────────────────────────────────

        private async Task<string> RunToolUseLoopAsync(
            ApiProfile profile,
            List<ApiRequestMessage> apiMessages,
            ChatBubble aiBubble,
            System.Threading.CancellationToken ct)
        {
            // 将 ApiRequestMessage 转换为 ApiMessageWithTools
            var toolMessages = new List<ApiMessageWithTools>();
            foreach (var m in apiMessages)
            {
                toolMessages.Add(new ApiMessageWithTools
                {
                    Role = m.Role,
                    Content = m.Content,
                    ImageBase64 = m.ImageBase64,
                    ImageMimeType = m.ImageMimeType,
                });
            }

            // 权限确认回调：write_file / calendar_create 需要用户弹窗确认
            ToolPermissionCallback permCb = async (toolName, desc) =>
            {
                try
                {
                    return await ShowPermissionPanel("AI 请求敏感操作", desc);
                }
                catch { return false; }
            };

            // 文件夹访问回调：request_folder_access 通过底部面板确认后调用系统选择器
            FolderAccessCallback folderCb = async (requestedPath) =>
            {
                bool confirmed = await ShowPermissionPanel(
                    "AI 请求文件夹访问权限",
                    "AI 想要访问以下文件夹：\n\n" + requestedPath + "\n\n点击「允许」后请通过系统文件夹选择器授权。");

                if (!confirmed) return null;
                await Task.Delay(200);

                var tcs = new TaskCompletionSource<string>();
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var picker = new FolderPicker
                        {
                            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                            ViewMode = PickerViewMode.List,
                        };
                        picker.FileTypeFilter.Add("*");
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder != null) {
                            string folderPath = folder.Path;
                            if (string.IsNullOrEmpty(folderPath))
                            {
                                // 某些 W10M 虚拟文件夹 Path 为空，使用 DisplayName 作为回退
                                folderPath = folder.DisplayName ?? "UnknownFolder";
                            }
                            // 使用 unchecked uint 转换避免 Math.Abs(int.MinValue) 溢出
                            string token = "fa_" + unchecked((uint)folderPath.ToLowerInvariant().GetHashCode()).ToString();
                            StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder, folderPath);
                            tcs.TrySetResult(folderPath);
                        }
                        else
                        {
                            tcs.TrySetResult(null);
                        }
                    }
                    catch { tcs.TrySetResult(null); }
                });
                return await tcs.Task;
            };

            // 工具步骤进度回调：追加到 ThinkChain
            ToolProgressCallback progCb = (phase, toolName, detail) =>
            {
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (phase == "thinking")
                        aiBubble.AddOrUpdateToolStep("💭", toolName, detail);
                    else if (phase == "calling")
                        aiBubble.AddOrUpdateToolStep("⏳", toolName, detail);
                    else if (phase == "result")
                        aiBubble.AddOrUpdateToolStep("✅", toolName, detail);
                    else if (phase == "error")
                        aiBubble.AddOrUpdateToolStep("❌", toolName, detail);

                    int running = aiBubble.ThinkChain.Count(e => e.Kind == ThinkChainKind.Tool && e.Icon == "⏳");
                    aiBubble.SearchStatusText = running > 0 ? "⏳ 执行中 (" + running + " 个工具)…" : "";
                });
            };

            // 中间文本实时推送回调
            ToolTextContentCallback textCb = (intermediateText) =>
            {
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.Content = intermediateText;
                });
            };

            // 思考过程实时推送回调：追加增量到 ThinkChain
            ToolReasoningCallback reasoningCb = (reasoningToken) =>
            {
                var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.AppendReasoningChunk(reasoningToken);
                });
            };

            // 用 FunctionCallEngine 跑完整工具循环
            FunctionCallLoopResult result = await FunctionCallEngine.RunFunctionCallLoopAsync(
                profile, toolMessages, _conv, permCb, folderCb, progCb, textCb, profile.VisionEnabled, reasoningCb);
            string content = result.Content;
            string reasoning = result.Reasoning ?? "";
            List<ApiMessageWithTools> allMessages = result.AllMessages ?? new List<ApiMessageWithTools>();

            // 推送 reasoning 到气泡（DeepSeek V4 等模型的思考过程）
            if (!string.IsNullOrEmpty(reasoning))
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.ReasoningContent = reasoning;
                    aiBubble.IsStreamingReasoning = false;
                });
            }
            else
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.IsStreamingReasoning = false;
                });
            }

            // 显示工具调用状态
            int toolCount = allMessages.Count(m => m.Role == "tool");
            if (toolCount > 0)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    aiBubble.SearchStatusText = "已使用 " + toolCount + " 个工具调用";
                });
            }

            return content;
        }

        private static string ExtractContentFromResponse(string responseBody, bool isClaudeProvider)
        {
            if (isClaudeProvider)
            {
                // Claude: find "type":"text" block then its "text" value
                int textIdx = responseBody.IndexOf("\"type\":\"text\"");
                if (textIdx >= 0)
                {
                    int textValIdx = responseBody.IndexOf("\"text\":", textIdx);
                    if (textValIdx >= 0)
                        return ExtractJsonString(responseBody, textValIdx + 7);
                }
            }
            else
            {
                // OpenAI format: choices[0].message.content
                // Skip over any "content": null occurrences from tool_calls
                int searchFrom = 0;
                while (true)
                {
                    int contentIdx = responseBody.IndexOf("\"content\":", searchFrom);
                    if (contentIdx < 0) break;
                    int valueStart = contentIdx + 10;
                    // skip whitespace
                    while (valueStart < responseBody.Length && responseBody[valueStart] == ' ') valueStart++;
                    if (valueStart < responseBody.Length && responseBody[valueStart] == '"')
                    {
                        string val = ExtractJsonString(responseBody, valueStart);
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                    searchFrom = contentIdx + 10;
                }
                // DeepSeek-R1 fallback: reasoning_content
                int rcIdx = responseBody.IndexOf("\"reasoning_content\":");
                if (rcIdx >= 0)
                {
                    string rc = ExtractJsonString(responseBody, rcIdx + 20);
                    if (!string.IsNullOrEmpty(rc)) return rc;
                }
                // Gemini fallback
                string gemini = ExtractGeminiText(responseBody);
                if (!string.IsNullOrEmpty(gemini)) return gemini;
            }
            return "";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

    }
}
