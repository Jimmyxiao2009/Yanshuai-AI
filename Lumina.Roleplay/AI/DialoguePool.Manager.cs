// DialoguePool.Manager.cs（DialoguePoolManager） — 拆分自 DialoguePool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════════

    public static class DialoguePoolManager
    {
        /// <summary>角色卡+用户人设 ID → 对话池</summary>
        private static Dictionary<string, DialoguePool> _poolCache = new Dictionary<string, DialoguePool>();
        /// <summary>保护 _poolCache 并发访问（后台总结任务 vs UI 导航）。本类所有方法均为同步，加锁无死锁风险。</summary>
        private static readonly object _poolLock = new object();

        private static string ActiveUserProfileId()
        {
            var up = DataManager.GetActiveUserProfile();
            return up?.Id ?? "";
        }

        private static string PoolKey(string characterCardId, string userProfileId)
            => (characterCardId ?? "") + "|" + (userProfileId ?? "");

        private static void EnsurePoolUser(DialoguePool pool)
        {
            if (pool == null) return;
            if (string.IsNullOrEmpty(pool.UserProfileId))
                pool.UserProfileId = ActiveUserProfileId();
            if (pool.Profile == null)
                pool.Profile = new CharacterProfile();
            if (pool.Profile.CoreTraits != null)
            {
                pool.Profile.CoreTraits.RemoveAll(t =>
                {
                    if (string.IsNullOrWhiteSpace(t)) return true;
                    var trimmed = t.Trim();
                    return trimmed == "名字:" || trimmed == "名字：" ||
                           trimmed == "描述:" || trimmed == "描述：" ||
                           trimmed == "性格:" || trimmed == "性格：";
                });
            }
            var up = DataManager.GetActiveUserProfile();
            if (string.IsNullOrEmpty(pool.Profile.UserPortrait) && pool.UserProfileId == (up?.Id ?? ""))
                pool.Profile.UserPortrait = up?.Description ?? "";
        }

        /// <summary>加载所有池（从 DataManager.Data 反序列化或重建）</summary>
        public static void LoadAll()
        {
            lock (_poolLock)
            {
                _poolCache.Clear();
                if (DataManager.Data == null) return;

                // 从 AppData 加载池列表
                if (DataManager.Data.DialoguePools == null)
                    DataManager.Data.DialoguePools = new List<DialoguePool>();

                foreach (var pool in DataManager.Data.DialoguePools)
                {
                    EnsurePoolUser(pool);
                    pool.RefreshCache();
                    var key = PoolKey(pool.CharacterCardId, pool.UserProfileId);
                    if (!string.IsNullOrEmpty(pool.CharacterCardId) && !_poolCache.ContainsKey(key))
                        _poolCache[key] = pool;
                }

                // 加载完成后预热嵌入缓存（从持久化 Embedding 恢复，无需重新编码）
                var embedder = AppState.Embedder;
                if (embedder != null)
                {
                    foreach (var pool in _poolCache.Values)
                        pool.EnsureEmbeddingCache(embedder);
                }
            }
        }

        /// <summary>获取或创建当前激活人设对应的池。</summary>
        public static DialoguePool GetOrCreatePool(CharacterCard card)
        {
            return GetOrCreatePool(card, ActiveUserProfileId());
        }

        /// <summary>获取或创建指定人设对应的池。</summary>
        public static DialoguePool GetOrCreatePool(CharacterCard card, string userProfileId)
        {
            if (card == null || string.IsNullOrEmpty(card.Id))
                return null;

            userProfileId = userProfileId ?? "";
            var userProfile = DataManager.Data?.UserProfiles?
                .FirstOrDefault(p => p.Id == userProfileId);
            if (userProfile == null && DataManager.Data?.UserProfile?.Id == userProfileId)
                userProfile = DataManager.Data.UserProfile;
            string key = PoolKey(card.Id, userProfileId);
            lock (_poolLock)
            {
                if (_poolCache.TryGetValue(key, out var pool))
                    return pool;

                pool = new DialoguePool
                {
                    CharacterCardId = card.Id,
                    UserProfileId = userProfileId,
                    PoolName = $"{card.Name} / {(string.IsNullOrEmpty(userProfile?.Name) ? "默认人设" : userProfile.Name)}",
                    Profile = new CharacterProfile
                    {
                        UserPortrait = userProfile?.Description ?? ""
                    }
                };

                // 将已有对话归入池
                foreach (var conv in DataManager.Data.Conversations)
                {
                    if (conv.CharacterCardId == card.Id &&
                        (string.IsNullOrEmpty(conv.UserProfileId) || conv.UserProfileId == userProfileId))
                        pool.AddConversation(conv);
                }

                DataManager.Data.DialoguePools.Add(pool);
                _poolCache[key] = pool;

                // 新池预热嵌入缓存
                var embedder = AppState.Embedder;
                if (embedder != null) pool.EnsureEmbeddingCache(embedder);
                return pool;
            }
        }

        /// <summary>获取角色当前激活人设的池。</summary>
        public static DialoguePool GetPool(string characterCardId)
        {
            return GetPool(characterCardId, ActiveUserProfileId());
        }

        /// <summary>获取指定角色+人设的池。</summary>
        public static DialoguePool GetPool(string characterCardId, string userProfileId)
        {
            if (string.IsNullOrEmpty(characterCardId)) return null;
            lock (_poolLock)
            {
                _poolCache.TryGetValue(PoolKey(characterCardId, userProfileId ?? ""), out var pool);
                return pool;
            }
        }

        /// <summary>获取所有池</summary>
        public static List<DialoguePool> GetAllPools()
        {
            lock (_poolLock)
                return _poolCache.Values.Distinct().ToList();
        }

        /// <summary>删除池</summary>
        public static void RemovePool(string characterCardId)
        {
            var pools = DataManager.Data.DialoguePools
                .Where(p => p.CharacterCardId == characterCardId)
                .ToList();
            foreach (var pool in pools)
                DataManager.Data.DialoguePools.Remove(pool);
            lock (_poolLock)
            {
                var keys = _poolCache.Keys.Where(k => k.StartsWith((characterCardId ?? "") + "|")).ToList();
                foreach (var key in keys) _poolCache.Remove(key);
            }
        }

        /// <summary>当对话被分配到角色卡时，自动加入对应池</summary>
        public static void EnsureConversationInPool(Conversation conv)
        {
            if (conv == null || string.IsNullOrEmpty(conv.CharacterCardId)) return;

            var card = DataManager.GetCharacterForConversation(conv);
            if (card == null) return;

            if (string.IsNullOrEmpty(conv.UserProfileId))
                conv.UserProfileId = ActiveUserProfileId();

            var pool = GetOrCreatePool(card, conv.UserProfileId);
            if (pool != null && !pool.ConversationIds.Contains(conv.Id))
            {
                pool.AddConversation(conv);
            }
        }

        /// <summary>删除对话时，从所有对话池移除该对话及其产生的池级 RAG 记忆。</summary>
        public static void RemoveConversationEverywhere(Conversation conv)
        {
            if (conv == null || string.IsNullOrEmpty(conv.Id)) return;

            foreach (var pool in GetAllPools())
            {
                pool.RemoveConversation(conv.Id);
            }

            if (DataManager.Data?.DialoguePools != null)
            {
                foreach (var pool in DataManager.Data.DialoguePools)
                {
                    pool.RemoveConversation(conv.Id);
                }
            }
        }
    }
}
