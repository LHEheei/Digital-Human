using System;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    internal static class ConversationUi
    {
        internal static readonly Color Ink = new Color(.08f, .18f, .25f);
        internal static readonly Color Accent = new Color(.05f, .42f, .53f);
        internal static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }
        internal static RectTransform Box(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }
        internal static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
        internal static void Fill(RectTransform rect, float padding = 0)
        { Place(rect, Vector2.zero, Vector2.one, Vector2.one * padding, Vector2.one * -padding); }
        internal static Text Text(string name, Transform parent, string value, int size = 24)
        {
            var text = Rect(name, parent).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = Ink;
            text.text = value;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.MiddleLeft;
            text.lineSpacing = 1.15f;
            return text;
        }
        internal static Button Button(string name, Transform parent, string label, Action action, float width = 112)
        {
            var rect = Box(name, parent, Accent);
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => action());
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width; layout.preferredHeight = 60; layout.minHeight = 60;
            var text = Text("Label", rect, label, 23);
            text.color = Color.white; text.alignment = TextAnchor.MiddleCenter;
            Fill(text.rectTransform, 8);
            return button;
        }
        internal static InputField Input(string name, Transform parent, string placeholder, int limit)
        {
            var rect = Box(name, parent, Color.white);
            var field = rect.gameObject.AddComponent<InputField>();
            field.characterLimit = limit;
            field.lineType = InputField.LineType.MultiLineNewline;
            var text = Text("Text", rect, "");
            text.alignment = TextAnchor.UpperLeft; text.verticalOverflow = VerticalWrapMode.Truncate;
            Fill(text.rectTransform, 16);
            var hint = Text("Placeholder", rect, placeholder);
            hint.color = new Color(.40f, .49f, .54f); hint.alignment = TextAnchor.UpperLeft;
            Fill(hint.rectTransform, 16);
            field.textComponent = text; field.placeholder = hint;
            return field;
        }
        internal static ScrollRect Scroll(string name, Transform parent, out RectTransform content)
        {
            var rect = Rect(name, parent);
            var scroll = rect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            var viewport = Box("Viewport", rect, new Color(1, 1, 1, .02f));
            Fill(viewport); viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", viewport);
            Place(content, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8); layout.spacing = 12;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            return scroll;
        }
        internal static HorizontalLayoutGroup Row(string name, Transform parent, out RectTransform rect)
        {
            rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 12; group.childControlWidth = true; group.childControlHeight = true;
            group.childForceExpandWidth = false; group.childForceExpandHeight = true;
            return group;
        }
    }
}
