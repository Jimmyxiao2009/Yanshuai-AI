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
                    Data = (AppData)ser.ReadObject(stream) ?? new AppData();
                }
                // Ensure lists are never null after deserialisation
                if (Data.ApiProfiles == null)   Data.ApiProfiles   = new List<ApiProfile>();
                if (Data.Conversations == null) Data.Conversations = new List<Conversation>();
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
            }
            catch
            {
                Data = new AppData();
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

                        var ser = new DataContractJsonSerializer(typeof(AppData));
                        using (var ms = new MemoryStream())
                        {
                            ser.WriteObject(ms, Data);
                            return ms.ToArray();
                        }
                    });

                    // Only the actual file I/O stays on the async path
                    var folder = ApplicationData.Current.LocalFolder;
                    var file = await folder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
                    using (var stream = await file.OpenStreamForWriteAsync())
                        await stream.WriteAsync(bytes, 0, bytes.Length);

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

        // ── Profile helpers ───────────────────────────────────────────────────

        public static ApiProfile GetSelectedApiProfile()
        {
            if (string.IsNullOrEmpty(Data.SelectedApiProfileId)) return null;
            return Data.ApiProfiles.Find(p => p.Id == Data.SelectedApiProfileId);
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
                ApiProfileId = Data.SelectedApiProfileId ?? "",
                Appearance   = DefaultAppearancePage.BuildDefaultAppearance(),
            };
            Data.Conversations.Add(conv);
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

    }
}
