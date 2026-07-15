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
        private void ScrollToBottom()
        {
            // 在 WP 上，滚动会让控件短暂获得焦点，触发虚拟键盘。
            // 滚动前把焦点转到 RootGrid（非文本控件），避免键盘弹出。
            // 只在 InputTextBox 当前没有焦点时才转移，不打断用户正在输入的状态。
            var focused = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as Control;
            if (focused != InputTextBox)
                this.Focus(FocusState.Programmatic);
            // 虚拟化 ListView：用 ScrollIntoView 滚到最后一条，避免依赖未实现的容器测量。
            if (_bubbles.Count > 0)
                ChatItems.ScrollIntoView(_bubbles[_bubbles.Count - 1]);
        }

        // ── 手动构建请求JSON（支持多模态content array）────────────────────────

        // ExtractGeminiText 仍委托 CardCompleter（其 ExtractJsonString 行为与 Agent 不同，保留原状）
        private static string ExtractGeminiText(string json) => CardCompleter.ExtractGeminiText(json);
        private static string ExtractClaudeText(string json) => ChatJson.ExtractClaudeText(json);
        // EscapeJson / BuildRequestJson 已下沉到 Lumina.Core/AI/ChatJson.cs（两项目共用）
        private static string EscapeJson(string s) => ChatJson.EscapeJson(s);

        private static string BuildRequestJson(string model, List<ApiRequestMessage> messages, bool stream, bool supportsVision = false, bool isClaudeProvider = false)
            => ChatJson.BuildRequestJson(model, messages, stream, supportsVision, isClaudeProvider);

        // ── Shared API helpers ────────────────────────────────────────────────

        private static void ApplyAuthHeaders(HttpRequestMessage req, ApiProfile profile)
        {
            if (profile.ProviderType == "claude")
            {
                req.Headers.TryAddWithoutValidation("x-api-key", profile.ApiKey);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            }
            else
            {
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
            }
        }

        // 统一解析 OpenAI / Claude / Gemini 非流式响应，返回文本内容
        private static string ExtractResponseText(string body, bool isClaude)
        {
            if (string.IsNullOrEmpty(body)) return null;

            // OpenAI 格式
            try
            {
                ApiResponse parsed;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                    parsed = (ApiResponse)new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(ms);
                string content = parsed?.Choices?.Count > 0 ? parsed.Choices[0]?.Message?.Content : null;
                if (!string.IsNullOrEmpty(content)) return content;
            }
            catch { }

            // Claude 格式: content[0].text
            if (isClaude)
            {
                string claudeContent = ExtractClaudeText(body);
                if (!string.IsNullOrEmpty(claudeContent)) return claudeContent;
            }

            // Gemini 格式
            return ExtractGeminiText(body);
        }

    }
}
