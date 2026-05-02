using System;
using System.Collections.Generic;

namespace yanshuai
{
    public class ApiLogEntry
    {
        public DateTime Timestamp  { get; set; }
        public string   Provider   { get; set; }   // "claude" / "openai" / ...
        public string   Model      { get; set; }
        public string   RequestJson  { get; set; }
        public string   ResponseBody { get; set; }
        public bool     IsError    { get; set; }

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");
        public string Title => $"{TimestampDisplay}  {Model ?? Provider}  {(IsError ? "❌" : "✓")}";
    }

    public static class ApiLogger
    {
        private const int MaxEntries = 30;
        private static readonly List<ApiLogEntry> _entries = new List<ApiLogEntry>();
        private static readonly object _lock = new object();

        public static IReadOnlyList<ApiLogEntry> Entries
        {
            get { lock (_lock) return _entries.ToArray(); }
        }

        public static void Log(string provider, string model, string requestJson, string responseBody, bool isError = false)
        {
            var entry = new ApiLogEntry
            {
                Timestamp    = DateTime.Now,
                Provider     = provider,
                Model        = model,
                RequestJson  = requestJson,
                ResponseBody = responseBody,
                IsError      = isError,
            };
            lock (_lock)
            {
                _entries.Insert(0, entry); // newest first
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
