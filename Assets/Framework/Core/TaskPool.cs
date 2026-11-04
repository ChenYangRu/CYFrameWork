using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;   // Coroutine：GameLoop.Run 的返回类型

namespace GameFramework
{
    /// <summary>
    /// 并发任务池：把一堆 IEnumerator 按"同时最多 N 个"跑完。
    ///
    /// 为什么需要（我们是 YooAsset，不是 GameFramework 的自研资源层）：
    ///   YooAsset 只限制 **bundle 下载**并发，不限制 **asset 加载**并发。
    ///   一次预热 200 个特效贴图=同时发起 200 个 AssetHandle，主线程会卡在
    ///   "同一帧创建几百个异步操作"上，表现为进战斗前一顿掉帧。
    ///
    /// 与 GameFramework 的 TaskPool 差异（不照抄的原因）：
    ///   · 它是 `ITaskAgent` + 优先级队列 + 等待/暂停状态机，为"资源+实体+UI"全套服务；
    ///   · 我们只需要"限流跑协程"，用 GameLoop 当宿主，20 行调度逻辑就够，
    ///     不需要引入 agent 生命周期、优先级、暂停恢复那一整套。
    ///
    /// 用法：
    ///   var tasks = locations.Select(loc => res.LoadAssetPersistent<Texture2D>(loc, ...)).ToArray();
    ///   TaskPool.Run(tasks, 8, (ok, err) => Debug.Log("预热完成"));
    /// </summary>
    public static class TaskPool
    {
        /// <summary>任务池默认并发（移动端经验值：4~8，太高会抢主线程）</summary>
        public const int DefaultConcurrency = 6;

        /// <summary>批量执行。onDone 在所有任务结束后回调（含异常中断）</summary>
        public static CoroutineHandle Run(IList<IEnumerator> tasks, int concurrency,
            Action<bool, string> onDone = null, Action<float> onProgress = null)
        {
            if (tasks == null || tasks.Count == 0)
            {
                if (onDone != null) onDone(true, null);
                return null;
            }
            if (concurrency <= 0) concurrency = DefaultConcurrency;

            var loop = GameLoop.Ensure();
            var runner = new Runner(tasks, concurrency, onDone, onProgress);
            runner.Coroutine = loop.Run(runner.Schedule());
            return new CoroutineHandle(runner.Coroutine);
        }

        /// <summary>把单个协程包一层异常隔离（一个任务炸了不拖垮整批，但会计入错误）</summary>
        public static IEnumerator Guard(IEnumerator inner, Action<Exception> onError = null)
        {
            if (inner == null) yield break;
            while (true)
            {
                object current;
                bool moved;
                try
                {
                    moved = inner.MoveNext();
                    current = moved ? inner.Current : null;
                }
                catch (Exception e)
                {
                    GameLog.Error("[TaskPool] 任务异常", e);
                    if (onError != null) onError(e);
                    yield break;
                }

                if (!moved) yield break;
                yield return current;
            }
        }

        /// <summary>可取消的句柄（只做"停止调度"，已在跑的任务让它自然结束）</summary>
        public class CoroutineHandle
        {
            private readonly Coroutine _coroutine;
            public bool Cancelled { get; private set; }

            internal CoroutineHandle(Coroutine c) { _coroutine = c; }

            public void Cancel()
            {
                Cancelled = true;
                if (_coroutine != null && GameLoop.Exists)
                    GameLoop.Instance.StopRun(_coroutine);
            }
        }

        // ---------------- 内部调度 ----------------

        private class Runner
        {
            private readonly IList<IEnumerator> _tasks;
            private readonly int _concurrency;
            private readonly Action<bool, string> _onDone;
            private readonly Action<float> _onProgress;

            private int _next;
            private int _running;
            private int _finished;
            private bool _failed;
            private string _firstError;

            public Coroutine Coroutine;

            public Runner(IList<IEnumerator> tasks, int concurrency, Action<bool, string> onDone, Action<float> onProgress)
            {
                _tasks = tasks;
                _concurrency = concurrency;
                _onDone = onDone;
                _onProgress = onProgress;
            }

            public IEnumerator Schedule()
            {
                var loop = GameLoop.Instance;

                while (_finished < _tasks.Count)
                {
                    // 补满并发槽位
                    while (_running < _concurrency && _next < _tasks.Count)
                    {
                        var task = _tasks[_next++];
                        _running++;
                        loop.Run(Execute(task));
                    }

                    if (_onProgress != null && _tasks.Count > 0)
                        _onProgress((float)_finished / _tasks.Count);

                    yield return null;
                }

                if (_onProgress != null) _onProgress(1f);
                if (_onDone != null) _onDone(!_failed, _firstError);
            }

            private IEnumerator Execute(IEnumerator task)
            {
                while (true)
                {
                    object current;
                    bool moved;
                    try
                    {
                        moved = task != null && task.MoveNext();
                        current = moved ? task.Current : null;
                    }
                    catch (Exception e)
                    {
                        _failed = true;
                        if (_firstError == null) _firstError = e.Message;
                        GameLog.Error("[TaskPool] 任务异常", e);
                        break;
                    }

                    if (!moved) break;
                    yield return current;
                }

                _running--;
                _finished++;
            }
        }
    }
}
