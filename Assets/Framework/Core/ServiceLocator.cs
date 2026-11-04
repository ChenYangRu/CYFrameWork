using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 轻量服务定位器。框架层服务（网络/资源/UI/热更）都在这里注册，
    /// 业务层只依赖抽象接口，换实现（例如换网络库）不动业务代码。
    /// ★ 热更程序集可以取服务，但不能把自己的服务注册进来（否则热更后引用悬空）。
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>(32);

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException("service");
            _services[typeof(T)] = service;
        }

        public static void Unregister<T>() where T : class
        {
            _services.Remove(typeof(T));
        }

        public static T Get<T>() where T : class
        {
            object svc;
            if (_services.TryGetValue(typeof(T), out svc)) return (T)svc;
            throw new InvalidOperationException("[ServiceLocator] 服务未注册: " + typeof(T).FullName);
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            object svc;
            if (_services.TryGetValue(typeof(T), out svc))
            {
                service = (T)svc;
                return true;
            }
            service = null;
            return false;
        }

        public static bool Has<T>() where T : class
        {
            return _services.ContainsKey(typeof(T));
        }

        public static void ClearAll()
        {
            _services.Clear();
        }
    }
}
