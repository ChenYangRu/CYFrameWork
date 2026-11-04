using System;

namespace GameFramework.Network
{
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
