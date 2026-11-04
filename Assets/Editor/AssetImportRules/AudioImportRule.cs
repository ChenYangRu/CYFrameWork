using UnityEditor;
using UnityEngine;   // AudioClipLoadType / AudioCompressionFormat 是运行时枚举，不在 UnityEditor 里

namespace GameFramework.EditorTools.ImportRules
{
    /// <summary>
    /// 音频导入规则：BGM/Voice 流式、Sfx 解压到内存、统一 Vorbis + 单声道（按目录）。
    ///
    /// 关键取舍：
    ///   · 短音效反复触发 → `DecompressOnLoad`（解压一次常驻，播放零开销）
    ///   · BGM/语音只播一次且长 → `Streaming`（不占内存，边读边播）
    ///   · 音效强制单声道：手机扬声器本来就分不出左右，立体声纯浪费一倍内存
    /// </summary>
    internal class AudioImportRule : AssetImportRuleBase
    {
        private void OnPreprocessAudio()
        {
            ImportRule rule;
            if (!TryGetRule(assetPath, ImportKind.Audio, out rule)) return;

            var importer = assetImporter as AudioImporter;
            if (importer == null) return;

            Apply(importer, rule);
            StampVersion(importer);
        }

        internal static void Apply(AudioImporter importer, ImportRule rule)
        {
            importer.forceToMono = rule.ForceMono;
            importer.loadInBackground = rule.LoadType == AudioClipLoadType.Streaming;

            // defaultSampleSettings 是结构体，必须整体赋值回去
            var s = importer.defaultSampleSettings;
            s.loadType = rule.LoadType;
            s.compressionFormat = rule.AudioFormat;
            s.quality = rule.AudioQuality;
            // 保持原始采样率，避免移动端二次重采样（我们规范里已统一 44.1kHz）
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
        }
    }
}
