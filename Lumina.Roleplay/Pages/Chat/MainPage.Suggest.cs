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
        private void UpdateApiInfoLabel()
        {
            try
            {
                var profile = DataManager.GetProfileForConversation(_conv);
                if (profile != null && !string.IsNullOrEmpty(profile.Name))
                    ApiInfoLabel.Text = profile.Name + "  ·  " + profile.Model;
                else
                    ApiInfoLabel.Text = "API: 未配置";
            }
            catch
            {
                ApiInfoLabel.Text = "API: 未配置";
            }

            // 更新好感度显示
            try
            {
                var character = DataManager.GetCharacterForConversation(_conv);
                if (character != null)
                {
                    var pool = DialoguePoolManager.GetPool(character.Id, _conv?.UserProfileId ?? "");
                    if (pool?.Profile != null)
                    {
                        int fav = pool.Profile.Favorability;
                        string trend = pool.Profile.FavorabilityTrend ?? "stable";
                        string icon = trend == "up" ? "▲" : trend == "down" ? "▼" : "─";
                        FavorabilityLabel.Text = $"{icon} {fav}";
                        FavorabilityLabel.Visibility = Visibility.Visible;
                        // 低好感度→冷色（蓝灰），高好感度→暖色（粉红）
                        double ratio = Math.Max(0.0, Math.Min(1.0, fav / 100.0));
                        byte r = (byte)(120 + ratio * 135);   // 120→255
                        byte g = (byte)(140 - ratio * 60);    // 140→80
                        byte b = (byte)(200 - ratio * 140);   // 200→60
                        FavorabilityLabel.Foreground = new SolidColorBrush(Color.FromArgb(220, r, g, b));
                        return;
                    }
                }
                FavorabilityLabel.Visibility = Visibility.Collapsed;
            }
            catch
            {
                FavorabilityLabel.Visibility = Visibility.Collapsed;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // ── 推荐回复 ───────────────────────────────────────────────────────

        private int _suggestPage = 0;

        // 顶栏缓存（只构建一次，后续只更新标题和头像）
        private Grid      _topBarContainer;
        private Border    _topBarAvatar;
        private TextBlock _topBarTitle;

        private async Task GenerateSuggestedRepliesAsync()
        {
            if (_conv == null || _conv.Messages.Count < 2)
            {
                await ShowSuggestMessageAsync(AppSettings.S("暂无推荐回复，请先确保已配置 API 并有至少 2 条对话。",
                    "No suggestions available. Configure API and have at least 2 messages."));
                return;
            }
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (SuggestLoadingPanel != null)
                {
                    SuggestLoadingPanel.Visibility = Visibility.Visible;
                    SuggestProgressRing.IsActive = true;
                    SuggestLoadingText.Text = AppSettings.S("生成推荐回复中…", "Generating suggestions…");
                }
            });
            try
            {
                var profile = DataManager.GetProfileForConversation(_conv);
                if (profile == null)
                {
                    await ShowSuggestMessageAsync(AppSettings.S("没有可用的 API 配置，请先在设置中配置。",
                        "No API profile available. Configure one in settings first."));
                    return;
                }

                // 取最近几轮对话
                var recent = _conv.Messages
                    .Skip(Math.Max(0, _conv.Messages.Count - 6))
                    .Where(m => m.Role == "user" || m.Role == "assistant")
                    .ToList();
                if (recent.Count == 0)
                {
                    await ShowSuggestMessageAsync(AppSettings.S("对话内容不足，无法生成推荐回复。",
                        "Not enough conversation content for suggestions."));
                    return;
                }

                var prompt = new StringBuilder();
                prompt.AppendLine("你是一个角色扮演回复建议助手。根据以下对话历史，生成 8-12 条简短、风格多样的用户可能回复建议。");
                prompt.AppendLine("每条建议应像用户说出来的话，10字以内，覆盖不同情绪和立场。");
                prompt.AppendLine("用数字前缀输出，每行一条：");
                prompt.AppendLine();
                foreach (var m in recent)
                    prompt.AppendLine($"【{(m.Role == "user" ? "用户" : "角色")}】{m.Content}");

                var suggestMessages = new List<ApiRequestMessage>
                {
                    new ApiRequestMessage { Role = "user", Content = prompt.ToString() }
                };
                string requestJson = BuildRequestJson(profile.Model, suggestMessages, false, false, profile.ProviderType == "claude");

                var req = new HttpRequestMessage(HttpMethod.Post, profile.Url);
                ApplyAuthHeaders(req, profile);
                req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        await ShowSuggestMessageAsync(AppSettings.S("生成推荐回复失败：API 返回错误。", "Failed to generate suggestions: API error."));
                        return;
                    }
                    var body = await resp.Content.ReadAsStringAsync();
                    var text = ExtractResponseText(body, profile.ProviderType == "claude");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        await ShowSuggestMessageAsync(AppSettings.S("生成推荐回复失败：响应为空。", "Failed to generate suggestions: empty response."));
                        return;
                    }

                    _suggestedReplies = text.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0 && char.IsDigit(l[0]))
                        .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^\d+[\.\)、\s]*", "").Trim())
                        .Where(l => l.Length > 0 && l.Length < 30)
                        .Take(12)
                        .ToList();
                }

                if (_suggestedReplies != null && _suggestedReplies.Count > 0)
                {
                    _suggestPage = 0;
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        BuildSuggestPanel();
                    });
                }
                else
                {
                    await ShowSuggestMessageAsync(AppSettings.S("暂无推荐回复，请先确保已配置 API 并有至少 2 条对话。",
                        "No suggestions available. Configure API and have at least 2 messages."));
                }
            }
            catch (Exception ex)
            {
                await ShowSuggestMessageAsync(AppSettings.S("生成推荐回复失败: ", "Failed to generate: ") + ex.Message);
            }
            finally
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (SuggestLoadingPanel != null)
                    {
                        SuggestLoadingPanel.Visibility = Visibility.Collapsed;
                        SuggestProgressRing.IsActive = false;
                    }
                });
            }
        }

        private async Task ShowSuggestMessageAsync(string message)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (SuggestPanelInner == null) return;
                SuggestPanelInner.Children.Clear();
                SuggestPanelInner.Children.Add(new TextBlock
                {
                    Text = message,
                    FontSize = 13,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 8)
                });
            });
        }

        private void BuildSuggestPanel()
        {
            if (SuggestPanelInner == null) return;
            if (_suggestedReplies == null) _suggestedReplies = new List<string>();
            SuggestPanelInner.Children.Clear();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            titleRow.Children.Add(new TextBlock
            {
                Text = AppSettings.S("推荐回复", "Suggestions"),
                FontSize = 14, FontWeight = Windows.UI.Text.FontWeights.SemiBold, Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (_suggestedReplies.Count > 4)
            {
                var pageLabel = new TextBlock
                {
                    Text = $" {_suggestPage + 1}/{(int)Math.Ceiling(_suggestedReplies.Count / 4.0)} ",
                    FontSize = 11, Opacity = 0.5, Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                titleRow.Children.Add(pageLabel);

                var prevBtn = new Button
                {
                    Content = "◀", FontSize = 10, Padding = new Thickness(6, 0, 6, 0),
                    Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
                    MinWidth = 0, Height = 24,
                    IsEnabled = _suggestPage > 0
                };
                prevBtn.Click += (s, e) => { _suggestPage--; BuildSuggestPanel(); };
                titleRow.Children.Add(prevBtn);

                var nextBtn = new Button
                {
                    Content = "▶", FontSize = 10, Padding = new Thickness(6, 0, 6, 0),
                    Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
                    MinWidth = 0, Height = 24,
                    IsEnabled = (_suggestPage + 1) * 4 < _suggestedReplies.Count
                };
                nextBtn.Click += (s, e) => { _suggestPage++; BuildSuggestPanel(); };
                titleRow.Children.Add(nextBtn);
            }

            SuggestPanelInner.Children.Add(titleRow);

            int start = _suggestPage * 4;
            int end = Math.Min(start + 4, _suggestedReplies.Count);
            for (int i = start; i < end; i++)
            {
                var reply = _suggestedReplies[i];
                var btn = new Button
                {
                    Content = reply,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                btn.Click += (s, e) =>
                {
                    if (_isSending) return;
                    InputTextBox.Text = reply;
                    InputTextBox.SelectionStart = reply.Length;
                    SuggestPanelOverlay.Visibility = Visibility.Collapsed;
                    // 直接发送，而非仅填入文本框
                    SubmitButton_Click(s, e);
                };
                SuggestPanelInner.Children.Add(btn);
            }
        }

        // ── Scroll-to-bottom ────────────────────────────────────────────────

    }
}
