using System;
using System.Collections.Generic;
using System.IO;
using GameFramework.Resource;
using GameFramework.Runtime;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// 热更构建工具链。目标：把 HybridCLR 那段"必须手点十几次"的流程压成 3 个菜单。
    ///
    /// 约定目录：
    ///   Assets/BundleAssets/HotUpdate/ 热更 DLL（.dll.bytes）→ 由 YooAsset 收集器打包
    ///   Assets/Resources/AOTMetadata/  补元数据（.bytes）    → 随主包，不可热更
    ///   CDN/{平台}/{App版本}/          构建产物 = 可直接丢 CDN 的目录
    /// </summary>
    public static class HotUpdateBuildTool
    {
        public const string HotUpdateAssetDir = "Assets/BundleAssets/HotUpdate";
        public const string AotMetadataResourcesDir = "Assets/Resources/AOTMetadata";
        public const string LaunchConfigPath = "Assets/Resources/Config/launch.json";

        private static BuildTarget ActiveTarget { get { return EditorUserBuildSettings.activeBuildTarget; } }

        // ---------------- 0. 登记热更程序集 ----------------

        [MenuItem("框架/热更/0. 登记热更程序集到 HybridCLR", false, 9)]
        public static void RegisterHotUpdateAssemblies()
        {
            var cfg = LoadLaunchConfig();
            if (cfg == null) return;

            var settings = HybridCLRSettings.Instance;
            settings.enable = true;
            // ★ 只写名字列表，不写 hotUpdateAssemblyDefinitions：
            //   两者会合并，重复登记会抛 duplicated 异常。
            settings.hotUpdateAssemblies = cfg.hotUpdateAssemblies;
            settings.patchAOTAssemblies = cfg.aotMetadataAssemblies;
            HybridCLRSettings.Save();

            Debug.Log("[框架] 已登记热更程序集: " + string.Join(", ", cfg.hotUpdateAssemblies));
            EditorUtility.DisplayDialog("登记完成",
                "热更程序集: " + string.Join(", ", cfg.hotUpdateAssemblies) +
                "\n补元数据: " + string.Join(", ", cfg.aotMetadataAssemblies) +
                "\n\n接下来：HybridCLR/Installer → Install（只需一次）", "好");
        }

        // ---------------- 1. 编译热更 DLL ----------------

        [MenuItem("框架/热更/1. 编译热更 DLL → .bytes", false, 10)]
        public static void CompileHotUpdateDll()
        {
            var cfg = LoadLaunchConfig();
            if (cfg == null) return;

            try
            {
                EditorUtility.DisplayProgressBar("编译热更 DLL", "CompileDll…", 0.3f);
                CompileDllCommand.CompileDll(ActiveTarget);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("编译失败",
                    "HybridCLR 未初始化？先在菜单 HybridCLR/Installer 点 Install。\n\n" + e.Message, "知道了");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string dllDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(ActiveTarget);
            if (!Directory.Exists(dllDir))
            {
                EditorUtility.DisplayDialog("编译失败", "未找到编译输出目录:\n" + dllDir, "知道了");
                return;
            }

            EnsureDirectory(HotUpdateAssetDir);

            int copied = 0;
            var missing = new List<string>();
            foreach (var asm in cfg.hotUpdateAssemblies)
            {
                string src = Path.Combine(dllDir, asm + ".dll");
                if (!File.Exists(src)) { missing.Add(asm + ".dll"); continue; }

                string dst = Path.Combine(HotUpdateAssetDir, asm + ".dll.bytes");
                File.Copy(src, dst, true);
                copied++;
                Debug.Log("[框架] 热更 DLL → " + dst);
            }

            AssetDatabase.Refresh();

            string msg = string.Format("已拷贝 {0} 个热更程序集到 {1}", copied, HotUpdateAssetDir);
            if (missing.Count > 0) msg += "\n\n缺失（检查 launch.json 与 asmdef 名是否一致）:\n" + string.Join("\n", missing.ToArray());
            Debug.Log("[框架] " + msg);
            EditorUtility.DisplayDialog("编译热更 DLL", msg, "好");
        }

        // ---------------- 2. 生成 AOT 补元数据 ----------------

        [MenuItem("框架/热更/2. 生成 AOT 补元数据 → Resources", false, 11)]
        public static void GenerateAotMetadata()
        {
            var cfg = LoadLaunchConfig();
            if (cfg == null) return;

            string stripDir;
            try
            {
                EditorUtility.DisplayProgressBar("生成 AOT 元数据", "裁剪 AOT DLL…", 0.4f);
                StripAOTDllCommand.GenerateStripedAOTDlls(ActiveTarget);
                stripDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(ActiveTarget);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("生成失败",
                    "需要先安装 HybridCLR（HybridCLR/Installer），且至少成功出过一次 IL2CPP 包。\n\n" + e.Message,
                    "知道了");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EnsureDirectory(AotMetadataResourcesDir);

            int copied = 0;
            var missing = new List<string>();
            foreach (var asm in cfg.aotMetadataAssemblies)
            {
                string src = Path.Combine(stripDir, asm + ".dll");
                if (!File.Exists(src)) { missing.Add(asm + ".dll"); continue; }

                string dst = Path.Combine(AotMetadataResourcesDir, asm + ".bytes");
                File.Copy(src, dst, true);
                copied++;
            }

            AssetDatabase.Refresh();

            string msg = string.Format("已拷贝 {0} 个补元数据到 {1}", copied, AotMetadataResourcesDir);
            if (missing.Count > 0)
                msg += "\n\n缺失（若热更报『类型缺失』就必须补上）:\n" + string.Join("\n", missing.ToArray());
            Debug.Log("[框架] " + msg);
            EditorUtility.DisplayDialog("AOT 补元数据", msg, "好");
        }

        // ---------------- 3. 一键 GenerateAll ----------------

        [MenuItem("框架/热更/3. 一键 GenerateAll（DLL + link.xml + 补元）", false, 12)]
        public static void GenerateAll()
        {
            try
            {
                EditorUtility.DisplayProgressBar("HybridCLR", "GenerateAll（首次较慢）…", 0.5f);
                PrebuildCommand.GenerateAll();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("GenerateAll 失败",
                    "先确认 HybridCLR 已 Install（菜单 HybridCLR/Installer）。\n\n" + e.Message, "知道了");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // GenerateAll 只产出 DLL 与裁剪产物，仍要把它们转成 .bytes 进资源目录
            CompileHotUpdateDll();
            GenerateAotMetadata();
        }

        // ---------------- 4. YooAsset 面板 ----------------

        [MenuItem("框架/资源/打开 YooAsset 收集器", false, 20)]
        public static void OpenCollector()
        {
            if (!EditorApplication.ExecuteMenuItem("YooAsset/Bundle Collector"))
                EditorUtility.DisplayDialog("未找到菜单", "请确认已安装 YooAsset 3.x（Packages/com.tuyoogame.yooasset）", "知道了");
        }

        [MenuItem("框架/资源/打开 YooAsset 打包器", false, 21)]
        public static void OpenBuilder()
        {
            if (!EditorApplication.ExecuteMenuItem("YooAsset/Bundle Builder"))
                EditorUtility.DisplayDialog("未找到菜单", "请确认已安装 YooAsset 3.x", "知道了");
        }

        // ---------------- 5. 同步到 CDN ----------------

        [MenuItem("框架/资源/同步构建产物 → CDN 目录", false, 22)]
        public static void SyncToCdn()
        {
            var resCfg = ResourceConfig.Load();
            if (resCfg == null) return;

            string src = EditorUtility.OpenFolderPanel("选择 YooAsset 构建产物目录（含 .version 的那一层）", "", "");
            if (string.IsNullOrEmpty(src)) return;

            // 产物目录名通常就是资源版本号（YooAsset 用时间戳命名）
            string resVersion = new DirectoryInfo(src).Name;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string platform = PlatformFolderOf(ActiveTarget);
            string dst = Path.Combine(projectRoot, "CDN");
            dst = Path.Combine(dst, platform);
            dst = Path.Combine(dst, resCfg.appVersion);
            dst = Path.Combine(dst, resVersion);

            try
            {
                CopyDirectory(src, dst);
                Debug.Log("[框架] 构建产物已同步 → " + dst);
                EditorUtility.DisplayDialog("同步完成",
                    string.Format("目标目录:\n{0}\n\nCDN 根地址格式：\n{1}/{2}/{3}\n（resource.json 的 hostServer 填 CDN 根，appVersion 填 {2}）",
                        dst, platform, resCfg.appVersion, resVersion), "好");
                EditorUtility.RevealInFinder(dst);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("同步失败", e.Message, "知道了");
            }
        }

        // ---------------- 9. 自检 ----------------

        [MenuItem("框架/热更/9. 自检：配置一致性", false, 90)]
        public static void Validate()
        {
            var cfg = LoadLaunchConfig();
            if (cfg == null) return;

            var sb = new System.Text.StringBuilder();
            int errors = 0;

            // ① launch.json 的热更程序集必须登记在 HybridCLR Settings
            var settings = HybridCLRSettings.Instance;
            var registered = new HashSet<string>(settings.hotUpdateAssemblies ?? new string[0]);
            foreach (var asm in cfg.hotUpdateAssemblies)
            {
                if (!registered.Contains(asm))
                {
                    sb.AppendLine("✗ 未登记到 HybridCLR 热更列表: " + asm);
                    errors++;
                }
                else sb.AppendLine("✓ HybridCLR 已登记: " + asm);
            }

            // ② .bytes 是否已生成
            foreach (var asm in cfg.hotUpdateAssemblies)
            {
                string p = Path.Combine(HotUpdateAssetDir, asm + ".dll.bytes");
                if (!File.Exists(p)) { sb.AppendLine("✗ 缺少热更 DLL 资源: " + p); errors++; }
                else sb.AppendLine("✓ 热更 DLL 就绪: " + p);
            }

            // ③ 补元数据是否已生成
            foreach (var asm in cfg.aotMetadataAssemblies)
            {
                string p = Path.Combine(AotMetadataResourcesDir, asm + ".bytes");
                if (!File.Exists(p)) { sb.AppendLine("· 补元数据缺失（真机可能报类型缺失）: " + p); }
                else sb.AppendLine("✓ 补元数据就绪: " + p);
            }

            // ④ 热更 asmdef 的 Auto Referenced 必须关闭
            string asmdef = "Assets/Game/HotUpdate/Game.HotUpdate.asmdef";
            if (File.Exists(asmdef))
            {
                string json = File.ReadAllText(asmdef);
                if (json.Contains("\"autoReferenced\": false")) sb.AppendLine("✓ 热更 asmdef 已关闭 Auto Referenced");
                else { sb.AppendLine("✗ 热更 asmdef 的 autoReferenced 必须为 false"); errors++; }
            }

            // ⑤ 运行模式与 CDN 配置
            var resCfg = ResourceConfig.Load();
            sb.AppendLine("· 运行模式: " + resCfg.playMode + "，CDN 根: " +
                          (string.IsNullOrEmpty(resCfg.hostServer) ? "(未配置)" : resCfg.BuildRemoteRoot()));

            sb.AppendLine();
            sb.AppendLine(errors == 0 ? "结论：可以打包" : "结论：" + errors + " 项必须修复");
            Debug.Log("[框架自检]\n" + sb);
            EditorUtility.DisplayDialog("框架自检", sb.ToString(), "好");
        }

        [MenuItem("框架/资源/打开 StreamingAssets", false, 23)]
        public static void OpenStreamingAssets()
        {
            EditorUtility.RevealInFinder(Application.streamingAssetsPath);
        }

        // ---------------- 工具方法 ----------------

        public static LaunchConfig LoadLaunchConfig()
        {
            if (!File.Exists(LaunchConfigPath))
            {
                EditorUtility.DisplayDialog("缺少配置", "找不到 " + LaunchConfigPath, "知道了");
                return null;
            }
            try
            {
                var cfg = JsonUtility.FromJson<LaunchConfig>(File.ReadAllText(LaunchConfigPath));
                if (cfg == null) throw new Exception("解析结果为空");
                return cfg;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("配置解析失败", LaunchConfigPath + "\n" + e.Message, "知道了");
                return null;
            }
        }

        public static string PlatformFolderOf(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                case BuildTarget.WebGL: return "WebGL";
                default: return target.ToString();
            }
        }

        private static void EnsureDirectory(string assetPath)
        {
            if (!Directory.Exists(assetPath)) Directory.CreateDirectory(assetPath);
        }

        private static void CopyDirectory(string src, string dst)
        {
            if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);

            foreach (var file in Directory.GetFiles(src))
            {
                string target = Path.Combine(dst, Path.GetFileName(file));
                File.Copy(file, target, true);
            }
            foreach (var dir in Directory.GetDirectories(src))
            {
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }
        }
    }
}
