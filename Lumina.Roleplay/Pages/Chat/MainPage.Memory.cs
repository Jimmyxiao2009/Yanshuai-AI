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
        private async Task CheckMemoryTriggerAsync()
        {
            // 全局 MemoryStore 整理（每小时至多一次）
            if ((DateTime.Now - _lastConsolidation).TotalMinutes >= 60)
            {
                _lastConsolidation = DateTime.Now;
                await MemoryStore.ConsolidateAsync();
            }

            if (_conv == null || !_conv.MemoryEnabled) return;
            if (_conv.Messages.Count < 3) return;
            if (_conv.ExchangesSinceLastSummary <= 0) return;

            _conv.ExchangesSinceLastSummary = 0;
            await RunMemorySummaryAsync();
            await RunDeepMemoryExtractionAsync();
        }

        private string BuildMemoryBlock()
        {
            if (!_conv.MemoryEnabled) return null;
            if (_conv.MemoryItems == null || _conv.MemoryItems.Count == 0) return null;
            if (_conv.ExchangesSinceLastInject < _conv.MemoryInjectInterval) return null;
            _conv.ExchangesSinceLastInject = 0;
            var sb = new StringBuilder();
            sb.AppendLine("[长期记忆]");
            foreach (var item in _conv.MemoryItems)
                sb.AppendLine("- " + item);
            return sb.ToString().Trim();
        }

        private async Task RunMemorySummaryAsync()
        {
            var result = await MemoryPipeline.SummarizeAndStoreAsync(_conv);
            if (result.Items.Count == 0 && string.IsNullOrEmpty(result.Error)) return;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                string text = string.IsNullOrEmpty(result.Error)
                    ? $"已生成 {result.Items.Count} 条短时记忆，并沉淀 {result.PoolItemCount} 条池级 RAG 记忆"
                    : "记忆总结失败：" + result.Error;
                if (AppSettings.RagDebugEnabled)
                    text += $"\n用时：{result.ElapsedMilliseconds} ms";

                _bubbles.Add(new ChatBubble
                {
                    Role = "system",
                    Content = text,
                    IsSystemMessage = true,
                });
                ScrollToBottom();
            });
        }

        private async Task RunDeepMemoryExtractionAsync()
        {
            var character = DataManager.GetCharacterForConversation(_conv);
            if (character == null) return;
            var pool = GetConversationPool(character);
            if (pool == null || !pool.Settings.AutoSummarizeConversations) return;

            var result = await MemoryPipeline.ExtractDeepMemoryAsync(_conv, pool);
            if (result == null) return;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                string text = AppSettings.S("深层记忆已自动更新", "Deep memory auto-updated");
                if (AppSettings.RagDebugEnabled)
                    text += "\n" + result.Length + " chars";

                _bubbles.Add(new ChatBubble
                {
                    Role = "system",
                    Content = text,
                    IsSystemMessage = true,
                });
                ScrollToBottom();
            });
        }

        // ── System prompt ─────────────────────────────────────────────────────

        // ── System Prompt (静态, KV-Cache 可复用) ──────────────────────

    }
}
