namespace GameFramework.Network
{
    /// <summary>
    /// 协议号表。**必须与服务端常量表一一对应**，改号等于改协议版本。
    /// 分段约定：
    ///   1~99        系统（心跳/握手/踢下线）
    ///   100~999     账号与登录
    ///   1000~1999   玩家数据
    ///   9000+       调试/回显
    /// </summary>
    public static class Cmd
    {
        // 系统
        public const uint HeartbeatReq = 1;
        public const uint HeartbeatAck = 2;
        public const uint KickOut = 3;

        // 登录
        public const uint LoginReq = 101;
        public const uint LoginAck = 102;

        // 玩家
        public const uint EnterWorldReq = 1001;
        public const uint EnterWorldAck = 1002;

        // 调试
        public const uint EchoReq = 9001;
        public const uint EchoAck = 9002;

        public static string NameOf(uint cmd)
        {
            switch (cmd)
            {
                case HeartbeatReq: return "HeartbeatReq";
                case HeartbeatAck: return "HeartbeatAck";
                case KickOut: return "KickOut";
                case LoginReq: return "LoginReq";
                case LoginAck: return "LoginAck";
                case EnterWorldReq: return "EnterWorldReq";
                case EnterWorldAck: return "EnterWorldAck";
                case EchoReq: return "EchoReq";
                case EchoAck: return "EchoAck";
                default: return "Unknown(" + cmd + ")";
            }
        }
    }
}
