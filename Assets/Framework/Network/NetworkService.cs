using System;
using System.Collections.Generic;

namespace GameFramework.Network
{
    /// <summary>
    /// 网络服务（框架对业务暴露的唯一入口）。
    /// 职责：连接管理 / 请求-响应配对 / 心跳保活 / 断线重连 / 主线程派发。
    /// ★ 线程模型：收包在网络线程，本类**统一 Post 回主线程**再回调业务；
    ///   业务只有在主线程才能碰 Unity API，这条不能破。
    /// ★ 重连成功不会自动重放请求（业务语义各异），业务监听 OnReconnected 自行补状态。
    /// </summary>
    public class NetworkService
    {
        private class PendingRequest
        {
            public uint Cmd;
            public Action<bool, Packet> Callback;
            public float Deadline;
        }

        public event Action OnConnected;                    // 首次连接成功
        public event Action<string, bool> OnDisconnected;   // (原因, 是否会自动重连)
        public event Action<int> OnReconnected;             // 第 n 次重连成功
        public event Action<uint, string> OnRequestTimeout; // (cmd, 名称)

        private NetworkConfig _cfg;
        private INetChannel _channel;
        private readonly Dictionary<uint, Action<Packet>> _handlers = new Dictionary<uint, Action<Packet>>(32);
        private readonly Dictionary<uint, PendingRequest> _pending = new Dictionary<uint, PendingRequest>(16);

        private uint _seq;
        private float _lastRecvTime;
        private float _nextHeartbeatTime;
        private float _reconnectAt = -1f;
        private int _reconnectCount;
        private bool _manualClose;
        private bool _everConnected;
        private bool _ticking;

        public NetworkConfig Config { get { return _cfg; } }
        public bool Connected { get { return _channel != null && _channel.Connected; } }
        public int ReconnectCount { get { return _reconnectCount; } }
        /// <summary>收到过的最后一个协议号（调试面板用）</summary>
        public uint LastRecvCmd { get; private set; }

        // ---------------- 生命周期 ----------------

        public void Init(NetworkConfig config)
        {
            _cfg = config ?? NetworkConfig.Load();
            _channel = CreateChannel(_cfg);
            _channel.OnConnected += HandleChannelConnected;
            _channel.OnClosed += HandleChannelClosed;
            _channel.OnPacket += HandleChannelPacket;

            if (!_ticking)
            {
                var loop = GameLoop.Ensure();
                loop.OnTick += Tick;
                _ticking = true;
            }
        }

        /// <summary>
        /// 按配置选通道。WebGL / 小游戏没有 BSD socket，即使配成 Tcp 也只能走 WebSocket —— 
        /// 这里做一次兜底并明确告警，避免"真机连不上但本地好好的"这种排查黑洞。
        /// </summary>
        private static INetChannel CreateChannel(NetworkConfig cfg)
        {
            if (cfg.transport == TransportType.WebSocket)
                return new WebSocketChannel(cfg.webSocketSecure, cfg.webSocketPath, cfg.webSocketUrl);

#if UNITY_WEBGL && !UNITY_EDITOR
            GameLog.Warn("[Net] 当前平台不支持 TCP，已自动切换为 WebSocket（network.json 的 transport 可改为 1）");
            return new WebSocketChannel(cfg.webSocketSecure, cfg.webSocketPath, cfg.webSocketUrl);
#else
            return new TcpChannel();
#endif
        }

        private static string TransportName(NetworkConfig cfg)
        {
            if (cfg.transport == TransportType.WebSocket) return cfg.webSocketSecure ? "WebSocket/wss" : "WebSocket/ws";
#if UNITY_WEBGL && !UNITY_EDITOR
            return "WebSocket（平台不支持 TCP，已自动切换）";
#else
            return "TCP";
#endif
        }

        public void Shutdown()
        {
            _manualClose = true;
            if (_channel != null) _channel.Close("shutdown");
            if (_ticking && GameLoop.Exists) GameLoop.Instance.OnTick -= Tick;
            _ticking = false;
            _handlers.Clear();
            _pending.Clear();
            if (ServiceLocator.Has<NetworkService>() &&
                ReferenceEquals(ServiceLocator.Get<NetworkService>(), this))
                ServiceLocator.Unregister<NetworkService>();
        }

        // ---------------- 连接 ----------------

