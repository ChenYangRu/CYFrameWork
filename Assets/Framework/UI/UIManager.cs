using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameFramework.UI
{
    /// <summary>
    /// UI 管理器：分层 Canvas + 面板栈 + 遮罩 + 异步加载。
    /// 设计要点：
    ///  · **注册式**而不是反射扫程序集——热更程序集里的面板由热更代码自己注册，AOT 层不需要认识它们；
    ///  · Normal 层互斥（换主界面自动关旧的），Popup/Top 压栈（Esc / 返回键关最上层）；
    ///  · 面板与 prefab 解耦：Register 时给 location 就走资源，不给就代码搭。
    /// </summary>
    public class UIManager
    {
        private class WindowDef
        {
            public string Name;
            public UILayer Layer;
            public string PrefabLocation;
            public bool FullScreen;
            public Type WindowType;
        }

        private readonly Dictionary<string, WindowDef> _defs = new Dictionary<string, WindowDef>(32);
        private readonly Dictionary<string, UIWindow> _opened = new Dictionary<string, UIWindow>(16);
        private readonly List<string> _stack = new List<string>(16);

        private readonly Transform[] _layerRoots = new Transform[5];
        private readonly GameObject[] _masks = new GameObject[5];

        private ResourceService _res;
        private Canvas _canvas;
        private bool _inited;
        private bool _ticking;

        public Canvas Canvas { get { return _canvas; } }
        public int OpenedCount { get { return _opened.Count; } }

        // ---------------- 初始化 ----------------

        public void Init(ResourceService resource)
        {
            if (_inited) return;
            _res = resource;

            var rootGo = new GameObject("[UIRoot]");
            UnityEngine.Object.DontDestroyOnLoad(rootGo);

            _canvas = rootGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = rootGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            rootGo.AddComponent<GraphicRaycaster>();

            // 层级容器（顺序即渲染顺序）
            string[] names = { "Layer_Scene", "Layer_Normal", "Layer_Popup", "Layer_Top", "Layer_Tips" };
            for (int i = 0; i < names.Length; i++)
            {
                var rt = UIUtil.CreateRect(names[i], rootGo.transform);
                UIUtil.Stretch(rt);
                _layerRoots[i] = rt;
            }

            EnsureEventSystem();

            if (!_ticking && GameLoop.Exists)
            {
                GameLoop.Instance.OnTick += Tick;
                _ticking = true;
            }

            _inited = true;
            GameLog.Info("[UI] 初始化完成");
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("[EventSystem]");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // ---------------- 注册 ----------------

        /// <summary>注册面板；prefabLocation 为空则走代码构建（子类实现 BuildUI）</summary>
        public void Register<T>(string name, UILayer layer, string prefabLocation = null, bool fullScreen = false)
            where T : UIWindow
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("面板名不能为空");
            _defs[name] = new WindowDef
            {
                Name = name,
                Layer = layer,
                PrefabLocation = prefabLocation,
                FullScreen = fullScreen,
                WindowType = typeof(T),
            };
        }

        public bool IsRegistered(string name) { return _defs.ContainsKey(name); }
        public bool IsOpen(string name) { return _opened.ContainsKey(name) && _opened[name].IsOpen; }

        public UIWindow GetWindow(string name)
        {
            UIWindow w;
            return _opened.TryGetValue(name, out w) ? w : null;
        }

        public T GetWindow<T>(string name) where T : UIWindow
        {
            return GetWindow(name) as T;
        }

        // ---------------- 打开 / 关闭 ----------------

        public void Open(string name, object args = null, Action<UIWindow> onOpened = null)
        {
            if (!_inited)
            {
                GameLog.Error("[UI] 未初始化，无法打开: " + name);
                return;
            }

            WindowDef def;
            if (!_defs.TryGetValue(name, out def))
            {
                GameLog.Error("[UI] 面板未注册: " + name);
                return;
            }

            UIWindow existing;
            if (_opened.TryGetValue(name, out existing) && existing != null)
            {
                existing.Args = args;
                existing.InternalOpen();
                BringToFront(name);
                if (onOpened != null) onOpened(existing);
                return;
            }

            // Normal 层互斥
            if (def.Layer == UILayer.Normal) CloseLayer(UILayer.Normal);

            if (!string.IsNullOrEmpty(def.PrefabLocation) && _res != null)
            {
                GameLoop.Instance.Run(OpenFromPrefab(def, args, onOpened));
            }
            else
            {
                CreateByCode(def, args, onOpened);
            }
        }

        private void CreateByCode(WindowDef def, object args, Action<UIWindow> onOpened)
        {
            var go = new GameObject("UI_" + def.Name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_layerRoots[(int)def.Layer], false);
            UIUtil.Stretch(rt);

            var window = go.AddComponent(def.WindowType) as UIWindow;
            FinishOpen(window, def, args, onOpened);
        }

        private IEnumerator OpenFromPrefab(WindowDef def, object args, Action<UIWindow> onOpened)
        {
            GameObject instance = null;
            yield return _res.Instantiate(def.PrefabLocation, _layerRoots[(int)def.Layer], delegate (GameObject go)
            {
                instance = go;
            },
            delegate (string err)
            {
                GameLog.Error("[UI] prefab 加载失败，回退代码构建: " + err);
            });

            UIWindow window = null;
            if (instance != null)
            {
                instance.name = "UI_" + def.Name;
                window = instance.GetComponent(def.WindowType) as UIWindow;
                if (window == null)
                {
                    GameLog.Warn("[UI] prefab 上缺少 " + def.WindowType.Name + " 组件，已自动补挂");
                    window = instance.AddComponent(def.WindowType) as UIWindow;
                }
            }
            else
            {
                // 资源缺失时用代码构建兜底，保证流程不中断
                var go = new GameObject("UI_" + def.Name);
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(_layerRoots[(int)def.Layer], false);
                UIUtil.Stretch(rt);
                window = go.AddComponent(def.WindowType) as UIWindow;
            }

            FinishOpen(window, def, args, onOpened);
        }

        private void FinishOpen(UIWindow window, WindowDef def, object args, Action<UIWindow> onOpened)
        {
            window.WindowName = def.Name;
            window.Layer = def.Layer;
            window.FullScreen = def.FullScreen;
            window.Manager = this;
            window.Args = args;

            if (def.FullScreen) CreateMask(def);

            // ★ 遮罩是"插在同层窗口之上"的，而窗口 GameObject 先于遮罩创建，
            //   必须把窗口重新提到最后，否则遮罩会挡住自己的面板（点不动）。
            window.transform.SetAsLastSibling();

            _opened[def.Name] = window;
            _stack.Remove(def.Name);
            _stack.Add(def.Name);

            window.InternalOpen();
            GameLog.Info("[UI] 打开 " + def.Name);
            if (onOpened != null) onOpened(window);
        }

        public void Close(string name)
        {
            UIWindow window;
            if (!_opened.TryGetValue(name, out window)) return;

            _opened.Remove(name);
            _stack.Remove(name);
            DestroyMask(name);

            if (window != null) window.InternalDestroy();
            GameLog.Info("[UI] 关闭 " + name);
        }

        /// <summary>关闭最上层（Esc / 安卓返回键）</summary>
        public bool CloseTop()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                string name = _stack[i];
                UIWindow w;
                if (_opened.TryGetValue(name, out w) && w != null)
                {
                    var def = _defs[name];
                    if (def.Layer == UILayer.Normal || def.Layer == UILayer.Scene) continue;
                    Close(name);
                    return true;
                }
            }
            return false;
        }

        public void CloseLayer(UILayer layer)
        {
            var toClose = new List<string>();
            foreach (var kv in _opened)
            {
                WindowDef def;
                if (_defs.TryGetValue(kv.Key, out def) && def.Layer == layer) toClose.Add(kv.Key);
            }
            for (int i = 0; i < toClose.Count; i++) Close(toClose[i]);
        }

        public void CloseAll()
        {
            var all = new List<string>(_opened.Keys);
            for (int i = 0; i < all.Count; i++) Close(all[i]);
            _stack.Clear();
        }

        // ---------------- 内部 ----------------

        private void BringToFront(string name)
        {
            UIWindow w;
            if (!_opened.TryGetValue(name, out w) || w == null) return;
            w.transform.SetAsLastSibling();
        }

        private void CreateMask(WindowDef def)
        {
            int idx = (int)def.Layer;
            if (_masks[idx] != null) return;

            var mask = UIUtil.CreatePanel("Mask_" + def.Layer, _layerRoots[idx], new Color(0f, 0f, 0f, 0.62f));
            UIUtil.Stretch(mask.rectTransform);
            mask.raycastTarget = true;
            _masks[idx] = mask.gameObject;
        }

        private void DestroyMask(string name)
        {
            // 该层还有别的全屏面板就保留遮罩
            WindowDef def;
            if (!_defs.TryGetValue(name, out def)) return;

            foreach (var kv in _opened)
            {
                WindowDef other;
                if (_defs.TryGetValue(kv.Key, out other) && other.Layer == def.Layer && other.FullScreen) return;
            }

            int idx = (int)def.Layer;
            if (_masks[idx] != null)
            {
                UnityEngine.Object.Destroy(_masks[idx]);
                _masks[idx] = null;
            }
        }

        private void Tick(float dt)
        {
            // 平台返回键（安卓 / Esc）关闭最上层弹窗
            if (Input.GetKeyDown(KeyCode.Escape)) CloseTop();

            // 倒序遍历，允许面板在 OnUpdate 里关闭自己
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                UIWindow w;
                if (i < _stack.Count && _opened.TryGetValue(_stack[i], out w) && w != null) w.InternalUpdate(dt);
            }
        }

        public void Shutdown()
        {
            CloseAll();
            if (_ticking && GameLoop.Exists) GameLoop.Instance.OnTick -= Tick;
            _ticking = false;
            _defs.Clear();
            _inited = false;
        }
    }
}
