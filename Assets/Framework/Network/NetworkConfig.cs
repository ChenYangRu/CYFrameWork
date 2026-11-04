using System;

namespace GameFramework.Network
{
    /// <summary>
    /// 传输方式。
    /// · Tcp：传统长连接（原生包用；WebGL/小游戏**不可用**，没有 socket）
    /// · WebSocket：浏览器与小游戏唯一可行方案，也能穿 CDN / 反代
    /// </summary>
    public enum TransportType
    {
        Tcp = 0,
        WebSocket = 1,
    }

    /// <summary>
    /// 网络配置。来源：Resources/Config/network.json（改地址不用改代码）。
    /// ★ JsonUtility 会静默忽略 JSON 里未声明的字段——加字段必须同步加这里的 public 字段。
    /// </summary>
    [Serializable]
    public class NetworkConfig
    {
        public string host = "127.0.0.1";
        public int port = 9000;
        /// <summary>连接超时（秒）</summary>
        public float connectTimeout = 8f;
        /// <summary>心跳发送间隔（秒）；&lt;=0 表示关闭心跳</summary>
        public float heartbeatInterval = 10f;
        /// <summary>多久没收到任何数据判定为掉线（秒），应 &gt; 2 倍心跳间隔</summary>
        public float heartbeatTimeout = 30f;
        public bool autoReconnect = true;
        public int maxReconnectTimes = 6;
        /// <summary>重连退避基数（秒）：1,2,4,8… 封顶 30s</summary>
        public float reconnectBaseDelay = 1f;
        /// <summary>请求超时（秒）</summary>
        public float requestTimeout = 10f;

        // ---------------- 传输方式 ----------------

        /// <summary>传输方式：0=Tcp，1=WebSocket（小游戏/WebGL 必须用 1）</summary>
        public TransportType transport = TransportType.WebSocket;
        /// <summary>是否用 wss（https 页面 / 小游戏正式环境必须 true，否则被当混合内容拦掉）</summary>
        public bool webSocketSecure = false;
        /// <summary>握手路径，默认 "/"；网关挂在 /ws 就填 "/ws"</summary>
        public string webSocketPath = "/";
        /// <summary>整条地址（如 wss://gw.example.com/ws）；非空时忽略 host/port</summary>
        public string webSocketUrl = "";

        public static NetworkConfig Load()
        {
            var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Config/network");
            if (ta == null)
            {
                GameLog.Warn("[Net] 未找到 Resources/Config/network.json，使用默认配置");
                return new NetworkConfig();
            }
            try
            {
                var cfg = UnityEngine.JsonUtility.FromJson<NetworkConfig>(ta.text);
                return cfg ?? new NetworkConfig();
            }
            catch (Exception e)
            {
                GameLog.Error("[Net] network.json 解析失败，使用默认配置", e);
                return new NetworkConfig();
            }
        }
    }
}
