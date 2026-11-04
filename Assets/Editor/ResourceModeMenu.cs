using GameFramework.Resource;
using UnityEditor;
using UnityEngine;
// ★ Unity 也有 UnityEngine.PlayMode（播放状态），与 GameFramework.Resource.PlayMode 同名。
//   本文件同时 using 两边 → 必须显式取名，否则 CS0104 二义性。
using PlayMode = GameFramework.Resource.PlayMode;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// 运行模式快速切换（仅编辑器生效，不动 resource.json，不影响真机）。
    ///
    /// 为什么需要：四种模式各自对应 YooAsset 的不同初始化方式，开发期要反复切
    /// （日常 Editor 模拟 → 验热更切 Host → 打包前验 Offline → 小游戏验 Web）。
    /// 每次手改 json 再重启很容易忘、也容易把 Host 的配置误提交。
    ///
    /// 实现：覆写值存 EditorPrefs，`ResourceConfig.Load()` 里读它覆盖 json 的 playMode。
    /// 真机永远以 resource.json 为准 —— 这个覆盖只在 UNITY_EDITOR 编译单元里存在。
    /// </summary>
    public static class ResourceModeMenu
    {
        private const string Root = "框架/资源/运行模式/";

        private const string ItemEditor = Root + "编辑器模拟（EditorSimulate）";
        private const string ItemOffline = Root + "离线单机（Offline）";
        private const string ItemHost = Root + "联机热更（Host）";
        private const string ItemWeb = Root + "Web / 小游戏（Web）";

        // ---------------- 切换 ----------------

        [MenuItem(ItemEditor, false, 30)]
        public static void UseEditorSimulate() { SetMode(PlayMode.EditorSimulate); }

        [MenuItem(ItemOffline, false, 31)]
        public static void UseOffline() { SetMode(PlayMode.Offline); }

        [MenuItem(ItemHost, false, 32)]
        public static void UseHost() { SetMode(PlayMode.Host); }

        [MenuItem(ItemWeb, false, 33)]
        public static void UseWeb() { SetMode(PlayMode.Web); }

        [MenuItem(Root + "清除覆盖（跟随 resource.json）", false, 40)]
        public static void ClearOverride()
        {
            EditorPrefs.DeleteKey(ResourceConfig.EditorPlayModeKey);
            Debug.Log("[框架] 已清除运行模式覆盖，下次 Play 使用 resource.json 的值：" +
                      ResourceConfig.Load().playMode);
        }

        // ---------------- 勾选状态 ----------------

        [MenuItem(ItemEditor, true)]
        public static bool VEditor() { Check(ItemEditor, PlayMode.EditorSimulate); return true; }

        [MenuItem(ItemOffline, true)]
        public static bool VOffline() { Check(ItemOffline, PlayMode.Offline); return true; }

        [MenuItem(ItemHost, true)]
        public static bool VHost() { Check(ItemHost, PlayMode.Host); return true; }

        [MenuItem(ItemWeb, true)]
        public static bool VWeb() { Check(ItemWeb, PlayMode.Web); return true; }

        // ---------------- 状态查看 ----------------

        [MenuItem("框架/资源/打印当前资源模式", false, 41)]
        public static void PrintStatus()
        {
            var ta = Resources.Load<TextAsset>("Config/resource");
            PlayMode jsonMode = PlayMode.EditorSimulate;
            if (ta != null)
            {
                try
                {
                    var cfg = JsonUtility.FromJson<ResourceConfig>(ta.text);
                    if (cfg != null) jsonMode = cfg.playMode;
                }
                catch { }
            }

            int overrideMode = EditorPrefs.GetInt(ResourceConfig.EditorPlayModeKey, -1);
            bool hasOverride = overrideMode >= 0 && System.Enum.IsDefined(typeof(PlayMode), overrideMode);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[资源运行模式]");
            sb.AppendLine("  resource.json  : " + jsonMode);
            sb.AppendLine("  编辑器覆盖    : " + (hasOverride ? ((PlayMode)overrideMode).ToString() : "(无)"));
            sb.AppendLine("  实际生效      : " + ResourceConfig.ResolveMode());
            sb.AppendLine();
            sb.AppendLine("  说明：EditorSimulate=直接读 AssetDatabase；Offline=只读包内资源；");
            sb.AppendLine("        Host=内置兜底+远端热更；Web=全部走 HTTP（WebGL/小游戏）");
            Debug.Log(sb.ToString());
        }

        // ---------------- 内部 ----------------

        private static void SetMode(PlayMode mode)
        {
            EditorPrefs.SetInt(ResourceConfig.EditorPlayModeKey, (int)mode);
            Debug.Log("[框架] 资源运行模式改为 " + mode + "（下次 Play 生效；真机不受影响）");

            if (mode == PlayMode.Host || mode == PlayMode.Web)
            {
                var cfg = ResourceConfig.Load();
                if (string.IsNullOrEmpty(cfg.hostServer))
                {
                    Debug.LogWarning("[框架] 当前 hostServer 为空：" + mode +
                                     " 模式会走远端，但 CDN 地址未配置，请求版本会失败。\n" +
                                     "请在 Assets/Resources/Config/resource.json 填 hostServer。");
                }
            }
        }

        private static void Check(string item, PlayMode mode)
        {
            // 勾选状态每次菜单重绘都会算，这里刻意避开 Load()（会读 Resources + 解析 json），
            // 改用 EditorPrefs + 一次缓存，避免菜单卡顿与日志刷屏。
            int ov = EditorPrefs.GetInt(ResourceConfig.EditorPlayModeKey, -1);
            bool active = ov >= 0
                ? (PlayMode)ov == mode
                : JsonPlayMode() == mode;
            Menu.SetChecked(item, active);
        }

        private static int _jsonModeCache = -1;

        private static PlayMode JsonPlayMode()
        {
            if (_jsonModeCache >= 0) return (PlayMode)_jsonModeCache;

            var mode = PlayMode.EditorSimulate;
            var ta = Resources.Load<TextAsset>("Config/resource");
            if (ta != null)
            {
                try
                {
                    var cfg = JsonUtility.FromJson<ResourceConfig>(ta.text);
                    if (cfg != null) mode = cfg.playMode;
                }
                catch { }
            }
            _jsonModeCache = (int)mode;
            return mode;
        }
    }
}
