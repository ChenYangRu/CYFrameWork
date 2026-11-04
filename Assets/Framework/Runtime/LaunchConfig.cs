using System;

namespace GameFramework.Runtime
{
    /// <summary>
    /// 启动配置。来源：Resources/Config/launch.json。
    /// ★ 三份名单必须与 HybridCLR Settings / YooAsset 收集器保持一致，否则真机必炸：
    ///   hotUpdateAssemblies  ⊆ HybridCLR "Hot Update Assemblies"
    ///   aotMetadataAssemblies ⊆ HybridCLR "补充元数据" 列表
    /// </summary>
    [Serializable]
    public class LaunchConfig
    {
        /// <summary>热更程序集名（不含 .dll），顺序即加载顺序（被依赖的放前面）</summary>
        public string[] hotUpdateAssemblies = { "Game.HotUpdate" };

        /// <summary>
        /// 需要补充元数据的 AOT 程序集名（不含 .dll）。
        /// 只列"热更代码引用到、但主包可能被 IL2CPP 裁切"的那些；越小启动越快。
        /// </summary>
        public string[] aotMetadataAssemblies = { "mscorlib", "System", "System.Core" };

        /// <summary>
        /// 热更 DLL 在 YooAsset 里的目录前缀。
        /// 实际 location = {root}/{程序集名}.dll
        /// （收集器用 AddressByPathInBundleAssets：相对 BundleAssets 的路径，只剥一层扩展名）
        /// </summary>
        public string hotUpdateLocationRoot = "HotUpdate";

        /// <summary>热更入口类全名</summary>
        public string entryType = "Game.HotUpdate.GameEntry";
        /// <summary>热更入口静态方法名（无参）</summary>
        public string entryMethod = "Start";

        /// <summary>补元数据字节放 Resources 下（随包，不可热更——必须与主包 IL2CPP 完全一致）</summary>
        public string aotMetadataResourceRoot = "AOTMetadata";

        /// <summary>编辑器下跳过补元数据（Mono/JIT 环境不需要）</summary>
        public bool skipMetadataInEditor = true;

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
