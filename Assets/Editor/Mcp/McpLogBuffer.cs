using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 控制台日志环形缓冲。
    /// 用 Application.logMessageReceived 收集，避免反射 UnityEditor 内部 LogEntries（版本间易失效）。
    /// </summary>
    public static class McpLogBuffer
    {
        private const int Capacity = 500;

        private sealed class Entry
        {
            public double Time;
            public string Level;
            public string Message;
            public string StackTrace;
        }

        private static readonly List<Entry> Entries = new List<Entry>(Capacity);
        private static readonly object Gate = new object();
        private static bool _attached;

        /// <summary>域重载后由 Bridge 调用；重复调用安全。</summary>
        public static void Attach()
        {
            if (_attached) return;
            Application.logMessageReceived -= OnLogReceived;
            Application.logMessageReceived += OnLogReceived;
            _attached = true;
        }

        public static void Detach()
        {
            if (!_attached) return;
            Application.logMessageReceived -= OnLogReceived;
            _attached = false;
        }

        public static void Clear()
        {
            lock (Gate) Entries.Clear();
        }

        /// <summary>
        /// 取最近若干条日志。level 支持 error/warning/log/exception（空表示全部）；contains 为消息子串过滤。
        /// </summary>
        public static List<object> Snapshot(int count, string level, string contains)
        {
            if (count <= 0) count = 50;
            if (count > Capacity) count = Capacity;

            var result = new List<object>();
            lock (Gate)
            {
                for (int i = Entries.Count - 1; i >= 0 && result.Count < count; i--)
                {
                    Entry e = Entries[i];
                    if (!string.IsNullOrEmpty(level) && !string.Equals(e.Level, level, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(contains) && e.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    result.Add(new Dictionary<string, object>
                    {
                        { "time", e.Time.ToString("F2") },
                        { "level", e.Level },
                        { "message", e.Message },
                        { "stackTrace", e.StackTrace ?? string.Empty },
                    });
                }
            }
            result.Reverse();
            return result;
        }

        private static void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            var entry = new Entry
            {
                Time = Time.realtimeSinceStartup,
                Level = Normalize(type),
                Message = condition ?? string.Empty,
                StackTrace = (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) ? stackTrace : null,
            };

            lock (Gate)
            {
                Entries.Add(entry);
                if (Entries.Count > Capacity) Entries.RemoveRange(0, Entries.Count - Capacity);
            }
        }

        private static string Normalize(LogType type)
        {
            switch (type)
            {
                case LogType.Error: return "error";
                case LogType.Assert: return "error";
                case LogType.Exception: return "exception";
                case LogType.Warning: return "warning";
                default: return "log";
            }
        }
    }
}
