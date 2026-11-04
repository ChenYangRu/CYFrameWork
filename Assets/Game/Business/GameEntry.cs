using System;
using GameFramework;
using GameFramework.Audio;
using GameFramework.Data;
using GameFramework.Network;
using GameFramework.Resource;
using GameFramework.Runtime;
using GameFramework.UI;
using UnityEngine;

namespace Game.Business
{
    /// <summary>
    /// 业务入口。由 AOT 的 Bootstrap 以方法组直接调用（`Run(GameEntry.Start)`），不经反射。
    /// ★ 必须满足：public 静态、无参、可抛异常（异常会被启动器捕获并显示到启动页）。
    /// ★ 这里只做"装配 + 打开第一个界面"，不要写业务逻辑。
    /// </summary>
    public static class GameEntry
    {
        /// <summary>
        /// 业务逻辑版本号（纯展示用）。以前用来验证"代码热更有没有生效"；
        /// 现在代码随包发布，它的作用是：出包后一眼看出玩家装的是哪一版业务逻辑。
        /// </summary>
        public const int LogicVersion = 1;

        public static UIManager UI { get; private set; }
        public static ResourceService Resource { get; private set; }
        public static NetworkService Network { get; private set; }
        public static AudioService Audio { get; private set; }
        /// <summary>数据服务（存档 + CRUD）。两种模式都有；离线时本地为权威数据</summary>
        public static DataService Data { get; private set; }
        /// <summary>当前游戏模式（离线 / 联网）</summary>
        public static GameMode Mode { get; private set; }

        public static void Start()
        {
            GameLog.Info("[Game] 业务启动，LogicVersion=" + LogicVersion);

            UI = ServiceLocator.Get<UIManager>();
            Resource = ServiceLocator.Get<ResourceService>();

            NetworkService net;
            Network = ServiceLocator.TryGet<NetworkService>(out net) ? net : null;

            AudioService audio;
            Audio = ServiceLocator.TryGet<AudioService>(out audio) ? audio : null;

            DataService data;
            Data = ServiceLocator.TryGet<DataService>(out data) ? data : null;

            Mode = GameLauncher.Instance != null ? GameLauncher.Instance.Mode : GameMode.Offline;
            GameLog.Info("[Game] 游戏模式=" + GameModeUtil.NameOf(Mode)
                         + (Data != null ? "，存档槽位=" + Data.SlotName : "，数据服务缺失"));

            RegisterWindows();
            RegisterProtocols();

            // 离线模式不需要账号体系：直接进主界面（网络服务此时根本不存在）
            // 联网模式才走登录页 → 连 WebSocket → 登录
            if (Mode == GameMode.Offline)
            {
                GameLog.Info("[Game] 离线模式：跳过登录，直接进入主界面");
                EnterMain();
            }
            else
            {
                UI.Open("Login");
            }
        }

        private static void RegisterWindows()
        {
            // 只有 Panel 名与层的映射；prefab 位置留空 = 代码构建（接入美术后填 "UI/LoginPanel" 即可）
            UI.Register<LoginWindow>("Login", UILayer.Normal);
            UI.Register<MainWindow>("Main", UILayer.Normal);
            UI.Register<DataDemoWindow>("DataDemo", UILayer.Normal, null, true);
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
