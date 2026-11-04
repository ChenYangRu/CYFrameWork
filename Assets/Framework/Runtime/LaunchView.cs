using System;
using GameFramework.Resource;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.Runtime
{
    /// <summary>
    /// 启动界面（框架侧，必须在任何业务代码之前就能显示）。
    /// 只用代码搭 UGUI，不依赖任何美术资源与 prefab——所以"资源还没更新完"也能显示进度。
    /// </summary>
    public class LaunchView
    {
        private GameObject _root;
        private Image _bar;
        private Text _status;
        private Text _percent;
        private GameObject _errorPanel;
        private Text _errorText;
        private Action _onRetry;

        private GameObject _confirmPanel;
        private Text _confirmText;
        private Action<bool> _confirmAnswer;

        public void Create()
        {
            if (_root != null) return;

            _root = new GameObject("[LaunchView]");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;          // 盖住所有业务 UI

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            var bg = UIUtil.CreatePanel("BG", _root.transform, new Color(0.06f, 0.07f, 0.09f, 1f));
            UIUtil.Stretch(bg.rectTransform);

            var title = UIUtil.CreateText("Title", _root.transform, "GAME FRAMEWORK", 64,
                new Color(0.85f, 0.88f, 0.92f));
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0.5f, 0.5f);
            titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.sizeDelta = new Vector2(1200, 100);
            titleRt.anchoredPosition = new Vector2(0, 120);

            _bar = UIUtil.CreateProgressBar("Progress", _root.transform, new Vector2(900, 22),
                new Color(0.35f, 0.72f, 0.95f));
            var barRt = _bar.transform.parent as RectTransform;
            barRt.anchorMin = new Vector2(0.5f, 0.5f);
            barRt.anchorMax = new Vector2(0.5f, 0.5f);
            barRt.anchoredPosition = new Vector2(0, -20);

            _status = UIUtil.CreateText("Status", _root.transform, "准备启动…", 26,
                new Color(0.72f, 0.76f, 0.82f));
            _status.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _status.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _status.rectTransform.sizeDelta = new Vector2(1200, 60);
            _status.rectTransform.anchoredPosition = new Vector2(0, -80);

            _percent = UIUtil.CreateText("Percent", _root.transform, "0%", 24,
                new Color(0.35f, 0.72f, 0.95f));
            _percent.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _percent.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _percent.rectTransform.sizeDelta = new Vector2(400, 50);
            _percent.rectTransform.anchoredPosition = new Vector2(0, -130);

            BuildErrorPanel();
            BuildConfirmPanel();
        }

        private void BuildErrorPanel()
        {
            var panel = UIUtil.CreatePanel("ErrorPanel", _root.transform, new Color(0.12f, 0.05f, 0.05f, 0.98f));
            UIUtil.Stretch(panel.rectTransform);
            _errorPanel = panel.gameObject;

            _errorText = UIUtil.CreateText("Msg", panel.transform, "", 28, new Color(1f, 0.72f, 0.68f));
            var rt = _errorText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200, 400);
            rt.anchoredPosition = new Vector2(0, 60);

            var retry = UIUtil.CreateButton("Retry", panel.transform, "重 试", delegate
            {
                _errorPanel.SetActive(false);
                if (_onRetry != null) _onRetry();
            }, new Vector2(280, 76), 30);
            var rrt = retry.transform as RectTransform;
            rrt.anchorMin = new Vector2(0.5f, 0.5f);
            rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.anchoredPosition = new Vector2(0, -220);

            _errorPanel.SetActive(false);
        }

        /// <summary>
        /// 下载前流量确认面板。**AOT 侧必须有**：在移动网络下静默下载几十 MB 是差评来源，
        /// 玩家点"拒绝"就等于放弃这次启动，不能静默继续。
        /// </summary>
        private void BuildConfirmPanel()
        {
            var panel = UIUtil.CreatePanel("ConfirmPanel", _root.transform, new Color(0.05f, 0.07f, 0.10f, 0.985f));
            UIUtil.Stretch(panel.rectTransform);
            _confirmPanel = panel.gameObject;

            _confirmText = UIUtil.CreateText("Msg", panel.transform, "", 28, new Color(0.9f, 0.93f, 0.97f));
            var rt = _confirmText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200, 320);
            rt.anchoredPosition = new Vector2(0, 80);

            var ok = UIUtil.CreateButton("Ok", panel.transform, "立即更新", delegate { AnswerConfirm(true); },
                new Vector2(300, 80), 28);
            var okRt = ok.transform as RectTransform;
            okRt.anchorMin = new Vector2(0.5f, 0.5f);
            okRt.anchorMax = new Vector2(0.5f, 0.5f);
            okRt.anchoredPosition = new Vector2(-180, -180);

            var cancel = UIUtil.CreateButton("Cancel", panel.transform, "退 出", delegate { AnswerConfirm(false); },
                new Vector2(300, 80), 28);
            var cRt = cancel.transform as RectTransform;
            cRt.anchorMin = new Vector2(0.5f, 0.5f);
            cRt.anchorMax = new Vector2(0.5f, 0.5f);
            cRt.anchoredPosition = new Vector2(180, -180);

            _confirmPanel.SetActive(false);
        }

        /// <summary>显示"需要下载 X MB"的确认框。answer 一定会被调用一次（确定或取消）。</summary>
        public void AskDownload(ResourceUpdateInfo info, Action<bool> answer)
        {
            if (_confirmPanel == null)
            {
                if (answer != null) answer(true);   // 没有界面就直接下，保证自动化流程不被卡死
                return;
            }

            _confirmAnswer = answer;
            if (_confirmText != null)
                _confirmText.text = info != null ? info.ToPlayerText() : "发现新版本，是否更新？";
            _confirmPanel.SetActive(true);
        }

        private void AnswerConfirm(bool ok)
        {
            if (_confirmPanel != null) _confirmPanel.SetActive(false);
            var a = _confirmAnswer;
            _confirmAnswer = null;
            if (a != null) a(ok);
        }

        public void SetProgress(float progress, string status)
        {
            UIUtil.SetBarFill(_bar, progress);
            if (_status != null && !string.IsNullOrEmpty(status)) _status.text = status;
            if (_percent != null) _percent.text = Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%";
        }

        public void SetStatus(string status)
        {
            if (_status != null) _status.text = status;
        }

        public void ShowError(string message, Action onRetry)
        {
            _onRetry = onRetry;
            if (_errorText != null) _errorText.text = message;
            if (_errorPanel != null) _errorPanel.SetActive(true);
            GameLog.Error("[Launch] " + message);
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}
