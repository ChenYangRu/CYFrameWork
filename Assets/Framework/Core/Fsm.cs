using System;
using System.Collections.Generic;

namespace GameFramework
{
    public interface IFsmState
    {
        void OnEnter(object context);
        void OnUpdate(float deltaTime);
        void OnExit();
    }

    /// <summary>
    /// 有限状态机。启动流程、登录流程、战斗流程都用它，避免一堆 bool 互相打架。
    /// 用法：Add(状态) → ChangeState&lt;T&gt;()；状态内部再 ChangeState 是安全的（延迟到下一帧生效）。
    /// </summary>
    public class Fsm
    {
        private readonly Dictionary<Type, IFsmState> _states = new Dictionary<Type, IFsmState>();
        private IFsmState _current;
        private Type _currentType;
        private Type _pendingType;
        private bool _changing;

        public Type CurrentType { get { return _currentType; } }
        public IFsmState Current { get { return _current; } }

        public void Add<T>(T state) where T : IFsmState
        {
            if (state == null) throw new ArgumentNullException("state");
            _states[typeof(T)] = state;
        }

        public T GetState<T>() where T : class, IFsmState
        {
            IFsmState s;
            if (_states.TryGetValue(typeof(T), out s)) return s as T;
            return null;
        }

        public void ChangeState<T>(object context = null) where T : IFsmState
        {
            // 状态机内部再次切换时，缓存到 pending，由 Tick 统一处理，避免重入
            if (_changing)
            {
                _pendingType = typeof(T);
                return;
            }
            ChangeState(typeof(T), context);
        }

        private void ChangeState(Type type, object context)
        {
            IFsmState next;
            if (!_states.TryGetValue(type, out next))
            {
                GameLog.Error("[Fsm] 未注册的状态: " + type.Name);
                return;
            }

            _changing = true;
            try
            {
                if (_current != null)
                {
                    try { _current.OnExit(); }
                    catch (Exception e) { GameLog.Error("[Fsm] " + _currentType.Name + ".OnExit 异常", e); }
                }

                _current = next;
                _currentType = type;

                try { _current.OnEnter(context); }
                catch (Exception e) { GameLog.Error("[Fsm] " + type.Name + ".OnEnter 异常", e); }
            }
            finally
            {
                _changing = false;
            }
        }

        public void Tick(float deltaTime)
        {
            if (_current != null)
            {
                try { _current.OnUpdate(deltaTime); }
                catch (Exception e) { GameLog.Error("[Fsm] " + _currentType.Name + ".OnUpdate 异常", e); }
            }

            if (_pendingType != null)
            {
                var next = _pendingType;
                _pendingType = null;
                ChangeState(next, null);
            }
        }

        public void Stop()
        {
            if (_current != null)
            {
                try { _current.OnExit(); } catch (Exception e) { GameLog.Error("[Fsm] Exit 异常", e); }
            }
            _current = null;
            _currentType = null;
            _pendingType = null;
        }
    }

    /// <summary>委托型状态，省事写法：new DelegateState(onEnter, onUpdate, onExit)</summary>
    public class DelegateState : IFsmState
    {
        private readonly Action<object> _onEnter;
        private readonly Action<float> _onUpdate;
        private readonly Action _onExit;

        public DelegateState(Action<object> onEnter = null, Action<float> onUpdate = null, Action onExit = null)
        {
            _onEnter = onEnter;
            _onUpdate = onUpdate;
            _onExit = onExit;
        }

        public void OnEnter(object context) { if (_onEnter != null) _onEnter(context); }
        public void OnUpdate(float deltaTime) { if (_onUpdate != null) _onUpdate(deltaTime); }
        public void OnExit() { if (_onExit != null) _onExit(); }
    }
}
