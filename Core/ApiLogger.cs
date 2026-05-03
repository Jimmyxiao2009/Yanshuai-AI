using System;
using System.Collections.Generic;

namespace yanshuai
{
    // 日志条目类型
    public enum ApiLogEntryType { Api, ToolCall }

    public class ToolCallLogEntry
    {
        public string Phase      { get; set; }  // "start" / "done" / "error"
        public string ToolName   { get; set; }
        public string ArgsJson   { get; set; }
        public string Result     { get; set; }
        public long   ElapsedMs  { get; set; }
    }

    public class ApiLogEntry
    {
        public DateTime          Timestamp    { get; set; }
        public ApiLogEntryType   EntryType    { get; set; } = ApiLogEntryType.Api;
        public string            Provider     { get; set; }
        public string            Model        { get; set; }
        public string            RequestJson  { get; set; }
        public string            ResponseBody { get; set; }
        public bool              IsError      { get; set; }

        // 工具调用日志（EntryType == ToolCall 时使用）
        public List<ToolCallLogEntry> ToolCalls { get; set; } = new List<ToolCallLogEntry>();

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");

        public string Title
        {
            get
            {
                if (EntryType == ApiLogEntryType.ToolCall)
                {
                    int done  = 0;
                    int error = 0;
                    foreach (var t in ToolCalls)
                    {
                        if (t.Phase == "done")  done++;
                        if (t.Phase == "error") error++;
                    }
                    string status = error > 0 ? "❌" : "✅";
                    return string.Format("{0}  工具调用  {1} 个  {2}", TimestampDisplay, ToolCalls.Count, status);
                }
                return string.Format("{0}  {1}  {2}", TimestampDisplay, Model ?? Provider, IsError ? "❌" : "✓");
            }
        }

        public string SubTitle
        {
            get
            {
                if (EntryType == ApiLogEntryType.ToolCall)
                {
                    var names = new List<string>();
                    foreach (var t in ToolCalls) names.Add(t.ToolName);
                    return string.Join("  /  ", names);
                }
                return Provider ?? "";
            }
        }

        // 合并所有工具调用信息为可显示的文本（供详情面板使用）
        public string ToolCallsText
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in ToolCalls)
                {
                    string icon = t.Phase == "error" ? "❌" : "✅";
                    sb.AppendLine(string.Format("{0} {1}  ({2}ms)", icon, t.ToolName, t.ElapsedMs));
                    if (!string.IsNullOrEmpty(t.ArgsJson))
                    {
                        sb.AppendLine("  参数: " + t.ArgsJson);
                    }
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

    public static class ApiLogger
    {
        private const int MaxEntries = 60;
        private static readonly List<ApiLogEntry> _entries = new List<ApiLogEntry>();
        private static readonly object _lock = new object();

        public static IReadOnlyList<ApiLogEntry> Entries
        {
            get { lock (_lock) return _entries.ToArray(); }
        }

        // 普通 API 请求日志
        public static void Log(string provider, string model, string requestJson, string responseBody, bool isError = false)
        {
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

        // 工具调用批次日志（一次 turn 的所有工具）
        public static void LogToolCalls(string model, List<ToolCallLogEntry> calls)
        {
            if (calls == null || calls.Count == 0) return;
            bool hasError = false;
            foreach (var c in calls) if (c.Phase == "error") hasError = true;
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
        }

        public static void Clear()
        {
            lock (_lock) { _entries.Clear(); }
        }
    }
}
