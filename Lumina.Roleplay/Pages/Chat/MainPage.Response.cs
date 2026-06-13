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
        private async Task HandleStreamingResponse(HttpResponseMessage resp, ChatBubble bubble, System.Threading.CancellationToken ct)
        {
            bool reasoningPhase = false;
            int tokensSinceScroll = 0;

            await Task.Run(async () =>
            {
                using (var stream = await resp.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (!reader.EndOfStream && !ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;
                        if (!line.StartsWith("data: ")) continue;
                        var data = line.Substring(6).Trim();
                        if (data == "[DONE]") break;

                        string ct2 = null;
                        string rt   = null;

                        // 尝试 OpenAI/DeepSeek/Groq 格式 (choices[].delta)
                        StreamChunk chunk = null;
                        try
                        {
                            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(data)))
                                chunk = (StreamChunk)new DataContractJsonSerializer(typeof(StreamChunk)).ReadObject(ms);
                        }
                        catch { }

                        var delta = chunk?.Choices?.Count > 0 ? chunk.Choices[0]?.Delta : null;
                        if (delta != null)
                        {
                            rt  = delta.ReasoningContent;
                            ct2 = delta.Content;
                        }
                        else
                        {
                            // 尝试 Gemini 格式: candidates[0].content.parts[0].text
                            ct2 = ExtractGeminiText(data);
                        }

                        if (ct2 == null && rt == null) continue;

                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            if (!string.IsNullOrEmpty(rt))
                            {
                                if (!reasoningPhase)
                                {
                                    reasoningPhase = true;
                                    bubble.IsStreamingReasoning = true;
                                    bubble.ReasoningExpanded = true;
                                }
                                bubble.ReasoningContent += rt;
                                tokensSinceScroll++;
                            }
                            if (!string.IsNullOrEmpty(ct2))
                            {
                                if (reasoningPhase)
                                {
                                    reasoningPhase = false;
                                    bubble.IsStreamingReasoning = false;
                                    bubble.ReasoningExpanded = false;
                                }
                                bubble.Content += ct2;
                                tokensSinceScroll++;
                            }
                            if (tokensSinceScroll >= 8) { tokensSinceScroll = 0; ScrollToBottom(); }
                        });
                    }
                }
            });
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                ScrollToBottom());
        }

        private async Task HandleRegularResponse(HttpResponseMessage resp, ChatBubble bubble)
        {
            var body = await resp.Content.ReadAsStringAsync();
            string newContent;
            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    ApiResponse parsed;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                    string openAiContent = parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null;
                    if (!string.IsNullOrEmpty(openAiContent))
                        newContent = openAiContent;
                    else
                        // 尝试 Gemini 格式: candidates[0].content.parts[0].text
                        newContent = ExtractGeminiText(body) ?? "（无响应）";
                }
                catch { newContent = body; }
            }
            else
            {
                ApiResponse err = null;
                try
                {
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        err = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                }
                catch { }
                newContent = $"错误 {(int)resp.StatusCode}：{err?.Error?.Message ?? body}";
            }
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                bubble.Content = newContent);
        }

        // ── Reasoning toggle ──────────────────────────────────────────────────

    }
}
