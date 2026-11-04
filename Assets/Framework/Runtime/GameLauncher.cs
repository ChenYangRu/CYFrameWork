using System;
using System.Collections;
using GameFramework.Audio;
using GameFramework.Data;
using GameFramework.Network;
using GameFramework.Resource;
using GameFramework.UI;
using UnityEngine;

namespace GameFramework.Runtime
{
    /// <summary>
    /// 启动器（AOT 侧，唯一常驻入口）。
    /// 固定流程 —— **顺序不可乱**：
    ///   判定模式 → 初始化服务（数据最先）→ 资源更新 → 调业务入口
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

        /// <summary>当前游戏模式（离线单机 / 联网）。离线时 <see cref="Network"/> 为 null</summary>
        public GameMode Mode { get; private set; }
        /// <summary>数据服务（存档槽位 + 本地 CRUD）。两种模式都有，区别只是本地是否为权威数据</summary>
        public DataService Data { get; private set; }
        /// <summary>玩家当前所在的存档槽位名</summary>
        public string SlotName = "default";

        private Action _entry;
        private LaunchView _view;
        private bool _running;
        private bool _enteredGame;

        public bool EnteredGame { get { return _enteredGame; } }

        public static GameLauncher Create()
        {
            if (Instance == null) Instance = new GameLauncher();
            return Instance;
        }

        /// <summary>
        /// 启动。<paramref name="entry"/> 是业务入口（通常传 GameEntry.Start）。
        /// 之所以用委托而不是"反射找某个类型"：代码不再热更，没必要为了找入口引入字符串与反射；
        /// 也让框架层与业务层彻底解耦（Runtime 不引用 Game.Business）。
        /// </summary>
        public void Run(Action entry = null)
        {
            _entry = entry;
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
            Run(_entry);          // ★ 必须把入口带过去，否则重试后业务永远不启动
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
            Mode = LaunchConfig.ResolveGameMode(Config.gameMode);
            GameLog.Info("[Launch] 游戏模式=" + GameModeUtil.NameOf(Mode)
                         + "（存档槽位=" + SlotName + "）");

            // ---------- 1. 服务装配 ----------

            // 数据服务最先起：业务在 Start 里就可能读档
            if (Data == null)
            {
                Data = new DataService();
                Data.Init(Mode, SlotName);
                ServiceLocator.Register(Data);
            }
            if (Resource == null)
            {
                Resource = new ResourceService();
                Resource.OnProgress += HandleResourceProgress;
                Resource.OnStageChanged += HandleResourceStage;
                // 下载前必须问玩家（移动网络下静默下载几十 MB 是差评来源）
                Resource.ConfirmDownload = HandleConfirmDownload;
                ServiceLocator.Register(Resource);
            }

            // 网络只在联网模式启动；离线模式下刻意不创建，
            // 这样业务里任何 Network 调用都会拿到 null 并走本地分支（不会误发请求也不会空转重连）
            if (Mode == GameMode.Online)
            {
                if (Network == null)
                {
                    Network = new NetworkService();
                    Network.Init(NetworkConfig.Load());
                    ServiceLocator.Register(Network);
                }
            }
            else if (Network == null)
            {
                GameLog.Info("[Net] 离线模式：不启动网络（如需联机请把 launch.json 的 gameMode 改为 1）");
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

            // ---------- 2. 资源更新 ----------
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

            _view.SetProgress(0.85f, "进入游戏…");
            yield return null;

            // ---------- 3. 进入业务 ----------
            // 代码随包发布（不再有 DLL 加载 / 补元数据这一步），业务由 Composition Root
            // （Game.AOT 的 Bootstrap）直接以方法组传进来，框架层不认识业务类型。
            if (_entry != null)
            {
                try
                {
                    _entry();
                }
                catch (Exception e)
                {
                    GameLog.Error("[Launch] 业务入口异常", e);
                    Fail("业务入口执行失败\n" + e.Message);
                    yield break;
                }
            }
            else
            {
                GameLog.Warn("[Launch] 未提供业务入口（Run(null)），仅完成框架启动");
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
                // 先存档再拆服务：Dispose 内部会 Save，顺序反了会丢最后一段进度
                if (Data != null) { Data.Dispose(); ServiceLocator.Unregister<DataService>(); Data = null; }
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
