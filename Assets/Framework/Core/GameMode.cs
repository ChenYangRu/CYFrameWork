namespace GameFramework
{
    /// <summary>
    /// 游戏运行模式。
    ///
    /// **离线（Offline）**：单机跑，数据在本地；不连任何服务器。
    /// **联网（Online）**：连服务端（WebSocket），服务端为权威数据源，本地只做缓存。
    ///
    /// 为什么做成"顶层模式"而不是一堆 if：
    ///   两种模式在**启动流程 / 数据来源 / 存档策略**上完全不同，
    ///   把它收成一个显式枚举，业务层只判断一次，避免到处散落 "if (isOnline)"。
    /// </summary>
    public enum GameMode
    {
        /// <summary>离线单机：不启网络，数据全部本地读写</summary>
        Offline = 0,
        /// <summary>联网：启网络（WebSocket），服务端数据为准</summary>
        Online = 1,
    }

    /// <summary>
    /// 模式覆盖项（给 Bootstrap 的 Inspector 与编辑器菜单用）。
    /// `GameMode` 没有"未指定"值，Inspector 需要一个"跟随配置"的默认项。
    /// </summary>
    public enum GameModeOverride
    {
        /// <summary>跟随 launch.json（编辑器下还可被菜单覆盖）</summary>
        UseConfig = 0,
        Offline = 1,
        Online = 2,
    }

    public static class GameModeUtil
    {
        public static GameMode ToMode(GameModeOverride value)
        {
            switch (value)
            {
                case GameModeOverride.Offline: return GameMode.Offline;
                case GameModeOverride.Online: return GameMode.Online;
                default: return GameMode.Offline;
            }
        }

        public static string NameOf(GameMode mode)
        {
            return mode == GameMode.Online ? "Online（联网）" : "Offline（离线）";
        }
    }
}
