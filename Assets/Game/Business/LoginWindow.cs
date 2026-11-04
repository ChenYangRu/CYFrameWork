using System;
using GameFramework;
using GameFramework.Network;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Business
{
    /// <summary>
    /// 登录界面（代码构建示例）。演示三件事：
    ///  ① 热更面板如何用框架 UIUtil 快速搭界面（无美术资源也能跑）
    ///  ② 网络层 Request/Ack 配对用法
    ///  ③ 连接失败必须给出可继续的出口（"单机进入"），不能把玩家卡死
    /// </summary>
    public class LoginWindow : UIWindow
    {
        private InputField _account;
        private Text _status;
        private Button _connectBtn;
        private bool _connecting;

        public override string ToString() { return "LoginWindow"; }

        protected override void BuildUI()
        {
            var bg = UIUtil.CreatePanel("BG", transform, new Color(0.07f, 0.09f, 0.12f, 1f));
            UIUtil.Stretch(bg.rectTransform);

            var title = UIUtil.CreateText("Title", transform, "登 录", 56, new Color(0.86f, 0.9f, 0.95f));
            title.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            title.rectTransform.sizeDelta = new Vector2(900, 90);
            title.rectTransform.anchoredPosition = new Vector2(0, 220);

            _account = UIUtil.CreateInput("Account", transform, "输入账号（任意，用于测试）", new Vector2(560, 72));
            var accRt = _account.transform as RectTransform;
            accRt.anchorMin = new Vector2(0.5f, 0.5f);
            accRt.anchorMax = new Vector2(0.5f, 0.5f);
            accRt.anchoredPosition = new Vector2(0, 80);
            _account.text = "test_player";

            _connectBtn = UIUtil.CreateButton("Connect", transform, "连接服务器并登录", OnClickConnect,
                new Vector2(560, 80), 28);
            var btnRt = _connectBtn.transform as RectTransform;
            btnRt.anchorMin = new Vector2(0.5f, 0.5f);
            btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.anchoredPosition = new Vector2(0, -20);

            var offline = UIUtil.CreateButton("Offline", transform, "以离线方式进入（不连服务器）",
                delegate { GameEntry.EnterMain(); }, new Vector2(560, 64), 24);
            var offRt = offline.transform as RectTransform;
            offRt.anchorMin = new Vector2(0.5f, 0.5f);
            offRt.anchorMax = new Vector2(0.5f, 0.5f);
            offRt.anchoredPosition = new Vector2(0, -120);

            _status = UIUtil.CreateText("Status", transform, "", 24, new Color(0.68f, 0.74f, 0.8f));
            _status.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _status.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _status.rectTransform.sizeDelta = new Vector2(1200, 80);
            _status.rectTransform.anchoredPosition = new Vector2(0, -230);
        }

        protected override void OnShow()
        {
            var net = GameEntry.Network;
            if (net == null)
            {
                SetStatus("网络服务未初始化");
                return;
            }
            SetStatus(string.Format("游戏模式 {0}｜{1}｜{2}:{3}",
                GameModeUtil.NameOf(GameEntry.Mode),
                TransportLabel(net.Config),
                net.Config.host, net.Config.port));
        }

        /// <summary>把传输方式翻成人话（WebSocket 时要提醒 wss，否则 https 页面会被拦）</summary>
        private static string TransportLabel(NetworkConfig cfg)
        {
            if (cfg.transport != TransportType.WebSocket) return "TCP";
            return cfg.webSocketSecure ? "WebSocket(wss)" : "WebSocket(ws)";
        }

        private void OnClickConnect()
        {
            if (_connecting) return;

            var net = GameEntry.Network;
            if (net == null) { SetStatus("网络服务不可用"); return; }

            _connecting = true;
            _connectBtn.interactable = false;
            SetStatus("连接中…");

            net.Connect(delegate (bool ok, string err)
            {
                if (!ok)
                {
                    _connecting = false;
                    _connectBtn.interactable = true;
                    SetStatus("连接失败：" + err + "\n（服务端未启动时可用『单机进入』）");
                    return;
                }

                SetStatus("已连接，登录中…");
                SendLogin();
            });
        }

        private void SendLogin()
        {
            var net = GameEntry.Network;

            var body = new ByteBuffer(64);
            body.WriteString(string.IsNullOrEmpty(_account.text) ? "guest" : _account.text);
            body.WriteString(Application.version);

            net.Request(Cmd.LoginReq, body, delegate (bool ok, Packet ack)
            {
                _connecting = false;
                _connectBtn.interactable = true;

                if (!ok || ack == null)
                {
                    SetStatus("登录超时（服务端未响应 LoginAck）");
                    return;
                }

                var reader = ack.ReadBody();
                int code = reader.ReadInt();
                string msg = reader.ReadString();

                if (code == 0)
                {
                    SetStatus("登录成功");
                    net.ResetReconnectCount();
                    GameEntry.EnterMain();
                }
                else
                {
                    SetStatus("登录失败(" + code + ")：" + msg);
                }
            });
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }
    }
}
