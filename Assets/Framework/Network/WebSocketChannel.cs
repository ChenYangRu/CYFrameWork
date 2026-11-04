using System;

namespace GameFramework.Network
{
    /// <summary>
    /// WebSocket 通道：把 <see cref="IWebSocketBackend"/> 适配成 <see cref="INetChannel"/>。
    ///
    /// 关键点：**业务协议完全不变**。
    ///   上下层仍然是"12 字节帧头 + 二进制包体"，WebSocket 只当管道用；
    ///   一条 WS 消息里可能粘了多个业务包，也可能一个业务包跨消息，所以这里照样跑
    ///   <see cref="PacketSplitter"/> 做流式拆帧 —— 这样 TCP 与 WebSocket 对业务是同一套代码。
    ///
    /// 为什么小游戏必须用 WebSocket：
    ///   WebGL / 小游戏运行时没有 BSD socket，`System.Net.Sockets` 根本连不出去；
    ///   而 `wss://` 还能穿过 https 页面与 CDN/网关。
    /// </summary>
    public sealed class WebSocketChannel : INetChannel
    {
        private readonly PacketSplitter _splitter = new PacketSplitter();
        private readonly bool _secure;
        private readonly string _path;
        private readonly string _absoluteUrl;

        private IWebSocketBackend _backend;
        private volatile bool _connected;
        private bool _closedRaised;

        /// <param name="secure">true = wss（https 页面 / 小游戏正式环境必须 true）</param>
        /// <param name="path">握手路径，默认 "/"；例如网关挂在 "/ws" 就填 "/ws"</param>
        /// <param name="absoluteUrl">整条地址；非空时忽略 host/port（用于 CDN 独立域名）</param>
        public WebSocketChannel(bool secure = false, string path = "/", string absoluteUrl = null)
        {
            _secure = secure;
            _path = string.IsNullOrEmpty(path) ? "/" : (path[0] == '/' ? path : "/" + path);
            _absoluteUrl = absoluteUrl;
        }

        public bool Connected
        {
            get { return _connected && _backend != null && _backend.Connected; }
        }

        /// <summary>当前连接用的完整地址（排障时打日志用）</summary>
        public string CurrentUrl { get; private set; }

        public event Action OnConnected;
        public event Action<string> OnClosed;
        public event Action<Packet> OnPacket;

        // ---------------- INetChannel ----------------

        public void Connect(string host, int port, float timeoutSeconds, Action<bool, string> onResult)
        {
            string url = BuildUrl(host, port);
            CurrentUrl = url;

            _splitter.Reset();
            _connected = false;
            _closedRaised = false;

            // 重连/换地址：先彻底断开旧后端，否则旧连接还在收包、事件会重复触发
            var old = _backend;
            if (old != null)
            {
                old.OnMessage -= HandleMessage;
                old.OnClosed -= HandleClosed;
                try { old.Close("重新连接"); } catch (System.Exception) { }
                _backend = null;
            }

            _backend = WebSocketBackendFactory.Create();
            _backend.OnMessage += HandleMessage;
            _backend.OnClosed += HandleClosed;

            GameLog.Info("[WS] 连接 " + url);

            _backend.Connect(url, timeoutSeconds, delegate (bool ok, string error)
            {
                _connected = ok;
                if (!ok) GameLog.Error("[WS] 连接失败: " + error);
                if (onResult != null)
                {
                    try { onResult(ok, error); }
                    catch (Exception e) { GameLog.Error("[WS] 连接回调异常", e); }
                }
                if (ok)
                {
                    var h = OnConnected;
                    if (h != null) { try { h(); } catch (Exception e) { GameLog.Error("[WS] OnConnected 异常", e); } }
                }
            });
        }

        public void Send(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return;
            var backend = _backend;
            if (backend == null || !backend.Connected) return;
            backend.Send(frame, frame.Length);
        }

        public void Close(string reason)
        {
            _connected = false;

            var backend = _backend;
            if (backend != null)
            {
                backend.OnMessage -= HandleMessage;
                backend.OnClosed -= HandleClosed;
                backend.Close(reason);
            }

            RaiseClosed(string.IsNullOrEmpty(reason) ? "已关闭" : reason);
        }

        // ---------------- 内部 ----------------

        private void HandleMessage(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                _splitter.Append(data, 0, data.Length);

                Packet packet;
                while (_splitter.TryParse(out packet))
                {
                    if (packet == null) continue;
                    var h = OnPacket;
                    if (h != null) h(packet);
                }
            }
            catch (ProtocolException e)
            {
                GameLog.Error("[WS] 协议错误，主动断开: " + e.Message);
                Close("协议错误: " + e.Message);
            }
            catch (Exception e)
            {
                GameLog.Error("[WS] 拆包异常", e);
            }
        }

        private void HandleClosed(string reason)
        {
            _connected = false;
            RaiseClosed(reason);
        }

        private void RaiseClosed(string reason)
        {
            if (_closedRaised) return;
            _closedRaised = true;
            var h = OnClosed;
            if (h != null) { try { h(reason); } catch (Exception e) { GameLog.Error("[WS] OnClosed 异常", e); } }
        }

        private string BuildUrl(string host, int port)
        {
            if (!string.IsNullOrEmpty(_absoluteUrl)) return _absoluteUrl;

            string scheme = _secure ? "wss" : "ws";
            string h = string.IsNullOrEmpty(host) ? "127.0.0.1" : host;

            // 小游戏 / https 页面下，ws:// 会被浏览器当成混合内容拦掉
            if (port > 0) return scheme + "://" + h + ":" + port + _path;
            return scheme + "://" + h + _path;
        }
    }
}
