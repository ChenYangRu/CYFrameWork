using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GameFramework.EditorTools
{
    /// <summary>
    /// 微信小游戏接入（WX-WASM-SDK）—— 框架级一键菜单。
    /// 跟着框架走：任何用本框架新建的工程，打开即有此菜单，不必每项目重写一遍。
    ///
    /// ★ 为什么做成菜单而不是写进文档让人照着点：
    ///   WebGL 转小游戏要改十来项设置，其中"压缩方式必须关""脚本后端必须 IL2CPP"
    ///   两项错了不会报错，只会在真机上白屏 / 包体积超限 —— 靠人记迟早漏。
    ///
    /// ★ 地址必须是"现在有效"的：
    ///   官方 SDK 仓库已从 minigame-unity-webgl-transform 改名为 minigame-tuanjie-transform-sdk，
    ///   旧地址会被 GitHub 直接封（Repository access blocked），点开等于没反应。
    ///   下载也不依赖浏览器：编辑器内 UnityWebRequest 直接拉官方直链再导入，
    ///   浏览器打不开、被墙、默认浏览器没关联都不会卡住这一步。
    ///
    /// ★ 本菜单只做三件事，都不破坏现状、可重复执行：
    ///   1. 检测 SDK 是否已装（装了就跳过，没装就给下载 / 导入两条路）；
    ///   2. 把 WebGL 侧必需设置一次配到位，并加 WX_MINIGAME 宏；
    ///   3. 构建入口：装了 SDK 就调它的转换器，没装就先出 WebGL 包。
    ///
    /// ★ 不会做的事：不自动切构建平台（切平台会触发全量重导入，很慢，交给用户决定）。
    /// </summary>
    public static class WeChatMiniGame
    {
        const string Root = "框架/微信小游戏/";
        const string DefineSymbol = "WX_MINIGAME";
        const string PrefKey = "CanFeng.WxSdkPackagePath";

        // 官方入口（2026-11 核实：GitHub 旧仓库已改名并封禁，以下为开发者文档与 CDN 直链）
        const string DocsPage = "https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform.html";
        const string InstallGuide = "https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/SDKInstaller";
        const string DownloadUrl = "https://game.weixin.qq.com/cgi-bin/gamewxagwasmsplitwap/getunityplugininfo?download=1";
        const string GitUrl = "https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk.git";
        const string PackageFileName = "WX-WASM-SDK-V2.unitypackage";

        static UnityWebRequest _req;
        static string _savePath;

        // ── 菜单 ─────────────────────────────────────────────

        [MenuItem(Root + "一键接入 WX-WASM-SDK", false, 300)]
        public static void Setup()
        {
            bool installed = SdkInstalled(out string where);

            if (!installed)
            {
                string pkg = FindLocalPackage();
                if (!string.IsNullOrEmpty(pkg))
                {
                    bool ok = EditorUtility.DisplayDialog(
                        "发现本地 SDK 包",
                        "找到：\n" + pkg + "\n\n现在导入吗？导入完成后请再点一次本菜单以应用工程设置。",
                        "导入", "取消");
                    if (ok) ImportPackage(pkg);
                    return;
                }

                int pick = EditorUtility.DisplayDialogComplex(
                    "还没有 WX-WASM-SDK",
                    "小游戏转换依赖微信官方 SDK（含转换器与运行时）。\n\n" +
                    "· 让我直接下 → 选「下载并导入」，编辑器内拉官方直链（约 40MB），不经过浏览器\n" +
                    "· 已经下好了 → 选「选择本地包」，指向 .unitypackage\n" +
                    "· 只想先把工程设置配好 → 选「只做设置」",
                    "下载并导入", "只做设置", "选择本地包");

                // 选「下载并导入」时不再弹"还差 SDK"的报告框 ——
                // 下载本身就是正在解决这件事，再盖一层提示只会挡住进度条
                if (pick == 0)
                {
                    ApplyProjectSettings();
                    DownloadAndImport();
                    return;
                }
                if (pick == 2) PickPackage();

                ApplyProjectSettings();
                Report(false);
                return;
            }

            ApplyProjectSettings();
            Report(true, where);
        }

        // ── 下载 / 导入 ───────────────────────────────────────

        [MenuItem(Root + "下载并导入 WX-WASM-SDK", false, 304)]
        public static void DownloadAndImport()
        {
            if (_req != null)
            {
                EditorUtility.DisplayDialog("正在下载", "已经在下了，看进度条。", "好");
                return;
            }

            string dir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
            _savePath = Path.Combine(dir, PackageFileName);

            if (File.Exists(_savePath) && new FileInfo(_savePath).Length > 1024 * 1024)
            {
                if (EditorUtility.DisplayDialog("已有安装包",
                        _savePath + "\n\n直接导入，还是重新下一份最新的？", "直接导入", "重新下载"))
                {
                    ImportPackage(_savePath);
                    return;
                }
            }

            Debug.Log("[WX] 开始下载 " + DownloadUrl);
            _req = UnityWebRequest.Get(DownloadUrl);
            _req.downloadHandler = new DownloadHandlerFile(_savePath);
            _req.SendWebRequest();
            EditorApplication.update += TickDownload;
        }

        static void TickDownload()
        {
            if (_req == null) { EditorApplication.update -= TickDownload; EditorUtility.ClearProgressBar(); return; }

            if (!_req.isDone)
            {
                float p = Mathf.Clamp01(_req.downloadProgress);
                EditorUtility.DisplayProgressBar("下载 WX-WASM-SDK", "下载中 " + (p * 100f).ToString("0") + "%", p);
                return;
            }

            EditorApplication.update -= TickDownload;
            EditorUtility.ClearProgressBar();

            bool ok = _req.result == UnityWebRequest.Result.Success;
            string err = ok ? null : (_req.error + " / " + _req.responseCode);
            _req.Dispose();
            _req = null;

            if (!ok)
            {
                EditorUtility.DisplayDialog("下载失败",
                    "原因：" + err + "\n\n" +
                    "可在「下载并导入」之外用「选择本地包」：文档页里能手动下载。\n" + DocsPage, "好");
                return;
            }

            long size = File.Exists(_savePath) ? new FileInfo(_savePath).Length : 0;
            Debug.Log("[WX] 下载完成：" + _savePath + "（" + (size / 1024 / 1024) + " MB）");
            ImportPackage(_savePath);
        }

        [MenuItem(Root + "选择本地 SDK 包并导入…", false, 305)]
        public static void PickPackage()
        {
            string p = EditorUtility.OpenFilePanel("选择 WX-WASM-SDK 的 unitypackage", "", "unitypackage");
            if (string.IsNullOrEmpty(p)) return;
            ImportPackage(p);
        }

        static void ImportPackage(string path)
        {
            EditorPrefs.SetString(PrefKey, path);
            Debug.Log("[WX] 导入 " + path);
            AssetDatabase.ImportPackage(path, true);
            EditorUtility.DisplayDialog("开始导入",
                "包：\n" + path + "\n\n导入完成后请再执行一次「一键接入 WX-WASM-SDK」应用工程设置。", "好");
        }

        /// <summary>
        /// 静默导入已下载的包（不弹 Import 窗口）。
        /// 用途：已经下好包、只想让它自己装完；也方便被外部工具调用。
        /// </summary>
        [MenuItem(Root + "导入已下载的包（静默）", false, 307)]
        public static void ImportDownloadedSilent()
        {
            string pkg = FindLocalPackage();
            if (string.IsNullOrEmpty(pkg))
            {
                Debug.LogWarning("[WX] 没找到本地 unitypackage，先执行「下载并导入」");
                return;
            }
            EditorPrefs.SetString(PrefKey, pkg);
            Debug.Log("[WX] 静默导入 " + pkg);
            AssetDatabase.ImportPackage(pkg, false);
        }

        [MenuItem(Root + "打开微信官方文档", false, 306)]
        public static void OpenOfficialDocs()
        {
            EditorGUIUtility.systemCopyBuffer = DocsPage;
            EditorUtility.DisplayDialog("微信官方入口（已复制文档链接）",
                "接入文档：\n" + DocsPage +
                "\n\nSDK 安装指引：\n" + InstallGuide +
                "\n\nPackageManager git 地址：\n" + GitUrl, "好");
        }

        [MenuItem(Root + "构建微信小游戏", false, 301)]
        public static void Build()
        {
            if (!SdkInstalled(out string where))
            {
                EditorUtility.DisplayDialog("缺少 SDK",
                    "还没装 WX-WASM-SDK-V2，无法调用小游戏转换器。\n先执行「一键接入 WX-WASM-SDK」。", "好");
                return;
            }

            ApplyProjectSettings();

            // SDK 的转换器是编辑器静态方法，用反射调用 —— 工程不装 SDK 时不能硬引用它的类型
            var t = FindType("WeChatWASM.WXConvertCore");
            if (t == null)
            {
                EditorUtility.DisplayDialog("找不到转换器",
                    "找到了 SDK（" + where + "），但找不到 WeChatWASM.WXConvertCore。\n" +
                    "请确认 SDK 版本是 V2，或直接用 SDK 自带的转换菜单。", "好");
                return;
            }

            var m = t.GetMethod("DoExport", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null)
            {
                EditorUtility.DisplayDialog("转换器入口不对",
                    "WeChatWASM.WXConvertCore 存在，但没有 DoExport 静态方法。\n" +
                    "请用 SDK 自带菜单转换，或告诉我 SDK 版本，我改调用入口。", "好");
                return;
            }

            try
            {
                m.Invoke(null, null);
                Debug.Log("[WX] 已调用 WXConvertCore.DoExport()");
            }
            catch (Exception e)
            {
                Debug.LogError("[WX] 转换失败：" + (e.InnerException ?? e));
            }
        }

        [MenuItem(Root + "接入检查（诊断）", false, 302)]
        public static void Diagnose()
        {
            ApplyProjectSettings();
            Report(SdkInstalled(out string where), where);
        }

        [MenuItem(Root + "打开接入文档", false, 303)]
        public static void OpenDoc()
        {
            // 文档在仓库 docs 下（不在 Unity 工程里），按几个常见相对位置找一遍
            string proj = Directory.GetCurrentDirectory();
            string[] candidates =
            {
                Path.Combine(proj, "docs/09_微信小游戏接入.md"),
                Path.Combine(proj, "../docs/09_微信小游戏接入.md"),
                Path.Combine(proj, "../../docs/09_微信小游戏接入.md"),
                Path.Combine(proj, "../../../docs/09_微信小游戏接入.md"),
            };

            foreach (var c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full)) { EditorUtility.RevealInFinder(full); return; }
            }

            OpenUrlSafe(DocsPage, "已打开微信官方接入文档");
        }

        /// <summary>
        /// 打开外链，并且**无论如何都复制到剪贴板** ——
        /// 浏览器没关联、被墙、被企业策略拦时 Application.OpenURL 会静默失败，
        /// 只弹一句"已打开"等于骗人；把链接塞进剪贴板至少让人拿得到。
        /// </summary>
        static void OpenUrlSafe(string url, string title)
        {
            try { Application.OpenURL(url); }
            catch (Exception e) { Debug.LogWarning("[WX] 调用浏览器失败：" + e.Message); }

            EditorGUIUtility.systemCopyBuffer = url;
            EditorUtility.DisplayDialog(title,
                "链接已复制到剪贴板：\n\n" + url +
                "\n\n如果浏览器没有反应，直接粘贴到浏览器地址栏。", "好");
        }

        // ── 设置 ─────────────────────────────────────────────

        /// <summary>WebGL 侧小游戏必需设置。可重复执行，不切平台。</summary>
        public static void ApplyProjectSettings()
        {
            // ① 脚本后端必须是 IL2CPP —— WebGL 下 Mono 不可用于小游戏
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);

            // ② API 兼容级别：.NET Standard 2.0（SDK 与 TMP 都按这个编译）
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.WebGL, ApiCompatibilityLevel.NET_Standard_2_0);

            // ③ ★ 压缩必须关：小游戏平台自己做分包与压缩，Unity 侧再压一层（gzip/brotli）
            //    会直接导致转换后加载失败 —— 这一项漏了只会在真机白屏，编辑器里毫无提示。
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            // ④ Wasm 链接目标 + 关线程（小游戏线程模型与浏览器不同）
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.WebGL.threadsSupport = false;

            // ⑤ 数据缓存开着（首包之后从本地缓存读，省下载）
            PlayerSettings.WebGL.dataCaching = true;

            // ⑥ 异常支持关掉：带上完整异常信息会让 wasm 体积暴涨
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;

            // ⑦ 裁剪不要太狠：SDK 运行时大量走反射，Strip High 会把它们裁掉（白屏/空引用）
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.stripEngineCode = false;

            // ⑧ 宏：业务侧以后要分平台写逻辑，只看这个宏
            AddDefine(BuildTargetGroup.WebGL, DefineSymbol);

            AssetDatabase.SaveAssets();
            Debug.Log("[WX] 已应用 WebGL 小游戏设置：IL2CPP / 压缩关闭 / Low Strip / " + DefineSymbol);
        }

        static void AddDefine(BuildTargetGroup group, string symbol)
        {
            string cur = PlayerSettings.GetScriptingDefineSymbolsForGroup(group) ?? "";
            var list = cur.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (list.Contains(symbol)) return;
            list.Add(symbol);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", list));
        }

        // ── 检测 / 导入 ───────────────────────────────────────

        /// <summary>
        /// 已装判定：能找到 WeChatWASM 命名空间下的任意类型，或工程里存在 SDK 目录。
        /// ★ 不靠目录名硬判 —— 用户可能把 SDK 放在 Packages 或改过文件夹名。
        /// </summary>
        public static bool SdkInstalled(out string where)
        {
            where = "";
            var t = FindType("WeChatWASM.WXSDKManager") ?? FindType("WeChatWASM.WXConvertCore");
            if (t != null)
            {
                where = "程序集 " + (t.Assembly != null ? t.Assembly.GetName().Name : "?");
                return true;
            }

            string[] dirs =
            {
                "Assets/WX-WASM-SDK-V2",
                "Assets/WeChatWASM",
                "Packages/WX-WASM-SDK-V2",
            };
            foreach (var d in dirs)
            {
                if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), d)))
                {
                    where = d;
                    return true;
                }
            }
            return false;
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); }
                catch { /* 个别动态程序集取类型会抛，跳过即可 */ }
                if (t != null) return t;
            }
            return null;
        }

        static string FindLocalPackage()
        {
            // 上次手选的路径优先
            string saved = EditorPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;

            string proj = Directory.GetCurrentDirectory();
            string[] roots =
            {
                proj,
                Path.Combine(proj, "tools"),
                Path.Combine(proj, ".."),
                Path.Combine(proj, "../.."),
            };

            foreach (var r in roots)
            {
                if (!Directory.Exists(r)) continue;
                try
                {
                    var hit = Directory.GetFiles(r, "*.unitypackage", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(p =>
                        {
                            var n = Path.GetFileName(p).ToLowerInvariant();
                            return n.Contains("wx") || n.Contains("wasm") || n.Contains("wechat");
                        });
                    if (!string.IsNullOrEmpty(hit)) return hit;
                }
                catch { /* 目录权限不足就跳过 */ }
            }
            return null;
        }

        // ── 报告 ─────────────────────────────────────────────

        static void Report(bool installed, string where = "")
        {
            string msg =
                "SDK：" + (installed ? "已安装（" + where + "）" : "未安装") + "\n" +
                "脚本后端：" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.WebGL) + "\n" +
                "压缩：" + PlayerSettings.WebGL.compressionFormat + "\n" +
                "裁剪：" + PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.WebGL) + "\n" +
                "宏：" + PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL) + "\n" +
                "当前平台：" + EditorUserBuildSettings.activeBuildTarget;

            Debug.Log("[WX] 接入状态\n" + msg);

            if (!installed)
            {
                EditorUtility.DisplayDialog("工程设置已配好，还差 SDK",
                    msg + "\n\n下一步：执行「框架 → 微信小游戏 → 下载并导入 WX-WASM-SDK」，" +
                    "编辑器内直接拉官方直链（约 40MB），导完再点一次「一键接入」。", "好");
            }
            else
            {
                EditorUtility.DisplayDialog("微信小游戏接入就绪", msg +
                    "\n\n构建：框架 → 微信小游戏 → 构建微信小游戏", "好");
            }
        }
    }
}
