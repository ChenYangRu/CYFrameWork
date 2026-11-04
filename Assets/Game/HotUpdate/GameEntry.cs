using System;
using GameFramework;
using GameFramework.Audio;
using GameFramework.Network;
using GameFramework.Resource;
using GameFramework.UI;
using UnityEngine;

namespace Game.HotUpdate
{
    /// <summary>
    /// 热更业务入口。由 AOT 的 HotUpdateLoader 反射调用（launch.json 的 entryType/entryMethod）。
    /// ★ 必须满足：public 静态、无参、可抛异常（异常会被 AOT 捕获并显示到启动页）。
    /// ★ 这里只做"装配 + 打开第一个界面"，不要写业务逻辑。
    /// </summary>
    public static class GameEntry
    {
        /// <summary>
        /// 热更验证用的版本号：改这个值 → 编译 DLL → 上传 → 重开游戏，
        /// 主界面上的"业务逻辑版本"会跟着变，说明代码热更通了。
        /// </summary>
        public const int HotLogicVersion = 1;

        public static UIManager UI { get; private set; }
        public static ResourceService Resource { get; private set; }
        public static NetworkService Network { get; private set; }
        public static AudioService Audio { get; private set; }

        public static void Start()
        {
            GameLog.Info("[Game] 热更业务启动，HotLogicVersion=" + HotLogicVersion);

            UI = ServiceLocator.Get<UIManager>();
            Resource = ServiceLocator.Get<ResourceService>();

            NetworkService net;
            Network = ServiceLocator.TryGet<NetworkService>(out net) ? net : null;

            AudioService audio;
            Audio = ServiceLocator.TryGet<AudioService>(out audio) ? audio : null;

            RegisterWindows();
            RegisterProtocols();

            // 打开登录界面（示例流程的第一步）
            UI.Open("Login");
        }

        private static void RegisterWindows()
        {
            // 只有 Panel 名与层的映射；prefab 位置留空 = 代码构建（接入美术后填 "UI/LoginPanel" 即可）
            UI.Register<LoginWindow>("Login", UILayer.Normal);
            UI.Register<MainWindow>("Main", UILayer.Normal);
        }

        private static void RegisterProtocols()
        {
            if (Network == null)
            {
                GameLog.Warn("[Game] 网络服务未就绪，跳过协议注册");
                return;
            }

            // 服务端推送（LoginAck 走 Request 配对，不需要注册）
            Network.RegisterHandler(Cmd.KickOut, delegate (Packet p)
            {
                GameLog.Warn("[Game] 被踢下线");
                UI.Open("Login");
            });
        }

        /// <summary>登录成功后的统一跳转（业务侧调用）</summary>
        public static void EnterMain()
        {
            UI.Open("Main");
        }
    }
}
