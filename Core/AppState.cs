using System.Collections.Generic;
using System.Threading.Tasks;

namespace yanshuai
{
    /// <summary>
    /// In-memory app-level state that survives page navigation.
    /// </summary>
    public static class AppState
    {
        public static Conversation ActiveConversation { get; set; }
        public static bool IsFirstLaunch { get; set; } = true;

        // ── Background send tasks（支持多对话并行生成）────────────────────────
        private static readonly Dictionary<string, BackgroundTask> _tasks
            = new Dictionary<string, BackgroundTask>();

        public class BackgroundTask
        {
            public Task   Task      { get; set; }
            public string Content   { get; set; } = "";
            public string Reasoning { get; set; } = "";
        }

        public static void RegisterTask(string convId, Task task)
        {
            lock (_tasks)
                _tasks[convId] = new BackgroundTask { Task = task };
        }

        public static void UpdateContent(string convId, string content, string reasoning)
        {
            lock (_tasks)
            {
                if (_tasks.TryGetValue(convId, out var bt))
                {
                    if (content  != null) bt.Content   = content;
                    if (reasoning != null) bt.Reasoning = reasoning;
                }
            }
        }

        public static BackgroundTask GetTask(string convId)
        {
            lock (_tasks)
                return _tasks.TryGetValue(convId, out var bt) ? bt : null;
        }

        public static bool IsRunning(string convId)
        {
            lock (_tasks)
                return _tasks.TryGetValue(convId, out var bt) && bt.Task != null && !bt.Task.IsCompleted;
        }

        public static void CompleteTask(string convId)
        {
            lock (_tasks)
                _tasks.Remove(convId);
        }

        // ── Legacy single-task shim（向后兼容，实际指向第一个任务）───────────
        public static string BackgroundConvId    { get; set; }
        public static bool IsSendingInBackground => BackgroundConvId != null && IsRunning(BackgroundConvId);
        public static string BackgroundContent   => GetTask(BackgroundConvId)?.Content  ?? "";
        public static string BackgroundReasoning => GetTask(BackgroundConvId)?.Reasoning ?? "";
        public static void ClearBackground() { CompleteTask(BackgroundConvId); BackgroundConvId = null; }
        public static Task BackgroundSendTask { set { if (BackgroundConvId != null) RegisterTask(BackgroundConvId, value); } }
    }
}
