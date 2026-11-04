using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.ImportRules
{
    internal enum ImportKind
    {
        Texture,
        Audio,
        Model,
    }

    /// <summary>
    /// 一条导入规则。字段按 Kind 分组，未用到的字段忽略。
    /// ★ 这里的数值就是 `Assets/BundleAssets/README.md` 里那套手游限制的**唯一代码真相源**，
    ///   改限制改这里，不要改 importer。
    /// </summary>
    internal class ImportRule
    {
        public string PathPrefix;
        public ImportKind Kind;

        // ---- Texture ----
        public TextureImporterType TexType = TextureImporterType.Default;
        public int MaxSize = 1024;
        public bool Mipmap = true;
        public bool Readable = false;
        public TextureImporterCompression Compression = TextureImporterCompression.Compressed;
        public bool Crunched = false;
        public int CompressionQuality = 50;
        public TextureImporterFormat FormatAndroid = TextureImporterFormat.ETC2_RGBA8;
        public TextureImporterFormat FormatIos = TextureImporterFormat.ASTC_6x6;

        // ---- Audio ----
        public AudioClipLoadType LoadType = AudioClipLoadType.DecompressOnLoad;
        public AudioCompressionFormat AudioFormat = AudioCompressionFormat.Vorbis;
        public float AudioQuality = 0.7f;
        public bool ForceMono = false;

        // ---- Model ----
        public ModelImporterMeshCompression MeshCompression = ModelImporterMeshCompression.Medium;
        public bool ImportAnimation = false;
        public bool OptimizeMesh = true;
    }

    /// <summary>
    /// 导入规则表：**路径前缀 → 规则**，最长前缀优先。
    ///
    /// 作用范围只有 `Assets/BundleAssets/` 下（见 AssetImportRuleBase.InScope），
    /// 绝不碰 YooAsset / HybridCLR 包内或工程其它资源。
    ///
    /// 新增目录的步骤：在 Rules 里加一行 → 同步更新该目录的 README → 跑菜单「按规则重导」。
    /// </summary>
    internal static class ImportRuleTable
    {
        public const string Root = "Assets/BundleAssets/";

        /// <summary>规则版本：改规则后 +1，已有资源会被判定为"待重导"</summary>
        public const int RuleVersion = 1;

        public static readonly List<ImportRule> Rules = new List<ImportRule>
        {
            // ================= 纹理 =================
            // UI：图集不压缩缓存、关 mipmap、关 Read/Write、2048 上限
            new ImportRule
            {
                PathPrefix = "UI/",
                Kind = ImportKind.Texture,
                TexType = TextureImporterType.Sprite,
                MaxSize = 2048,
                Mipmap = false,
                Readable = false,
                Compression = TextureImporterCompression.CompressedHQ,
                CompressionQuality = 80,
                FormatAndroid = TextureImporterFormat.ETC2_RGBA8,
                FormatIos = TextureImporterFormat.ASTC_6x6,
            },

            // 图标：128、关 mipmap、crunched（体积优先）
            new ImportRule
            {
                PathPrefix = "Art/Icon/",
                Kind = ImportKind.Texture,
                TexType = TextureImporterType.Sprite,
                MaxSize = 128,
                Mipmap = false,
                Readable = false,
                Compression = TextureImporterCompression.Compressed,
                Crunched = true,
                CompressionQuality = 50,
                FormatAndroid = TextureImporterFormat.ETC2_RGBA8,
                FormatIos = TextureImporterFormat.ASTC_6x6,
            },

            // 角色：1024、开 mipmap（3D 空间）
            new ImportRule
            {
                PathPrefix = "Art/Character/",
                Kind = ImportKind.Texture,
                TexType = TextureImporterType.Default,
                MaxSize = 1024,
                Mipmap = true,
                Readable = false,
                FormatAndroid = TextureImporterFormat.ETC2_RGBA8,
                FormatIos = TextureImporterFormat.ASTC_6x6,
            },

            // 道具 / 场景：1024
            new ImportRule
            {
                PathPrefix = "Art/Prop/",
                Kind = ImportKind.Texture,
                MaxSize = 1024,
                Mipmap = true,
                Readable = false,
                FormatAndroid = TextureImporterFormat.ETC2_RGBA8,
                FormatIos = TextureImporterFormat.ASTC_6x6,
            },
            new ImportRule
            {
                PathPrefix = "Art/Scene/",
                Kind = ImportKind.Texture,
                MaxSize = 1024,
                Mipmap = true,
                Readable = false,
                FormatAndroid = TextureImporterFormat.ETC2_RGBA8,
                FormatIos = TextureImporterFormat.ASTC_6x6,
            },

            // 特效：256，3D 空间需要 mipmap 抑制闪烁
            new ImportRule
            {
                PathPrefix = "Art/Effect/",
                Kind = ImportKind.Texture,
                MaxSize = 256,
                Mipmap = true,
                Readable = false,
                FormatAndroid = TextureImporterFormat.ETC2_RGBA8,
                FormatIos = TextureImporterFormat.ASTC_6x6,
            },

            // ================= 音频 =================
            // BGM：流式，OGG 128kbps 等价（quality 0.7）
            new ImportRule
            {
                PathPrefix = "Audio/Bgm/",
                Kind = ImportKind.Audio,
                LoadType = AudioClipLoadType.Streaming,
                AudioFormat = AudioCompressionFormat.Vorbis,
                AudioQuality = 0.7f,
                ForceMono = false,
            },
            // 音效：解压到内存（短音效反复播，流式反而更贵）
            new ImportRule
            {
                PathPrefix = "Audio/Sfx/",
                Kind = ImportKind.Audio,
                LoadType = AudioClipLoadType.DecompressOnLoad,
                AudioFormat = AudioCompressionFormat.Vorbis,
                AudioQuality = 0.5f,
                ForceMono = true,   // 音效单声道足够，内存减半
            },
            // 语音：流式 + 单声道
            new ImportRule
            {
                PathPrefix = "Audio/Voice/",
                Kind = ImportKind.Audio,
                LoadType = AudioClipLoadType.Streaming,
                AudioFormat = AudioCompressionFormat.Vorbis,
                AudioQuality = 0.5f,
                ForceMono = true,
            },

            // ================= 模型 =================
            new ImportRule
            {
                PathPrefix = "Art/Character/",
                Kind = ImportKind.Model,
                MeshCompression = ModelImporterMeshCompression.Medium,
                ImportAnimation = true,
                OptimizeMesh = true,
            },
            new ImportRule
            {
                PathPrefix = "Art/Prop/",
                Kind = ImportKind.Model,
                MeshCompression = ModelImporterMeshCompression.Medium,
                ImportAnimation = false,
                OptimizeMesh = true,
            },
        };

        /// <summary>按相对路径 + 类型找规则（最长前缀优先），找不到返回 null = 不干预。</summary>
        public static ImportRule Find(string relativePath, ImportKind kind)
        {
            ImportRule best = null;
            int bestLen = -1;

            for (int i = 0; i < Rules.Count; i++)
            {
                var rule = Rules[i];
                if (rule.Kind != kind) continue;
                if (rule.PathPrefix.Length <= bestLen) continue;
                if (relativePath.StartsWith(rule.PathPrefix))   // 区分大小写：资产命名规范要求全小写
                {
                    best = rule;
                    bestLen = rule.PathPrefix.Length;
                }
            }
            return best;
        }

        /// <summary>是否在受管范围内（只有 BundleAssets 下的资源才套规则）</summary>
        public static bool InScope(string assetPath, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrEmpty(assetPath)) return false;

            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Root)) return false;

            relativePath = p.Substring(Root.Length);
            return true;
        }
    }
}
