using GameFramework;
using GameFramework.Resource;
using GameFramework.Runtime;
using UnityEngine;

namespace Game.AOT
{
    /// <summary>
    /// AOT 唯一入口。挂在主场景的常驻物体上（本工程：Assets/Scenes/SampleScene.unity）。
    /// ★ 它只做三件事：应用 Inspector 启动参数、起主循环、起启动器。
    ///   业务代码全部在热更程序集里，AOT 侧不留业务逻辑（改了就要发版）。
    /// ★ 打包时 HybridCLR 需要一个"只存在于 AOT"的入口场景，所以本场景不进热更包。
    ///
    /// 启动参数为什么放在 MonoBehaviour 上而不是全塞 json：
    ///   这些值随**场景**序列化进包，属于"这个包/这个场景明确要什么"，出包前在 Inspector 里定，
    ///   比改 json 更直观，也不怕被同事的本地改动带偏。json 保留为"没有场景指定时的默认值"。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class Bootstrap : MonoBehaviour
    {
        [Header("启动流程")]
        [Tooltip("是否自动开始启动流程。关掉后需要自己调 GameLauncher.Create().Run()（用于写自动化/自测脚本）")]
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

        [Header("调试")]
        [Tooltip("勾选后跳过资源系统初始化，直接进业务（联调业务时省掉模拟构建时间）。\n" +
                 "注意：之后所有资源加载都会失败并记警告，不要带进正式包。")]
        [SerializeField]
        private bool skipResourceSystem;

        private void Awake()
        {
            // ① 先把 Inspector 选择落到配置层，之后任何 ResourceConfig.Load() 都会读到它
            ResourceConfig.SetRuntimePlayModeOverride(resourcePlayMode);
            if (skipResourceSystem) ResourceConfig.SetRuntimeSkipResource(true);

            // ② 主循环必须最先建立：后面所有服务（网络线程回调、定时器、UI Tick）都挂在它上面
            GameLoop.Ensure();

            GameLog.Info("[Bootstrap] AOT 入口启动");
            GameLog.Info("[Bootstrap] 平台=" + Application.platform
                         + " 包名=" + Application.identifier
                         + " 版本=" + Application.version);
            GameLog.Info("[Bootstrap] 资源模式选择=" + resourcePlayMode
                         + " 实际生效=" + ResourceConfig.ResolveMode()
                         + (skipResourceSystem ? "（已勾选跳过资源系统）" : string.Empty));

            if (autoStart)
            {
                GameLauncher.Create().Run();
            }
            else
            {
                GameLog.Warn("[Bootstrap] autoStart 已关闭，需要手动调用 GameLauncher.Create().Run()");
            }
        }

        private void OnApplicationQuit()
        {
            GameLog.Info("[Bootstrap] 退出，落盘日志");
            GameLog.Shutdown();
        }
    }
}
