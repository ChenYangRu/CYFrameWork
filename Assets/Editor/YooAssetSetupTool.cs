using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameFramework.Resource;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// YooAsset 收集器一键初始化。
    ///
    /// 为什么要有这个：
    /// YooAsset 3.x 的收集配置是 ScriptableObject，新工程必须先在面板里点点点，
    /// 否则 EditorSimulateMode 直接抛"没有收集配置"——新同事拉代码第一天就跑不起来。
    /// 这里按 `Assets/BundleAssets/` 的标准目录结构，用官方 Editor API 生成一份完整配置（幂等）。
    ///
    /// 目录规范见 `Assets/BundleAssets/README.md`；收集分组见下方 Specs 表。
    /// </summary>
    public static class YooAssetSetupTool
    {
        /// <summary>资源根目录（一切进包资源都在这下面）</summary>
        public const string Root = "Assets/BundleAssets";

        private const string BuiltinTag = "builtin";
        private const string HotfixTag = "hotfix";

        private class Spec
        {
            public string Group;
            public string Folder;
            public string Tag;

            public Spec(string group, string folder, string tag)
            {
                Group = group;
                Folder = folder;
                Tag = tag;
            }
        }

        /// <summary>
        /// 分组表。改这里 = 改打包结构，**不要**再去面板手工点。
        /// builtin = 进首包；hotfix = 走 CDN 增量。
        /// </summary>
        private static readonly Spec[] Specs =
        {
            // 首包：进游戏前必须有，否则首屏空白 / 材质丢 shader
            new Spec("Builtin",  Root + "/UI/Common",       BuiltinTag),
            new Spec("Builtin",  Root + "/Config",          BuiltinTag),
            new Spec("Builtin",  Root + "/Text",            BuiltinTag),
            new Spec("Builtin",  Root + "/Shader",          BuiltinTag),
            new Spec("Builtin",  Root + "/Art/Icon",        BuiltinTag),

            // 界面：按模块下载
            new Spec("UI",       Root + "/UI/Login",        HotfixTag),
            new Spec("UI",       Root + "/UI/Lobby",        HotfixTag),
            new Spec("UI",       Root + "/UI/Battle",       HotfixTag),
            new Spec("UI",       Root + "/UI/Shop",         HotfixTag),

            // 美术：进入战斗前预下载
            new Spec("Art",      Root + "/Art/Character",   HotfixTag),
            new Spec("Art",      Root + "/Art/Prop",        HotfixTag),
            new Spec("Art",      Root + "/Art/Effect",      HotfixTag),
            new Spec("Art",      Root + "/Art/Scene",       HotfixTag),

            // 音频
            new Spec("Audio",    Root + "/Audio/Bgm",       HotfixTag),
            new Spec("Audio",    Root + "/Audio/Sfx",       HotfixTag),
            new Spec("Audio",    Root + "/Audio/Voice",     HotfixTag),

            // 视频：点播，永不预载
            new Spec("Video",    Root + "/Video",           HotfixTag),

        };

        [MenuItem("框架/资源/初始化 YooAsset 收集器（首次必点）", false, 10)]
        public static void InitCollector()
        {
            var resCfg = ResourceConfig.Load();
            string packageName = resCfg != null && !string.IsNullOrEmpty(resCfg.packageName)
                ? resCfg.packageName
                : "GamePackage";

            foreach (var spec in Specs) EnsureFolder(spec.Folder);

            var setting = BundleCollectorSettingData.Setting;   // 不存在会自动创建 asset
            if (setting == null)
            {
                EditorUtility.DisplayDialog("失败", "无法创建 BundleCollectorSetting，请检查 YooAsset 是否安装完整", "知道了");
                return;
            }

            setting.ShowPackageView = true;

            var package = setting.Packages.FirstOrDefault(p => p.PackageName == packageName);
            if (package == null)
            {
                package = new BundleCollectorPackage
                {
                    PackageName = packageName,
                    PackageDesc = "游戏主资源包（由框架自动创建）",
                    EnableAddressable = false,
                    SupportExtensionless = true,   // location 不带扩展名
                };
                setting.Packages.Add(package);
                Debug.Log("[框架] 新建资源包裹: " + packageName);
            }

            // ★ 关键：用自定义忽略规则，静默跳过各目录里的 README.md，避免打包时刷一屏警告
            package.IgnoreRuleName = nameof(DocIgnoreRule);

            // 自愈：清掉模板自带 / 历史遗留的失效收集器
            // （YooAsset 新建配置会带一个 Default Group + 空 CollectPath，留着会导致构建报错）
            int cleaned = CleanupStale(package);

            int added = 0;
            foreach (var spec in Specs)
            {
                if (AddCollector(package, spec)) added++;
            }

            BundleCollectorSettingData.SaveFile();
            AssetDatabase.SaveAssets();
            // ★ 不要在这里 AssetDatabase.Refresh()：
            //   它会触发资源重新导入（可能连带域重载），而紧跟着就要弹模态对话框，
            //   结果是对话框持有的 GUI 状态被销毁 → Unity 每帧刷 "the GUIStateObj is deleted"，
            //   能把 Editor.log 刷到几十 MB，排障时真错误全被淹没。
            //   SaveFile() 内部已 SaveAssets，足够落盘。

            var groups = package.Groups.Select(g => g.GroupName + "(" + g.Collectors.Count + ")").ToArray();
            string detail = string.Join("  ", groups);

            EditorUtility.DisplayDialog("YooAsset 收集器已初始化",
                string.Format(
                    "包裹：{0}\n忽略规则：{1}\n\n新增收集器 {2} 个，清理失效 {3} 个\n\n分组：\n{4}\n\n" +
                    "资源根目录：{5}\n规范见该目录下的 README.md",
                    packageName, package.IgnoreRuleName, added, cleaned, detail, Root),
                "好");

            Debug.Log("[框架] 收集器分组: " + detail);
        }

        /// <summary>
        /// 清掉失效收集器：空路径、目录已删除、或者不在 BundleAssets 下的。
        /// 清空后变成空的分组也一并删除（下次按 Specs 重建）。
        /// 返回移除的收集器数量。
        /// </summary>
        private static int CleanupStale(BundleCollectorPackage package)
        {
            int removed = 0;
            for (int i = package.Groups.Count - 1; i >= 0; i--)
            {
                var group = package.Groups[i];
                for (int j = group.Collectors.Count - 1; j >= 0; j--)
                {
                    var c = group.Collectors[j];
                    bool invalid = string.IsNullOrEmpty(c.CollectPath)
                                   || !Directory.Exists(c.CollectPath)
                                   || !c.CollectPath.Replace('\\', '/').StartsWith(Root + "/");
                    if (invalid)
                    {
                        Debug.Log("[框架] 移除失效收集器: " + (string.IsNullOrEmpty(c.CollectPath) ? "(空路径)" : c.CollectPath)
                                  + " @ " + group.GroupName);
                        group.Collectors.RemoveAt(j);
                        removed++;
                    }
                }
            }

            for (int i = package.Groups.Count - 1; i >= 0; i--)
            {
                if (package.Groups[i].Collectors.Count == 0)
                {
                    Debug.Log("[框架] 移除空分组: " + package.Groups[i].GroupName);
                    package.Groups.RemoveAt(i);
                }
            }
            return removed;
        }

        /// <summary>把一条 spec 落到"分组 + 收集器"上。返回 true 表示新建了收集器。</summary>
        private static bool AddCollector(BundleCollectorPackage package, Spec spec)
        {
            var group = package.Groups.FirstOrDefault(g => g.GroupName == spec.Group);
            if (group == null)
            {
                group = new BundleCollectorGroup
                {
                    GroupName = spec.Group,
                    GroupDesc = "框架自动创建（见 BundleAssets/README.md）",
                    ActiveRuleName = nameof(EnableGroup),
                };
                package.Groups.Add(group);
                Debug.Log("[框架] 新建分组: " + spec.Group);
            }

            var existing = group.Collectors.FirstOrDefault(c => c.CollectPath == spec.Folder);
            if (existing != null)
            {
                // 幂等：已存在则同步规则与 tag，避免历史配置残留（改过寻址规则要能覆盖过来）
                existing.AddressRuleName = nameof(AddressByPathInBundleAssets);
                existing.PackRuleName = nameof(PackDirectory);
                existing.FilterRuleName = nameof(CollectAll);
                existing.AssetTags = spec.Tag;
                existing.CollectorGUID = AssetDatabase.AssetPathToGUID(spec.Folder);
                return false;
            }

            group.Collectors.Add(new BundleCollector
            {
                CollectPath = spec.Folder,
                CollectorGUID = AssetDatabase.AssetPathToGUID(spec.Folder),
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByPathInBundleAssets), // location = 相对路径去扩展名
                PackRuleName = nameof(PackDirectory),          // 一目录一 bundle
                FilterRuleName = nameof(CollectAll),
                AssetTags = spec.Tag,
                UserData = string.Empty,
            });
            Debug.Log("[框架] 新增收集器: " + spec.Group + " ← " + spec.Folder + "  [" + spec.Tag + "]");
            return true;
        }

        /// <summary>
        /// 建目录（只建目录，不写文件）。各目录的 README.md 由框架随仓库提供，
        /// 这里不覆盖，避免每次初始化把人工补充的规范冲掉。
        /// </summary>
        private static void EnsureFolder(string assetPath)
        {
            if (Directory.Exists(assetPath)) return;
            Directory.CreateDirectory(assetPath);
            AssetDatabase.Refresh();
        }
    }
}
