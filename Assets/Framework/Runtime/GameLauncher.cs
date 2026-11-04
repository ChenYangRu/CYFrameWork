using System;
using System.Collections;
using GameFramework.Audio;
using GameFramework.Network;
using GameFramework.Resource;
using GameFramework.UI;
using UnityEngine;

namespace GameFramework.Runtime
{
    /// <summary>
    /// 启动器（AOT 侧，唯一常驻入口）。
    /// 固定流程 —— **顺序不可乱**（这是热更框架最经典的坑）：
    ///   初始化服务 → 资源热更 → 补充元数据 → 加载热更 DLL → 反射进业务
    /// 任何一步失败都停在 LaunchView 的错误页，可重试（不白屏、不静默）。
    /// </summary>
    public class GameLauncher
    {
        public static GameLauncher Instance { get; private set; }

        public LaunchConfig Config { get; private set; }
        public ResourceService Resource { get; private set; }
        public NetworkService Network { get; private set; }
        public UIManager UI { get; private set; }
        public AudioService Audio { get; private set; }

        private LaunchView _view;
        private bool _running;
        private bool _enteredGame;

        public bool EnteredGame { get { return _enteredGame; } }

        public static GameLauncher Create()
        {
            if (Instance == null) Instance = new GameLauncher();
            return Instance;
        }

        public void Run()
        {
            if (_running)
            {
                GameLog.Warn("[Launch] 启动流程已在执行中");
                return;
            }

            var loop = GameLoop.Ensure();
            if (_view == null) _view = new LaunchView();
            _view.Create();
            _view.SetProgress(0f, "启动中…");
            loop.Run(Flow());
        }

        public void Retry()
        {
            GameLog.Info("[Launch] 用户点击重试");
            Teardown();
            Run();
        }

        private IEnumerator Flow()
        {
            _running = true;
            _enteredGame = false;

            // ---------- 0. 基础环境 ----------
            Application.targetFrameRate = 60;
            GameLog.EnableFile(true);
            GameLog.Info("==============================");
            GameLog.Info(string.Format("启动 {0} | {1} | Unity {2}",
                Application.productName, Application.version, Application.unityVersion));
            _view.SetProgress(0.02f, "初始化框架…");
            yield return null;

            Config = LaunchConfig.Load();

            // ---------- 1. 服务装配 ----------
            if (Resource == null)
            {
                Resource = new ResourceService();
                Resource.OnProgress += HandleResourceProgress;
                Resource.OnStageChanged += HandleResourceStage;
                // 下载前必须问玩家（移动网络下静默下载几十 MB 是差评来源）
                Resource.ConfirmDownload = HandleConfirmDownload;
                ServiceLocator.Register(Resource);
            }

            if (Network == null)
            {
                Network = new NetworkService();
                Network.Init(NetworkConfig.Load());
                ServiceLocator.Register(Network);
            }

            if (UI == null)
            {
                UI = new UIManager();
                UI.Init(Resource);
                ServiceLocator.Register(UI);
            }

            if (Audio == null)
            {
                Audio = new AudioService();
                Audio.Init(Resource);
                ServiceLocator.Register(Audio);
            }

            _view.SetProgress(0.05f, "检查资源更新…");
            yield return null;

            // ---------- 2. 资源热更 ----------
            bool ok = false;
            string error = null;
            yield return Resource.InitializeAndUpdate(delegate (bool success, string err)
            {
                ok = success;
                error = err;
            });

            if (!ok)
            {
                Fail("资源更新失败\n" + error);
                yield break;
            }

            _view.SetProgress(0.55f, "补充 AOT 元数据…");
            yield return null;

            // ---------- 3. 补充元数据（必须在加载 DLL 之前）----------
            ok = false;
            yield return HotUpdateLoader.LoadMetadata(Config, delegate (bool success, string err)
            {
                ok = success;
                error = err;
            });
            if (!ok)
            {
                Fail("补充元数据失败\n" + error);
                yield break;
            }

            _view.SetProgress(0.7f, "加载热更代码…");
            yield return null;

            // ---------- 4. 加载热更程序集 ----------
            ok = false;
            yield return HotUpdateLoader.LoadAssemblies(Resource, Config, delegate (bool success, string err)
            {
                ok = success;
                error = err;
            });
            if (!ok)
            {
                Fail("热更代码加载失败\n" + error);
                yield break;
            }

            _view.SetProgress(0.9f, "进入游戏…");
            yield return null;

            // ---------- 5. 反射进入业务 ----------
            string entryError;
            if (!HotUpdateLoader.InvokeEntry(Config, out entryError))
            {
                Fail("业务入口执行失败\n" + entryError);
                yield break;
            }

            _enteredGame = true;
            _running = false;
            _view.SetProgress(1f, "完成");
            yield return null;
            _view.Destroy();
            _view = null;

            GameLog.Info("[Launch] 启动完成");
        }

        private void HandleResourceProgress(float progress, string status)
        {
            if (_view != null) _view.SetProgress(0.05f + progress * 0.5f, status);
        }

        /// <summary>更新阶段变化：把"检查版本 / 比对差异 / 下载 / 清理"显示到启动页</summary>
        private void HandleResourceStage(UpdateStage stage, string message)
        {
            if (_view == null) return;
            if (stage == UpdateStage.Done || stage == UpdateStage.Failed) return;   // 进度条那条已经覆盖
            _view.SetStatus(string.IsNullOrEmpty(message) ? stage.ToString() : message);
        }

        /// <summary>下载确认：交给启动页弹框；页面不存在时直接放行（自动化/无头场景）</summary>
        private void HandleConfirmDownload(ResourceUpdateInfo info, Action<bool> answer)
        {
            if (_view == null) { answer(true); return; }
            _view.AskDownload(info, answer);
        }

        private void Fail(string message)
        {
            _running = false;
            if (_view == null)
            {
                _view = new LaunchView();
                _view.Create();
            }
            _view.ShowError(message, Retry);
        }

        private void Teardown()
        {
            try
            {
                if (UI != null) { UI.CloseAll(); }
                if (Audio != null) { Audio.Dispose(); ServiceLocator.Unregister<AudioService>(); Audio = null; }
                if (Network != null) { Network.Shutdown(); Network = null; }
                if (Resource != null) { ServiceLocator.Unregister<ResourceService>(); Resource = null; }
                if (UI != null) { ServiceLocator.Unregister<UIManager>(); UI = null; }
            }
            catch (Exception e)
            {
                GameLog.Error("[Launch] 清理异常", e);
            }
        }
    }
}
