using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools.ImportRules
{
    /// <summary>
    /// 纹理导入规则。按目录套用 `ImportRuleTable` 的规则，同时写 Android / iOS 两套平台覆盖。
    ///
    /// 只管 `Assets/BundleAssets/` 下的资源；其它目录（含 YooAsset 包内）一律不动。
    ///
    /// 为什么必须设平台覆盖：Editor 默认格式（RGBA32）在真机上会退回未压缩，
    /// 内存直接 ×4 —— 这是移动端爆内存最常见的原因，比包体问题更致命。
    /// </summary>
    internal class TextureImportRule : AssetImportRuleBase
    {
        private void OnPreprocessTexture()
        {
            ImportRule rule;
            if (!TryGetRule(assetPath, ImportKind.Texture, out rule)) return;

            var importer = assetImporter as TextureImporter;
            if (importer == null) return;

            Apply(importer, rule);
            StampVersion(importer);
        }

        internal static void Apply(TextureImporter importer, ImportRule rule)
        {
            importer.textureType = rule.TexType;
            importer.maxTextureSize = rule.MaxSize;
            importer.mipmapEnabled = rule.Mipmap;
            importer.isReadable = rule.Readable;
            importer.textureCompression = rule.Compression;
            importer.crunchedCompression = rule.Crunched;
            importer.compressionQuality = rule.CompressionQuality;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;

            // 只对 Sprite 开透明通道处理，避免把法线图当颜色图处理
            if (rule.TexType == TextureImporterType.Sprite)
            {
                importer.alphaIsTransparency = true;
                importer.spriteImportMode = SpriteImportMode.Single;
            }

            ApplyPlatform(importer, "Android", rule.FormatAndroid, rule);
            ApplyPlatform(importer, "iPhone", rule.FormatIos, rule);
        }

        private static void ApplyPlatform(TextureImporter importer, string platform,
            TextureImporterFormat format, ImportRule rule)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.name = platform;
            settings.overridden = true;
            settings.maxTextureSize = rule.MaxSize;
            settings.format = format;
            settings.textureCompression = rule.Compression;
            settings.compressionQuality = rule.CompressionQuality;
            settings.crunchedCompression = rule.Crunched;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
