using System;

namespace GameFramework.Network
{
    /// <summary>
    /// 传输通道抽象。当前只实现 TCP（12 字节帧头 + 二进制包体，与自研网关协议兼容）；
    /// 将来要接 WebSocket / KCP 只加实现类，NetworkService 不动。
    /// ★ 所有事件都在**网络线程**触发，订阅方必须自己 Post 回主线程（NetworkService 已代做）。
    /// </summary>
    public interface INetChannel
    {
        bool Connected { get; }

        /// <summary>发起连接，onResult(success, error) 在主线程回调</summary>
        void Connect(string host, int port, float timeoutSeconds, Action<bool, string> onResult);

        /// <summary>发送已编码好的完整帧（线程安全，内部入队）</summary>
        void Send(byte[] frame);

        void Close(string reason);

        event Action OnConnected;
        event Action<string> OnClosed;
        event Action<Packet> OnPacket;
    }
}
