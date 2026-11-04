using GameFramework;
using GameFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// 游戏模式快速切换（仅编辑器生效，不改 launch.json，不影响真机包）。
    ///
    /// 为什么需要：离线（本地存档）与联网（WebSocket）在启动流程上分叉，
    /// 开发期要来回切；手改 json 再重启容易忘，也容易把联网配置误提交。
    /// 覆盖值存 EditorPrefs，`LaunchConfig.ResolveGameMode` 读它 —— 真机永远以 json / Bootstrap 为准。
    /// </summary>
    public static class GameModeMenu
    {
        private const string Root = "框架/游戏模式/";

        private const string ItemOffline = Root + "离线单机（Offline）";
        private const string ItemOnline = Root + "联网（Online）";

        [MenuItem(ItemOffline, false, 20)]
        public static void UseOffline() { SetMode(GameMode.Offline); }

        [MenuItem(ItemOnline, false, 21)]
        public static void UseOnline() { SetMode(GameMode.Online); }

        [MenuItem(Root + "清除覆盖（跟随 launch.json）", false, 30)]
        public static void ClearOverride()
        {
            EditorPrefs.DeleteKey(LaunchConfig.EditorGameModeKey);
            Debug.Log("[框架] 已清除游戏模式覆盖，下次 Play 使用 launch.json 的值："
                      + LaunchConfig.Load().gameMode);
        }

        [MenuItem(ItemOffline, true)]
        public static bool ValidateOffline() { Check(ItemOffline, GameMode.Offline); return true; }

        [MenuItem(ItemOnline, true)]
        public static bool ValidateOnline() { Check(ItemOnline, GameMode.Online); return true; }

        [MenuItem("框架/游戏模式/打印当前游戏模式", false, 31)]
        public static void PrintStatus()
        {
            var cfg = LaunchConfig.Load();
            int ov = EditorPrefs.GetInt(LaunchConfig.EditorGameModeKey, -1);
            bool hasOverride = ov >= 0;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[游戏模式]");
            sb.AppendLine("  launch.json   : " + cfg.gameMode);
            sb.AppendLine("  编辑器覆盖    : " + (hasOverride ? ((GameMode)ov).ToString() : "(无)"));
            sb.AppendLine("  实际生效      : " + LaunchConfig.ResolveGameMode(cfg.gameMode));
            sb.AppendLine();
            sb.AppendLine("  说明：Offline=不启网络，数据全部存本地 txt；Online=启 WebSocket，服务端为准");
            sb.AppendLine("  存档目录：<persistentDataPath>/save/<槽位>/");
            Debug.Log(sb.ToString());
        }

        private static void SetMode(GameMode mode)
        {
            EditorPrefs.SetInt(LaunchConfig.EditorGameModeKey, (int)mode);
            Debug.Log("[框架] 游戏模式改为 " + mode + "（下次 Play 生效；真机不受影响）");
        }

        private static void Check(string item, GameMode mode)
        {
            // ★ 菜单 validate 会被高频调用（鼠标划过一次就触发多次），
            //   这里只读 EditorPrefs + 一次 json 缓存，绝不在里面反复 Load()。
            int ov = EditorPrefs.GetInt(LaunchConfig.EditorGameModeKey, -1);
            bool active = ov >= 0 ? (GameMode)ov == mode : JsonMode() == mode;
            Menu.SetChecked(item, active);
        }

        private static int _jsonModeCache = -1;

        private static GameMode JsonMode()
        {
            if (_jsonModeCache >= 0) return (GameMode)_jsonModeCache;
            var mode = LaunchConfig.Load().gameMode;
            _jsonModeCache = (int)mode;
            return mode;
        }
    }
}
