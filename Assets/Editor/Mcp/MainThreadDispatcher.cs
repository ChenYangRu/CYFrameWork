using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace GameFramework.EditorTools.Mcp
{
    /// <summary>
    /// 后台线程 → Unity 主线程的单向投递队列。
    /// Unity API 只能在主线程调用，Bridge 的网络线程收到请求后必须经此投递。
    /// </summary>
    public static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static int _mainThreadId;

        static MainThreadDispatcher()
        {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>当前线程是否 Unity 主线程。已在主线程时不要再 Post + Wait，否则会自己等死。</summary>
        public static bool IsMainThread
        {
            get { return System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId; }
        }

        /// <summary>从任意线程投递。action 会在下一次 EditorApplication.update 执行。</summary>
        public static void Post(Action action)
        {
            if (action == null) return;
            Queue.Enqueue(action);
        }

        /// <summary>由 McpBridge 挂在 EditorApplication.update 上，外部不要调用。</summary>
        public static void Pump()
        {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            while (Queue.Count > 0)
            {
                Action action;
                if (!Queue.TryDequeue(out action)) break;
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError("[MCP] 主线程任务异常: " + e.Message);
                }
            }
        }

        /// <summary>当前积压数量，用于健康检查和诊断。</summary>
        public static int PendingCount
        {
            get { return Queue.Count; }
        }
    }
}
