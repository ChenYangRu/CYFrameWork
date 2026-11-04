using System.IO;
using UnityEditor;
using UnityEngine;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// 占位资源工具：一键在 BundleAssets 里造出"最小可用"的测试资源。
    ///
    /// 为什么需要它：
    /// 框架刚拉下来时 `Assets/BundleAssets/` 里只有各目录的 `README.md` 和 `.gitkeep`，
    /// 而收集器的忽略规则正好把它们全过滤掉 —— 于是 YooAsset 模拟构建拿到的资源列表为空，
    /// 抛 `ErrorCode202 Pack asset list is empty`，`ResourceService` 进入降级模式。
    /// 这个状态是**正确**的（框架不该因为没资源就启动不了），但新人没法验证"打包链路到底通不通"。
    ///
    /// 造三个体积可忽略的占位文件（图 / 音 / 配置各一个，覆盖三条最常见的导入规则），
    /// 让 EditorSimulate 构建能真正跑一遍。验证完可以用「清理占位资源」一键删掉。
    /// </summary>
    public static class SampleAssetTool
    {
        private const string Root = "Assets/BundleAssets";

        private const string PngPath = Root + "/UI/Common/ui_placeholder.png";
        private const string WavPath = Root + "/Audio/Sfx/sfx_placeholder.wav";
        private const string JsonPath = Root + "/Config/config_placeholder.json";

        // ---------------- 生成 ----------------

        [MenuItem("框架/资源/生成占位资源（验证打包链路）", false, 45)]
        public static void Generate()
        {
            EnsureDir(Path.GetDirectoryName(PngPath));
            EnsureDir(Path.GetDirectoryName(WavPath));
            EnsureDir(Path.GetDirectoryName(JsonPath));

            bool createdPng = WritePng(PngPath);
            bool createdWav = WriteWav(WavPath);
            bool createdJson = WriteJson(JsonPath);

            // ★ Refresh 放最后、且后面不弹模态框：
            //   Refresh 会触发重新导入 + 可能的域重载，此时再开 DisplayDialog 会把 GUI 状态搞坏
            //   （历史症状：Editor.log 被 "GUIStateObj is deleted" 刷到几十 MB）。
            AssetDatabase.Refresh();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[框架] 占位资源已就绪：");
            sb.AppendLine("  图   " + PngPath + (createdPng ? "（新建）" : "（已存在，跳过）"));
            sb.AppendLine("  音   " + WavPath + (createdWav ? "（新建）" : "（已存在，跳过）"));
            sb.AppendLine("  配置 " + JsonPath + (createdJson ? "（新建）" : "（已存在，跳过）"));
            sb.AppendLine();
            sb.AppendLine("下一步：直接 Play。资源模式为 EditorSimulate 时应看到");
            sb.AppendLine("  [Res] 100% 资源就绪（EditorSimulate）   ← 不再是「降级模式」");
            Debug.Log(sb.ToString());

            var png = AssetDatabase.LoadAssetAtPath<Object>(PngPath);
            if (png != null) EditorGUIUtility.PingObject(png);
        }

        // ---------------- 清理 ----------------

        [MenuItem("框架/资源/清理占位资源", false, 46)]
        public static void Cleanup()
        {
            bool any = File.Exists(PngPath) || File.Exists(WavPath) || File.Exists(JsonPath);
            if (!any)
            {
                Debug.Log("[框架] 没有占位资源需要清理。");
                return;
            }

            // 先问、再删、最后 Refresh（顺序与上面相反，避免 Refresh 之后弹模态框）
            if (!EditorUtility.DisplayDialog("清理占位资源",
                    "将删除以下文件（含 .meta）：\n\n" + PngPath + "\n" + WavPath + "\n" + JsonPath +
                    "\n\n只影响这三个占位文件，不会动你自己的资源。", "删除", "取消"))
            {
                return;
            }

            AssetDatabase.DeleteAsset(PngPath);
            AssetDatabase.DeleteAsset(WavPath);
            AssetDatabase.DeleteAsset(JsonPath);
            AssetDatabase.Refresh();
            Debug.Log("[框架] 占位资源已清理。");
        }

        // ---------------- 文件生成 ----------------

        private static void EnsureDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        /// <summary>128×128 的棋盘格 + 边框：一眼能看出"贴图真的加载了"，也能看出是否被压缩坏。</summary>
        private static bool WritePng(string path)
        {
            if (File.Exists(path)) return false;

            const int size = 128;
            const int cell = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var bg = new Color(0.16f, 0.18f, 0.22f, 1f);
            var fg = new Color(0.55f, 0.62f, 0.70f, 1f);
            var border = new Color(0.95f, 0.45f, 0.25f, 1f);

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool odd = ((x / cell) + (y / cell)) % 2 == 1;
                    pixels[y * size + x] = odd ? fg : bg;
                }
            }
            // 2px 边框
            for (int i = 0; i < size; i++)
            {
                for (int t = 0; t < 2; t++)
                {
                    pixels[t * size + i] = border;
                    pixels[(size - 1 - t) * size + i] = border;
                    pixels[i * size + t] = border;
                    pixels[i * size + (size - 1 - t)] = border;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);

            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (bytes == null) return false;

            File.WriteAllBytes(path, bytes);
            return true;
        }

        /// <summary>0.25 秒 440Hz 正弦（带淡入淡出防爆音），44.1kHz / 16bit / 单声道。</summary>
        private static bool WriteWav(string path)
        {
            if (File.Exists(path)) return false;

            const int sampleRate = 44100;
            const float duration = 0.25f;
            const float freq = 440f;

            int sampleCount = Mathf.RoundToInt(sampleRate * duration);
            int dataBytes = sampleCount * 2;          // 16bit 单声道

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // RIFF 头
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataBytes);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });

                // fmt 块
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);                          // 块大小
                bw.Write((short)1);                    // PCM
                bw.Write((short)1);                    // 声道数
                bw.Write(sampleRate);                  // 采样率
                bw.Write(sampleRate * 2);              // 字节率
                bw.Write((short)2);                    // 块对齐
                bw.Write((short)16);                   // 位深

                // data 块
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(dataBytes);

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = i / (float)sampleRate;
                    float env = Mathf.Min(1f, i / (sampleRate * 0.01f)) *
                                Mathf.Min(1f, (sampleCount - i) / (sampleRate * 0.03f));
                    float v = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.35f * env;
                    bw.Write((short)Mathf.Clamp(v * short.MaxValue, short.MinValue, short.MaxValue));
                }
            }
            return true;
        }

        /// <summary>占位配置：证明 BundleAssets/Config 也能被收集（真项目里换成真实表）。</summary>
        private static bool WriteJson(string path)
        {
            if (File.Exists(path)) return false;
            File.WriteAllText(path,
                "{\n" +
                "  \"note\": \"占位配置，仅用于验证打包链路，可随时删除\",\n" +
                "  \"version\": 0\n" +
                "}\n",
                new System.Text.UTF8Encoding(false));
            return true;
        }
    }
}
