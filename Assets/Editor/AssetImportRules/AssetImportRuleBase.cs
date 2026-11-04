using System.Collections.Generic;
using UnityEditor;

namespace GameFramework.EditorTools.ImportRules
{
    /// <summary>
    /// 导入规则基类：提供"是否受管 / 取规则 / 版本标记"三件事，三个具体 importer 共用。
    ///
    /// ★ 版本标记写在 `AssetImporter.userData` 里（形如 `gfrule:1`），用途有两个：
    ///   ① 审计工具据此判断"这个资源是不是按当前规则导入过"；
    ///   ② 不抢占别的工具的 userData —— 用分号分段，只维护自己那一段。
    ///
    /// 注意：本类不声明任何 OnPreprocess*，因此不会被 Unity 当作有效 postprocessor 调用，
    /// 只是给三个子类提供公共方法（写成普通类而非 abstract，避免 Unity 反射时的边界问题）。
    /// </summary>
    internal class AssetImportRuleBase : AssetPostprocessor
    {
        private const string TagPrefix = "gfrule:";

        protected static string CurrentTag
        {
            get { return TagPrefix + ImportRuleTable.RuleVersion; }
        }

        /// <summary>取当前资源的规则；不在 BundleAssets 下或该类型无规则时返回 false</summary>
        protected static bool TryGetRule(string assetPath, ImportKind kind, out ImportRule rule)
        {
            rule = null;
            string rel;
            if (!ImportRuleTable.InScope(assetPath, out rel)) return false;
            rule = ImportRuleTable.Find(rel, kind);
            return rule != null;
        }

        /// <summary>把当前规则版本写进 userData（清掉自己的旧段，保留其它工具的段）</summary>
        protected static void StampVersion(AssetImporter importer)
        {
            if (importer == null) return;

            string tag = CurrentTag;
            var kept = new List<string>(4);
            string current = importer.userData ?? string.Empty;

            string[] parts = current.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (p.StartsWith(TagPrefix)) continue;   // 丢掉自己的旧版本号
                kept.Add(p);
            }
            kept.Add(tag);

            importer.userData = string.Join(";", kept.ToArray());
        }

        /// <summary>该资源是否已按当前规则版本导入过</summary>
        internal static bool HasCurrentVersion(AssetImporter importer)
        {
            if (importer == null) return false;
            string data = importer.userData ?? string.Empty;
            return data.Contains(CurrentTag);
        }
    }
}
