using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OrbitRender.UI
{
    // Small runtime uGUI builder. OrbitRender is distributed as a single DLL,
    // so its canvases cannot rely on prefabs or assets from an AssetBundle.
    internal static class UguiFactory
    {
        internal static readonly Color Backdrop = new Color(0.055f, 0.043f, 0.058f, 1f);
        internal static readonly Color Surface = new Color(0.18f, 0.15f, 0.19f, 1f);
        internal static readonly Color Control = new Color(0.27f, 0.23f, 0.29f, 1f);
        internal static readonly Color ControlHover = new Color(0.36f, 0.31f, 0.38f, 1f);
        internal static readonly Color Accent = new Color(0.78f, 0.74f, 0.84f, 1f);
        internal static readonly Color Foreground = new Color(0.98f, 0.98f, 0.98f, 1f);
        internal static readonly Color Muted = new Color(0.69f, 0.68f, 0.72f, 1f);
        internal static readonly Color Error = new Color(1f, 0.55f, 0.55f, 1f);
        private static Font font;

        internal static GameObject Canvas(string name, int sortingOrder)
        {
            EnsureEventSystem();
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Stretch(root.GetComponent<RectTransform>());
            return root;
        }

        internal static GameObject Image(Transform parent, string name, Color color, bool raycast = false)
        {
            var go = New(parent, name, typeof(Image));
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return go;
        }

        internal static Text Text(Transform parent, string value, int size = 22,
            TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            var go = New(parent, "Text", typeof(Text));
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = value ?? string.Empty;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color ?? Foreground;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        internal static Button Button(Transform parent, string caption, Action action, bool primary = false)
        {
            var go = Image(parent, "Button", primary ? Accent : Control, true);
            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = ControlHover;
            colors.pressedColor = new Color(0.72f, 0.68f, 0.78f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(() => action());
            var label = Text(go.transform, caption, 20, TextAnchor.MiddleCenter,
                primary ? new Color(0.10f, 0.09f, 0.12f, 1f) : Foreground);
            Stretch(label.rectTransform);
            return button;
        }

        internal static InputField Input(Transform parent, string value, Action<string> changed)
        {
            var go = Image(parent, "Input", new Color(0.10f, 0.09f, 0.12f, 1f), true);
            var input = go.AddComponent<InputField>();
            var text = Text(go.transform, value, 20);
            SetOffsets(text.rectTransform, 10f, 8f, 10f, 8f);
            input.textComponent = text;
            input.text = value ?? string.Empty;
            if (changed != null) input.onValueChanged.AddListener(value2 => changed(value2));
            return input;
        }

        internal static Toggle Toggle(Transform parent, string caption, bool value, Action<bool> changed)
        {
            var go = New(parent, "Toggle", typeof(Toggle), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().preferredHeight = 42f;
            var toggle = go.GetComponent<Toggle>();
            var box = Image(go.transform, "Box", Control, true).GetComponent<Image>();
            Anchor(box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(4f, -14f), new Vector2(32f, 14f));
            var mark = Image(box.transform, "Checkmark", Accent).GetComponent<Image>();
            SetOffsets(mark.rectTransform, 5f, 5f, 5f, 5f);
            toggle.targetGraphic = box;
            toggle.graphic = mark;
            var label = Text(go.transform, caption, 20);
            SetOffsets(label.rectTransform, 44f, 0f, 0f, 0f);
            toggle.isOn = value;
            if (changed != null) toggle.onValueChanged.AddListener(v => changed(v));
            return toggle;
        }

        internal static Dropdown Dropdown(Transform parent, string[] values, int selected, Action<int> changed)
        {
            var go = Image(parent, "Dropdown", Control, true);
            var dropdown = go.AddComponent<Dropdown>();
            var label = Text(go.transform, string.Empty, 19);
            SetOffsets(label.rectTransform, 12f, 4f, 30f, 4f);
            dropdown.captionText = label;
            dropdown.options.Clear();
            foreach (var value in values) dropdown.options.Add(new Dropdown.OptionData(value));

            var template = Image(go.transform, "Template", Surface, true);
            var templateRect = template.GetComponent<RectTransform>();
            Anchor(templateRect, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, -220f), new Vector2(0f, 0f));
            var viewport = Image(template.transform, "Viewport", Color.white);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var content = New(viewport.transform, "Content", typeof(RectTransform), typeof(ToggleGroup));
            Stretch(content.GetComponent<RectTransform>());
            var item = New(content.transform, "Item", typeof(Toggle));
            var itemBackground = Image(item.transform, "Item Background", Control, true).GetComponent<Image>();
            Stretch(itemBackground.rectTransform);
            var itemCheck = Image(item.transform, "Item Checkmark", Accent).GetComponent<Image>();
            Anchor(itemCheck.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, -7f), new Vector2(22f, 7f));
            var itemLabel = Text(item.transform, string.Empty, 18);
            SetOffsets(itemLabel.rectTransform, 30f, 0f, 4f, 0f);
            var itemToggle = item.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBackground;
            itemToggle.graphic = itemCheck;
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            template.SetActive(false);
            dropdown.value = Mathf.Clamp(selected, 0, values.Length - 1);
            dropdown.RefreshShownValue();
            if (changed != null) dropdown.onValueChanged.AddListener(v => changed(v));
            return dropdown;
        }

        internal static Slider Slider(Transform parent, float min, float max, float value, Action<float> changed)
        {
            var go = New(parent, "Slider", typeof(Slider));
            var background = Image(go.transform, "Background", Backdrop).GetComponent<Image>();
            Anchor(background.rectTransform, new Vector2(0f, .5f), new Vector2(1f, .5f),
                new Vector2(0f, -4f), new Vector2(0f, 4f));
            var fillArea = New(go.transform, "Fill Area", typeof(RectTransform));
            SetOffsets(fillArea.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            var fill = Image(fillArea.transform, "Fill", Accent).GetComponent<Image>();
            Stretch(fill.rectTransform);
            var handle = Image(go.transform, "Handle", Foreground, true).GetComponent<Image>();
            var handleRect = handle.rectTransform;
            handleRect.sizeDelta = new Vector2(18f, 28f);
            var slider = go.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (changed != null) slider.onValueChanged.AddListener(v => changed(v));
            return slider;
        }

        internal static GameObject Row(Transform parent, float height = 44f)
        {
            var row = New(parent, "Row", typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            row.GetComponent<LayoutElement>().preferredHeight = height;
            return row;
        }

        internal static void Preferred(Component component, float width, float height = -1f)
        {
            var element = component.gameObject.GetComponent<LayoutElement>()
                ?? component.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            if (height >= 0f) element.preferredHeight = height;
        }

        internal static GameObject New(Transform parent, string name, params Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static void SetOffsets(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        internal static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("Arial.ttf"));

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("OrbitRender.EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
    }
}
