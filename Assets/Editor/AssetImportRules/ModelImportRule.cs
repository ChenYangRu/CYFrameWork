using UnityEditor;

namespace GameFramework.EditorTools.ImportRules
{
    /// <summary>
    /// 模型导入规则：只做三件安全的事 —— 网格压缩、关 Read/Write、优化网格 / 按目录决定是否导入动画。
    ///
    /// 刻意**不碰**这几项（改错会直接毁资源或需要美术重新导出）：
    ///   · `animationType`（Humanoid / Generic 必须人工定，猜错骨架就废）
    ///   · 材质生成（`materialImportMode`，项目若用自研 shader 需手工配置）
    ///   · `globalScale`（改缩放会让所有挂点错位）
    /// </summary>
    internal class ModelImportRule : AssetImportRuleBase
    {
        private void OnPreprocessModel()
        {
            ImportRule rule;
            if (!TryGetRule(assetPath, ImportKind.Model, out rule)) return;

            var importer = assetImporter as ModelImporter;
            if (importer == null) return;

            Apply(importer, rule);
            StampVersion(importer);
        }

        internal static void Apply(ModelImporter importer, ImportRule rule)
        {
            importer.meshCompression = rule.MeshCompression;
            importer.isReadable = false;               // 运行时不需要 CPU 侧顶点数据
            importer.optimizeMesh = rule.OptimizeMesh;
            importer.importAnimation = rule.ImportAnimation;

            // 场景里不会用到的节点类型，一律不导入（省内存 + 省导入时间）
            importer.importCameras = false;
            importer.importLights = false;
        }
    }
}
