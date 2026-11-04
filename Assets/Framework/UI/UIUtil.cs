using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI
{
    /// <summary>
    /// 代码搭 UI 的快捷工厂。目的是让"没有一张美术图"也能把流程跑通，
    /// 正式项目把这里换成"加载 prefab"即可，接口不变。
    /// </summary>
    public static class UIUtil
    {
        private static Font _font;

        /// <summary>取一个能显示中文的字体（优先系统字体，失败回退内置）</summary>
        public static Font DefaultFont
        {
            get
            {
                if (_font != null) return _font;

                string[] candidates = { "Microsoft YaHei", "微软雅黑", "PingFang SC", "Noto Sans CJK SC", "SimHei", "Arial" };
                for (int i = 0; i < candidates.Length; i++)
                {
                    try
                    {
                        var f = Font.CreateDynamicFontFromOSFont(candidates[i], 24);
                        if (f != null) { _font = f; return _font; }
                    }
                    catch { }
                }

                // 内置字体：2022.2 起改名为 LegacyRuntime.ttf，2021 仍是 Arial.ttf。
                // 用宏分支而不是 try 两个名字——取不到会往 Console 打一条 error。
#if UNITY_2022_2_OR_NEWER
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
#else
                try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
#endif
                if (_font == null) GameLog.Warn("[UI] 未取到任何字体，文本可能不显示");
                return _font;
            }
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            rt.anchoredPosition3D = Vector3.zero;
            return rt;
        }

        public static Image CreatePanel(string name, Transform parent, Color color)
        {
            var rt = CreateRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>铺满父节点</summary>
        public static void Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static Text CreateText(string name, Transform parent, string content, int fontSize = 24,
            Color? color = null, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var rt = CreateRect(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.text = content;
            text.color = color ?? Color.white;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(string name, Transform parent, string label, Action onClick,
            Vector2 size, int fontSize = 24)
        {
            var rt = CreateRect(name, parent);
            rt.sizeDelta = size;

            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.16f, 0.42f, 0.62f, 0.95f);

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.22f, 0.55f, 0.78f, 1f);
            colors.pressedColor = new Color(0.10f, 0.30f, 0.46f, 1f);
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(delegate { onClick(); });

            var txt = CreateText("Label", rt, label, fontSize);
            Stretch(txt.rectTransform);
            return btn;
        }

        public static InputField CreateInput(string name, Transform parent, string placeholder, Vector2 size, int fontSize = 22)
        {
            var rt = CreateRect(name, parent);
            rt.sizeDelta = size;

            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.92f);

            var input = rt.gameObject.AddComponent<InputField>();

            var textRt = CreateRect("Text", rt);
            Stretch(textRt, 10, 6, 10, 6);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.color = new Color(0.1f, 0.1f, 0.1f);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var phRt = CreateRect("Placeholder", rt);
            Stretch(phRt, 10, 6, 10, 6);
            var ph = phRt.gameObject.AddComponent<Text>();
            ph.font = DefaultFont;
            ph.fontSize = fontSize;
            ph.color = new Color(0.4f, 0.4f, 0.4f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.text = placeholder;
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;

            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }

        /// <summary>
        /// 进度条（底 + 填充）。返回填充 Image，配合 SetBarFill 使用。
        /// ★ 注意：无 sprite 的 Image 用 FillMethod 不会有视觉变化（没有图可填），
        ///   所以这里用改锚点宽度的方式做进度，不依赖任何美术资源。
        /// </summary>
        public static Image CreateProgressBar(string name, Transform parent, Vector2 size, Color fillColor)
        {
            var rt = CreateRect(name, parent);
            rt.sizeDelta = size;

            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);
            bg.raycastTarget = false;

            var fillRt = CreateRect("Fill", rt);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(2, 2);
            fillRt.offsetMax = new Vector2(-2, -2);

            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;
            return fill;
        }

        /// <summary>设置进度条填充比例（0~1），改的是 anchorMax.x</summary>
        public static void SetBarFill(Image fill, float value)
        {
            if (fill == null) return;
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        public static VerticalLayoutGroup AddVerticalLayout(RectTransform rt, float spacing = 12f,
            int padding = 24, TextAnchor align = TextAnchor.UpperCenter)
        {
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childAlignment = align;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }
    }
}
