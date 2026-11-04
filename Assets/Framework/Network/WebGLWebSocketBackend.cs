#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace GameFramework.Network
{
    /// <summary>
    /// WebGL / 小游戏侧的 JS ↔ Unity 收发桥。
    ///
    /// 为什么需要它：
    ///   浏览器（以及小游戏运行时）里没有 BSD socket，WebSocket 只能由 JS 侧持有，
    ///   C# 通过 jslib 的 DllImport 调用 JS，JS 再通过 <c>SendMessage</c> 把数据推回 Unity。
    ///
    /// 三条关键约定（改动会同时影响 jslib）：
    ///   1. 消息格式一律是 <c>"id,base64"</c>（SendMessage 只能传字符串）
    ///   2. 收到的二进制**先入队，在 Update 里主线程派发** —— 不在 SendMessage 回调里直接回调业务，
    ///      否则会在 Unity 内部的回调栈里重入游戏逻辑
    ///   3. 方法必须挂 <c>[Preserve]</c>：IL2CPP + Strip 会按"有没有人调用"裁剪，
    ///      这些方法只被 JS 反射调用，不标必被裁掉（症状：JS 打日志说发了，C# 毫无反应）
    /// </summary>
    [Preserve]
    public class WebSocketBridge : MonoBehaviour
    {
        private const string GoName = "WebSocketBridge";

        private static WebSocketBridge _instance;

        private readonly Dictionary<int, Action> _onOpen = new Dictionary<int, Action>(4);
        private readonly Dictionary<int, Action<string>> _onClose = new Dictionary<int, Action<string>>(4);
        private readonly Dictionary<int, Action<string>> _onError = new Dictionary<int, Action<string>>(4);
        private readonly Dictionary<int, Action<byte[]>> _onBinary = new Dictionary<int, Action<byte[]>>(4);
        private readonly Dictionary<int, Action> _onTimeout = new Dictionary<int, Action>(4);
        private readonly Dictionary<int, float> _deadlines = new Dictionary<int, float>(4);

        private readonly Queue<KeyValuePair<int, byte[]>> _recvQueue = new Queue<KeyValuePair<int, byte[]>>(16);
        private readonly object _queueLock = new object();

        /// <summary>取桥对象（没有就建一个常驻的）</summary>
        public static WebSocketBridge Ensure()
        {
            if (_instance != null) return _instance;

            var go = new GameObject(GoName);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WebSocketBridge>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        // ---------------- C# 侧调用 ----------------

        public int Create(string url)
        {
            try { return CyWsCreate(url); }
            catch (Exception e)
            {
                Debug.LogError("[WS] jslib 调用失败（Assets/Plugins/WebGL/websocket.jslib 是否随包？）: " + e);
                return -1;
            }
        }

        public void Send(int id, byte[] data, int offset, int length)
        {
            if (data == null || length <= 0) return;
            if (offset < 0 || offset + length > data.Length) return;

            // 参考实现用 AllocHGlobal + IntPtr 传参：byte[] 直接 DllImport 在各版本上表现不一致
            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(length);
                Marshal.Copy(data, offset, buffer, length);
                CyWsSend(id, buffer, length);
            }
            catch (Exception e)
            {
                Debug.LogError("[WS] 发送失败: " + e.Message);
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        public void Close(int id)
        {
            try { CyWsClose(id); }
            catch (Exception) { }
        }

        /// <summary>1 = OPEN（与浏览器 WebSocket.readyState 对齐）</summary>
        public int State(int id)
        {
            try { return CyWsState(id); }
            catch (Exception) { return 3; }
        }

        public void Bind(int id, Action onOpen, Action<string> onClose, Action<string> onError, Action<byte[]> onBinary)
        {
            _onOpen[id] = onOpen;
            _onClose[id] = onClose;
            _onError[id] = onError;
            _onBinary[id] = onBinary;
        }

        public void SetTimeout(int id, float seconds, Action onTimeout)
        {
            if (seconds <= 0f) return;
            _onTimeout[id] = onTimeout;
            _deadlines[id] = Time.realtimeSinceStartup + seconds;
        }

        public void Unbind(int id)
        {
            _onOpen.Remove(id);
            _onClose.Remove(id);
            _onError.Remove(id);
            _onBinary.Remove(id);
            _onTimeout.Remove(id);
            _deadlines.Remove(id);
        }

        // ---------------- JS 回调入口（名字与 jslib 里一致） ----------------

        [Preserve]
        public void OnOpen(string arg)
        {
            int id;
            if (!int.TryParse(arg, out id)) return;
            _onTimeout.Remove(id);
            _deadlines.Remove(id);

            Action cb;
            if (_onOpen.TryGetValue(id, out cb) && cb != null) cb();
        }

        /// <summary>JS → Unity："id,base64"</summary>
        [Preserve]
        public void OnMessageBase64(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;

            int idx = arg.IndexOf(',');
            if (idx <= 0) return;

            int id;
            if (!int.TryParse(arg.Substring(0, idx), out id)) return;

            string base64 = arg.Substring(idx + 1);
            if (string.IsNullOrEmpty(base64)) return;

            byte[] data;
            try { data = Convert.FromBase64String(base64); }
            catch (Exception e)
            {
                Debug.LogError("[WS] base64 解码失败: " + e.Message);
                return;
            }

            lock (_queueLock) { _recvQueue.Enqueue(new KeyValuePair<int, byte[]>(id, data)); }
        }

        /// <summary>JS → Unity："id,msg"</summary>
        [Preserve]
        public void OnError(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;

            int idx = arg.IndexOf(',');
            if (idx <= 0) return;

            int id;
            if (!int.TryParse(arg.Substring(0, idx), out id)) return;
            string msg = arg.Substring(idx + 1);

            _onTimeout.Remove(id);
            _deadlines.Remove(id);

            Action<string> cb;
            if (_onError.TryGetValue(id, out cb) && cb != null) cb(msg);
        }

        /// <summary>JS → Unity："id,code,reason"</summary>
        [Preserve]
        public void OnClose(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;

            string[] parts = arg.Split(',');
            if (parts.Length < 2) return;

            int id;
            if (!int.TryParse(parts[0], out id)) return;

            int code;
            int.TryParse(parts[1], out code);

            string reason = string.Empty;
            if (parts.Length >= 3)
            {
                int start = parts[0].Length + 1 + parts[1].Length + 1;
                if (start >= 0 && start < arg.Length) reason = arg.Substring(start);
            }

            _onTimeout.Remove(id);
            _deadlines.Remove(id);

            Action<string> cb;
            if (_onClose.TryGetValue(id, out cb) && cb != null)
                cb(string.IsNullOrEmpty(reason) ? ("连接关闭 code=" + code) : reason);
        }

        // ---------------- 主线程派发 ----------------

        private void Update()
        {
            // 1) 数据：只在主线程回调业务
            while (true)
            {
                KeyValuePair<int, byte[]> item;
                lock (_queueLock)
                {
                    if (_recvQueue.Count == 0) break;
                    item = _recvQueue.Dequeue();
                }

                Action<byte[]> cb;
                if (_onBinary.TryGetValue(item.Key, out cb) && cb != null)
                {
                    try { cb(item.Value); }
                    catch (Exception e) { Debug.LogError("[WS] 消息处理异常: " + e); }
                }
            }

            // 2) 连接超时（JS 迟迟不回调 open 时不至于永远卡住）
            if (_deadlines.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                List<int> expired = null;
                foreach (var kv in _deadlines)
                {
                    if (now < kv.Value) continue;
                    if (expired == null) expired = new List<int>(2);
                    expired.Add(kv.Key);
                }

                if (expired != null)
                {
                    for (int i = 0; i < expired.Count; i++)
                    {
                        int id = expired[i];
                        Action cb;
                        if (_onTimeout.TryGetValue(id, out cb) && cb != null) cb();
                        _onTimeout.Remove(id);
                        _deadlines.Remove(id);
                    }
                }
            }
        }

        // ---------------- jslib ----------------

        [DllImport("__Internal")] private static extern int CyWsCreate(string url);
        [DllImport("__Internal")] private static extern void CyWsSend(int id, IntPtr data, int length);
        [DllImport("__Internal")] private static extern void CyWsClose(int id);
        [DllImport("__Internal")] private static extern int CyWsState(int id);
    }

    /// <summary>
    /// WebGL / 小游戏后端：把 <see cref="IWebSocketBackend"/> 落到 jslib 上。
    /// 行为与桌面端的 DotNetWebSocketBackend 对齐（连接/收发/关闭/超时）。
    /// </summary>
    internal sealed class WebGLWebSocketBackend : IWebSocketBackend
    {
        private WebSocketBridge _bridge;
        private int _id = -1;
        private Action<bool, string> _onResult;
        private bool _reported;
        private bool _closedRaised;

        public event Action<byte[]> OnMessage;
        public event Action<string> OnClosed;

        public bool Connected
        {
            get { return _id >= 0 && _bridge != null && _bridge.State(_id) == 1; }
        }

        public void Connect(string url, float timeoutSeconds, Action<bool, string> onResult)
        {
            Close("重新连接");

            _reported = false;
            _closedRaised = false;
            _onResult = onResult;

            _bridge = WebSocketBridge.Ensure();
            _id = _bridge.Create(url);
            if (_id < 0)
            {
                Report(false, "jslib 未就绪（检查 Assets/Plugins/WebGL/websocket.jslib 是否打进包）");
                RaiseClosed("jslib 不可用");
                return;
            }

            _bridge.Bind(_id, HandleOpen, HandleClose, HandleError, HandleBinary);
            _bridge.SetTimeout(_id, timeoutSeconds, HandleTimeout);
        }

        public void Send(byte[] data, int length)
        {
            if (_bridge == null || _id < 0 || data == null || length <= 0) return;
            _bridge.Send(_id, data, 0, length);
        }

        public void Close(string reason)
        {
            bool wasActive = _bridge != null && _id >= 0;

            if (wasActive)
            {
                _bridge.Close(_id);
                _bridge.Unbind(_id);
            }

            _id = -1;
            _onResult = null;

            // 只有"确实连过"才报断开；否则首次 Connect 前的那次 Close 会误发一条关闭事件，
            // 触发 NetworkService 走重连流程（典型症状：一启动就疯狂重连）
            if (wasActive && !_reported) RaiseClosed(string.IsNullOrEmpty(reason) ? "已关闭" : reason);
        }

        // ---------------- 桥回调 ----------------

        private void HandleOpen()
        {
            _closedRaised = false;
            Report(true, null);
        }

        private void HandleBinary(byte[] data)
        {
            var handler = OnMessage;
            if (handler == null || data == null || data.Length == 0) return;
            try { handler(data); }
            catch (Exception e) { GameLog.Error("[WS] 消息处理异常", e); }
        }

        private void HandleError(string message)
        {
            Report(false, message);
            RaiseClosed(string.IsNullOrEmpty(message) ? "连接出错" : message);
        }

        private void HandleClose(string reason)
        {
            RaiseClosed(string.IsNullOrEmpty(reason) ? "连接关闭" : reason);
        }

        private void HandleTimeout()
        {
            Report(false, "连接超时");
            Close("连接超时");
        }

        private void Report(bool ok, string error)
        {
            if (_reported) return;
            _reported = true;

            var cb = _onResult;
            if (cb == null) return;
            try { cb(ok, error); }
            catch (Exception e) { GameLog.Error("[WS] 连接回调异常", e); }
        }

        private void RaiseClosed(string reason)
        {
            if (_closedRaised) return;
            _closedRaised = true;

            var handler = OnClosed;
            if (handler == null) return;
            try { handler(reason); }
            catch (Exception e) { GameLog.Error("[WS] OnClosed 回调异常", e); }
        }
    }
}
#endif
