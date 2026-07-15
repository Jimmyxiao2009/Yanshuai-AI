using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Windows.Storage;

namespace yanshuai
{
    // 日志条目类型
    public enum ApiLogEntryType { Api, ToolCall }

    [DataContract]
    public class ToolCallLogEntry
    {
        [DataMember] public string Phase     { get; set; }
        [DataMember] public string ToolName  { get; set; }
        [DataMember] public string ArgsJson  { get; set; }
        [DataMember] public string Result    { get; set; }
        [DataMember] public long   ElapsedMs { get; set; }
    }

    [DataContract]
    public class ApiLogEntry
    {
        [DataMember] public DateTime         Timestamp    { get; set; }
        [DataMember] public ApiLogEntryType  EntryType    { get; set; } = ApiLogEntryType.Api;
        [DataMember] public string           Provider     { get; set; }
        [DataMember] public string           Model        { get; set; }
        [DataMember] public string           RequestJson  { get; set; }
        [DataMember] public string           ResponseBody { get; set; }
        [DataMember] public bool             IsError      { get; set; }
        [DataMember] public List<ToolCallLogEntry> ToolCalls { get; set; } = new List<ToolCallLogEntry>();

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");

        public string Title
        {
            get
            {
                if (EntryType == ApiLogEntryType.ToolCall)
                {
                    int error = ToolCalls.Count(t => t.Phase == "error");
                    return string.Format("{0}  工具调用  {1} 个  {2}", TimestampDisplay, ToolCalls.Count, error > 0 ? "❌" : "✅");
                }
                return string.Format("{0}  {1}  {2}", TimestampDisplay, Model ?? Provider, IsError ? "❌" : "✓");
            }
        }

        public string SubTitle
        {
            get
            {
                if (EntryType == ApiLogEntryType.ToolCall)
                    return string.Join("  /  ", ToolCalls.Select(t => t.ToolName));
                return Provider ?? "";
            }
        }

