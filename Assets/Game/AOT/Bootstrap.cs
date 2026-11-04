using GameFramework;
using GameFramework.Resource;
using GameFramework.Runtime;
using UnityEngine;

namespace Game.AOT
{
    /// <summary>
    /// AOT 唯一入口。挂在主场景的常驻物体上（本工程：Assets/Scenes/SampleScene.unity）。
    /// ★ 它只做三件事：应用 Inspector 启动参数、起主循环、起启动器。
    ///   业务代码全部在 `Game.Business` 里，这里只做组合（把业务入口交给启动器），不留业务逻辑。
    /// ★ 代码随包发布（不做代码热更），本场景就是包里的启动场景；能热更的只有资源。
    ///
    /// 启动参数为什么放在 MonoBehaviour 上而不是全塞 json：
    ///   这些值随**场景**序列化进包，属于"这个包/这个场景明确要什么"，出包前在 Inspector 里定，
    ///   比改 json 更直观，也不怕被同事的本地改动带偏。json 保留为"没有场景指定时的默认值"。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class Bootstrap : MonoBehaviour
    {
        [Header("启动流程")]
        [Tooltip("是否自动开始启动流程。关掉后需要自己调 GameLauncher.Create().Run(GameEntry.Start)（用于写自动化/自测脚本）")]
        public bool autoStart = true;

        [Header("资源运行模式")]
        [Tooltip("UseConfig = 跟随 Assets/Resources/Config/resource.json（编辑器下还会被菜单「框架/资源/运行模式」覆盖）\n\n" +
                 "EditorSimulate = 直接读 AssetDatabase，不打包、无更新流程（日常开发）\n" +
                 "Offline = 只读随包资源，无网络、无更新流程（单机/无网验收）\n" +
                 "Host = 内置兜底 + 远端增量下载（Android/iOS/PC 正式包）\n" +
                 "Web = 资源全走 HTTP 不落磁盘（WebGL/微信小游戏）\n\n" +
                 "★ 这个值会覆盖 json，且**真机同样生效**（随场景进包）。")]
        [SerializeField]
        private PlayModeOverride resourcePlayMode = PlayModeOverride.UseConfig;

        [Header("游戏模式（离线 / 联网）")]
        [Tooltip("UseConfig = 跟随 Assets/Resources/Config/launch.json 的 gameMode\n\n" +
                 "Offline = 离线单机：不启动网络，数据全部存本地 txt（存档在 persistentDataPath/save/<槽位>/）\n" +
                 "Online = 联网：启动 WebSocket，服务端为权威数据源，本地仅作缓存\n\n" +
                 "★ 与资源模式同理：这个值随场景进包，**真机同样生效**，优先级高于 json 与编辑器菜单。")]
        [SerializeField]
        private GameModeOverride gameMode = GameModeOverride.UseConfig;

        [Header("调试")]
        [Tooltip("勾选后跳过资源系统初始化，直接进业务（联调业务时省掉模拟构建时间）。\n" +
                 "注意：之后所有资源加载都会失败并记警告，不要带进正式包。")]
        [SerializeField]
        private bool skipResourceSystem;

        private void Awake()
        {
            // ① 先把 Inspector 选择落到配置层，之后任何 ResourceConfig.Load() 都会读到它
            ResourceConfig.SetRuntimePlayModeOverride(resourcePlayMode);
            LaunchConfig.SetRuntimeGameModeOverride(gameMode);
            if (skipResourceSystem) ResourceConfig.SetRuntimeSkipResource(true);

            // ② 主循环必须最先建立：后面所有服务（网络线程回调、定时器、UI Tick）都挂在它上面
            GameLoop.Ensure();

            GameLog.Info("[Bootstrap] AOT 入口启动");
            // ★ 同机常有多份工程副本，认错工程会导致"改了没生效"的假故障 —— 首行就把路径打出来
            GameLog.Info("[Bootstrap] 工程路径=" + Application.dataPath);
            GameLog.Info("[Bootstrap] 平台=" + Application.platform
                         + " 包名=" + Application.identifier
                         + " 版本=" + Application.version);
            GameLog.Info("[Bootstrap] 游戏模式选择=" + gameMode
                         + " 实际生效=" + LaunchConfig.ResolveGameMode(LaunchConfig.Load().gameMode));
            GameLog.Info("[Bootstrap] 资源模式选择=" + resourcePlayMode
                         + " 实际生效=" + ResourceConfig.ResolveMode()
                         + (skipResourceSystem ? "（已勾选跳过资源系统）" : string.Empty));

            if (autoStart)
            {
                // 业务入口用方法组直接传（不再有"反射找 GameEntry"这一步）
                GameLauncher.Create().Run(Game.Business.GameEntry.Start);
            }
            else
            {
                GameLog.Warn("[Bootstrap] autoStart 已关闭，需要手动调用 GameLauncher.Create().Run(Game.Business.GameEntry.Start)");
            }
        }

        private void OnApplicationQuit()
        {
            GameLog.Info("[Bootstrap] 退出，落盘日志");
            GameLog.Shutdown();
        }
    }
}
