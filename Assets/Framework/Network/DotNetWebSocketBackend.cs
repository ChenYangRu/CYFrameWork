#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace GameFramework.Network
{
    /// <summary>
    /// 桌面 / 移动端的 WebSocket 实现：.NET 的 <see cref="ClientWebSocket"/>（真 socket，走 ws/wss）。
    ///
    /// ★ WebGL / 小游戏走不了这里（没有 socket，且 IL2CPP 在浏览器里没有 BSD socket），
    ///   那条路径在 <c>WebGLWebSocketBackend</c>（jslib）。
    ///
    /// 线程模型：
    ///   · 连接、收包、发包都在线程池
    ///   · <see cref="OnMessage"/> 从收包循环里回调 → **可能在非主线程**；
    ///     上层 <see cref="WebSocketChannel"/> 只做拆帧，NetworkService 会 Post 回主线程。
    /// </summary>
    internal sealed class DotNetWebSocketBackend : IWebSocketBackend
    {
        private const int ReceiveBufferSize = 16 * 1024;

        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;

        private readonly Queue<byte[]> _sendQueue = new Queue<byte[]>(16);
        private readonly object _sendLock = new object();
        private readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);

        private int _closedRaised;
        private volatile bool _connecting;

        public event Action<byte[]> OnMessage;
        public event Action<string> OnClosed;

        public bool Connected
        {
            get
            {
                var s = _socket;
                return s != null && s.State == WebSocketState.Open;
            }
        }

        public void Connect(string url, float timeoutSeconds, Action<bool, string> onResult)
        {
            // ★ 这里必须"静默清理"，不能走 Close：
            //   Close 会抛 OnClosed，上层（NetworkService）收到就当成掉线并触发重连，
            //   表现是一启动就无限重连。重连时上层自己会先 Close 旧后端。
            CleanupSilently();

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                Report(onResult, false, "WebSocket 地址非法: " + url);
                return;
            }
            if (uri.Scheme != "ws" && uri.Scheme != "wss")
            {
                Report(onResult, false, "WebSocket 协议必须是 ws:// 或 wss://，当前: " + uri.Scheme);
                return;
            }

            _closedRaised = 0;
            _connecting = true;

            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            _socket = socket;

            var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1f, timeoutSeconds)));

            bool reported = false;
            Action<bool, string> once = delegate (bool ok, string err)
            {
                if (reported) return;
                reported = true;
                Report(onResult, ok, err);
            };

            Task.Run(async delegate
            {
                try
                {
                    await socket.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);

                    // 握手用的超时 token 到此作废，换成长连接 token
                    _cts = new CancellationTokenSource();
                    _connecting = false;
                    once(true, null);

                    StartSender(socket, _cts.Token);
                    await ReceiveLoop(socket, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _connecting = false;
                    once(false, "连接超时（" + timeoutSeconds.ToString("0.#") + "s）");
                    RaiseClosed("连接超时");
                }
                catch (Exception e)
                {
                    _connecting = false;
                    once(false, e.Message);
                    RaiseClosed(e.Message);
                }
                finally
                {
                    try { connectCts.Dispose(); } catch { }
                }
            });
        }

        public void Send(byte[] data, int length)
        {
            if (data == null || length <= 0) return;
            if (!Connected) return;

            // 必须拷贝：调用方的缓冲区会被复用（Packet.Encode 每次新建，但别依赖这一点）
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);

            lock (_sendLock) { _sendQueue.Enqueue(copy); }
            try { _sendSignal.Release(); } catch (SemaphoreFullException) { }
        }

        /// <summary>丢弃旧连接但不产生"已断开"事件（仅用于重新连接前清理）</summary>
        private void CleanupSilently()
        {
            var cts = _cts;
            _cts = null;
            if (cts != null) { try { cts.Cancel(); } catch { } }

            var socket = _socket;
            _socket = null;
            if (socket != null) { try { socket.Dispose(); } catch { } }

            lock (_sendLock) { _sendQueue.Clear(); }
        }

        public void Close(string reason)
        {
            var cts = _cts;
            _cts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
            }

            var socket = _socket;
            _socket = null;
            if (socket != null)
            {
                try { socket.Dispose(); } catch { }
            }

            lock (_sendLock) { _sendQueue.Clear(); }

            if (!_connecting) RaiseClosed(string.IsNullOrEmpty(reason) ? "已关闭" : reason);
        }

        // ---------------- 内部 ----------------

        private void StartSender(ClientWebSocket socket, CancellationToken token)
        {
            Task.Run(async delegate
            {
                while (!token.IsCancellationRequested)
                {
                    try { await _sendSignal.WaitAsync(token).ConfigureAwait(false); }
                    catch (Exception) { return; }

                    byte[] payload;
                    lock (_sendLock)
                    {
                        if (_sendQueue.Count == 0) continue;
                        payload = _sendQueue.Dequeue();
                    }

                    try
                    {
                        await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, token)
                                    .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        GameLog.Warn("[WS] 发送失败: " + e.Message);
                        RaiseClosed("发送失败: " + e.Message);
                        return;
                    }
                }
            });
        }

        private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[ReceiveBufferSize];
            var assembled = new System.IO.MemoryStream(ReceiveBufferSize);

            while (!token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close) break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    // 本框架协议是二进制；文本帧一律丢弃（避免污染拆包器）
                    GameLog.Warn("[WS] 收到文本帧，已忽略（协议只支持二进制）");
                    assembled.SetLength(0);
                    continue;
                }

                assembled.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                byte[] message = assembled.ToArray();
                assembled.SetLength(0);

                var handler = OnMessage;
                if (handler != null)
                {
                    try { handler(message); }
                    catch (Exception e) { GameLog.Error("[WS] 消息处理异常", e); }
                }
            }

            RaiseClosed("服务端关闭或连接中断");
        }

        private void RaiseClosed(string reason)
        {
            if (Interlocked.Exchange(ref _closedRaised, 1) != 0) return;
            var handler = OnClosed;
            if (handler != null)
            {
                try { handler(reason); }
                catch (Exception e) { GameLog.Error("[WS] OnClosed 回调异常", e); }
            }
        }

        private static void Report(Action<bool, string> onResult, bool ok, string error)
        {
            if (onResult == null) return;
            try { onResult(ok, error); }
            catch (Exception e) { GameLog.Error("[WS] 连接回调异常", e); }
        }
    }
}
#endif