        public void Connect(Action<bool, string> onResult = null)
        {
            if (_cfg == null) Init(null);

            _manualClose = false;
            _reconnectAt = -1f;
            GameLog.Info(string.Format("[Net] 连接 {0}:{1}（{2}）", _cfg.host, _cfg.port, TransportName(_cfg)));

            _channel.Connect(_cfg.host, _cfg.port, _cfg.connectTimeout, delegate (bool ok, string err)
            {
                if (!ok)
                {
                    GameLog.Error("[Net] 连接失败: " + err);
                    if (_cfg.autoReconnect && !_manualClose) ScheduleReconnect();
                }
                if (onResult != null) onResult(ok, err);
            });
        }

        public void Disconnect()
        {
            _manualClose = true;
            _reconnectAt = -1f;
            if (_channel != null) _channel.Close("客户端主动断开");
        }

        private void ScheduleReconnect()
        {
            if (!_cfg.autoReconnect || _manualClose) return;
            if (_reconnectCount >= _cfg.maxReconnectTimes)
            {
                GameLog.Error("[Net] 重连次数已用尽，放弃");
                RaiseDisconnected("重连失败", false);
                return;
            }

            _reconnectCount++;
            float delay = _cfg.reconnectBaseDelay * (float)Math.Pow(2, _reconnectCount - 1);
            if (delay > 30f) delay = 30f;
            _reconnectAt = UnityEngine.Time.realtimeSinceStartup + delay;
            GameLog.Warn(string.Format("[Net] {0:0.0}s 后第 {1} 次重连", delay, _reconnectCount));
            RaiseDisconnected("连接中断，准备重连", true);
        }

        // ---------------- 收发 ----------------

        public void RegisterHandler(uint cmd, Action<Packet> handler)
        {
            if (handler == null) return;
            _handlers[cmd] = handler;
        }

        public void UnregisterHandler(uint cmd)
        {
            _handlers.Remove(cmd);
        }

        /// <summary>单向发送（不等回包）</summary>
        public void Send(uint cmd, ByteBuffer body)
        {
            SendRaw(cmd, 0, body == null ? null : body.ToArray());
        }

        public void Send(uint cmd, byte[] body)
        {
            SendRaw(cmd, 0, body);
        }

        private void SendRaw(uint cmd, uint seq, byte[] body)
        {
            if (_channel == null || !_channel.Connected)
            {
                GameLog.Warn("[Net] 连接不可用，发送被丢弃: " + Cmd.NameOf(cmd));
                return;
            }
            var p = new Packet(cmd, seq, body);
            _channel.Send(p.Encode());
        }

        /// <summary>
        /// 请求-响应。回包按 seq 配对；超时回调 onAck(false, null)。
        /// 返回 seq（可用于日志追踪）。
        /// </summary>
        public uint Request(uint cmd, ByteBuffer body, Action<bool, Packet> onAck, float timeout = -1f)
        {
            uint seq = ++_seq;
            if (seq == 0) seq = ++_seq; // 0 保留给单向消息

            if (onAck != null)
            {
                float t = timeout > 0 ? timeout : (_cfg != null ? _cfg.requestTimeout : 10f);
                _pending[seq] = new PendingRequest
                {
                    Cmd = cmd,
                    Callback = onAck,
                    Deadline = UnityEngine.Time.realtimeSinceStartup + t,
                };
            }

            SendRaw(cmd, seq, body == null ? null : body.ToArray());
            return seq;
        }

        // ---------------- 内部事件 ----------------

        private void HandleChannelConnected()
        {
            Post(delegate
            {
                _lastRecvTime = UnityEngine.Time.realtimeSinceStartup;
                _nextHeartbeatTime = _lastRecvTime + (_cfg != null ? _cfg.heartbeatInterval : 10f);
                _reconnectAt = -1f;
                // 重连成功属于同一次会话，保留计数直到业务主动重置
                if (_everConnected)
                {
                    GameLog.Info("[Net] 重连成功");
                    var h = OnReconnected;
                    if (h != null) h(_reconnectCount);
                }
                else
                {
                    _everConnected = true;
                    var h = OnConnected;
                    if (h != null) h();
                }
            });
        }

