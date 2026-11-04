using UnityEditor;
using YooAsset.Editor;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// 资源收集忽略规则：在 YooAsset 默认规则之上，**静默**跳过文档类文件。
    ///
    /// 为什么需要它：
    /// `BundleAssets/` 每个目录都有 `README.md` 写规范。Unity 把 `.md` 导入为 `DefaultAsset`，
    /// 而 YooAsset 的 `NormalIgnoreRule` 对 DefaultAsset 会走 `Debug.LogWarning("Default asset cannot be packed")`
    /// —— 结果是每打一次包就刷几十条黄字，把真正的警告埋掉。
    ///
    /// 这里提前返回 true，既不进包也不打日志。其余资源仍然交给默认规则处理，行为不变。
    ///
    /// 用法：`BundleCollectorPackage.IgnoreRuleName = nameof(DocIgnoreRule)`（由
    /// 菜单「框架/资源/初始化 YooAsset 收集器」自动写入）。
    /// </summary>
    public class DocIgnoreRule : IAssetIgnoreRule
    {
        private static readonly NormalIgnoreRule Fallback = new NormalIgnoreRule();

        /// <summary>不进包的文档/工程类扩展名（小写，含点）</summary>
        private static readonly string[] DocExtensions =
        {
            ".md", ".txt", ".pdf", ".doc", ".docx", ".xlsx", ".xls", ".ppt", ".pptx",
            ".keep", ".gitkeep", ".gitignore", ".ini", ".log",
        };

        public bool IsIgnoreAsset(EditorAssetInfo assetInfo)
        {
            string ext = (assetInfo.FileExtension ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i < DocExtensions.Length; i++)
            {
                if (ext == DocExtensions[i]) return true;
            }
            return Fallback.IsIgnoreAsset(assetInfo);
        }
    }
}
