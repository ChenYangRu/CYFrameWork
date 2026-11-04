using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace GameFramework.Network
{
    /// <summary>
    /// TCP 通道：独立收/发线程 + 拆帧 + 优雅关闭。
    /// 设计取舍：
    ///  · **不用 async/await**（IL2CPP + 老 Unity 上 Task 会引入线程池不可控项），纯 Thread 最可控；
    ///  · 接收线程只做"读字节 → 拆帧 → 入队"，绝不碰 Unity API；
    ///  · 发送走独立线程 + 队列，避免主线程被 Socket.Send 阻塞（弱网时 Send 会卡住几十毫秒）。
    /// </summary>
    public class TcpChannel : INetChannel
    {
        public event Action OnConnected;
        public event Action<string> OnClosed;
        public event Action<Packet> OnPacket;

        private Socket _socket;
        private Thread _recvThread;
        private Thread _sendThread;
        private volatile bool _running;
        private volatile bool _connected;
        private bool _closedNotified;
        private string _closeReason;

        private readonly Queue<byte[]> _sendQueue = new Queue<byte[]>(32);
        private readonly object _sendLock = new object();
        private readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
        private readonly PacketSplitter _splitter = new PacketSplitter();

        private string _host;
        private int _port;
        private float _timeout;
        private Action<bool, string> _connectCallback;

        public bool Connected { get { return _connected; } }

        public void Connect(string host, int port, float timeoutSeconds, Action<bool, string> onResult)
        {
            Close("reconnect");

            _host = host;
            _port = port;
            _timeout = timeoutSeconds <= 0 ? 8f : timeoutSeconds;
            _connectCallback = onResult;
            _closeReason = null;
            _closedNotified = false;
            _splitter.Reset();
            _running = true;

            _recvThread = new Thread(ReceiveThread) { IsBackground = true, Name = "NetRecv" };
            _sendThread = new Thread(SendThread) { IsBackground = true, Name = "NetSend" };
            _recvThread.Start();
            _sendThread.Start();
        }

        public void Send(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return;
            if (!_connected)
            {
                GameLog.Warn("[Net] 未连接，丢弃发送");
                return;
            }
            lock (_sendLock) _sendQueue.Enqueue(frame);
            _sendSignal.Set();
        }

        public void Close(string reason)
        {
            if (!_running && _socket == null) return;

            _closeReason = reason;
            _running = false;
            _connected = false;

            try { _sendSignal.Set(); } catch { }

            var sock = _socket;
            _socket = null;
            if (sock != null)
            {
                try { if (sock.Connected) sock.Shutdown(SocketShutdown.Both); } catch { }
                try { sock.Close(); } catch { }
            }

            lock (_sendLock) _sendQueue.Clear();
        }

        private void ReceiveThread()
        {
            Socket sock = null;
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.NoDelay = true;            // 关 Nagle：小包即时发出，帧同步游戏必需
                sock.SendTimeout = 10000;
                sock.ReceiveTimeout = 0;        // 0 = 无限等待（阻塞读，靠 Close 打断）

                var ar = sock.BeginConnect(_host, _port, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(_timeout));
                if (!ok)
                {
                    sock.Close();
                    NotifyConnectResult(false, "连接超时");
                    RaiseClosed("连接超时");
                    return;
                }
                sock.EndConnect(ar);            // 连接被拒会在这里抛

                _socket = sock;
                _connected = true;
                GameLog.Info("[Net] 已连接 " + _host + ":" + _port);
                NotifyConnectResult(true, null);
                RaiseConnected();

                var buffer = new byte[8192];
                while (_running)
                {
                    int n;
                    try { n = sock.Receive(buffer); }
                    catch (SocketException se)
                    {
                        if (!_running) break;
                        RaiseClosed("接收异常: " + se.SocketErrorCode);
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (n <= 0)
                    {
                        RaiseClosed("服务端主动断开");
                        return;
                    }

                    _splitter.Append(buffer, 0, n);
                    Packet p;
                    while (true)
                    {
                        try
                        {
                            if (!_splitter.TryParse(out p)) break;
                        }
                        catch (Exception e)
                        {
                            GameLog.Error("[Net] 拆帧失败，断开连接", e);
                            RaiseClosed("协议错误: " + e.Message);
                            return;
                        }
                        RaisePacket(p);
                    }
                }
            }
            catch (SocketException se)
            {
                NotifyConnectResult(false, se.SocketErrorCode.ToString());
                RaiseClosed("连接失败: " + se.SocketErrorCode);
            }
            catch (Exception e)
            {
                NotifyConnectResult(false, e.Message);
                RaiseClosed("连接异常: " + e.Message);
            }
            finally
            {
                _connected = false;
                try { if (sock != null) sock.Close(); } catch { }
                if (_socket == sock) _socket = null;
            }
        }

        private void SendThread()
        {
            while (_running)
            {
                _sendSignal.WaitOne(200);

                while (true)
                {
                    byte[] frame = null;
                    lock (_sendLock)
                    {
                        if (_sendQueue.Count > 0) frame = _sendQueue.Dequeue();
                    }
                    if (frame == null) break;

                    var sock = _socket;
                    if (sock == null || !_connected) continue;

                    try
                    {
                        int sent = 0;
                        while (sent < frame.Length)
                            sent += sock.Send(frame, sent, frame.Length - sent, SocketFlags.None);
                    }
                    catch (Exception e)
                    {
                        if (_running) RaiseClosed("发送异常: " + e.Message);
                        return;
                    }
                }
            }
        }

        // ---------- 事件安全派发（网络线程 → 订阅方）----------

        private void NotifyConnectResult(bool success, string error)
        {
            var cb = _connectCallback;
            _connectCallback = null;
            if (cb == null) return;
            if (GameLoop.Exists) GameLoop.Instance.Post(delegate { cb(success, error); });
            else cb(success, error);
        }

        private void RaiseConnected()
        {
            var h = OnConnected;
            if (h != null) { try { h(); } catch (Exception e) { GameLog.Error("[Net] OnConnected 异常", e); } }
        }

        private void RaiseClosed(string reason)
        {
            // 同一次连接生命周期内只派发一次（收线程/发线程都可能触发）
            if (_closedNotified) return;
            _closedNotified = true;
            _closeReason = reason;

            var h = OnClosed;
            if (h != null) { try { h(reason); } catch (Exception e) { GameLog.Error("[Net] OnClosed 异常", e); } }
        }

        private void RaisePacket(Packet p)
        {
            var h = OnPacket;
            if (h != null) { try { h(p); } catch (Exception e) { GameLog.Error("[Net] 包处理异常", e); } }
        }
    }
}
