using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace yanshuai
{
    [DataContract]
    public class ApiProfile
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; } = "New API";
        [DataMember] public string Url { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
        [DataMember] public string ApiKey { get; set; } = "";
        [DataMember] public string Model { get; set; } = "llama-3.3-70b-versatile";
        /// <summary>提供商类型：openai（兼容）或 claude（Anthropic 原生）</summary>
        [DataMember] public string ProviderType { get; set; } = "openai";
        /// <summary>是否支持视觉/多模态（图片）。开启后图片以 content array 发送，关闭则降级为纯文字</summary>
        [DataMember] public bool   VisionEnabled    { get; set; } = false;
        /// <summary>此API独立破墙提示词，为空则使用全局设置</summary>
        [DataMember] public bool   JailbreakEnabled { get; set; } = false;
        [DataMember] public string JailbreakPrompt  { get; set; } = "";
        public override string ToString() => Name ?? "Unnamed";

        /// <summary>Shown in lists; prepends ★ when this profile is the global default.</summary>
        public string NameWithDefault =>
            AppSettings.DefaultApiProfileId == Id ? $"★  {Name}" : Name;

        /// <summary>预设 API 配置列表，供用户快速选择</summary>
        public static List<ApiProfile> GetPresets()
        {
            return new List<ApiProfile>
            {
                new ApiProfile { Name = "DeepSeek V4 Pro", Url = "https://api.deepseek.com/v1/chat/completions", Model = "deepseek-v4-pro", ProviderType = "openai" },
                new ApiProfile { Name = "DeepSeek V4 Flash", Url = "https://api.deepseek.com/v1/chat/completions", Model = "deepseek-v4-flash", ProviderType = "openai" },
                new ApiProfile { Name = "Groq (Llama 4 Scout)", Url = "https://api.groq.com/openai/v1/chat/completions", Model = "meta-llama/llama-4-scout-17b-16e-instruct", ProviderType = "openai" },
                new ApiProfile { Name = "Groq (Qwen3 32B)", Url = "https://api.groq.com/openai/v1/chat/completions", Model = "qwen/qwen3-32b", ProviderType = "openai" },
                new ApiProfile { Name = "OpenRouter", Url = "https://openrouter.ai/api/v1/chat/completions", Model = "anthropic/claude-sonnet-4", ProviderType = "openai" },
                new ApiProfile { Name = "Mistral Large", Url = "https://api.mistral.ai/v1/chat/completions", Model = "mistral-large-latest", ProviderType = "openai" },
                new ApiProfile { Name = "Mistral Small", Url = "https://api.mistral.ai/v1/chat/completions", Model = "mistral-small-latest", ProviderType = "openai" },
                new ApiProfile { Name = "Google Gemini", Url = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", Model = "gemini-2.5-flash", ProviderType = "openai", VisionEnabled = true },
                new ApiProfile { Name = "Moonshot Kimi", Url = "https://api.moonshot.cn/v1/chat/completions", Model = "kimi-k2", ProviderType = "openai" },
                new ApiProfile { Name = "智谱 GLM-4", Url = "https://open.bigmodel.cn/api/paas/v4/chat/completions", Model = "glm-4-flash", ProviderType = "openai" },
                new ApiProfile { Name = "SiliconFlow", Url = "https://api.siliconflow.cn/v1/chat/completions", Model = "deepseek-ai/DeepSeek-V3", ProviderType = "openai" },
                new ApiProfile { Name = "Together AI", Url = "https://api.together.xyz/v1/chat/completions", Model = "meta-llama/Llama-4-Maverick-17B-128E-Instruct-FP8", ProviderType = "openai" },
                new ApiProfile { Name = "Claude (Anthropic)", Url = "https://api.anthropic.com/v1/messages", Model = "claude-sonnet-4-6", ProviderType = "claude", VisionEnabled = true },
            };
        }
    }

    [DataContract]
    public class ConvAppearance
    {
        /// <summary>none | solid | image</summary>
        [DataMember] public string BackgroundType  { get; set; } = "none";
        /// <summary>#RRGGBB for solid; base64 jpeg/png for image</summary>
        [DataMember] public string BackgroundValue { get; set; } = "";
        /// <summary>Custom user-bubble color #RRGGBB, empty = accent</summary>
        [DataMember] public string UserBubbleColor { get; set; } = "";
        /// <summary>Custom AI-bubble color #RRGGBB, empty = darkened accent</summary>
        [DataMember] public string AiBubbleColor   { get; set; } = "";
        /// <summary>图片背景暗度 0-100，默认50</summary>
        [DataMember] public int DimOpacity { get; set; } = 50;
        /// <summary>引号内文字颜色 #RRGGBB，空=默认橘黄</summary>
        [DataMember] public string QuoteColor   { get; set; } = "";
        /// <summary>括号内文字颜色 #RRGGBB，空=默认灰色</summary>
        [DataMember] public string BracketColor { get; set; } = "";
    }

    /// <summary>One branch snapshot — a complete message list from branch-creation point onward.</summary>
    [DataContract]
    public class ConversationBranch
    {
        [DataMember] public List<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
    }

    /// <summary>Branch point anchored to a fixed position in the message list.</summary>
    [DataContract]
    public class BranchPoint
    {
        /// <summary>Legacy — kept for deserialisation compat only. Not used for lookup.</summary>
        [DataMember] public string AnchorMessageId { get; set; }
        /// <summary>Index in Conversation.Messages where this branch point sits.
        /// This is the authoritative lookup key — never changes after creation.</summary>
        [DataMember] public int AnchorIndex { get; set; } = -1;
        /// <summary>Which branch is currently live in Conversation.Messages.</summary>
        [DataMember] public int ActiveIndex { get; set; } = 0;
        /// <summary>All branch snapshots, including the current one.</summary>
        [DataMember] public List<ConversationBranch> Branches { get; set; } = new List<ConversationBranch>();
        public int Count => Branches?.Count ?? 0;
    }

    [DataContract]
    public class ImageEntry
    {
        public string Base64 { get; set; }
        public string Mime   { get; set; }
    }

    public class ConversationMessage
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Role { get; set; }
        [DataMember] public string Content { get; set; } = "";
        [DataMember] public string ReasoningContent { get; set; } = "";
        /// <summary>用户附加的图片列表（每项 Base64），仅user消息有效</summary>
        [DataMember] public List<string> ImagesBase64 { get; set; }
        [DataMember] public List<string> ImagesMimeType { get; set; }
        // 兼容旧数据
        [DataMember] public string ImageBase64 { get; set; }
        [DataMember] public string ImageMimeType { get; set; }
        /// <summary>附加的文本文件名（显示用，不含内容）</summary>
        [DataMember] public List<string> AttachedFileNames { get; set; }
        [DataMember] public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>获取所有图片（合并新旧字段）</summary>
        public List<ImageEntry> GetAllImages()
        {
            var result = new List<ImageEntry>();
            if (ImagesBase64 != null && ImagesMimeType != null)
            {
                for (int i = 0; i < ImagesBase64.Count; i++)
                    result.Add(new ImageEntry { Base64 = ImagesBase64[i], Mime = i < ImagesMimeType.Count ? ImagesMimeType[i] : "image/jpeg" });
            }
            else if (!string.IsNullOrEmpty(ImageBase64))
            {
                result.Add(new ImageEntry { Base64 = ImageBase64, Mime = ImageMimeType ?? "image/jpeg" });
            }
            return result;
        }
    }

    [DataContract]
    public class Conversation
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; } = "新对话";
        [DataMember] public string ApiProfileId { get; set; } = "";

        /// <summary>
        /// Live message list — always points to AllBranches[ActiveBranchIndex].Messages.
        /// Kept as [DataMember] for back-compat; on load, InitBranches() syncs it.
        /// </summary>
        [DataMember] public List<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();

        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        [DataMember] public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // ── Long memory ───────────────────────────────────────────────────────
        [DataMember] public List<string> MemoryItems { get; set; } = new List<string>();
        [DataMember] public int ExchangesSinceLastSummary { get; set; } = 0;
        [DataMember] public int ExchangesSinceLastInject { get; set; } = 0;
        [DataMember] public bool MemoryEnabled { get; set; } = false;
        [DataMember] public string MemoryApiProfileId { get; set; } = "";
        [DataMember] public int MemorySummaryInterval { get; set; } = 10;
        [DataMember] public int MemoryInjectInterval { get; set; } = 1;

        // ── Context window ────────────────────────────────────────────────────
        /// <summary>Max messages to send to API. 0 = unlimited.</summary>
        [DataMember] public int ContextWindow { get; set; } = 20;

        // ── Appearance ────────────────────────────────────────────────────────
        [DataMember] public ConvAppearance Appearance { get; set; } = new ConvAppearance();

        // ── Branching ─────────────────────────────────────────────────────────
        /// <summary>Per-message branch points (new system).</summary>
        [DataMember] public List<BranchPoint> BranchPoints { get; set; } = new List<BranchPoint>();
        /// <summary>Full message snapshots for every branch. Index 0 = original.</summary>
        [DataMember] public List<ConversationBranch> AllBranches { get; set; } = new List<ConversationBranch>();
        [DataMember] public int ActiveBranchIndex { get; set; } = 0;

        /// <summary>
        /// Call once after deserialisation (or on first use) to make Messages
        /// point to AllBranches[ActiveBranchIndex].Messages.
        /// </summary>
        public void InitBranches()
        {
            if (AllBranches == null) AllBranches = new List<ConversationBranch>();
            if (AllBranches.Count == 0)
            {
                AllBranches.Add(new ConversationBranch { Messages = Messages ?? new List<ConversationMessage>() });
                ActiveBranchIndex = 0;
            }
            if (ActiveBranchIndex < 0 || ActiveBranchIndex >= AllBranches.Count)
                ActiveBranchIndex = 0;
            Messages = AllBranches[ActiveBranchIndex].Messages;
        }

        /// <summary>
        /// Create a new branch starting from editedMessages (messages 0..pivot + the edited message).
        /// Returns the new branch index.
        /// </summary>
        public int CreateBranch(List<ConversationMessage> startMessages)
        {
            InitBranches();
            // Save current state back into the current branch
            AllBranches[ActiveBranchIndex].Messages = Messages.ToList();
            // Create new branch
            var newBranch = new ConversationBranch { Messages = new List<ConversationMessage>(startMessages) };
            AllBranches.Add(newBranch);
            ActiveBranchIndex = AllBranches.Count - 1;
            Messages = AllBranches[ActiveBranchIndex].Messages;
            return ActiveBranchIndex;
        }

        /// <summary>Switch to branch at index; reloads Messages.</summary>
        public void SwitchBranch(int index)
        {
            InitBranches();
            if (index < 0 || index >= AllBranches.Count) return;
            // Save current messages back
            AllBranches[ActiveBranchIndex].Messages = Messages.ToList();
            ActiveBranchIndex = index;
            Messages = AllBranches[ActiveBranchIndex].Messages;
        }

        /// <summary>
        /// Call before saving: syncs the current live Messages tail back into
        /// BranchPoints[*].Branches[ActiveIndex] so no AI responses are lost on restart.
        /// </summary>
        public void SyncActiveBranch()
        {
            if (BranchPoints == null) return;
            foreach (var bp in BranchPoints)
            {
                if (bp.AnchorIndex < 0 || bp.AnchorIndex > Messages.Count) continue;
                if (bp.ActiveIndex < 0 || bp.ActiveIndex >= bp.Branches.Count) continue;
                bp.Branches[bp.ActiveIndex] = new ConversationBranch
                {
                    Messages = Messages.Skip(bp.AnchorIndex).Select(m => new ConversationMessage
                    {
                        Id = m.Id, Role = m.Role, Content = m.Content,
                        ReasoningContent = m.ReasoningContent, Timestamp = m.Timestamp
                    }).ToList()
                };
            }
        }

        [DataMember] public bool   IsPinned  { get; set; } = false;
        [DataMember] public string GroupName { get; set; } = "";

        // UI helper — not persisted
        public Windows.UI.Xaml.Visibility PinIconVisibility =>
            IsPinned ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;

        // ── Display helpers ───────────────────────────────────────────────────
        public string LastMessagePreview
        {
            get
            {
                if (Messages == null || Messages.Count == 0) return "空对话";
                var text = Messages[Messages.Count - 1].Content ?? "";
                text = StripMarkdown(text);
                return text.Length > 55 ? text.Substring(0, 55) + "\u2026" : text;
            }
        }


        private static string StripMarkdown(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder();
            foreach (var raw in s.Split(new[]{'\n'}, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                int h = 0;
                while (h < line.Length && line[h] == '#') h++;
                if (h > 0 && h < line.Length && line[h] == ' ') line = line.Substring(h + 1).Trim();
                line = System.Text.RegularExpressions.Regex.Replace(line, @"[*_]{1,2}(.+?)[*_]{1,2}", "$1");
                line = System.Text.RegularExpressions.Regex.Replace(line, @"`(.+?)`", "$1");
                if (line.StartsWith("```")) continue;
                if (line.Length > 2 && (line[0] == '-' || line[0] == '*' || line[0] == '>') && line[1] == ' ')
                    line = line.Substring(2);
                if (!string.IsNullOrWhiteSpace(line)) { sb.Append(line); sb.Append(' '); }
            }
            return sb.ToString().Trim();
        }
        public string UpdatedAtDisplay
        {
            get
            {
                var now = DateTime.Now;
                if (UpdatedAt.Date == now.Date) return UpdatedAt.ToString("HH:mm");
                if ((now - UpdatedAt).TotalDays < 7) return UpdatedAt.ToString("ddd HH:mm");
                return UpdatedAt.ToString("MM-dd");
            }
        }
    }

    [DataContract]
    public class UserProfile
    {
        [DataMember] public string Id           { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name        { get; set; } = "";
        [DataMember] public string Description { get; set; } = "";
        [DataMember] public string AvatarBase64  { get; set; }
        [DataMember] public string AvatarMimeType { get; set; }
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarBase64);
    }

    /// <summary>搜索 API 池条目</summary>
    [DataContract]
    public class SearchApiEntry
    {
        [DataMember] public string Id       { get; set; } = Guid.NewGuid().ToString();
        /// <summary>searxng / ddg / bing</summary>
        [DataMember] public string Type     { get; set; } = "searxng";
        /// <summary>显示名称</summary>
        [DataMember] public string Name     { get; set; } = "";
        /// <summary>SearXNG实例URL 或 Bing API Key</summary>
        [DataMember] public string Value    { get; set; } = "";
        [DataMember] public bool   Enabled  { get; set; } = true;
        /// <summary>内置条目不可删除</summary>
        [DataMember] public bool   BuiltIn  { get; set; } = false;

        public string TypeLabel => Type == "bing" ? "Bing" : Type == "ddg" ? "DuckDuckGo" : "SearXNG";
    }

    [DataContract]
    public class AppData
    {
        [DataMember] public List<ApiProfile> ApiProfiles { get; set; } = new List<ApiProfile>();
        [DataMember] public List<Conversation> Conversations { get; set; } = new List<Conversation>();
        [DataMember] public string SelectedApiProfileId { get; set; }
        [DataMember] public string LastActiveConversationId { get; set; }
        /// <summary>Global default API — used when a conversation has no ApiProfileId set.</summary>
        [DataMember] public string DefaultApiProfileId { get; set; } = "";
        [DataMember] public UserProfile UserProfile { get; set; } = new UserProfile();
        [DataMember] public List<UserProfile> UserProfiles { get; set; } = new List<UserProfile>();
        [DataMember] public string ActiveUserProfileId { get; set; } = "";
    }
}
