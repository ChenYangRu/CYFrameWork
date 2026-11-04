using System;

namespace GameFramework.Resource
{
    /// <summary>
    /// 资源运行模式（与 YooAsset 3.x 的四种初始化方式一一对应）。
    /// </summary>
    public enum PlayMode
    {
        /// <summary>编辑器模拟：不打包 AB，直接走 AssetDatabase（日常开发）</summary>
        EditorSimulate = 0,
        /// <summary>离线单机：只读随包的 StreamingAssets，无网络、无热更</summary>
        Offline = 1,
        /// <summary>联机热更：内置资源兜底 + 远端增量下载（Android / iOS / PC 正式包）</summary>
        Host = 2,
        /// <summary>
        /// Web 模式：资源全部走 HTTP（不落本地磁盘，靠浏览器缓存）。WebGL / 微信小游戏用这个。
        /// ★ 与 Host 的区别：Host 会把 bundle 下载到 persistentDataPath 再读，Web 端没有可写的
        ///   文件系统（小游戏虚拟 FS 语义也不稳），所以只能"边下边读 + 依赖浏览器缓存"。
        /// </summary>
        Web = 3,
    }

    /// <summary>
    /// 运行模式覆盖项（给 Bootstrap 的 Inspector 用）。
    ///
    /// 为什么要这个枚举而不是直接用 <see cref="PlayMode"/>：
    /// `PlayMode` 没有"未指定"值，而 Inspector 需要一个"跟随配置文件"的默认项，
    /// 否则每次新建场景都得手动选一次，选错就是真机跑成编辑器模拟模式。
    /// </summary>
    public enum PlayModeOverride
    {
        /// <summary>跟随 resource.json（真机）/ 编辑器菜单覆盖（编辑器）</summary>
        UseConfig = 0,
        EditorSimulate = 1,
        Offline = 2,
        Host = 3,
        Web = 4,
    }

    /// <summary>
    /// 资源层配置。来源：Resources/Config/resource.json。
    /// ★ 加字段必须同步加到这里（JsonUtility 静默忽略未声明字段）。
    /// </summary>
    [Serializable]
    public class ResourceConfig
    {
        /// <summary>YooAsset 包裹名，必须与收集器里的 Package 名一致</summary>
        public string packageName = "GamePackage";
        /// <summary>运行模式（编辑器下若配置为 Host 且没有可用的远端，可以改成 EditorSimulate 快速迭代）</summary>
        public PlayMode playMode = PlayMode.EditorSimulate;
        /// <summary>CDN 根地址，例：https://cdn.example.com/mygame</summary>
        public string hostServer = "";
        /// <summary>备用 CDN（主源失败时 YooAsset 自动回退）</summary>
        public string fallbackHostServer = "";
        /// <summary>App 版本号，参与 CDN 目录拼接：host/Android/{appVersion}/{resVersion}</summary>
        public string appVersion = "1.0.0";
        /// <summary>资源版本号（空 = 运行时向远端请求最新）</summary>
        public string resVersion = "";
        /// <summary>同时下载的 bundle 数</summary>
        public int downloadMaxConcurrency = 4;
        /// <summary>单文件失败重试次数</summary>
        public int downloadRetryCount = 3;
        /// <summary>下载完成后清掉不再使用的旧 bundle</summary>
        public bool clearUnusedCache = true;
        /// <summary>请求版本/清单超时（秒）</summary>
        public int requestTimeout = 60;

        /// <summary>编辑器里临时覆盖运行模式的 EditorPrefs 键（-1 = 不覆盖，用 json 里的值）</summary>
        public const string EditorPlayModeKey = "res.playMode.override";

        public static ResourceConfig Load()
        {
            ResourceConfig cfg = null;

            var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Config/resource");
            if (ta == null)
            {
                GameLog.Warn("[Res] 未找到 Resources/Config/resource.json，使用默认配置");
                cfg = new ResourceConfig();
            }
            else
            {
                try
                {
                    cfg = UnityEngine.JsonUtility.FromJson<ResourceConfig>(ta.text) ?? new ResourceConfig();
                }
                catch (Exception e)
                {
                    GameLog.Error("[Res] resource.json 解析失败，使用默认配置", e);
                    cfg = new ResourceConfig();
                }
            }

            ApplyOverrides(cfg);
            return cfg;
        }



