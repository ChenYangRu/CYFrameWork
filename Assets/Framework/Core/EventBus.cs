using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>所有事件的基接口（仅为可读性约束，不做运行时检查）</summary>
    public interface IEvent { }

    /// <summary>
    /// 类型安全的全局事件总线。零反射：Dictionary&lt;Type, Delegate&gt;。
    /// 单个订阅者抛异常不会中断其他订阅者（按 InvocationList 分发）。
    /// 注意：跨程序集使用后必须 Unsubscribe，否则热更重载后旧委托会成为野指针。
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> _map = new Dictionary<Type, Delegate>(64);

        public static void Subscribe<T>(Action<T> callback)
        {
            if (callback == null) return;
            var type = typeof(T);
            Delegate existing;
            if (_map.TryGetValue(type, out existing))
                _map[type] = Delegate.Combine(existing, callback);
            else
                _map[type] = callback;
        }

        public static void Unsubscribe<T>(Action<T> callback)
        {
            if (callback == null) return;
            var type = typeof(T);
            Delegate existing;
            if (!_map.TryGetValue(type, out existing)) return;

            var result = Delegate.Remove(existing, callback);
            if (result == null) _map.Remove(type);
            else _map[type] = result;
        }

        public static void Publish<T>(T evt)
        {
            Delegate existing;
            if (!_map.TryGetValue(typeof(T), out existing) || existing == null) return;

            var list = existing.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T>)list[i]).Invoke(evt);
                }
                catch (Exception e)
                {
                    GameLog.Error("[EventBus] 事件 " + typeof(T).Name + " 处理异常", e);
                }
            }
        }

        public static void ClearAll()
        {
            _map.Clear();
        }

        /// <summary>定向清理：热更程序集卸载 / 场景切换时只清掉指定类型的事件</summary>
        public static void Clear<T>()
        {
            _map.Remove(typeof(T));
        }
    }
}
