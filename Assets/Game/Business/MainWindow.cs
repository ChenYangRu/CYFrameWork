using System.Text;
using GameFramework;
using GameFramework.Data;
using GameFramework.Network;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Business
{
    /// <summary>
    /// 主界面。同时充当**热更验证面板**：
    ///   · "业务逻辑版本" 是业务层的常量 → 出包后一眼看出玩家装的是哪一版逻辑
    ///   · "资源版本" 来自 YooAsset → 换图换配置，热更后版本变 = 资源热更通了
    ///   · "网络" 区显示连接状态与最后一次协议 → 调试网络层
    /// </summary>
    public class MainWindow : UIWindow
    {
        private Text _info;
        private Text _netInfo;
        private float _refreshTimer;
        private static readonly StringBuilder _sb = new StringBuilder(256);

        protected override void BuildUI()
        {
            var bg = UIUtil.CreatePanel("BG", transform, new Color(0.09f, 0.11f, 0.14f, 1f));
            UIUtil.Stretch(bg.rectTransform);

            var title = UIUtil.CreateText("Title", transform, "主 界 面", 52, new Color(0.86f, 0.9f, 0.95f));
            title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(900, 90);
            title.rectTransform.anchoredPosition = new Vector2(0, -100);

            _info = UIUtil.CreateText("Info", transform, "", 26, new Color(0.76f, 0.82f, 0.9f),
                TextAnchor.UpperLeft);
            var infoRt = _info.rectTransform;
            infoRt.anchorMin = new Vector2(0.5f, 0.5f);
            infoRt.anchorMax = new Vector2(0.5f, 0.5f);
            infoRt.sizeDelta = new Vector2(1300, 300);
            infoRt.anchoredPosition = new Vector2(0, 60);

            _netInfo = UIUtil.CreateText("NetInfo", transform, "", 24, new Color(0.6f, 0.85f, 0.7f),
                TextAnchor.UpperLeft);
            var netRt = _netInfo.rectTransform;
            netRt.anchorMin = new Vector2(0.5f, 0.5f);
            netRt.anchorMax = new Vector2(0.5f, 0.5f);
            netRt.sizeDelta = new Vector2(1300, 200);
            netRt.anchoredPosition = new Vector2(0, -240);

            var dataBtn = UIUtil.CreateButton("Data", transform, "存档 / CRUD", delegate
            {
                GameEntry.UI.Open("DataDemo");
            }, new Vector2(300, 72), 24);
            BottomCenter(dataBtn.transform as RectTransform, -330, 120);

            var echo = UIUtil.CreateButton("Echo", transform, "发送 Echo 测试包", OnClickEcho,
                new Vector2(300, 72), 24);
            BottomCenter(echo.transform as RectTransform, 0, 120);

            var logout = UIUtil.CreateButton("Logout", transform, "返回登录", delegate
            {
                if (GameEntry.Network != null) GameEntry.Network.Disconnect();
                GameEntry.UI.Open("Login");
            }, new Vector2(300, 72), 24);
            BottomCenter(logout.transform as RectTransform, 330, 120);
        }

        protected override void OnShow()
        {
            Refresh();
        }

        protected override void OnUpdate(float deltaTime)
        {
            _refreshTimer += deltaTime;
            if (_refreshTimer < 0.5f) return;   // 半秒刷一次，别每帧刷文本（Text 脏标记很贵）
            _refreshTimer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            _sb.Length = 0;
            _sb.Append("业务逻辑版本：v").Append(GameEntry.LogicVersion).Append('\n');
            if (GameEntry.Resource != null)
            {
                _sb.Append("资源包裹：").Append(GameEntry.Resource.PackageName).Append('\n');
                _sb.Append("资源版本：").Append(GameEntry.Resource.PackageVersion).Append('\n');
                _sb.Append("运行模式：").Append(GameEntry.Resource.Mode).Append('\n');
                _sb.Append("本次更新：").Append(GameEntry.Resource.LastDownloadCount).Append(" 个文件 / ")
                   .Append((GameEntry.Resource.LastDownloadBytes / 1048576f).ToString("0.00")).Append(" MB\n");
            }
            _sb.Append("游戏模式：").Append(GameModeUtil.NameOf(GameEntry.Mode)).Append('\n');
            var data = GameEntry.Data;
            if (data != null)
            {
                _sb.Append("存档槽位：").Append(data.SlotName)
                   .Append("  （").Append(data.IsAuthoritative ? "本地权威" : "本地仅缓存").Append("）").Append('\n');
                var items = data.Database != null ? data.Database.Table<ItemSave>() : null;
                if (items != null) _sb.Append("背包：").Append(items.Count).Append(" 条\n");
            }
            _sb.Append("App 版本：").Append(Application.version);
            if (_info != null) _info.text = _sb.ToString();

            _sb.Length = 0;
            var net = GameEntry.Network;
            if (net == null)
            {
                _sb.Append(GameEntry.Mode == GameMode.Offline
                    ? "网络：离线模式（未启用）\n存档与玩法全部在本地，不依赖服务器"
                    : "网络：未初始化");
            }
            else
            {
                _sb.Append("网络：").Append(net.Connected ? "已连接" : "未连接").Append('\n');
                _sb.Append("服务器：").Append(net.Config.host).Append(':').Append(net.Config.port).Append('\n');
                _sb.Append("重连次数：").Append(net.ReconnectCount).Append('\n');
                _sb.Append("最后协议：").Append(Cmd.NameOf(net.LastRecvCmd));
            }
            if (_netInfo != null) _netInfo.text = _sb.ToString();
        }

        /// <summary>底部居中定位（三个按钮等宽排布）</summary>
        private static void BottomCenter(RectTransform rt, float x, float y)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private void OnClickEcho()
        {
            var net = GameEntry.Network;
            if (net == null || !net.Connected)
            {
                GameLog.Warn("[Game] 未连接，无法发送 Echo");
                return;
            }

            var body = new ByteBuffer(32);
            body.WriteString("hello-" + Time.realtimeSinceStartup.ToString("0.0"));

            net.Request(Cmd.EchoReq, body, delegate (bool ok, Packet ack)
            {
                if (!ok || ack == null) { GameLog.Warn("[Game] Echo 超时"); return; }
                var r = ack.ReadBody();
                GameLog.Info("[Game] Echo 回包: " + r.ReadString());
            });
        }
    }
}