        /// <summary>当前生效的运行模式（含编辑器覆盖），日志/调试面板用</summary>
        public static PlayMode ResolveMode()
        {
            var cfg = Load();
            return cfg != null ? cfg.playMode : PlayMode.EditorSimulate;
        }

        /// <summary>
        /// 运行时覆盖（来自 Bootstrap 的 Inspector 序列化字段），优先级最高，真机也生效。
        /// -1 = 不覆盖。用 <see cref="SetRuntimePlayModeOverride"/> 设置。
        /// </summary>
        private static int _runtimeModeOverride = -1;

        /// <summary>
        /// 由启动器（Bootstrap）在 Awake 里调用。传 null / UseConfig 表示不覆盖。
        /// ★ 必须在第一次 <see cref="Load"/> 之前调用，否则本次启动仍用旧值。
        /// </summary>
        public static void SetRuntimePlayModeOverride(PlayModeOverride mode)
        {
            switch (mode)
            {
                case PlayModeOverride.EditorSimulate: _runtimeModeOverride = (int)PlayMode.EditorSimulate; break;
                case PlayModeOverride.Offline: _runtimeModeOverride = (int)PlayMode.Offline; break;
                case PlayModeOverride.Host: _runtimeModeOverride = (int)PlayMode.Host; break;
                case PlayModeOverride.Web: _runtimeModeOverride = (int)PlayMode.Web; break;
                default: _runtimeModeOverride = -1; break;
            }
        }

        /// <summary>当前运行时覆盖值（-1 = 无），调试面板/日志用</summary>
        public static int RuntimeModeOverride { get { return _runtimeModeOverride; } }

        /// <summary>
        /// 运行时"跳过资源系统"开关（来自 Bootstrap 的 Inspector）。
        /// 勾选后不进 YooAsset、直接进业务，用于纯业务联调；资源加载全部失败但不阻塞启动。
        /// </summary>
        private static bool _runtimeSkipResource;

        public static void SetRuntimeSkipResource(bool skip) { _runtimeSkipResource = skip; }

        public static bool RuntimeSkipResource { get { return _runtimeSkipResource; } }

        /// <summary>
        /// 覆盖优先级：**Bootstrap(Inspector) > 编辑器菜单(EditorPrefs) > resource.json**
        ///
        /// 为什么这么排：Bootstrap 是随场景序列化进包的，属于"这个场景/这个包明确要什么"，
        /// 应该压过配置文件；而编辑器菜单只是开发期临时手段，不能盖掉打包时定下的值。
        /// </summary>
        private static void ApplyOverrides(ResourceConfig cfg)
        {
            // 1) 运行时（Bootstrap Inspector）—— 真机也生效
            if (_runtimeModeOverride >= 0 && Enum.IsDefined(typeof(PlayMode), _runtimeModeOverride))
            {
                cfg.playMode = (PlayMode)_runtimeModeOverride;
                return;
            }

#if UNITY_EDITOR
            // 2) 编辑器菜单覆盖（仅编辑器）
            int overrideMode = UnityEditor.EditorPrefs.GetInt(EditorPlayModeKey, -1);
            if (overrideMode >= 0 && Enum.IsDefined(typeof(PlayMode), overrideMode))
            {
                // 不在这里打日志：菜单的勾选校验会高频调用 Load()，一打就刷屏。
                // 实际生效的模式由 ResourceService 在启动时统一打一行。
                cfg.playMode = (PlayMode)overrideMode;
            }
#endif
        }

        /// <summary>远端资源根地址（含平台/App版本），空表示未配置 CDN</summary>
        public string BuildRemoteRoot()
        {
            if (string.IsNullOrEmpty(hostServer)) return string.Empty;
            return PathUtil.CombineUrl(PathUtil.CombineUrl(hostServer, PathUtil.PlatformFolder), appVersion);
        }
    }
}
