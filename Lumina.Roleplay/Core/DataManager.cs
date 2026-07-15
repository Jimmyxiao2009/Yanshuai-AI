using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace yanshuai
{
    public static class DataManager
    {
        private const string FileName = "yanshuaiAppData.json";
        public static AppData Data { get; private set; } = new AppData();

        // Prevents concurrent SaveAsync calls from racing on the same file
        private static readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        // If a save is already in progress, additional callers coalesce into one extra write
        private static volatile bool _pendingSave = false;

        public static async Task LoadAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(FileName);
                using (var stream = await file.OpenStreamForReadAsync())
                {
                    var ser = new DataContractJsonSerializer(typeof(AppData));
                    Data = await Task.Run(() => (AppData)ser.ReadObject(stream)) ?? new AppData();
                }
                // Ensure lists are never null after deserialisation
                if (Data.ApiProfiles == null)      Data.ApiProfiles      = new List<ApiProfile>();
                if (Data.CharacterCards == null)   Data.CharacterCards   = new List<CharacterCard>();
                if (Data.WorldBookEntries == null) Data.WorldBookEntries = new List<WorldBookEntry>();
                if (Data.Conversations == null)    Data.Conversations    = new List<Conversation>();
                if (Data.UserProfile == null)      Data.UserProfile      = new UserProfile();
                // 迁移旧单用户资料到列表
                if (Data.UserProfiles == null) Data.UserProfiles = new List<UserProfile>();
                if (Data.UserProfiles.Count == 0 && !string.IsNullOrEmpty(Data.UserProfile?.Name))
                {
                    Data.UserProfiles.Add(Data.UserProfile);
                    Data.ActiveUserProfileId = Data.UserProfile.Id;
                }
                // Ensure branch state is consistent after deserialisation
                foreach (var conv in Data.Conversations)
                    conv.InitBranches();
                // Ensure dialogue pools are not null
                if (Data.DialoguePools == null)
                    Data.DialoguePools = new List<DialoguePool>();
                // Load dialogue pool cache
                DialoguePoolManager.LoadAll();
                // Inject built-in cards on first run
                BuiltinCards.EnsureLoaded();
                await MemoryStore.LoadAsync();
            }
            catch
            {
                Data = new AppData();
                BuiltinCards.EnsureLoaded();
            }
        }

        public static async Task SaveAsync()
        {
            // If the lock is already held, just mark that another save is needed.
            // The current holder will loop and do one more write after finishing.
            if (!await _saveLock.WaitAsync(0))
            {
                _pendingSave = true;
                return;
            }

            try
            {
                do
                {
                    _pendingSave = false;

                    // JSON serialization is CPU-bound — run on the thread pool so the
                    // UI thread is not blocked while the caller awaits.
                    byte[] bytes = await Task.Run(() =>
                    {
                        if (Data?.Conversations != null)
                            foreach (var conv in Data.Conversations)
                                conv.SyncActiveBranch();

                        // 持久化前补齐缺失的嵌入，保证磁盘数据永远有缓存
                        SealPoolEmbeddings();

                        var ser = new DataContractJsonSerializer(typeof(AppData));
                        using (var ms = new MemoryStream())
                        {
                            ser.WriteObject(ms, Data);
                            return ms.ToArray();
                        }
                    });

                    // Only the actual file I/O stays on the async path.
                    // 原子写入：先写临时文件，完全成功后再重命名覆盖正式文件。
                    // 低内存 WP10 设备上若写入中途被挂起/杀死，旧数据保持完好（与 Lumina.Agent 一致）。
                    var folder = ApplicationData.Current.LocalFolder;
                    var tmp = await folder.CreateFileAsync(FileName + ".tmp", CreationCollisionOption.ReplaceExisting);
                    using (var stream = await tmp.OpenStreamForWriteAsync())
                        await stream.WriteAsync(bytes, 0, bytes.Length);
                    await tmp.RenameAsync(FileName, NameCollisionOption.ReplaceExisting);

                } while (_pendingSave); // flush any coalesced request
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DataManager save error: " + ex.Message);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        // ── 嵌入持久化兜底 ────────────────────────────────────────────
        // 在 SaveAsync 的序列化前调用，确保所有池级记忆的 Embedding
        // 字段非空。这样磁盘上永远有缓存，下次启动免重新编码。

        private static void SealPoolEmbeddings()
        {
            var embedder = AppState.Embedder;
            if (embedder == null) return;

            var pools = Data?.DialoguePools;
            if (pools == null) return;

            foreach (var pool in pools)
            {
                if (pool.SharedMemories == null) continue;
                foreach (var mem in pool.SharedMemories)
                {
                    if (mem.Embedding != null && mem.Embedding.Length > 0)
                        continue; // 已持久化，跳过

                    if (mem.EmbeddingCache != null && mem.EmbeddingCache.Length > 0)
                        mem.Embedding = mem.EmbeddingCache; // 运行时缓存→持久化
                    else if (!string.IsNullOrEmpty(mem.Summary))
                    {
                        try
                        {
                            mem.Embedding = embedder.Encode(mem.Summary);
                            mem.EmbeddingCache = mem.Embedding;
                        }
                        catch { }
                    }
                }
            }
        }

        // ── Profile helpers ───────────────────────────────────────────────────

        public static ApiProfile GetSelectedApiProfile()
        {
            if (string.IsNullOrEmpty(Data.SelectedApiProfileId)) return null;
            return Data.ApiProfiles.Find(p => p.Id == Data.SelectedApiProfileId);
        }

        public static CharacterCard GetSelectedCharacterCard()
        {
            if (string.IsNullOrEmpty(Data.SelectedCharacterCardId)) return null;
            return Data.CharacterCards.Find(c => c.Id == Data.SelectedCharacterCardId);
        }

        // ── UserProfile helpers ───────────────────────

        public static UserProfile GetActiveUserProfile()
        {
            if (Data.UserProfiles != null && Data.UserProfiles.Count > 0)
            {
                if (!string.IsNullOrEmpty(Data.ActiveUserProfileId))
                {
                    var p = Data.UserProfiles.Find(u => u.Id == Data.ActiveUserProfileId);
                    if (p != null) return p;
                }
                return Data.UserProfiles[0];
            }
            return Data.UserProfile ?? new UserProfile();
        }

        // ── Conversation helpers ──────────────────────────────────────────────

        public static Conversation GetOrCreateDefaultConversation()
        {
            // Try to restore the last active conversation
            if (!string.IsNullOrEmpty(Data.LastActiveConversationId))
            {
                var last = Data.Conversations.Find(c => c.Id == Data.LastActiveConversationId);
                if (last != null) return last;
            }
            // Otherwise use the most-recently-updated one, or create a brand new one
            if (Data.Conversations.Count > 0)
                return Data.Conversations.OrderByDescending(c => c.UpdatedAt).First();
            return CreateNewConversation();
        }

        public static Conversation CreateNewConversation()
        {
            var conv = new Conversation
            {
                Title = "新对话",
                ApiProfileId      = Data.SelectedApiProfileId ?? "",
                CharacterCardId   = Data.SelectedCharacterCardId ?? "",
                UserProfileId     = GetActiveUserProfile()?.Id ?? "",
                MemoryEnabled     = true,
                Appearance        = DefaultAppearancePage.BuildDefaultAppearance(),
            };
            Data.Conversations.Add(conv);
            // 自动加入对话池
            DialoguePoolManager.EnsureConversationInPool(conv);
            return conv;
        }

        public static ApiProfile GetProfileForConversation(Conversation conv)
        {
            if (conv != null && !string.IsNullOrEmpty(conv.ApiProfileId))
            {
                var p = Data.ApiProfiles.Find(x => x.Id == conv.ApiProfileId);
                if (p != null) return p;
            }
            // 2. AppSettings global default (set via "设为默认" button)
            var defId = AppSettings.DefaultApiProfileId;
            if (!string.IsNullOrEmpty(defId))
            {
                var d = Data.ApiProfiles.Find(x => x.Id == defId);
                if (d != null) return d;
            }
            // 3. Legacy AppData.DefaultApiProfileId
            if (!string.IsNullOrEmpty(Data.DefaultApiProfileId))
            {
                var d = Data.ApiProfiles.Find(x => x.Id == Data.DefaultApiProfileId);
                if (d != null) return d;
            }
            // 4. Legacy SelectedApiProfileId, then first available
            return GetSelectedApiProfile()
                ?? (Data.ApiProfiles.Count > 0 ? Data.ApiProfiles[0] : null);
        }

        /// <summary>按角色获取 API 配置</summary>
        public static ApiProfile GetProfileForRole(string role)
        {
            // 首先尝试默认配置中匹配角色的
            var defId = AppSettings.DefaultApiProfileId;
            if (!string.IsNullOrEmpty(defId))
            {
                var d = Data.ApiProfiles.Find(x => x.Id == defId && (x.Role == role || x.Role == "both"));
                if (d != null) return d;
            }
            // 回退到任意匹配角色的
            var p = Data.ApiProfiles.Find(x => x.Role == role || x.Role == "both");
            if (p != null) return p;
            // 最后回退第一个可用
            return Data.ApiProfiles.Count > 0 ? Data.ApiProfiles[0] : null;
        }

        public static CharacterCard GetCharacterForConversation(Conversation conv)
        {
            if (conv == null) return null;
            if (string.IsNullOrEmpty(conv.CharacterCardId)) return null;
            return Data.CharacterCards.Find(x => x.Id == conv.CharacterCardId);
        }
    }
}