        public string ToolCallsText
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var t in ToolCalls)
                {
                    string icon = t.Phase == "error" ? "❌" : "✅";
                    sb.AppendLine(string.Format("{0} {1}  ({2}ms)", icon, t.ToolName, t.ElapsedMs));
                    if (!string.IsNullOrEmpty(t.ArgsJson))
                        sb.AppendLine("  参数: " + t.ArgsJson);
                    if (!string.IsNullOrEmpty(t.Result))
                    {
                        string r = t.Result.Length > 300 ? t.Result.Substring(0, 300) + "…" : t.Result;
                        sb.AppendLine("  结果: " + r);
                    }
                    sb.AppendLine();
                }
                return sb.ToString().TrimEnd();
            }
        }
    }

    [DataContract]
    public class ApiLogStore
    {
        [DataMember] public List<ApiLogEntry> Entries { get; set; } = new List<ApiLogEntry>();
    }

    public static class ApiLogger
    {
        private const int MaxEntries = 200;
        private const string FileName = "yanshuaiApiLog.json";
        private static readonly List<ApiLogEntry> _entries = new List<ApiLogEntry>();
        private static readonly object _lock = new object();
        private static readonly object _loadLock = new object();   // 仅序列化首次加载，避免与 _lock 嵌套
        private static bool _loaded = false;

        public static IReadOnlyList<ApiLogEntry> Entries
        {
            get
            {
                EnsureLoaded();
                lock (_lock) return _entries.ToArray();
            }
        }

        /// <summary>
        /// 异步加载持久化日志。UI 路径应优先 await 本方法再读取 <see cref="Entries"/>，
        /// 避免在 UI 线程上对 WinRT 异步做 Task.Wait（sync-over-async 死锁/超时丢日志）。
        /// </summary>
        public static async System.Threading.Tasks.Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            string text = null;
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var item = await folder.TryGetItemAsync(FileName);
                if (item is StorageFile file)
                    text = await FileIO.ReadTextAsync(file);
            }
            catch { }
            ApplyLoadedText(text);
        }

        /// <summary>把读到的 JSON 文本并入内存条目并置位 _loaded（双重检查，幂等）。</summary>
        private static void ApplyLoadedText(string text)
        {
            lock (_loadLock)
            {
                if (_loaded) return;   // 双重检查：避免两个线程都进入加载导致条目重复
                try
                {
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        using (var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(text)))
                        {
                            var ser = new DataContractJsonSerializer(typeof(ApiLogStore));
                            var store = (ApiLogStore)ser.ReadObject(ms);
                            if (store?.Entries != null)
                            {
                                lock (_lock)
                                {
                                    // 不 Clear：保留加载期间并发 Log 进来的新条目（在前），
                                    // 把持久化的旧条目接到其后，避免初始化竞态丢失日志。
                                    _entries.AddRange(store.Entries);
                                }
                            }
                        }
                    }
                }
                catch { }
                _loaded = true;   // 仅在整个加载流程结束后置位
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_loadLock)
            {
                if (_loaded) return;   // 双重检查：避免两个线程都进入加载导致条目重复
                try
                {
                    var folder = ApplicationData.Current.LocalFolder;
                    // 同步读取（仅在首次访问时执行一次）
                    var task = folder.GetFileAsync(FileName).AsTask();
                    task.Wait(500);
                    if (task.IsCompleted && task.Result != null)
                    {
                        var readTask = FileIO.ReadTextAsync(task.Result).AsTask();
                        readTask.Wait(500);
                        if (readTask.IsCompleted && !string.IsNullOrWhiteSpace(readTask.Result))
                        {
                            using (var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(readTask.Result)))
                            {
                                var ser = new DataContractJsonSerializer(typeof(ApiLogStore));
                                var store = (ApiLogStore)ser.ReadObject(ms);
                                if (store?.Entries != null)
                                {
                                    lock (_lock)
                                    {
                                        // 不 Clear：保留加载期间并发 Log 进来的新条目（在前），
                                        // 把持久化的旧条目接到其后，避免初始化竞态丢失日志。
                                        _entries.AddRange(store.Entries);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
                _loaded = true;   // 仅在整个加载流程结束后置位
            }
        }

        private static void SaveAsync()
        {
            // fire-and-forget
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    ApiLogStore store;
                    lock (_lock) { store = new ApiLogStore { Entries = new List<ApiLogEntry>(_entries) }; }
                    using (var ms = new System.IO.MemoryStream())
                    {
                        var ser = new DataContractJsonSerializer(typeof(ApiLogStore));
                        ser.WriteObject(ms, store);
                        string json = Encoding.UTF8.GetString(ms.ToArray());
                        var folder = ApplicationData.Current.LocalFolder;
                        var file = await folder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(file, json);
                    }
                }
                catch { }
            });
        }

        public static void Log(string provider, string model, string requestJson, string responseBody, bool isError = false)
        {
            _ = EnsureLoadedAsync();   // 不阻塞调用线程；旧条目加载完成后会接到新条目之后
            var entry = new ApiLogEntry
            {
                Timestamp    = DateTime.Now,
                EntryType    = ApiLogEntryType.Api,
                Provider     = provider,
                Model        = model,
                RequestJson  = requestJson,
                ResponseBody = responseBody,
                IsError      = isError,
            };
            AddEntry(entry);
        }

        public static void LogToolCalls(string model, List<ToolCallLogEntry> calls)
        {
            if (calls == null || calls.Count == 0) return;
            _ = EnsureLoadedAsync();   // 不阻塞调用线程
            bool hasError = calls.Any(c => c.Phase == "error");
            var entry = new ApiLogEntry
            {
                Timestamp = DateTime.Now,
                EntryType = ApiLogEntryType.ToolCall,
                Model     = model,
                IsError   = hasError,
                ToolCalls = new List<ToolCallLogEntry>(calls),
            };
            AddEntry(entry);
        }

        private static void AddEntry(ApiLogEntry entry)
        {
            lock (_lock)
            {
                _entries.Insert(0, entry);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveAt(_entries.Count - 1);
            }
            SaveAsync();
        }

        public static void Clear()
        {
            lock (_lock) { _entries.Clear(); }
            SaveAsync();
        }
    }
}
