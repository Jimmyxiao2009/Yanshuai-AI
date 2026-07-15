using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

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
        /// <summary>模型上下文窗口 token 数，0=自动检测</summary>
        [DataMember] public int    MaxContextTokens { get; set; } = 0;
        /// <summary>嵌入模型名称，空=自动推断</summary>
        [DataMember] public string EmbeddingModel   { get; set; } = "";
        /// <summary>该供应商可用的模型列表（缓存）</summary>
        [DataMember] public List<string> AvailableModels { get; set; }
        /// <summary>上次获取模型列表的时间</summary>
        [DataMember] public DateTime ModelsLastFetched { get; set; } = new DateTime(2000, 1, 1);
        public override string ToString() => Name ?? "Unnamed";

        /// <summary>Shown in lists; prepends ★ when this profile is the global default.</summary>
        public string NameWithDefault =>
            AppSettings.DefaultApiProfileId == Id ? $"★  {Name}" : Name;

        /// <summary>预设 API 配置列表，供用户快速选择</summary>
        public static List<ApiProfile> GetPresets()
        {
            return new List<ApiProfile>
            {
                // ── 免费 / 低价 ────────────────────────────────────────────
                new ApiProfile { Name = "DeepSeek V4 Pro", Url = "https://api.deepseek.com/v1/chat/completions", Model = "deepseek-v4-pro", ProviderType = "openai", MaxContextTokens = 131072 },
                new ApiProfile { Name = "DeepSeek V4 Flash", Url = "https://api.deepseek.com/v1/chat/completions", Model = "deepseek-v4-flash", ProviderType = "openai", MaxContextTokens = 131072 },
                new ApiProfile { Name = "DeepSeek R1 (推理)", Url = "https://api.deepseek.com/v1/chat/completions", Model = "deepseek-reasoner", ProviderType = "openai", MaxContextTokens = 65536 },
                new ApiProfile { Name = "Groq (Llama 4 Scout)", Url = "https://api.groq.com/openai/v1/chat/completions", Model = "meta-llama/llama-4-scout-17b-16e-instruct", ProviderType = "openai", MaxContextTokens = 131072 },
                new ApiProfile { Name = "Groq (Qwen3 32B)", Url = "https://api.groq.com/openai/v1/chat/completions", Model = "qwen/qwen3-32b", ProviderType = "openai", MaxContextTokens = 32768 },
                new ApiProfile { Name = "智谱 GLM-4 Flash", Url = "https://open.bigmodel.cn/api/paas/v4/chat/completions", Model = "glm-4-flash", ProviderType = "openai", MaxContextTokens = 128000 },
                new ApiProfile { Name = "SiliconFlow (DeepSeek V3)", Url = "https://api.siliconflow.cn/v1/chat/completions", Model = "deepseek-ai/DeepSeek-V3", ProviderType = "openai", MaxContextTokens = 65536 },
                // ── 中端 ──────────────────────────────────────────────────
                new ApiProfile { Name = "Moonshot Kimi K2", Url = "https://api.moonshot.cn/v1/chat/completions", Model = "kimi-k2", ProviderType = "openai", MaxContextTokens = 131072 },
                new ApiProfile { Name = "Mistral Large", Url = "https://api.mistral.ai/v1/chat/completions", Model = "mistral-large-latest", ProviderType = "openai", MaxContextTokens = 128000 },
                new ApiProfile { Name = "Mistral Small", Url = "https://api.mistral.ai/v1/chat/completions", Model = "mistral-small-latest", ProviderType = "openai", MaxContextTokens = 32000 },
                new ApiProfile { Name = "Cohere Command A", Url = "https://api.cohere.com/v2/chat", Model = "command-a-03-2025", ProviderType = "openai", MaxContextTokens = 256000 },
                new ApiProfile { Name = "Together AI (Llama 4)", Url = "https://api.together.xyz/v1/chat/completions", Model = "meta-llama/Llama-4-Maverick-17B-128E-Instruct-FP8", ProviderType = "openai", MaxContextTokens = 65536 },
                // ── 高端 ──────────────────────────────────────────────────
                new ApiProfile { Name = "OpenRouter", Url = "https://openrouter.ai/api/v1/chat/completions", Model = "anthropic/claude-sonnet-4", ProviderType = "openai", MaxContextTokens = 200000 },
                new ApiProfile { Name = "Google Gemini 2.5 Flash", Url = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", Model = "gemini-2.5-flash", ProviderType = "openai", VisionEnabled = true, MaxContextTokens = 1048576 },
                new ApiProfile { Name = "Google Gemini 2.5 Pro", Url = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", Model = "gemini-2.5-pro", ProviderType = "openai", VisionEnabled = true, MaxContextTokens = 1048576 },
                new ApiProfile { Name = "Claude Sonnet 4", Url = "https://api.anthropic.com/v1/messages", Model = "claude-sonnet-4-6", ProviderType = "claude", VisionEnabled = true, MaxContextTokens = 200000 },
                new ApiProfile { Name = "Claude Haiku 4.5", Url = "https://api.anthropic.com/v1/messages", Model = "claude-haiku-4-5-20251001", ProviderType = "claude", VisionEnabled = true, MaxContextTokens = 200000 },
                new ApiProfile { Name = "GPT-4.1", Url = "https://api.openai.com/v1/chat/completions", Model = "gpt-4.1", ProviderType = "openai", VisionEnabled = true, MaxContextTokens = 1047576 },
                new ApiProfile { Name = "GPT-4.1 Mini", Url = "https://api.openai.com/v1/chat/completions", Model = "gpt-4.1-mini", ProviderType = "openai", VisionEnabled = true, MaxContextTokens = 1047576 },
                new ApiProfile { Name = "x.AI Grok", Url = "https://api.x.ai/v1/chat/completions", Model = "grok-3-mini-fast", ProviderType = "openai", MaxContextTokens = 131072 },
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

    /// <summary>思维链中的一步：一段推理文本或一次工具调用。按发生顺序持久化，
    /// 用于在切换/重开对话后还原 AI 的“思考过程 + 工具调用”展示。</summary>
    [DataContract]
    public class ThinkStep
    {
        /// <summary>"reasoning"（推理段）或 "tool"（工具调用）</summary>
        [DataMember] public string Kind { get; set; } = "reasoning";
        /// <summary>推理文本（Kind=reasoning）</summary>
        [DataMember] public string Text { get; set; } = "";
        /// <summary>工具名（Kind=tool）</summary>
        [DataMember] public string ToolName { get; set; } = "";
        /// <summary>工具调用详情（Kind=tool）</summary>
        [DataMember] public string Detail { get; set; } = "";
        /// <summary>工具状态图标 ⏳/✅/❌（Kind=tool）</summary>
        [DataMember] public string Icon { get; set; } = "";
        /// <summary>工具调用参数（Kind=tool）</summary>
        [DataMember] public string Args { get; set; } = "";
        /// <summary>工具执行结果（Kind=tool）</summary>
        [DataMember] public string Result { get; set; } = "";
    }

    public class ConversationMessage
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Role { get; set; }
        [DataMember] public string Content { get; set; } = "";
        [DataMember] public string ReasoningContent { get; set; } = "";
        /// <summary>完整思维链步骤（推理段 + 工具调用，按顺序）。切换对话后据此还原思考/工具展示。
        /// 为 null 时回退到仅用 ReasoningContent 还原（兼容旧数据）。</summary>
        [DataMember] public List<ThinkStep> ThinkSteps { get; set; }
        /// <summary>用户附加的图片列表（每项 Base64），仅user消息有效。
        /// 注意：图片现已外置到 ImageStore，此字段仅用于旧数据迁移/临时；
        /// 持久化的真正来源是 ImageRefs，正常情况下这些 base64 字段在迁移后为 null。</summary>
        [DataMember] public List<string> ImagesBase64 { get; set; }
        [DataMember] public List<string> ImagesMimeType { get; set; }
        /// <summary>外置图片文件引用 id 列表（对应 ImageStore 中的文件），与 ImagesMimeType 对齐。</summary>
        [DataMember] public List<string> ImageRefs { get; set; }
        // 兼容旧数据
        [DataMember] public string ImageBase64 { get; set; }
        [DataMember] public string ImageMimeType { get; set; }
        /// <summary>附加的文本文件名（显示用，不含内容）</summary>
        [DataMember] public List<string> AttachedFileNames { get; set; }
        /// <summary>PLAA 潜状态 S_t / e_mem 的 JSON 序列化（评估用）</summary>
        [DataMember] public string LatentStateJson { get; set; } = "";
        [DataMember] public DateTime Timestamp { get; set; } = DateTime.Now;
        /// <summary>该消息消耗的输入 token 数（API 上报）</summary>
        [DataMember] public int TokensInput { get; set; } = 0;
        /// <summary>该消息消耗的输出 token 数（API 上报）</summary>
        [DataMember] public int TokensOutput { get; set; } = 0;
        /// <summary>提示命中缓存的 token 数（OpenAI prompt_tokens_details.cached_tokens / Anthropic cache_read_input_tokens）</summary>
        [DataMember] public int CachedTokens { get; set; } = 0;

        public ConversationMessage Clone()
        {
            return new ConversationMessage
            {
                Id = Id,
                Role = Role,
                Content = Content,
                ReasoningContent = ReasoningContent,
                ThinkSteps = ThinkSteps != null ? ThinkSteps.Select(s => new ThinkStep
                {
                    Kind = s.Kind,
                    Text = s.Text,
                    ToolName = s.ToolName,
                    Detail = s.Detail,
                    Icon = s.Icon,
                    Args = s.Args,
                    Result = s.Result
                }).ToList() : null,
                ImagesBase64 = ImagesBase64 != null ? new List<string>(ImagesBase64) : null,
                ImagesMimeType = ImagesMimeType != null ? new List<string>(ImagesMimeType) : null,
                ImageRefs = ImageRefs != null ? new List<string>(ImageRefs) : null,
                ImageBase64 = ImageBase64,
                ImageMimeType = ImageMimeType,
                AttachedFileNames = AttachedFileNames != null ? new List<string>(AttachedFileNames) : null,
                LatentStateJson = LatentStateJson,
                Timestamp = Timestamp,
                TokensInput = TokensInput,
                TokensOutput = TokensOutput,
                CachedTokens = CachedTokens
            };
        }

        /// <summary>此消息是否带图片（看引用或内联 base64，均不读盘）。</summary>
        public bool HasImages =>
            (ImageRefs != null && ImageRefs.Count > 0) ||
            (ImagesBase64 != null && ImagesBase64.Count > 0) ||
            !string.IsNullOrEmpty(ImageBase64);

        /// <summary>获取所有内联图片（仅旧数据/临时；不读外置文件）。</summary>
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

        /// <summary>获取所有图片：优先从外置 ImageStore 按引用读取，回退到旧内联 base64。
        /// 返回的 base64 仅在调用方（构建请求/显示）期间存在，用完即释放，不常驻内存。</summary>
        public async Task<List<ImageEntry>> GetAllImagesAsync()
        {
            if (ImageRefs != null && ImageRefs.Count > 0)
            {
                var result = new List<ImageEntry>(ImageRefs.Count);
                for (int i = 0; i < ImageRefs.Count; i++)
                {
                    string b64 = await ImageStore.LoadBase64Async(ImageRefs[i]);
                    if (!string.IsNullOrEmpty(b64))
                        result.Add(new ImageEntry
                        {
                            Base64 = b64,
                            Mime = (ImagesMimeType != null && i < ImagesMimeType.Count) ? ImagesMimeType[i] : "image/jpeg"
                        });
                }
                if (result.Count > 0) return result;
            }
            // 回退：旧数据仍内联的 base64
            return GetAllImages();
        }

        /// <summary>把内联 base64 迁移到外置 ImageStore（一次性）。迁移后清空内联 base64 字段。
        /// 返回 true 表示发生了迁移（调用方据此决定是否需要保存）。</summary>
        public async Task<bool> MigrateImagesToStoreAsync()
        {
            // 已有外置引用则视为已迁移
            if (ImageRefs != null && ImageRefs.Count > 0) return false;

            var inline = GetAllImages();
            if (inline.Count == 0) return false;

            var refs  = new List<string>(inline.Count);
            var mimes = new List<string>(inline.Count);
            foreach (var img in inline)
            {
                string id = await ImageStore.SaveBase64Async(img.Base64);
                if (id == null) return false; // 写盘失败：保持原样，下次再试
                refs.Add(id);
                mimes.Add(img.Mime ?? "image/jpeg");
            }
            ImageRefs      = refs;
            ImagesMimeType = mimes;
            // 清空内联 base64，使其不再随 AppData 持久化/常驻
            ImagesBase64 = null;
            ImageBase64  = null;
            return true;
        }
    }

    [DataContract]
    public class Conversation
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; } = "新对话";
        [DataMember] public string ApiProfileId { get; set; } = "";
        /// <summary>所属项目ID，空=无项目</summary>
        [DataMember] public string ProjectId { get; set; } = "";

        /// <summary>
        /// Live message list — always points to AllBranches[ActiveBranchIndex].Messages.
        /// Kept as [DataMember] for back-compat; on load, InitBranches() syncs it.
        /// </summary>
        [DataMember] public List<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();

        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        [DataMember] public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // ── Context compression ──────────────────────────────────────────
        /// <summary>压缩后的上下文摘要文本</summary>
        [DataMember] public string ContextSummary { get; set; } = "";
        /// <summary>已摘要到的消息索引（之前的消息已被压缩为 ContextSummary，API 调用时跳过）</summary>
        [DataMember] public int SummarizedUpTo { get; set; } = 0;
        /// <summary>本对话累计消耗的 token 数</summary>
        [DataMember] public int TotalTokensUsed { get; set; } = 0;
        /// <summary>最近一次自动 compact 的时间（MinValue = 从未 compact）</summary>
        [DataMember] public long LastCompactAtTicks { get; set; } = 0;

        /// <summary>DateTime 访问器（规避 DataContractJsonSerializer 对 DateTime.MinValue 的 UTC 越界 bug）</summary>
        [IgnoreDataMember]
        public DateTime LastCompactAt
        {
            get => LastCompactAtTicks > 0 ? new DateTime(LastCompactAtTicks, DateTimeKind.Local) : DateTime.MinValue;
            set => LastCompactAtTicks = (value > new DateTime(2000, 1, 1)) ? value.Ticks : 0;
        }

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
                    Messages = Messages.Skip(bp.AnchorIndex).Select(m => m.Clone()).ToList()
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


        // 缓存为静态实例，避免会话列表里每条对话渲染预览时都重新编译这两个模式串
        private static readonly System.Text.RegularExpressions.Regex _reEmphasis =
            new System.Text.RegularExpressions.Regex(@"[*_]{1,2}(.+?)[*_]{1,2}");
        private static readonly System.Text.RegularExpressions.Regex _reInlineCode =
            new System.Text.RegularExpressions.Regex(@"`(.+?)`");

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
                line = _reEmphasis.Replace(line, "$1");
                line = _reInlineCode.Replace(line, "$1");
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

        public string TypeLabel => Type == "bing" ? "Bing" : Type == "ddg" ? "DuckDuckGo" : Type == "tavily" ? "Tavily" : "SearXNG";
    }

    /// <summary>MCP 服务器配置（Model Context Protocol）</summary>
    [DataContract]
    public class McpServer
    {
        [DataMember] public string Id          { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name        { get; set; } = "新 MCP 服务器";
        [DataMember] public string Description { get; set; } = "";
        /// <summary>http / stdio / websocket</summary>
        [DataMember] public string TransportType { get; set; } = "http";
        /// <summary>HTTP/WebSocket: URL；Stdio: 命令</summary>
        [DataMember] public string Endpoint    { get; set; } = "";
        /// <summary>命令行参数(Stdio)或自定义请求头(JSON KV)</summary>
        [DataMember] public string Args        { get; set; } = "";
        /// <summary>认证 token（可选）</summary>
        [DataMember] public string AuthToken   { get; set; } = "";
        [DataMember] public bool   Enabled     { get; set; } = true;
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        public override string ToString() => Name;
    }

    /// <summary>Skill: 用户可触发的预定义提示模板/动作</summary>
    [DataContract]
    public class Skill
    {
        [DataMember] public string Id          { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name        { get; set; } = "新技能";
        [DataMember] public string Icon        { get; set; } = "✨";
        [DataMember] public string Description { get; set; } = "";
        /// <summary>触发关键词，逗号分隔</summary>
        [DataMember] public string Triggers    { get; set; } = "";
        /// <summary>技能对应的提示词模板（注入到 user message 前或作为 system 前缀）</summary>
        [DataMember] public string PromptTemplate { get; set; } = "";
        /// <summary>system | prefix | replace（默认 prefix：附加到用户消息开头）</summary>
        [DataMember] public string InjectMode  { get; set; } = "prefix";
        [DataMember] public bool   Enabled     { get; set; } = true;
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        public override string ToString() => Name;
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
        // ── 项目 ────────────────────────────────────────────────────────
        [DataMember] public List<Project> Projects { get; set; } = new List<Project>();
        [DataMember] public string ActiveProjectId { get; set; } = "";
        // ── 全局记忆 ────────────────────────────────────────────────────
        [DataMember] public List<MemoryItem> GlobalMemories { get; set; } = new List<MemoryItem>();
        // ── PLAA 评估 ────────────────────────────────────────────────────────
        [DataMember] public List<EvaluationResult> EvaluationResults { get; set; } = new List<EvaluationResult>();
        [DataMember] public List<EvaluationExperiment> EvaluationExperiments { get; set; } = new List<EvaluationExperiment>();
        // ── MCP 服务器 & Skills ─────────────────────────────────────────────
        [DataMember] public List<McpServer> McpServers { get; set; } = new List<McpServer>();
        [DataMember] public List<Skill>     Skills     { get; set; } = new List<Skill>();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PLAA 评估模型
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>一条评估结果</summary>
    [DataContract]
    public class EvaluationResult
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string ExperimentId { get; set; }
        [DataMember] public string ExperimentName { get; set; }
        [DataMember] public string BaselineModel { get; set; } = "";
        [DataMember] public string PlaaModel { get; set; } = "";
        [DataMember] public DateTime Timestamp { get; set; } = DateTime.Now;
        /// <summary>完整对话记录（含 LatentState）</summary>
        [DataMember] public List<ConversationMessage> BaselineMessages { get; set; } = new List<ConversationMessage>();
        [DataMember] public List<ConversationMessage> PlaaMessages { get; set; } = new List<ConversationMessage>();
        /// <summary>评估指标得分</summary>
        [DataMember] public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();
        /// <summary>降维后的 S_t 轨迹（PCA 2D）</summary>
        [DataMember] public List<LatentPoint> BaselineTrajectory { get; set; } = new List<LatentPoint>();
        [DataMember] public List<LatentPoint> PlaaTrajectory { get; set; } = new List<LatentPoint>();
        /// <summary>实验配置快照</summary>
        [DataMember] public string ConfigSnapshot { get; set; } = "";
    }

    /// <summary>S_t 降维后的 2D 点</summary>
    [DataContract]
    public class LatentPoint
    {
        [DataMember] public int Turn { get; set; }
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
        [DataMember] public string Label { get; set; } = "";
    }

    /// <summary>实验定义</summary>
    [DataContract]
    public class EvaluationExperiment
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; }
        [DataMember] public string Description { get; set; }
        /// <summary>实验类型：persona | emotion | identity | inertia | probe</summary>
        [DataMember] public string ExperimentType { get; set; }
        /// <summary>对话轮数</summary>
        [DataMember] public int Turns { get; set; } = 40;
        /// <summary>是否启用情绪事件注入</summary>
        [DataMember] public bool InjectEmotionalEvent { get; set; } = false;
        /// <summary>初始角色卡 JSON（CharacterCard 序列化）</summary>
        [DataMember] public string CharacterCardJson { get; set; } = "";
        /// <summary>对话脚本（预定义用户消息列表）</summary>
        [DataMember] public List<string> ScriptMessages { get; set; } = new List<string>();
        /// <summary>关键验证消息（用于 Identity Retention 验证）</summary>
        [DataMember] public string ValidationQuestion { get; set; } = "";
        [DataMember] public string ExpectedKeyword { get; set; } = "";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 项目
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>项目：对话分组 + 知识库 + 指令 + 记忆</summary>
    [DataContract]
    public class Project
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; } = "新项目";
        [DataMember] public string Description { get; set; } = "";
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        [DataMember] public DateTime UpdatedAt { get; set; } = DateTime.Now;
        /// <summary>自定义图标（Segoe MDL2 Assets 字符，如 \uE8B7 文件夹）</summary>
        [DataMember] public string IconGlyph { get; set; } = "\uE8B7";
        /// <summary>项目级 system prompt（所有该项目下的对话都会注入）</summary>
        [DataMember] public string SystemPrompt { get; set; } = "";
        /// <summary>知识库文件列表（每项 "filename\x01content"）</summary>
        [DataMember] public List<string> KnowledgeFiles { get; set; } = new List<string>();
        /// <summary>关联的对话ID列表</summary>
        [DataMember] public List<string> ConversationIds { get; set; } = new List<string>();
        /// <summary>项目级记忆</summary>
        [DataMember] public List<MemoryItem> ProjectMemories { get; set; } = new List<MemoryItem>();
        /// <summary>项目绑定的 API profile ID，空=使用全局默认</summary>
        [DataMember] public string ApiProfileId { get; set; } = "";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 上下文压缩：已知模型上下文长度
    // ══════════════════════════════════════════════════════════════════════════

    public static class ModelContextLimits
    {
        private static readonly Dictionary<string, int> _known = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // DeepSeek
            ["deepseek-v4-pro"] = 131072,
            ["deepseek-v4-flash"] = 131072,
            ["deepseek-reasoner"] = 65536,
            ["deepseek-chat"] = 65536,
            // Llama
            ["meta-llama/llama-4-scout-17b-16e-instruct"] = 131072,
            ["meta-llama/Llama-4-Maverick-17B-128E-Instruct-FP8"] = 65536,
            // Qwen
            ["qwen/qwen3-32b"] = 32768,
            // Claude
            ["claude-sonnet-4-6"] = 200000,
            ["claude-haiku-4-5-20251001"] = 200000,
            // GPT
            ["gpt-4.1"] = 1047576,
            ["gpt-4.1-mini"] = 1047576,
            ["gpt-4o"] = 128000,
            ["gpt-4o-mini"] = 128000,
            // Gemini
            ["gemini-2.5-flash"] = 1048576,
            ["gemini-2.5-pro"] = 1048576,
            // Mistral
            ["mistral-large-latest"] = 128000,
            ["mistral-small-latest"] = 32000,
            // Others
            ["kimi-k2"] = 131072,
            ["glm-4-flash"] = 128000,
            ["grok-3-mini-fast"] = 131072,
            ["command-a-03-2025"] = 256000,
        };

        /// <summary>获取模型上下文窗口 token 数。未知模型返回 defaultValue。</summary>
        public static int GetLimit(string modelName, int defaultValue = 32000)
        {
            if (string.IsNullOrEmpty(modelName)) return defaultValue;
            int val;
            if (_known.TryGetValue(modelName, out val)) return val;
            // 模糊匹配：模型名包含已知key
            foreach (var kv in _known)
            {
                if (modelName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            }
            return defaultValue;
        }

        /// <summary>获取 ApiProfile 的有效上下文限制</summary>
        public static int GetEffectiveLimit(ApiProfile profile)
        {
            if (profile == null) return 32000;
            if (profile.MaxContextTokens > 0) return profile.MaxContextTokens;
            return GetLimit(profile.Model);
        }
    }
}
