using System;

namespace GameFramework.Runtime
{
    /// <summary>
    /// 启动配置。来源：Resources/Config/launch.json。
    ///
    /// ★ 这里**故意只放"启动要什么"**，不放业务开关：
    ///   业务配置走各自的 Config 文件 / 数据层，别把 launch.json 变成杂物间。
    ///
    /// 为什么没有 hotUpdateAssemblies / AOTMetadata 这些字段了：
    ///   本框架面向小游戏 / WebGL，**不做代码热更**（平台不允许加载外部代码），
    ///   业务程序集（Game.Business）随包编译进原包；能热更的只有资源（YooAsset）。
    /// </summary>
    [Serializable]
    public class LaunchConfig
    {
        /// <summary>
        /// 游戏模式：0 = 离线单机（本地存档为权威数据），1 = 联网（WebSocket，服务端为权威）。
        /// ★ 这一项决定"要不要启动网络"和"数据写本地还是走服务器"，是本次框架分叉的总开关。
        /// </summary>
        public GameMode gameMode = GameMode.Offline;

        /// <summary>编辑器菜单覆盖运行模式的 EditorPrefs 键（-1 = 不覆盖）</summary>
        public const string EditorGameModeKey = "app.gameMode.override";

        private static int _runtimeGameModeOverride = -1;

        /// <summary>
        /// 由 Bootstrap 在 Awake 里调用（随场景进包，真机生效）。
        /// 传 UseConfig 表示不覆盖。
        /// </summary>
        public static void SetRuntimeGameModeOverride(GameModeOverride mode)
        {
            switch (mode)
            {
                case GameModeOverride.Offline: _runtimeGameModeOverride = (int)GameMode.Offline; break;
                case GameModeOverride.Online: _runtimeGameModeOverride = (int)GameMode.Online; break;
                default: _runtimeGameModeOverride = -1; break;
            }
        }

        /// <summary>当前运行时覆盖值（-1 = 无），调试用</summary>
        public static int RuntimeGameModeOverride { get { return _runtimeGameModeOverride; } }

        /// <summary>
        /// 优先级：Bootstrap(Inspector) &gt; 编辑器菜单(EditorPrefs) &gt; launch.json
        /// 与资源模式保持同一套规则，避免"两个设置互相打脸"。
        /// </summary>
        public static GameMode ResolveGameMode(GameMode configValue)
        {
            if (_runtimeGameModeOverride >= 0 && Enum.IsDefined(typeof(GameMode), _runtimeGameModeOverride))
                return (GameMode)_runtimeGameModeOverride;

#if UNITY_EDITOR
            int ov = UnityEditor.EditorPrefs.GetInt(EditorGameModeKey, -1);
            if (ov >= 0 && Enum.IsDefined(typeof(GameMode), ov))
                return (GameMode)ov;
#endif

            return configValue;
        }

        public static LaunchConfig Load()
        {
            var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Config/launch");
            if (ta == null)
            {
                GameLog.Warn("[Launch] 未找到 Resources/Config/launch.json，使用默认配置");
                return new LaunchConfig();
            }
            try
            {
                var cfg = UnityEngine.JsonUtility.FromJson<LaunchConfig>(ta.text);
                return cfg ?? new LaunchConfig();
            }
            catch (Exception e)
            {
                GameLog.Error("[Launch] launch.json 解析失败，使用默认配置", e);
                return new LaunchConfig();
            }
        }
    }
}
