// DialoguePool.Models.cs（CharacterProfile/PoolMemoryItem/PoolSettings） — 拆分自 DialoguePool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace yanshuai
{
    [DataContract]
    public class CharacterProfile
    {
        /// <summary>角色对用户的核心印象</summary>
        [DataMember] public List<string> CoreTraits { get; set; } = new List<string>();
        /// <summary>角色记住的用户经历/互动经历</summary>
        [DataMember] public List<string> ExperienceItems { get; set; } = new List<string>();
        /// <summary>角色知晓的用户事实</summary>
        [DataMember] public List<string> KnownFacts { get; set; } = new List<string>();
        /// <summary>用户画像（角色视角下的用户）</summary>
        [DataMember] public string UserPortrait { get; set; } = "";
        /// <summary>累计对话数</summary>
        [DataMember] public int TotalConversations { get; set; } = 0;
        /// <summary>上次总结时间</summary>
        [DataMember] public DateTime LastUpdated { get; set; } = DateTime.Now;
        private int _favorability = 0;
        /// <summary>好感度 (0–100)</summary>
        [DataMember]
        public int Favorability
        {
            get => _favorability;
            set => _favorability = Math.Max(0, Math.Min(100, value));
        }
        /// <summary>好感度趋势 ("up"/"down"/"stable")</summary>
        [DataMember] public string FavorabilityTrend { get; set; } = "stable";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 池记忆项
    // ══════════════════════════════════════════════════════════════════════════

    [DataContract]
    public class PoolMemoryItem
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Summary { get; set; }
        [DataMember] public float[] Embedding { get; set; }
        [DataMember] public string SourceConversationId { get; set; }
        [DataMember] public float Importance { get; set; } = 1.0f;
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;
        [DataMember] public int AccessCount { get; set; } = 0;

        /// <summary>运行时嵌入缓存（反序列化后初次搜索时惰性填充）</summary>
        [IgnoreDataMember] public float[] EmbeddingCache { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 池设置
    // ══════════════════════════════════════════════════════════════════════════

    [DataContract]
    public class PoolSettings
    {
        [DataMember] public bool EnableSharedMemory { get; set; } = true;
        [DataMember] public bool EnableRAG { get; set; } = true;
        [DataMember] public int MaxMemories { get; set; } = 100;
        [DataMember] public int MinMessagesForSummary { get; set; } = 10;
        [DataMember] public int RAGTopK { get; set; } = 5;
        [DataMember] public bool AutoSummarizeConversations { get; set; } = true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 池管理（全局）
}
