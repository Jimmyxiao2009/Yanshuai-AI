using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace yanshuai
{
    /// <summary>子代理会话记录</summary>
    public class SubagentRecord : INotifyPropertyChanged
    {
        private string _status;
        private string _result;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Task { get; set; } = "";
        public string Status { get => _status; set { _status = value; OnProp(); OnProp(nameof(StatusIcon)); OnProp(nameof(StatusColor)); } }
        public string Result { get => _result; set { _result = value; OnProp(); } }
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime FinishedAt { get; set; }

        public string StatusIcon
        {
            get
            {
                if (Status == "running") return "\uE895";
                if (Status == "done") return "\uE73E";
                if (Status == "error") return "\uE783";
                return "\uE8A1";
            }
        }

        public string StatusColor
        {
            get
            {
                if (Status == "running") return "#FFD700";
                if (Status == "done") return "#4CAF50";
                if (Status == "error") return "#F44336";
                return "#888888";
            }
        }

        public string TimeLabel
        {
            get
            {
                var dt = Status == "running" ? DateTime.Now : FinishedAt;
                var span = dt - StartedAt;
                if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
                return $"{(int)span.TotalHours}h{(int)span.Minutes}m";
            }
        }

        public string TaskPreview => (Task ?? "").Length > 60 ? Task.Substring(0, 60) + "…" : (Task ?? "");

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnProp([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>子代理全局跟踪器</summary>
    public static class SubagentTracker
    {
        public static ObservableCollection<SubagentRecord> Records { get; } = new ObservableCollection<SubagentRecord>();
        private static readonly object _lock = new object();

        public static int RunningCount
        {
            get { lock (_lock) return Records.Count(r => r.Status == "running"); }
        }
        public static int TotalCount
        {
            get { lock (_lock) return Records.Count; }
        }

        /// <summary>启动一个子代理（返回其记录对象，供调用方更新）</summary>
        public static SubagentRecord Start(string task)
        {
            var record = new SubagentRecord { Task = task, Status = "running" };
            lock (_lock)
            {
                Records.Add(record);
                // 最多保留 50 条
                while (Records.Count > 50)
                    Records.RemoveAt(0);
            }
            return record;
        }

        /// <summary>标记完成</summary>
        public static void Complete(SubagentRecord record, string result)
        {
            record.Result = result;
            record.Status = "done";
            record.FinishedAt = DateTime.Now;
        }

        /// <summary>标记失败</summary>
        public static void Fail(SubagentRecord record, string error)
        {
            record.Result = error;
            record.Status = "error";
            record.FinishedAt = DateTime.Now;
        }

        public static void Clear()
        {
            lock (_lock) Records.Clear();
        }
    }
}