        private void HandleChannelClosed(string reason)
        {
            Post(delegate
            {
                GameLog.Warn("[Net] 断开: " + reason);
                FailAllPending("连接断开");
                if (_manualClose)
                {
                    RaiseDisconnected(reason, false);
                }
                else
                {
                    ScheduleReconnect();
                }
            });
        }

        private void HandleChannelPacket(Packet p)
        {
            Post(delegate { Dispatch(p); });
        }

        private void Dispatch(Packet p)
        {
            _lastRecvTime = UnityEngine.Time.realtimeSinceStartup;
            LastRecvCmd = p.Cmd;

            // 1) 请求-响应配对优先
            if (p.Seq != 0)
            {
                PendingRequest req;
                if (_pending.TryGetValue(p.Seq, out req))
                {
                    _pending.Remove(p.Seq);
                    try { req.Callback(true, p); }
                    catch (Exception e) { GameLog.Error("[Net] 请求回调异常 cmd=" + Cmd.NameOf(req.Cmd), e); }
                    return;
                }
            }

            // 2) 普通推送
            if (p.Cmd == Cmd.HeartbeatAck) return; // 心跳只刷新活跃时间

            Action<Packet> handler;
            if (_handlers.TryGetValue(p.Cmd, out handler))
            {
                try { handler(p); }
                catch (Exception e) { GameLog.Error("[Net] 协议处理异常 cmd=" + Cmd.NameOf(p.Cmd), e); }
            }
            else
            {
                GameLog.Warn("[Net] 未注册的协议: " + Cmd.NameOf(p.Cmd));
            }
        }

        // ---------------- 心跳 / 超时 / 重连 ----------------

        private void Tick(float dt)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;

            // 请求超时
            if (_pending.Count > 0)
            {
                List<uint> expired = null;
                foreach (var kv in _pending)
                {
                    if (now >= kv.Value.Deadline)
                    {
                        if (expired == null) expired = new List<uint>();
                        expired.Add(kv.Key);
                    }
                }
                if (expired != null)
                {
                    for (int i = 0; i < expired.Count; i++)
                    {
                        var req = _pending[expired[i]];
                        _pending.Remove(expired[i]);
                        GameLog.Warn("[Net] 请求超时: " + Cmd.NameOf(req.Cmd));
                        var th = OnRequestTimeout;
                        if (th != null) th(req.Cmd, Cmd.NameOf(req.Cmd));
                        try { req.Callback(false, null); } catch (Exception e) { GameLog.Error("[Net] 超时回调异常", e); }
                    }
                }
            }

            // 重连倒计时
            if (_reconnectAt > 0f && now >= _reconnectAt)
            {
                _reconnectAt = -1f;
                GameLog.Info("[Net] 执行重连…");
                _channel.Connect(_cfg.host, _cfg.port, _cfg.connectTimeout, delegate (bool ok, string err)
                {
                    if (!ok && _cfg.autoReconnect && !_manualClose) ScheduleReconnect();
                });
                return;
            }

            if (!Connected || _cfg == null) return;

            // 心跳
            if (_cfg.heartbeatInterval > 0f && now >= _nextHeartbeatTime)
            {
                _nextHeartbeatTime = now + _cfg.heartbeatInterval;
                var bb = new ByteBuffer(8);
                bb.WriteLong((long)(now * 1000f));
                Send(Cmd.HeartbeatReq, bb);
            }

            // 心跳超时（任意包都算活跃，不只心跳回包）
            if (_cfg.heartbeatTimeout > 0f && now - _lastRecvTime > _cfg.heartbeatTimeout)
            {
                GameLog.Error("[Net] 心跳超时，主动断开");
                _channel.Close("心跳超时");
            }
        }

        private void FailAllPending(string reason)
        {
            if (_pending.Count == 0) return;
            var list = new List<PendingRequest>(_pending.Values);
            _pending.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                try { list[i].Callback(false, null); } catch { }
            }
        }

        private void RaiseDisconnected(string reason, bool willReconnect)
        {
            var h = OnDisconnected;
            if (h != null) h(reason, willReconnect);
        }

        /// <summary>业务层确认稳定后清零重连计数（例如成功登录后）</summary>
        public void ResetReconnectCount()
        {
            _reconnectCount = 0;
        }

        private static void Post(Action action)
        {
            if (GameLoop.Exists) GameLoop.Instance.Post(action);
            else action();
        }
    }
}
