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
        /// <summary>副模型（可选，用于记忆总结、RAG 等辅助任务）</summary>
        [DataMember] public string SubModel { get; set; } = "";
        /// <summary>角色：main / sub / both</summary>
        [DataMember] public string Role { get; set; } = "main";
        public override string ToString() => Name ?? "Unnamed";

        /// <summary>Shown in lists; prepends ★ when this profile is the global default.</summary>
        public string NameWithDefault =>
            AppSettings.DefaultApiProfileId == Id ? $"★  {Name}" : Name;
    }

    [DataContract]
    public class CharacterCard
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; } = "New Character";
        [DataMember] public string Description { get; set; } = "";
        [DataMember] public string Personality { get; set; } = "";
        [DataMember] public string Scenario { get; set; } = "";
        [DataMember] public string FirstMessage { get; set; } = "";
        [DataMember] public string SystemPrompt             { get; set; } = "";
        [DataMember] public string PostHistoryInstructions  { get; set; } = "";
        [DataMember] public string MesExample               { get; set; } = "";
        [DataMember] public string CreatorNotes             { get; set; } = "";
        [DataMember] public string Tags                     { get; set; } = "";
        [DataMember] public string Creator                  { get; set; } = "";
        [DataMember] public string CharacterVersion         { get; set; } = "";
        /// <summary>Base64编码的头像图片（可为null）</summary>
        [DataMember] public string AvatarBase64 { get; set; }
        /// <summary>头像MIME类型，如 image/jpeg</summary>
        [DataMember] public string AvatarMimeType { get; set; }
        /// <summary>立绘图片Base64（用于对话背景等）</summary>
        [DataMember] public string IllustrationBase64 { get; set; }
        [DataMember] public string IllustrationMimeType { get; set; }
        [DataMember] public bool   IsPinned  { get; set; } = false;
        [DataMember] public bool   IsR18G   { get; set; } = false;
        [DataMember] public string GroupName { get; set; } = "";
        public override string ToString() => Name ?? "Unnamed";
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarBase64);
        public bool HasIllustration => !string.IsNullOrEmpty(IllustrationBase64);

        // UI helper — not persisted
        public Windows.UI.Xaml.Visibility PinIconVisibility =>
            IsPinned ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
        public Windows.UI.Xaml.Visibility R18GVisibility =>
            IsR18G ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
    }

    [DataContract]
    public class WorldBookEntry
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; } = "New Entry";
        [DataMember] public string Keywords { get; set; } = "";
        [DataMember] public string Content { get; set; } = "";
        [DataMember] public bool AlwaysActive { get; set; } = false;
        // 旧字段保留，仅用于反序列化迁移
        [DataMember] public string CharacterId { get; set; } = "";
        [DataMember] public List<string> CharacterIds { get; set; } = new List<string>();
        [DataMember] public int Order { get; set; } = 100;
        [DataMember] public bool Disable { get; set; } = false;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (CharacterIds == null) CharacterIds = new List<string>();
            if (!string.IsNullOrEmpty(CharacterId) && !CharacterIds.Contains(CharacterId))
                CharacterIds.Add(CharacterId);
        }

        public override string ToString() => Name ?? "Unnamed";
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
    public class ConversationMessage
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Role { get; set; }
        [DataMember] public string Content { get; set; } = "";
        [DataMember] public string ReasoningContent { get; set; } = "";
        /// <summary>用户附加的图片（Base64），仅user消息有效</summary>
        [DataMember] public string ImageBase64 { get; set; }
        [DataMember] public string ImageMimeType { get; set; }
        [DataMember] public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    [DataContract]
    public class Conversation
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; } = "新对话";
        [DataMember] public string ApiProfileId { get; set; } = "";
        [DataMember] public string CharacterCardId { get; set; } = "";
        [DataMember] public string UserProfileId { get; set; } = "";

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
            AllBranches.RemoveAll(branch => branch == null);
            if (AllBranches.Count == 0)
            {
                AllBranches.Add(new ConversationBranch { Messages = Messages ?? new List<ConversationMessage>() });
                ActiveBranchIndex = 0;
            }
            if (ActiveBranchIndex < 0 || ActiveBranchIndex >= AllBranches.Count)
                ActiveBranchIndex = 0;
            foreach (var branch in AllBranches)
                if (branch.Messages == null) branch.Messages = new List<ConversationMessage>();
            Messages = AllBranches[ActiveBranchIndex].Messages;

            if (BranchPoints == null) BranchPoints = new List<BranchPoint>();
            BranchPoints.RemoveAll(bp => bp == null);
            foreach (var bp in BranchPoints)
            {
                if (bp.Branches == null) bp.Branches = new List<ConversationBranch>();
                bp.Branches.RemoveAll(branch => branch == null);
                foreach (var branch in bp.Branches)
                    if (branch.Messages == null) branch.Messages = new List<ConversationMessage>();
                if (bp.ActiveIndex < 0 || bp.ActiveIndex >= bp.Branches.Count)
                    bp.ActiveIndex = 0;
            }
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
                if (bp.Branches == null) continue;
                if (bp.AnchorIndex < 0 || bp.AnchorIndex >= Messages.Count) continue;
                if (bp.ActiveIndex < 0 || bp.ActiveIndex >= bp.Branches.Count) continue;
                bp.Branches[bp.ActiveIndex] = new ConversationBranch
                {
                    Messages = Messages.Skip(bp.AnchorIndex).Select(m => new ConversationMessage
                    {
                        Id = m.Id, Role = m.Role, Content = m.Content,
                        ReasoningContent = m.ReasoningContent,
                        ImageBase64 = m.ImageBase64,
                        ImageMimeType = m.ImageMimeType,
                        Timestamp = m.Timestamp
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

    [DataContract]
    public class AppData
    {
        [DataMember] public List<ApiProfile> ApiProfiles { get; set; } = new List<ApiProfile>();
        [DataMember] public List<CharacterCard> CharacterCards { get; set; } = new List<CharacterCard>();
        [DataMember] public List<WorldBookEntry> WorldBookEntries { get; set; } = new List<WorldBookEntry>();
        [DataMember] public List<Conversation> Conversations { get; set; } = new List<Conversation>();
        [DataMember] public string SelectedApiProfileId { get; set; }
        [DataMember] public string SelectedCharacterCardId { get; set; }
        [DataMember] public string LastActiveConversationId { get; set; }
        /// <summary>Global default API — used when a conversation has no ApiProfileId set.</summary>
        [DataMember] public string DefaultApiProfileId { get; set; } = "";
        [DataMember] public UserProfile UserProfile { get; set; } = new UserProfile();
        [DataMember] public List<UserProfile> UserProfiles { get; set; } = new List<UserProfile>();
        [DataMember] public string ActiveUserProfileId { get; set; } = "";
        [DataMember] public List<DialoguePool> DialoguePools { get; set; } = new List<DialoguePool>();
    }
}
