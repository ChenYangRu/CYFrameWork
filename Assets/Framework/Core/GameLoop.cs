using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 框架主循环。唯一常驻的 MonoBehaviour（DontDestroyOnLoad），承担三件事：
    /// ① 主线程调度（网络线程不能碰 Unity API，必须 Post 回来）
    /// ② 协程宿主（框架层不依赖具体场景）
    /// ③ 全局 Tick 广播（定时器 / 状态机 / 下载进度）
    /// </summary>
    public class GameLoop : MonoBehaviour
    {
        private static GameLoop _instance;

        public static GameLoop Instance { get { return _instance; } }
        public static bool Exists { get { return _instance != null; } }

        /// <summary>每帧回调（参数为 Time.deltaTime）</summary>
        public event Action<float> OnTick;

        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>(64);
        private readonly object _queueLock = new object();

        public static GameLoop Ensure()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("[GameLoop]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameLoop>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            Application.targetFrameRate = 60;
        }

        private void Update()
        {
            FlushMainThreadQueue();

            var tick = OnTick;
            if (tick != null) tick(Time.deltaTime);
        }

        /// <summary>线程安全：任意线程投递，下一帧主线程执行</summary>
        public void Post(Action action)
        {
            if (action == null) return;
            lock (_queueLock) _mainThreadQueue.Enqueue(action);
        }

        private void FlushMainThreadQueue()
        {
            // 双检 + 上限保护：避免单帧无限执行导致卡死
            int budget = 256;
            while (budget-- > 0)
            {
                Action action = null;
                lock (_queueLock)
                {
                    if (_mainThreadQueue.Count == 0) break;
                    action = _mainThreadQueue.Dequeue();
                }

                if (action == null) continue;
                try { action(); }
                catch (Exception e) { GameLog.Error("[GameLoop] 主线程任务异常", e); }
            }
        }

        public Coroutine Run(IEnumerator routine)
        {
            return StartCoroutine(routine);
        }

        public void StopRun(Coroutine routine)
        {
            if (routine != null) StopCoroutine(routine);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
