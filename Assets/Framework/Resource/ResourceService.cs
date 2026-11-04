using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace GameFramework.Resource
{
    /// <summary>
    /// 资源服务：把 YooAsset 3.0.5 的复杂流程收敛成"一个协程 + 一个进度回调"。
    /// 顺序不可乱（热更框架铁律）：初始化包裹 → 请求版本 → 加载清单 → 下载 → 清理 → 完成。
    /// ★ 业务层禁止直连 YooAsset：将来换 Addressables 只改本文件。
    /// </summary>
    public class ResourceService
    {
        public event Action<float, string> OnProgress;   // (0~1, 状态文本)

        private ResourceConfig _cfg;
        private ResourcePackage _package;

        public string PackageName { get { return _cfg != null ? _cfg.packageName : string.Empty; } }
        public string PackageVersion { get; private set; }
        public PlayMode Mode { get { return _cfg != null ? _cfg.playMode : PlayMode.EditorSimulate; } }
        public bool Initialized { get; private set; }

        /// <summary>是否处于降级模式：资源系统不可用，但游戏继续跑（新工程还没资源时会命中）</summary>
        public bool Degraded { get; private set; }
        /// <summary>本次更新下载了多少字节（0 表示无更新）</summary>
        public long LastDownloadBytes { get; private set; }
        public int LastDownloadCount { get; private set; }

        /// <summary>当前更新阶段（启动器/更新界面用来显示"正在干什么"）</summary>
        public UpdateStage Stage { get; private set; }

        /// <summary>阶段变化事件：(阶段, 文案)。在下载开始**之前**就会触发，供 UI 提前切界面</summary>
        public event Action<UpdateStage, string> OnStageChanged;

        /// <summary>
        /// 本次更新概要（FileCount / TotalBytes / 远端版本）。下载前填好。
        /// 无更新或未进入远端流程时为 null。
        /// </summary>
        public ResourceUpdateInfo PendingUpdate { get; private set; }

        /// <summary>
        /// 下载前的流量确认回调。**不注入就静默直接下载**（适合编辑器/自动化）；
        /// 正式包务必注入一个弹窗，否则玩家会在流量下被吃掉几十 MB。
        /// 实现方必须最终调用 `answer(true/false)`，否则启动会一直等待。
        /// </summary>
        public Action<ResourceUpdateInfo, Action<bool>> ConfirmDownload;

        private void SetStage(UpdateStage stage, string message)
        {
            Stage = stage;
            var handler = OnStageChanged;
            if (handler != null)
            {
                try { handler(stage, message); } catch (Exception e) { GameLog.Error("[Res] 阶段回调异常", e); }
            }
        }

        // ---------------- 初始化 + 热更 ----------------

        public IEnumerator InitializeAndUpdate(Action<bool, string> onDone)
        {
            _cfg = ResourceConfig.Load();
            GameLog.Info(string.Format("[Res] 运行模式={0} 包裹={1}", _cfg.playMode, _cfg.packageName));
            SetStage(UpdateStage.Initialize, "初始化资源系统…");
            Report(0f, "初始化资源系统…");

            // Bootstrap 上勾了「跳过资源系统」：纯业务联调时省掉模拟构建/下载
            if (ResourceConfig.RuntimeSkipResource)
            {
                EnterDegradedMode("Bootstrap 勾选了「跳过资源系统」", false);
                onDone(true, null);
                yield break;
            }

            // ★ 先算初始化参数（编辑器模拟模式要在这里触发"模拟构建"）。
            //   框架阶段 BundleAssets 里常常还没有任何可打包资源，YooAsset 会抛
            //   ErrorCode202「Pack asset list is empty」。这种情况**不能中断启动**，
            //   否则新同事拉代码一进 Play 就是红字，连登录页都看不到。
            string buildError;
            var options = TryBuildInitOptions(_cfg, out buildError);
            if (options == null)
            {
                EnterDegradedMode(buildError);
                onDone(true, null);
                yield break;
            }

            if (!YooAssets.IsInitialized) YooAssets.Initialize();

            // YooAsset 3.x：TryGetPackage 是 (string, out ResourcePackage) → bool，
            // 单参返回值的版本在 YOOASSET_LEGACY_API 兼容层里，默认不编译。
            ResourcePackage existing;
            _package = YooAssets.TryGetPackage(_cfg.packageName, out existing)
                ? existing
                : YooAssets.CreatePackage(_cfg.packageName);
            if (_package == null)
            {
                Fail(onDone, "创建资源包裹失败: " + _cfg.packageName);
                yield break;
            }

            // ---- 1. 初始化包裹 ----
            SetStage(UpdateStage.Initialize, "初始化资源包裹…");
            var initOp = _package.InitializePackageAsync(options);
            yield return initOp;
            if (initOp.Status != EOperationStatus.Succeeded)
            {
                Fail(onDone, "资源包裹初始化失败: " + initOp.Error);
                yield break;
            }
            Report(0.15f, "资源系统就绪");

            // 编辑器模拟 / 离线：没有"远端版本"概念，直接读包内清单，无更新流程
            if (_cfg.playMode == PlayMode.EditorSimulate || _cfg.playMode == PlayMode.Offline)
            {
                Initialized = true;
                PackageVersion = _package.GetPackageVersion();
                SetStage(UpdateStage.Done, "资源就绪（" + _cfg.playMode + "，无更新流程）");
                Report(1f, "资源就绪（" + _cfg.playMode + "）");
                onDone(true, null);
                yield break;
            }

            // 从这里开始是**更新流程**（Host / Web 共用）
            string localBefore = _package.GetPackageVersion();

            // ---- 2. 请求版本 ----
            SetStage(UpdateStage.RequestVersion, "检查资源版本…");
            Report(0.2f, "检查资源版本…");
            var versionOp = _package.RequestPackageVersionAsync(
                new RequestPackageVersionOptions(false, _cfg.requestTimeout));
            yield return versionOp;
            if (versionOp.Status != EOperationStatus.Succeeded)
            {
                Fail(onDone, "请求资源版本失败: " + versionOp.Error);
                yield break;
            }
            PackageVersion = versionOp.PackageVersion;
            GameLog.Info(string.Format("[Res] 资源版本 本地={0} 远端={1}", localBefore, PackageVersion));

            // ---- 3. 加载清单 ----
            SetStage(UpdateStage.LoadManifest, "加载资源清单…");
            Report(0.28f, "加载资源清单…");
            var manifestOp = _package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(PackageVersion, _cfg.requestTimeout));
            yield return manifestOp;
            if (manifestOp.Status != EOperationStatus.Succeeded)
            {
                Fail(onDone, "加载资源清单失败: " + manifestOp.Error);
                yield break;
            }

            // ---- 4. 预取清单 + 创建下载器 ----
            SetStage(UpdateStage.Prefetch, "比对资源差异…");
            var prefetchOp = _package.PrefetchManifestAsync(
                new PrefetchManifestOptions(PackageVersion, _cfg.requestTimeout));
            yield return prefetchOp;
            if (prefetchOp.Status != EOperationStatus.Succeeded)
            {
                Fail(onDone, "预取清单失败: " + prefetchOp.Error);
                yield break;
            }

            var downloader = prefetchOp.CreateResourceDownloader(
                new ResourceDownloaderOptions(_cfg.downloadMaxConcurrency, _cfg.downloadRetryCount));

            LastDownloadCount = downloader.TotalDownloadCount;
            LastDownloadBytes = downloader.TotalDownloadBytes;

            PendingUpdate = new ResourceUpdateInfo
            {
                FileCount = LastDownloadCount,
                TotalBytes = LastDownloadBytes,
                RemoteVersion = PackageVersion,
                LocalVersion = localBefore,
                RemoteRoot = _cfg.BuildRemoteRoot(),
            };

            if (PendingUpdate.HasUpdate)
            {
                GameLog.Info("[Res] " + PendingUpdate);

                // ---- 5. 流量提示：先问玩家，再下载 ----
                if (ConfirmDownload != null)
                {
                    SetStage(UpdateStage.WaitConfirm, "等待确认更新…");
                    bool approved = false;
                    bool answered = false;
                    ConfirmDownload(PendingUpdate, delegate (bool ok)
                    {
                        approved = ok;
                        answered = true;
                    });

                    while (!answered) yield return null;   // 等 UI 点击，不阻塞主线程

                    if (!approved)
                    {
                        Fail(onDone, "已取消更新（需要下载 " + PendingUpdate.SizeMB.ToString("0.0") + " MB）");
                        yield break;
                    }
                }

                // ---- 6. 下载 ----
                SetStage(UpdateStage.Downloading, "下载资源…");
                downloader.DownloadProgressChanged += HandleDownloadProgress;
                downloader.DownloadError += HandleDownloadError;
                downloader.StartDownload();
                yield return downloader;

                if (downloader.Status != EOperationStatus.Succeeded)
                {
                    Fail(onDone, "资源下载失败: " + downloader.Error);
                    yield break;
                }
                Report(0.95f, "更新完成，正在清理…");
            }
            else
            {
                GameLog.Info("[Res] 无资源更新（已是最新）");
            }

            // ---- 7. 清理旧 bundle ----
            if (_cfg.clearUnusedCache)
            {
                SetStage(UpdateStage.Cleanup, "清理旧资源…");
                var clearOp = _package.ClearCacheAsync(
                    new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles));
                yield return clearOp;
            }

            Initialized = true;
            SetStage(UpdateStage.Done, "资源更新完成");
            Report(1f, "资源更新完成");
            onDone(true, null);
        }

        private void Fail(Action<bool, string> onDone, string message)
        {
            GameLog.Error("[Res] " + message);
            SetStage(UpdateStage.Failed, message);
            onDone(false, message);
        }

        /// <summary>
        /// 计算初始化参数。返回 null 表示"资源系统不可用"，调用方应进入降级模式而不是中断启动。
        /// 编辑器模拟模式的"模拟构建"在这里触发，是最容易因为**资源为空**而失败的环节。
        /// </summary>
        private InitializePackageOptions TryBuildInitOptions(ResourceConfig cfg, out string error)
        {
            error = null;
            switch (cfg.playMode)
            {
                case PlayMode.EditorSimulate:
                {
#if UNITY_EDITOR
                    // ★ 先自己做一次"有没有可打包资源"的体检，再决定要不要调模拟构建。
                    //   原因：BundleAssets 里只有 README.md / .gitkeep 时，YooAsset 内部的
                    //   EditorSimulateBuildPipeline 会先 Debug.LogError("Build pipeline failed")
                    //   再抛 ErrorCode202，栈是红的 —— 但这其实是**框架允许的状态**（新工程）。
                    //   提前拦住 → Console 只留一条可读警告，不污染红字。
                    if (!HasAnyCollectableAsset())
                    {
                        error = "Assets/BundleAssets 里还没有可打包资源（当前只有 README.md / .gitkeep）。" +
                                "菜单「框架/资源/生成占位资源」可一键造一张测试图来验证打包链路。";
                        return null;
                    }

                    // 编辑器模拟构建：产物在临时目录，不走 AB 打包
                    try
                    {
                        var buildResult = EditorSimulateBuildInvoker.Build(
                            cfg.packageName, (int)EBundleType.VirtualAssetBundle);
                        var parameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                            buildResult.PackageRootDirectory);
                        return new EditorSimulateModeOptions { EditorFileSystemParameters = parameters };
                    }
                    catch (Exception e)
                    {
                        // 兜底：体检通过但构建仍失败（收集器为空、分组未激活等）
                        error = "编辑器模拟构建失败：" + e.Message;
                        return null;
                    }
#else
                    error = "真机不可使用编辑器模拟模式，请把 resource.json 的 playMode 改为 Host";
                    return null;
#endif
                }

                case PlayMode.Offline:
                {
                    try
                    {
                        var parameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                        return new OfflinePlayModeOptions { BuiltinFileSystemParameters = parameters };
                    }
                    catch (Exception e)
                    {
                        error = "离线模式参数构建失败：" + e.Message;
                        return null;
                    }
                }

                case PlayMode.Web:
                {
                    try
                    {
                        // WebGL / 微信小游戏：bundle 全部走 HTTP 直读，不落本地磁盘。
                        // 没有可写文件系统（小游戏虚拟 FS 语义不稳定），所以不走 Sandbox 缓存。
                        // ★ 两个文件系统都要给：WebServerFileSystem 负责读 bundle，
                        //   WebNetworkFileSystem 负责版本/清单等小文件（它需要远程地址服务）。
                        if (string.IsNullOrEmpty(cfg.hostServer))
                        {
                            error = "Web 模式需要 CDN 地址，但 resource.json 的 hostServer 为空";
                            return null;
                        }
                        var remote = new RemoteServices(cfg.BuildRemoteRoot(), cfg.fallbackHostServer);
                        var webServer = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
                        var webNetwork = FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(remote);
                        return new WebPlayModeOptions
                        {
                            WebServerFileSystemParameters = webServer,
                            WebNetworkFileSystemParameters = webNetwork,
                        };
                    }
                    catch (Exception e)
                    {
                        error = "Web 模式参数构建失败：" + e.Message;
                        return null;
                    }
                }

                default:
                {
                    try
                    {
                        // Host 必须有远端：否则"请求版本"必然失败，报错信息会很难懂。
                        // 只想读包内资源、不要远端时请用 Offline 模式。
                        if (string.IsNullOrEmpty(cfg.hostServer))
                        {
                            error = "Host（联机热更）模式需要 CDN 地址，但 resource.json 的 hostServer 为空。" +
                                    "只想读包内资源请改用 Offline 模式。";
                            return null;
                        }
                        var builtin = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                        var remote = new RemoteServices(cfg.BuildRemoteRoot(), cfg.fallbackHostServer);
                        // v3 原生命名：Cache 文件系统 = Sandbox 文件系统
                        var cache = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remote);
                        return new HostPlayModeOptions
                        {
                            BuiltinFileSystemParameters = builtin,
                            CacheFileSystemParameters = cache,
                        };
                    }
                    catch (Exception e)
                    {
                        error = "联机模式参数构建失败：" + e.Message;
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// 资源目录体检（仅编辑器）：BundleAssets 下是否存在**会被收集器收进包**的文件。
        ///
        /// 判定与 `Assets/Editor/YooAssetRules/DocIgnoreRule.cs` 保持一致 ——
        /// 那边忽略什么，这边就不算"可打包资源"。两边必须同步，否则会出现
        /// "体检说没有、构建又失败"或反过来的假绿。
        /// </summary>
        private static bool HasAnyCollectableAsset()
        {
#if UNITY_EDITOR
            string root = System.IO.Path.Combine(Application.dataPath, "BundleAssets");
            if (!System.IO.Directory.Exists(root)) return false;

            string[] files;
            try { files = System.IO.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories); }
            catch (Exception) { return false; }

            for (int i = 0; i < files.Length; i++)
            {
                string name = System.IO.Path.GetFileName(files[i]);
                string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();

                if (ext == ".meta") continue;                    // Unity 自己的导入元数据
                if (ext == ".keep" || ext == ".gitkeep") continue;
                if (ext == ".gitignore") continue;
                if (ext == ".md" || ext == ".txt") continue;
                if (ext == ".pdf" || ext == ".doc" || ext == ".docx") continue;
                if (ext == ".xlsx" || ext == ".xls" || ext == ".ppt" || ext == ".pptx") continue;
                if (ext == ".ini" || ext == ".log") continue;

                return true;
            }
            return false;
#else
            return true;   // 真机不体检（不打 EditorSimulate，也就没有模拟构建）
#endif
        }

        /// <summary>
        /// 降级模式：资源系统不可用，但**游戏照常启动**。
        ///
        /// 为什么必须提供：框架刚拉下来时 `Assets/BundleAssets/` 里往往一个资源都没有，
        /// 这时如果中断启动，新同事第一眼看到的就是红字 + 白屏，会以为框架坏了。
        /// 降级后：登录/主界面（代码构建的 UI）照常显示，任何资源加载请求返回失败并记警告。
        /// </summary>
        private void EnterDegradedMode(string reason, bool showHint = true)
        {
            Degraded = true;
            Initialized = true;
            PackageVersion = "(无资源)";
            _package = null;

            GameLog.Warn("[Res] 资源系统降级运行，本次不会加载任何打包资源（游戏照常跑，UI 走代码构建）");
            GameLog.Warn("[Res] 原因：" + (string.IsNullOrEmpty(reason) ? "未知" : reason));
            if (showHint)
            {
                GameLog.Warn("[Res] 两种恢复方式：\n" +
                             "  1) 菜单「框架/资源/生成占位资源」→ 一键造一张测试图，立刻验证打包链路；\n" +
                             "  2) 把真资源放进 Assets/BundleAssets/{UI,Art,Audio,Config,Text,Shader}/，\n" +
                             "     再执行「框架/资源/初始化 YooAsset 收集器」，然后重新 Play");
            }
            SetStage(UpdateStage.Done, "无资源（降级模式）");
            Report(1f, "无资源（降级模式）");
        }

        private void HandleDownloadProgress(DownloadProgressChangedEventArgs e)
        {
            if (e.TotalDownloadBytes <= 0) return;
            float p = 0.3f + 0.65f * ((float)e.CurrentDownloadBytes / e.TotalDownloadBytes);
            Report(p, string.Format("下载资源 {0:0.0}/{1:0.0} MB",
                e.CurrentDownloadBytes / 1048576f, e.TotalDownloadBytes / 1048576f));
        }

        private void HandleDownloadError(DownloadErrorEventArgs e)
        {
            GameLog.Error("[Res] 下载失败: " + e.FileName + " → " + e.ErrorInfo);
        }

        // ---------------- 资源加载 ----------------

        /// <summary>异步加载单个资源（协程版，回调在主线程）</summary>
        public IEnumerator LoadAsset<T>(string location, Action<T> onLoaded, Action<string> onFailed = null)
            where T : UnityEngine.Object
        {
            if (_package == null)
            {
                if (onFailed != null) onFailed("资源包裹未初始化");
                yield break;
            }

            var handle = _package.LoadAssetAsync<T>(location);
            yield return handle;

            if (handle.Status != EOperationStatus.Succeeded)
            {
                string err = "加载失败 " + location + ": " + handle.Error;
                GameLog.Error("[Res] " + err);
                handle.Release();
                if (onFailed != null) onFailed(err);
                yield break;
            }

            // 注意：调用方负责在使用完后 ReleaseHandle(handle)
            if (onLoaded != null) onLoaded(handle.AssetObject as T);
        }

        /// <summary>加载 TextAsset 的字节（热更 DLL 走这里：.bytes 就是 TextAsset）</summary>
        public IEnumerator LoadRawBytes(string location, Action<byte[]> onLoaded, Action<string> onFailed = null)
        {
            if (_package == null)
            {
                if (onFailed != null) onFailed("资源包裹未初始化");
                yield break;
            }

            var handle = _package.LoadAssetAsync<TextAsset>(location);
            yield return handle;

            if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
            {
                string err = "加载二进制失败 " + location + ": " + handle.Error;
                GameLog.Error("[Res] " + err);
                handle.Release();
                if (onFailed != null) onFailed(err);
                yield break;
            }

            var ta = handle.AssetObject as TextAsset;
            byte[] bytes = ta != null ? ta.bytes : null;
            handle.Release();

            if (onLoaded != null) onLoaded(bytes);
        }

        /// <summary>加载并实例化预制体。返回实例，句柄内部已释放（对象生命周期交给调用方）</summary>
        public IEnumerator Instantiate(string location, Transform parent, Action<GameObject> onLoaded,
            Action<string> onFailed = null)
        {
            if (_package == null)
            {
                if (onFailed != null) onFailed("资源包裹未初始化");
                yield break;
            }

            var handle = _package.LoadAssetAsync<GameObject>(location);
            yield return handle;

            if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
            {
                string err = "实例化失败 " + location + ": " + handle.Error;
                GameLog.Error("[Res] " + err);
                handle.Release();
                if (onFailed != null) onFailed(err);
                yield break;
            }

            var prefab = handle.AssetObject as GameObject;
            var go = UnityEngine.Object.Instantiate(prefab, parent);
            handle.Release();

            if (onLoaded != null) onLoaded(go);
        }

        public IEnumerator LoadScene(string location, UnityEngine.SceneManagement.LoadSceneMode mode,
            Action<bool> onDone)
        {
            if (_package == null)
            {
                if (onDone != null) onDone(false);
                yield break;
            }

            var handle = _package.LoadSceneAsync(location, mode,
                UnityEngine.SceneManagement.LocalPhysicsMode.None, true);
            yield return handle;

            bool ok = handle.Status == EOperationStatus.Succeeded;
            if (!ok) GameLog.Error("[Res] 场景加载失败: " + handle.Error);
            handle.Release();

            if (onDone != null) onDone(ok);
        }

        /// <summary>卸载无引用资源（切场景 / 关闭大界面后调），由业务自行 yield 等待</summary>
        public UnloadUnusedAssetsOperation UnloadUnused()
        {
            if (_package == null) return null;
            return _package.UnloadUnusedAssetsAsync();
        }

        private void Report(float progress, string status)
        {
            var h = OnProgress;
            if (h != null) h(progress, status);
            GameLog.Info(string.Format("[Res] {0:0%} {1}", progress, status));
        }

        /// <summary>
        /// 远端地址服务（YooAsset 3.x 原生接口 IRemoteService）。
        /// 返回**按优先级排序的候选地址列表**：主 CDN 在前、备用 CDN 在后，
        /// YooAsset 下载失败会自动依次回退。URL 统一走 PathUtil.CombineUrl 归一化，避免双斜杠。
        /// </summary>
        private class RemoteServices : IRemoteService
        {
            private readonly string _main;
            private readonly string _fallback;
            private readonly List<string> _urls = new List<string>(2);

            public RemoteServices(string main, string fallback)
            {
                _main = main ?? string.Empty;
                _fallback = string.IsNullOrEmpty(fallback) ? _main : fallback;
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                _urls.Clear();
                _urls.Add(PathUtil.CombineUrl(_main, fileName));
                if (_fallback != _main) _urls.Add(PathUtil.CombineUrl(_fallback, fileName));
                return _urls;
            }
        }
    }
}
