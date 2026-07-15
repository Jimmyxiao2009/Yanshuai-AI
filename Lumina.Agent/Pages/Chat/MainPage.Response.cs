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
        // ── Streaming ─────────────────────────────────────────────────────────

        // 缓存反序列化器——之前每个 SSE chunk 都 new 一个，造成流式响应最热循环的 GC 压力
        private static readonly DataContractJsonSerializer _streamChunkSer = new DataContractJsonSerializer(typeof(StreamChunk));
        private static readonly DataContractJsonSerializer _apiRespSer     = new DataContractJsonSerializer(typeof(ApiResponse));

        // 流式 UI 刷新节流：把 token 累积在后台线程，按固定时间间隔批量推到 UI，
        // 而不是每来一个 SSE chunk 就 Dispatch 一次。这样无论 token 多快，UI 重排/滚动次数都有上限——
        // 绑定的 TextBlock 每次内容变化都要对整段文本做一次布局（O(n)），原来「每 token 一次」会累积成
        // O(n²)，导致长回复后段越来越卡。
        // 仿 ChatGPT：每秒只刷新几次（≈4 次），新追加的那段文本由 StreamFade 做淡入动画，
        // 低频更新也不显得跳，同时把每秒布局次数从 ~20 压到 ~4。
        private const int StreamUiIntervalMs     = 250;  // UI 刷新最小间隔（≈4 次/秒）
        private const int StreamScrollIntervalMs = 200;  // 自动滚动到底最小间隔

        private async Task<ApiUsageInfo> HandleStreamingResponse(HttpResponseMessage resp, ChatBubble bubble, System.Threading.CancellationToken ct)
        {
            ApiUsageInfo lastUsage = null;

            // 后台累积、尚未推到 UI 的增量（仅后台线程访问，推送前快照成 string 再交给 UI 线程）
            var pendingReasoning = new StringBuilder();
            var pendingContent   = new StringBuilder();
            int lastUiTick     = Environment.TickCount;
            int lastScrollTick = Environment.TickCount - StreamScrollIntervalMs;
            bool reasoningPhase = false;

            // 把累积的增量批量应用到气泡。force=true 时无视时间间隔（收尾/最后一批）。
            // await Dispatcher 提供天然背压，避免 UI 渲染跟不上时把队列堆满。
            async Task PushBatch(bool force)
            {
                if (pendingReasoning.Length == 0 && pendingContent.Length == 0) return;
                int nowTick = Environment.TickCount;
                if (!force && unchecked(nowTick - lastUiTick) < StreamUiIntervalMs) return;
                lastUiTick = nowTick;

                string rDelta = pendingReasoning.Length > 0 ? pendingReasoning.ToString() : null;
                string cDelta = pendingContent.Length   > 0 ? pendingContent.ToString()   : null;
                pendingReasoning.Clear();
                pendingContent.Clear();

                bool doScroll = force || unchecked(nowTick - lastScrollTick) >= StreamScrollIntervalMs;
                if (doScroll) lastScrollTick = nowTick;

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (rDelta != null) { bubble.AppendReasoningToken(rDelta); reasoningPhase = true; }
                    if (cDelta != null)
                    {
                        if (reasoningPhase) { reasoningPhase = false; bubble.StopReasoningStreaming(); }
                        bubble.AppendStreamToken(cDelta);
                    }
                    bubble.FlushStream();
                    if (rDelta != null) bubble.AppendReasoningChunk(bubble.ReasoningContent);
                    if (doScroll) ScrollToBottom();
                });
            }

            await Task.Run(async () =>
            {
                using (var stream = await resp.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (!reader.EndOfStream && !ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;
                        // SSE 规范：data: 后的单个空格可选，部分网关发 data:{...}（无空格）
                        if (!line.StartsWith("data:")) continue;
                        var data = line.Substring(5);
                        if (data.StartsWith(" ")) data = data.Substring(1);
                        data = data.Trim();
                        if (data == "[DONE]") break;

                        string ct2 = null;
                        string rt   = null;

                        // 尝试 OpenAI/DeepSeek/Groq 格式 (choices[].delta)
                        StreamChunk chunk = null;
                        try
                        {
                            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(data)))
                                chunk = (StreamChunk)_streamChunkSer.ReadObject(ms);
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
                            string claude = ExtractClaudeText(data);
                            if (!string.IsNullOrEmpty(claude))
                                ct2 = claude;
                            else
                                ct2 = ExtractGeminiText(data);
                        }

                        if (chunk?.Usage != null) lastUsage = chunk.Usage;
                        // 兜底扫描 cached_tokens（DataContract 不支持嵌套动态 key）
                        // 仅当本 chunk 确实含有 cached_tokens 时才做昂贵的 JsonObject.Parse，
                        // 避免对绝大多数普通 token chunk 都全量解析一遍 JSON
                        if (lastUsage != null && data.IndexOf("cached_tokens", StringComparison.Ordinal) >= 0)
                            PatchCachedTokens(data, lastUsage);

                        if (!string.IsNullOrEmpty(rt))  pendingReasoning.Append(rt);
                        if (!string.IsNullOrEmpty(ct2)) pendingContent.Append(ct2);

                        // 按时间节流推送：未到间隔则继续累积，到点了才整批推一次 UI
                        await PushBatch(false);
                    }
                }
            });

            // 推送剩余增量 + 收尾
            await PushBatch(true);
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                bubble.StopReasoningStreaming();
                bubble.FinalizeStream();
                ScrollToBottom();
            });
            return lastUsage;
        }

        /// <summary>
        /// DataContractJsonSerializer 不能解析嵌套对象里的动态 key，
        /// 所以单独用 JsonObject 补扫 prompt_tokens_details.cached_tokens。
        /// </summary>
        private static void PatchCachedTokens(string dataJson, ApiUsageInfo usage)
        {
            if (usage == null || string.IsNullOrEmpty(dataJson)) return;
            try
            {
                var root = Windows.Data.Json.JsonObject.Parse(dataJson);
                if (!root.ContainsKey("usage")) return;
                var u = root.GetNamedObject("usage");
                if (u.ContainsKey("prompt_tokens_details"))
                {
                    var d = u.GetNamedObject("prompt_tokens_details");
                    int cached = (int)d.GetNamedNumber("cached_tokens", 0);
                    if (cached > 0) usage.CacheReadInputTokens = cached;
                }
            }
            catch { }
        }

        private async Task<ApiUsageInfo> HandleRegularResponse(HttpResponseMessage resp, ChatBubble bubble)
        {
            var body = await resp.Content.ReadAsStringAsync();
            string newContent;
            ApiUsageInfo usage = null;
            if (resp.IsSuccessStatusCode)
            {
                try
                {
                    ApiResponse parsed;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = (ApiResponse)_apiRespSer.ReadObject(ms);
                    string openAiContent = parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null;
                    if (!string.IsNullOrEmpty(openAiContent))
                        newContent = openAiContent;
                    else
                    {
                        string claude = ExtractClaudeText(body);
                        if (!string.IsNullOrEmpty(claude))
                            newContent = claude;
                        else
                            newContent = ExtractGeminiText(body) ?? "（无响应）";
                    }
                    usage = parsed?.Usage;
                    if (usage != null) PatchCachedTokens(body, usage);
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
            return usage;
        }

        // ── Bubble tap toggles action strip ──────────────────────────────────

    }
}
