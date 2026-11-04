using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 定时器服务（挂在 GameLoop.OnTick 上，不用协程，零 GC 友好）。
    /// 支持：延迟一次 / 重复 / 取消。业务层常用（技能 CD、心跳、UI 动画节流）。
    /// </summary>
    public class TimerService
    {
        private class Timer
        {
            public int Id;
            public float Remain;
            public float Interval;
            public bool Repeat;
            public Action Callback;
            public bool Alive;
        }

        private readonly List<Timer> _timers = new List<Timer>(32);
        private readonly Stack<Timer> _pool = new Stack<Timer>(16);
        private int _nextId = 1;
        private bool _hooked;

        public void Start()
        {
            if (_hooked) return;
            if (!GameLoop.Exists) GameLoop.Ensure();
            GameLoop.Instance.OnTick += Tick;
            _hooked = true;
        }

        public void Stop()
        {
            if (!_hooked) return;
            if (GameLoop.Exists) GameLoop.Instance.OnTick -= Tick;
            _hooked = false;
            _timers.Clear();
        }

        /// <summary>延迟一次执行，返回 id（可用于 Cancel）</summary>
        public int Delay(float seconds, Action callback)
        {
            return Add(seconds, 0f, false, callback);
        }

        /// <summary>重复执行（首次延迟 = interval）</summary>
        public int Repeat(float interval, Action callback)
        {
            return Add(interval, interval, true, callback);
        }

        private int Add(float firstDelay, float interval, bool repeat, Action callback)
        {
            if (callback == null) return 0;
            var t = _pool.Count > 0 ? _pool.Pop() : new Timer();
            t.Id = _nextId++;
            t.Remain = firstDelay;
            t.Interval = interval;
            t.Repeat = repeat;
            t.Callback = callback;
            t.Alive = true;
            _timers.Add(t);
            return t.Id;
        }

        public void Cancel(int id)
        {
            if (id <= 0) return;
            for (int i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Id == id) { _timers[i].Alive = false; return; }
            }
        }

        private void Tick(float dt)
        {
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                var t = _timers[i];
                if (!t.Alive)
                {
                    _timers.RemoveAt(i);
                    _pool.Push(t);
                    continue;
                }

                t.Remain -= dt;
                if (t.Remain > 0f) continue;

                // 先回收再回调：回调里 Cancel 自己是合法的
                if (t.Repeat)
                {
                    t.Remain += t.Interval;
                    if (t.Remain <= 0f) t.Remain = t.Interval;
                    try { t.Callback(); } catch (Exception e) { GameLog.Error("[Timer] 回调异常", e); }
                }
                else
                {
                    t.Alive = false;
                    _timers.RemoveAt(i);
                    _pool.Push(t);
                    try { t.Callback(); } catch (Exception e) { GameLog.Error("[Timer] 回调异常", e); }
                }
            }
        }
    }
}
