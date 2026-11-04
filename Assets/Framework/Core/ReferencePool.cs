using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 可复用对象接口：实现 <see cref="Clear"/> 把自身状态复位，才能安全地放回池里。
    /// </summary>
    public interface IReference
    {
        void Clear();
    }

    /// <summary>
    /// 引用池：**普通 C# 对象**的对象池（区别于 GameObjectPool 只管 GameObject）。
    ///
    /// 适用场景（本项目里的真实热点）：
    ///   · 网络层每帧解包产生的 byte[] / 包体对象
    ///   · UI 列表项的数据包装类
    ///   · 战斗里每次伤害结算的临时结构体包装
    ///
    /// 设计来源：EllanJiang/GameFramework 的 ReferencePool，按类型分池 + 严格检查模式。
    /// 相比"自己 new"，收益是减少 GC 峰值；相比"复用同一个对象"，收益是不会串状态
    /// （每个池按类型隔离，`Clear()` 强制复位）。
    ///
    /// ★ 用法约束：
    ///   1. 类型必须有公共无参构造；
    ///   2. `Release` 之后**不能再持有该引用**，否则会拿到被下一个人改过的对象；
    ///   3. 只放"纯数据/可复位"的对象，别放持有 MonoBehaviour 或 Unity 句柄的。
    /// </summary>
    public static class ReferencePool
    {
        /// <summary>严格检查：开池/取对象时做类型与状态校验，仅建议在开发期开（有性能开销）</summary>
        public static bool EnableStrictCheck = false;

        private sealed class ReferenceCollection
        {
            private readonly Queue<IReference> _references;
            private readonly Type _referenceType;

            public int UsingReferenceCount;
            public int AcquireReferenceCount;
            public int ReleaseReferenceCount;
            public int AddReferenceCount;
            public int RemoveReferenceCount;

            public ReferenceCollection(Type referenceType)
            {
                _references = new Queue<IReference>(16);
                _referenceType = referenceType;
                UsingReferenceCount = 0;
                AcquireReferenceCount = 0;
                ReleaseReferenceCount = 0;
                AddReferenceCount = 0;
                RemoveReferenceCount = 0;
            }

            public Type ReferenceType { get { return _referenceType; } }
            public int UnusedReferenceCount { get { return _references.Count; } }

            public T Acquire<T>() where T : class, IReference, new()
            {
                if (typeof(T) != _referenceType)
                    throw new InvalidOperationException("引用池类型不匹配: " + typeof(T).FullName + " vs " + _referenceType.FullName);

                UsingReferenceCount++;
                AcquireReferenceCount++;

                if (_references.Count > 0)
                    return (T)_references.Dequeue();

                AddReferenceCount++;
                return new T();
            }

            public IReference Acquire()
            {
                UsingReferenceCount++;
                AcquireReferenceCount++;

                if (_references.Count > 0)
                    return _references.Dequeue();

                AddReferenceCount++;
                return (IReference)Activator.CreateInstance(_referenceType);
            }

            public void Release(IReference reference)
            {
                if (reference == null) throw new ArgumentNullException("reference");
                if (EnableStrictCheck && reference.GetType() != _referenceType)
                    throw new InvalidOperationException("释放的引用类型与池不匹配: " + reference.GetType().FullName);

                // ★ 必须清零：不清等于把状态泄漏给下一个使用者（最难排查的一类 bug）
                reference.Clear();

                if (EnableStrictCheck && _references.Contains(reference))
                    throw new InvalidOperationException("重复释放同一个引用: " + _referenceType.FullName);

                _references.Enqueue(reference);
                ReleaseReferenceCount++;
                UsingReferenceCount--;
            }

            public void Add(int count)
            {
                if (count <= 0) throw new ArgumentOutOfRangeException("count");
                for (int i = 0; i < count; i++)
                {
                    AddReferenceCount++;
                    _references.Enqueue((IReference)Activator.CreateInstance(_referenceType));
                }
            }

            public void Remove(int count)
            {
                if (count <= 0) throw new ArgumentOutOfRangeException("count");
                if (count > _references.Count) count = _references.Count;

                for (int i = 0; i < count; i++)
                {
                    _references.Dequeue();
                    RemoveReferenceCount++;
                }
            }

            public void RemoveAll()
            {
                RemoveReferenceCount += _references.Count;
                _references.Clear();
            }
        }

        private static readonly Dictionary<Type, ReferenceCollection> _collections =
            new Dictionary<Type, ReferenceCollection>(32);

        public static int Count { get { return _collections.Count; } }

        private static ReferenceCollection GetCollection(Type type)
        {
            ReferenceCollection col;
            if (_collections.TryGetValue(type, out col)) return col;

            if (EnableStrictCheck && type.IsAbstract)
                throw new InvalidOperationException("引用池类型不能是抽象类: " + type.FullName);

            col = new ReferenceCollection(type);
            _collections.Add(type, col);
            return col;
        }

        /// <summary>取一个对象（池空则新建）</summary>
        public static T Acquire<T>() where T : class, IReference, new()
        {
            return GetCollection(typeof(T)).Acquire<T>();
        }

        /// <summary>取一个对象（非泛型版，配合反射/基类引用场景）</summary>
        public static IReference Acquire(Type type)
        {
            if (type == null) throw new ArgumentNullException("type");
            return GetCollection(type).Acquire();
        }

        /// <summary>归还对象（内部会先调用 Clear()）</summary>
        public static void Release(IReference reference)
        {
            if (reference == null) throw new ArgumentNullException("reference");

            Type type = reference.GetType();
            ReferenceCollection col;
            if (!_collections.TryGetValue(type, out col))
                throw new InvalidOperationException("引用未从池中获取，无法归还: " + type.FullName);

            col.Release(reference);
        }

        /// <summary>预热：提前创建 count 个，避免运行时首次分配的尖峰</summary>
        public static void Add<T>(int count) where T : class, IReference, new()
        {
            GetCollection(typeof(T)).Add(count);
        }

        /// <summary>丢弃 count 个空闲对象（内存吃紧时收缩池）</summary>
        public static void Remove<T>(int count) where T : class, IReference, new()
        {
            GetCollection(typeof(T)).Remove(count);
        }

        /// <summary>清空某类型的全部空闲对象</summary>
        public static void RemoveAll<T>() where T : class, IReference, new()
        {
            GetCollection(typeof(T)).RemoveAll();
        }

        /// <summary>清空所有池（切场景 / 内存告警时用）</summary>
        public static void ClearAll()
        {
            foreach (var pair in _collections) pair.Value.RemoveAll();
            _collections.Clear();
        }
    }
}
