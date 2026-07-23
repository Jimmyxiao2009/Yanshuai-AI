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
        private UserProfile GetConversationUserProfile()
        {
            if (_conv == null) return DataManager.GetActiveUserProfile();

            var profiles = DataManager.Data?.UserProfiles;
            if (profiles != null && !string.IsNullOrEmpty(_conv.UserProfileId))
            {
                var matched = profiles.FirstOrDefault(p => p.Id == _conv.UserProfileId);
                if (matched != null) return matched;
            }

            if (DataManager.Data?.UserProfile != null &&
                DataManager.Data.UserProfile.Id == _conv.UserProfileId)
                return DataManager.Data.UserProfile;

            return DataManager.GetActiveUserProfile();
        }

        private DialoguePool GetConversationPool(CharacterCard character)
        {
            if (character == null || _conv == null) return null;
            return DialoguePoolManager.GetPool(character.Id, _conv.UserProfileId);
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();

            var profile = DataManager.GetProfileForConversation(_conv);
            if (profile != null && profile.JailbreakEnabled &&
                !string.IsNullOrEmpty(profile.JailbreakPrompt))
            { sb.AppendLine(profile.JailbreakPrompt); sb.AppendLine(); }
            else if (AppSettings.JailbreakEnabled && !string.IsNullOrEmpty(AppSettings.JailbreakPrompt))
            { sb.AppendLine(AppSettings.JailbreakPrompt); sb.AppendLine(); }

            var character = DataManager.GetCharacterForConversation(_conv);
            if (character != null)
            {
                sb.AppendLine("【角色设定】");
                sb.AppendLine($"- 你扮演的角色是：{character.Name}。始终以该角色身份回应，不跳出、不解释。");
                if (!string.IsNullOrEmpty(character.Description)) sb.AppendLine($"- 外貌与背景：{character.Description}");
                if (!string.IsNullOrEmpty(character.Personality)) sb.AppendLine($"- 性格特征：{character.Personality}");
                if (!string.IsNullOrEmpty(character.Scenario))    sb.AppendLine($"- 当前情境：{character.Scenario}");
                if (!string.IsNullOrEmpty(character.SystemPrompt)) { sb.AppendLine(); sb.AppendLine(character.SystemPrompt); }
                sb.AppendLine();
                sb.AppendLine("【回复要求】");
                sb.AppendLine("- 动作神态用 * * 或（ ）包裹。对话用 「」或 \"\" 包裹。叙述直接书写。");
                sb.AppendLine("- 回应要有画面感和沉浸感，自然地融入角色性格。");
                sb.AppendLine("- 根据角色设定和对话历史中的上下文做出符合角色逻辑的回应。");
            }

            var up = GetConversationUserProfile();
            if (up != null && (!string.IsNullOrEmpty(up.Name) || !string.IsNullOrEmpty(up.Description)))
            {
                sb.AppendLine();
                sb.AppendLine("【用户信息】");
                if (!string.IsNullOrEmpty(up.Name)) sb.AppendLine("用户名：" + up.Name);
                if (!string.IsNullOrEmpty(up.Description)) sb.AppendLine("用户描述：" + up.Description);
            }

            var poolForPersona = GetConversationPool(character);
            if (poolForPersona?.Profile != null)
            {
                var deep = poolForPersona.Profile;
                bool hasDeepMemory =
                    !string.IsNullOrWhiteSpace(deep.UserPortrait) ||
                    (deep.CoreTraits != null && deep.CoreTraits.Any(x => !string.IsNullOrWhiteSpace(x))) ||
                    (deep.ExperienceItems != null && deep.ExperienceItems.Any(x => !string.IsNullOrWhiteSpace(x))) ||
                    (deep.KnownFacts != null && deep.KnownFacts.Any(x => !string.IsNullOrWhiteSpace(x)));

                if (hasDeepMemory)
                {
                    sb.AppendLine();
                    sb.AppendLine("【深层记忆：角色对用户的长期认知】");
                    sb.AppendLine("这些是稳定的长期认知，优先级高于临时检索到的相关记忆。");
                    if (!string.IsNullOrWhiteSpace(deep.UserPortrait))
                        sb.AppendLine("总体认知：" + deep.UserPortrait.Trim());
                    AppendDeepMemoryList(sb, "对我的印象：", deep.CoreTraits);
                    AppendDeepMemoryList(sb, "关键互动经历：", deep.ExperienceItems);
                    AppendDeepMemoryList(sb, "已确认事实：", deep.KnownFacts);
                }
            }

            // 新对话提醒：让 AI 主动利用 RAG 和深层记忆
            if (_conv.Messages.Count <= 3)
            {
                sb.AppendLine();
                sb.AppendLine("【系统提醒】这是与用户的新对话。请主动利用【深层记忆】和每条用户消息中附带的【相关记忆】来理解用户，即使记忆内容有限也请基于已有信息自然回应。");
            }

            return sb.ToString().Trim();
        }

        private static void AppendDeepMemoryList(StringBuilder sb, string title, List<string> items)
        {
            if (items == null || !items.Any(x => !string.IsNullOrWhiteSpace(x))) return;
            sb.AppendLine(title);
            foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x)).Take(12))
                sb.AppendLine("- " + item.Trim());
        }

        // ── 动态上下文 (拼接到用户消息, 每轮重新生成) ────────────────

        private string BuildDynamicContext(string userInput)
        {
            var sb = new StringBuilder();

            string recentText = userInput.ToLower();
            int start = Math.Max(0, _conv.Messages.Count - 6);
            for (int i = start; i < _conv.Messages.Count; i++)
                recentText += " " + _conv.Messages[i].Content.ToLower();

            bool hasWB = false;
            var worldEntries = (DataManager.Data.WorldBookEntries ?? new List<WorldBookEntry>())
                .Where(entry => entry != null && !entry.Disable)
                .OrderBy(entry => entry.Order)
                .ThenBy(entry => entry.Name ?? "");
            foreach (var entry in worldEntries)
            {
                string activeCharId = _conv?.CharacterCardId ?? "";
                bool scopeMatch = entry.CharacterIds == null || entry.CharacterIds.Count == 0
                    || (!string.IsNullOrEmpty(activeCharId) && entry.CharacterIds.Contains(activeCharId));
                if (!scopeMatch) continue;

                if (!string.IsNullOrEmpty(entry.Content) &&
                    (entry.AlwaysActive || HasKeywordMatch(recentText, entry.Keywords)))
                {
                    if (!hasWB) { sb.AppendLine("【背景资料】"); hasWB = true; }
                    sb.AppendLine($"「{entry.Name}」{entry.Content}");
                }
            }

            var character = DataManager.GetCharacterForConversation(_conv);
            if (character != null && !string.IsNullOrEmpty(userInput))
            {
                var pool = GetConversationPool(character);
                if (pool != null && pool.Settings.EnableRAG)
                {
                    AppendRecentOtherConversationMemories(sb, pool);
                    try
                    {
                        var ragResults = RAGForPrompt ?? pool.SearchMemories(userInput);
                        if (ragResults.Count > 0)
                        {
                            sb.AppendLine("【相关记忆】");
                            foreach (var m in ragResults) sb.AppendLine($"- {m}");
                        }
                    }
                    catch { }
                    finally { RAGForPrompt = null; }
                }
            }

            return sb.ToString().Trim();
        }

        private void AppendRecentOtherConversationMemories(StringBuilder sb, DialoguePool pool)
        {
            if (pool == null || _conv == null || _conv.Messages.Count > 2) return;

            var items = new List<string>();
            if (pool.SharedMemories != null)
            {
                foreach (var mem in pool.SharedMemories
                    .OrderByDescending(m => m.CreatedAt)
                    .Where(m => m.SourceConversationId != _conv.Id)
                    .Take(6))
                {
                    var source = pool.CachedConversations?
                        .FirstOrDefault(c => c.Id == mem.SourceConversationId);
                    string sourceTitle = source != null
                        ? $"来自另一段对话「{source.Title}」的最近记忆："
                        : "来自该对话池其他对话总结的最近记忆：";
                    if (!string.IsNullOrWhiteSpace(mem.Summary))
                        items.Add(sourceTitle + mem.Summary);
                }
            }

            if (items.Count == 0) return;
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("【其他对话的最近记忆】");
            foreach (var item in items.Take(6))
                sb.AppendLine("- " + item);
        }

        private async System.Threading.Tasks.Task PrefetchCloudRagAsync(string userInput, System.Threading.CancellationToken ct = default)
        {
            RAGForPrompt = null;
            _ragDebugText = "";
            var debugLines = new List<string>();
            try
            {
                var character = DataManager.GetCharacterForConversation(_conv);
                if (character != null)
                {
                    var pool = GetConversationPool(character);
                    if (pool != null && pool.Settings.EnableRAG)
                    {
                        // 后台预热嵌入缓存（让 BuildDynamicContext 中的 SearchMemories 走缓存）
                        await pool.BuildEmbeddingCacheAsync();

                        var profile = DataManager.GetProfileForConversation(_conv);
                        if (profile != null)
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            RAGForPrompt = await pool.SearchMemoriesCloudAsync(userInput, profile, pool.Settings.RAGTopK, ct);
                            sw.Stop();
                            debugLines.Add($"对话池 RAG：{sw.ElapsedMilliseconds} ms，命中 {RAGForPrompt?.Count ?? 0}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RAGForPrompt = null;
                debugLines.Add("对话池 RAG 失败：" + ex.Message);
            }

            if (AppSettings.RagDebugEnabled)
                _ragDebugText = string.Join("\n", debugLines);
        }

        private bool HasKeywordMatch(string text, string keywords)
        {
            if (string.IsNullOrEmpty(keywords) || string.IsNullOrEmpty(text)) return false;
            // 兼容中英文逗号/分号分隔
            char[] seps = { ',', '，', ';', '；', '|' };
            return keywords.Split(seps)
                .Select(k => k.Trim().ToLower())
                .Where(k => k.Length > 0)
                .Any(k => text.Contains(k));
        }

        private void MaybeShowFirstMessage()
        {
            if (_conv.Messages.Count > 0) return;
            var character = DataManager.GetCharacterForConversation(_conv);
            if (character == null || string.IsNullOrEmpty(character.FirstMessage)) return;

            // 只在对话池的首个对话显示开场白
            var pool = DialoguePoolManager.GetOrCreatePool(character);
            if (pool != null && pool.ConversationIds.Count > 1) return;

            // 写入 Messages，让 AI 在第一轮回复时能看到自己的开场白
            var firstMsg = new ConversationMessage
            {
                Role = "assistant",
                Content = character.FirstMessage,
                Timestamp = DateTime.Now,
            };
            _conv.Messages.Add(firstMsg);

            var bubble = new ChatBubble
            {
                Role = "assistant", Content = character.FirstMessage,
                MessageId = firstMsg.Id,
                IsFirstMessage = true,
                BackgroundColor = AiBubbleBg(), ForegroundColor = AiBubbleFg(),
                ReasoningBgColor = _reasoningBrush,
                QuoteColor   = _conv?.Appearance?.QuoteColor   ?? "",
                BracketColor = _conv?.Appearance?.BracketColor ?? "",
            };
            _bubbles.Add(bubble);
        }

        // ── Streaming ─────────────────────────────────────────────────────────

    }
}
