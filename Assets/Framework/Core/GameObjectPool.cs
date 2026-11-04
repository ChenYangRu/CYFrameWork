using System.Collections.Generic;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 通用对象池（GameObject 版）。资源层实例化 + 池化回收一条链，
    /// 子弹/飘字/列表项这类高频对象一律走这里，避免反复 Instantiate 造成卡顿。
    /// </summary>
    public class GameObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly Stack<GameObject> _idle = new Stack<GameObject>(16);
        private readonly List<GameObject> _used = new List<GameObject>(32);
        private readonly int _maxIdle;

        public GameObjectPool(GameObject prefab, Transform parent, int maxIdle = 64)
        {
            _prefab = prefab;
            _parent = parent;
            _maxIdle = maxIdle;
        }

        public GameObject Get()
        {
            GameObject go = null;
            while (_idle.Count > 0 && go == null)
            {
                go = _idle.Pop();
            }

            if (go == null)
            {
                go = Object.Instantiate(_prefab, _parent);
                go.name = _prefab.name + "(Pooled)";
            }

            go.SetActive(true);
            _used.Add(go);
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null) return;
            _used.Remove(go);
            go.SetActive(false);
            go.transform.SetParent(_parent, false);

            if (_idle.Count < _maxIdle) _idle.Push(go);
            else Object.Destroy(go);
        }

        public void ReleaseAll()
        {
            for (int i = _used.Count - 1; i >= 0; i--) Release(_used[i]);
            _used.Clear();
        }

        public void Destroy()
        {
            while (_idle.Count > 0)
            {
                var go = _idle.Pop();
                if (go != null) Object.Destroy(go);
            }
            ReleaseAll();
        }
    }
}
