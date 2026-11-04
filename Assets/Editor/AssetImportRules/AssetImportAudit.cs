using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.ImportRules
{
    /// <summary>
    /// 导入规则审计。
    ///
    /// 为什么需要：`AssetPostprocessor` **只在导入时生效**。存量资源（同事拉下来的、
    /// 从旧工程拷进来的）不会自动符合规则，必须在打包前体检一次并强制重导，
    /// 否则"规范写在文档里、资源实际是 RGBA32"——真机内存爆了才被发现。
    ///
    /// 依赖 userData 版本戳（`gfrule:N`）：规则表改了版本号 +1，这里就会把存量资源标出来。
    /// </summary>
    public static class AssetImportAudit
    {
        private const int MaxReportLines = 30;

        [MenuItem("框架/资源/导入规则/审计（只报告，不改文件）", false, 60)]
        public static void Audit()
        {
            var targets = CollectTargets();
            int total = 0, bad = 0;
            var sb = new StringBuilder();
            var badSummary = new Dictionary<string, int>();

            for (int i = 0; i < targets.Count; i++)
            {
                string path = targets[i];
                ImportKind kind;
                ImportRule rule;
                if (!TryResolve(path, out kind, out rule)) continue;

                total++;
                var importer = AssetImporter.GetAtPath(path);
                var problems = Check(importer, kind, rule);
                if (problems.Count == 0) continue;

                bad++;
                for (int k = 0; k < problems.Count; k++)
                {
                    string key = problems[k];
                    int n;
                    badSummary.TryGetValue(key, out n);
                    badSummary[key] = n + 1;
                }

                if (bad <= MaxReportLines)
                    sb.AppendLine("  " + path + "\n      → " + string.Join("; ", problems.ToArray()));
            }

            var head = new StringBuilder();
            head.AppendLine("[导入规则审计] 受管资源 " + total + " 个，不合规 " + bad + " 个");
            if (bad > MaxReportLines) head.AppendLine("（下面只列前 " + MaxReportLines + " 个，完整清单看分类统计）");

            head.AppendLine();
            head.AppendLine("按问题分类：");
            foreach (var kv in badSummary)
                head.AppendLine("  - " + kv.Key + " × " + kv.Value);

            if (bad > 0) head.AppendLine("\n明细：").Append(sb);

            Debug.Log(head.ToString());

            EditorUtility.DisplayDialog("导入规则审计",
                string.Format("受管资源 {0} 个\n不合规 {1} 个\n\n{2}", total, bad,
                    bad == 0
                        ? "全部符合当前规则版本（v" + ImportRuleTable.RuleVersion + "）"
                        : "明细已打印到 Console；\n执行菜单「按规则重导」可批量修复"),
                "好");
        }

        [MenuItem("框架/资源/导入规则/按规则重导（耗时，改规则后用）", false, 61)]
        public static void ReimportAll()
        {
            var targets = CollectTargets();

            // 只重导"有规则且当前不合规"的，避免全量重导几百个文件白等
            var todo = new List<string>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                ImportKind kind;
                ImportRule rule;
                if (!TryResolve(targets[i], out kind, out rule)) continue;

                var importer = AssetImporter.GetAtPath(targets[i]);
                if (Check(importer, kind, rule).Count > 0) todo.Add(targets[i]);
            }

            if (todo.Count == 0)
            {
                EditorUtility.DisplayDialog("无需重导", "所有受管资源都已符合当前规则（v" + ImportRuleTable.RuleVersion + "）。", "好");
                return;
            }

            if (!EditorUtility.DisplayDialog("确认重导",
                "将按规则重导 " + todo.Count + " 个资源。\n数量多时可能需要几分钟，期间编辑器会卡。", "开始", "取消"))
                return;

            try
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("按规则重导", todo[i], (float)i / todo.Count);
                    AssetDatabase.ImportAsset(todo[i],
                        ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            Debug.Log("[导入规则] 已重导 " + todo.Count + " 个资源");
            EditorUtility.DisplayDialog("完成", "已重导 " + todo.Count + " 个资源。\n建议再跑一次审计确认。", "好");
        }

        // ---------------- 内部 ----------------

        private static List<string> CollectTargets()
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(ImportRuleTable.Root.TrimEnd('/'))) return list;

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { ImportRuleTable.Root.TrimEnd('/') });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;

                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".tga" || ext == ".jpg" || ext == ".jpeg" || ext == ".psd"
                    || ext == ".wav" || ext == ".ogg" || ext == ".mp3"
                    || ext == ".fbx" || ext == ".obj")
                {
                    list.Add(path);
                }
            }
            return list;
        }

        private static bool TryResolve(string assetPath, out ImportKind kind, out ImportRule rule)
        {
            rule = null;
            string ext = System.IO.Path.GetExtension(assetPath).ToLowerInvariant();

            if (ext == ".png" || ext == ".tga" || ext == ".jpg" || ext == ".jpeg" || ext == ".psd")
                kind = ImportKind.Texture;
            else if (ext == ".wav" || ext == ".ogg" || ext == ".mp3")
                kind = ImportKind.Audio;
            else if (ext == ".fbx" || ext == ".obj")
                kind = ImportKind.Model;
            else
            {
                kind = ImportKind.Texture;
                return false;
            }

            return TryGetRuleStatic(assetPath, kind, out rule);
        }

        private static bool TryGetRuleStatic(string assetPath, ImportKind kind, out ImportRule rule)
        {
            rule = null;
            string rel;
            if (!ImportRuleTable.InScope(assetPath, out rel)) return false;
            rule = ImportRuleTable.Find(rel, kind);
            return rule != null;
        }

        /// <summary>返回问题描述列表（空 = 合规）</summary>
        private static List<string> Check(AssetImporter importer, ImportKind kind, ImportRule rule)
        {
            var problems = new List<string>(4);
            if (importer == null)
            {
                problems.Add("取不到 importer");
                return problems;
            }

            if (!AssetImportRuleBase.HasCurrentVersion(importer))
                problems.Add("规则版本落后（未按 v" + ImportRuleTable.RuleVersion + " 导入过）");

            if (kind == ImportKind.Texture)
            {
                var t = importer as TextureImporter;
                if (t == null) { problems.Add("不是 TextureImporter"); return problems; }
                if (t.maxTextureSize != rule.MaxSize) problems.Add("maxSize=" + t.maxTextureSize + "≠" + rule.MaxSize);
                if (t.mipmapEnabled != rule.Mipmap) problems.Add("mipmap=" + t.mipmapEnabled + "≠" + rule.Mipmap);
                if (t.isReadable != rule.Readable) problems.Add("Read/Write 开着");
                if (t.textureType != rule.TexType) problems.Add("类型=" + t.textureType + "≠" + rule.TexType);
            }
            else if (kind == ImportKind.Audio)
            {
                var a = importer as AudioImporter;
                if (a == null) { problems.Add("不是 AudioImporter"); return problems; }
                var s = a.defaultSampleSettings;
                if (s.loadType != rule.LoadType) problems.Add("loadType=" + s.loadType + "≠" + rule.LoadType);
                if (s.compressionFormat != rule.AudioFormat) problems.Add("格式=" + s.compressionFormat);
                if (a.forceToMono != rule.ForceMono) problems.Add("单声道=" + a.forceToMono + "≠" + rule.ForceMono);
            }
            else
            {
                var m = importer as ModelImporter;
                if (m == null) { problems.Add("不是 ModelImporter"); return problems; }
                if (m.meshCompression != rule.MeshCompression) problems.Add("网格压缩=" + m.meshCompression);
                if (m.isReadable) problems.Add("Read/Write 开着");
                if (m.importAnimation != rule.ImportAnimation) problems.Add("导入动画=" + m.importAnimation + "≠" + rule.ImportAnimation);
            }

            return problems;
        }
    }
}
