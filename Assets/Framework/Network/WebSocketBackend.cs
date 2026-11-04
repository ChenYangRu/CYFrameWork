using System;

namespace GameFramework.Network
{
    /// <summary>
    /// WebSocket 底层实现抽象。
    ///
    /// 为什么要分后端：
    ///   · 编辑器 / Android / iOS / PC：用 .NET 的 <c>ClientWebSocket</c>（真 socket）
    ///   · WebGL / 微信小游戏：**没有 socket**，只能由 JS 侧提供（jslib → 浏览器 WebSocket / wx.connectSocket）
    ///
    /// 上层 <see cref="WebSocketChannel"/> 只面对这个接口，两套实现行为一致：
    /// 收到的每条完整消息都会回调一次 <see cref="OnMessage"/>（二进制）。
    /// </summary>
    public interface IWebSocketBackend
    {
        bool Connected { get; }

        /// <summary>发起连接；onResult 只回调一次（成功或失败）</summary>
        void Connect(string url, float timeoutSeconds, Action<bool, string> onResult);

        /// <summary>发送一帧二进制（长度取前 length 字节）；线程安全，内部排队</summary>
        void Send(byte[] data, int length);

        void Close(string reason);

        /// <summary>一条完整消息（可能是 1..n 个业务包，交给上层拆帧）。**可能在非主线程触发**</summary>
        event Action<byte[]> OnMessage;

        /// <summary>连接关闭/出错（只触发一次）。**可能在非主线程触发**</summary>
        event Action<string> OnClosed;
    }

    public static class WebSocketBackendFactory
    {
        /// <summary>当前平台是否能用 WebSocket（本框架里 WebSocket 是联网模式的唯一通道）</summary>
        public static bool IsSupported { get { return true; } }

        public static IWebSocketBackend Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLWebSocketBackend();
#else
            return new DotNetWebSocketBackend();
#endif
        }
    }
}
