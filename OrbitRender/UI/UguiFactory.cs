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
        private static Sprite roundedSprite;
        private static Sprite largeRoundedSprite;
        private static Sprite toggleTrackSprite;
        private static Sprite circleSprite;
        private static Sprite sliderTrackSprite;

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
            // Canvas order is compared within a sorting layer. The editor's
            // UI may use a layer above Default, regardless of our high order.
            var topLayer = 0;
            var topLayerValue = int.MinValue;
            foreach (var layer in SortingLayer.layers)
            {
                var value = SortingLayer.GetLayerValueFromID(layer.id);
                if (value <= topLayerValue) continue;
                topLayer = layer.id; topLayerValue = value;
            }
            canvas.sortingLayerID = topLayer;
            var scaler = root.GetComponent<CanvasScaler>();
            // Progress cards and prompts used screen pixels in IMGUI. The export
            // dialog applies its own DPI/height scale without depending on aspect ratio.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
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

        internal static Image ProgressFill(Transform parent)
        {
            var fill = Image(parent, "Progress", Accent).GetComponent<Image>();
            SetProgress(fill, 0f);
            return fill;
        }

        internal static void SetProgress(Image fill, float value)
        {
            // Sprite-less Images always draw a full quad, ignoring fillAmount.
            // Size that quad relative to the track so it also follows resizing.
            var rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static Text Text(Transform parent, string value, int size = UiLayout.LabelFontSize,
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
            Round(go.GetComponent<Image>());
            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = ControlHover;
            colors.pressedColor = new Color(0.72f, 0.68f, 0.78f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(() => action());
            var label = Text(go.transform, caption, UiLayout.LabelFontSize, TextAnchor.MiddleCenter,
                primary ? new Color(0.10f, 0.09f, 0.12f, 1f) : Foreground);
            label.fontStyle = FontStyle.Bold;
            Stretch(label.rectTransform);
            return button;
        }

        internal static Button TextButton(Transform parent, string caption, Action action)
        {
            var label = Text(parent, caption);
            label.raycastTarget = true;
            var button = label.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            var colors = button.colors;
            colors.normalColor = Accent;
            colors.highlightedColor = Foreground;
            colors.selectedColor = Foreground;
            colors.pressedColor = Muted;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }

        internal static InputField Input(Transform parent, string value, Action<string> changed)
        {
            var go = Image(parent, "Input", new Color(0.10f, 0.09f, 0.12f, 1f), true);
            Round(go.GetComponent<Image>());
            var input = go.AddComponent<InputField>();
            var text = Text(go.transform, value);
            SetOffsets(text.rectTransform, 9f, 6f, 9f, 6f);
            input.textComponent = text;
            input.text = value ?? string.Empty;
            if (changed != null) input.onValueChanged.AddListener(value2 => changed(value2));
            return input;
        }

        internal static Toggle Toggle(Transform parent, string caption, bool value, Action<bool> changed)
        {
            var go = New(parent, "Toggle", typeof(Toggle), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().preferredHeight = 36f;
            var toggle = go.GetComponent<Toggle>();
            var track = Image(go.transform, "Track", Control, true).GetComponent<Image>();
            track.sprite = ToggleTrackSprite;
            track.type = UnityEngine.UI.Image.Type.Simple;
            Anchor(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-42f, -10f), new Vector2(-4f, 10f));
            var knob = Image(track.transform, "Knob", Foreground).GetComponent<Image>();
            Circle(knob);
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0f, .5f);
            knob.rectTransform.sizeDelta = new Vector2(14f, 14f);
            toggle.targetGraphic = track;
            toggle.graphic = null;
            var label = Text(go.transform, caption);
            SetOffsets(label.rectTransform, 2f, 0f, 56f, 0f);
            Action<bool> refresh = selected => {
                track.color = selected ? Accent : Control;
                knob.color = selected ? new Color(.13f, .11f, .15f, 1f) : Foreground;
                knob.rectTransform.anchoredPosition = new Vector2(selected ? 28f : 10f, 0f);
            };
            toggle.isOn = value;
            refresh(value);
            toggle.onValueChanged.AddListener(v => { refresh(v); if (changed != null) changed(v); });
            return toggle;
        }

        internal static Dropdown Dropdown(Transform parent, string[] values, int selected, Action<int> changed,
            int[] separatorIndices = null)
        {
            var go = Image(parent, "Dropdown", Control, true);
            Round(go.GetComponent<Image>());
            var dropdown = go.AddComponent<OverlayDropdown>();
            dropdown.SeparatorIndices = separatorIndices;
            dropdown.targetGraphic = go.GetComponent<Image>();
            var label = Text(go.transform, string.Empty);
            SetOffsets(label.rectTransform, 12f, 4f, 30f, 4f);
            dropdown.captionText = label;
            var arrow = Text(go.transform, "▼", 11, TextAnchor.MiddleCenter, Muted);
            Anchor(arrow.rectTransform, new Vector2(1f, 0f), Vector2.one,
                new Vector2(-28f, 0f), new Vector2(-6f, 0f));
            dropdown.options.Clear();
            foreach (var value in values) dropdown.options.Add(new Dropdown.OptionData(value));

            var template = Image(go.transform, "Template", Surface, true);
            Round(template.GetComponent<Image>());
            var templateRect = template.GetComponent<RectTransform>();
            // Set the pivot before the offsets: changing it afterwards moves
            // the popup down by half its height, leaving a gap below the field.
            templateRect.pivot = new Vector2(.5f, 1f);
            Anchor(templateRect, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, -4f - Mathf.Min(224f, Mathf.Max(32f, values.Length * 32f))), new Vector2(0f, -4f));
            var viewport = Image(template.transform, "Viewport", Color.white);
            // Clip the option backgrounds to the same rounded silhouette.
            Round(viewport.GetComponent<Image>());
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var content = New(viewport.transform, "Content", typeof(RectTransform), typeof(ToggleGroup));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f); contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(.5f, 1f); contentRect.sizeDelta = new Vector2(0f, 32f);
            var scroll = template.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = contentRect;
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 32f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            if (values.Length > 7)
            {
                viewport.GetComponent<RectTransform>().offsetMax = new Vector2(-14f, 0f);
                var scrollbarObject = Image(template.transform, "Scrollbar", Backdrop, true);
                Round(scrollbarObject.GetComponent<Image>());
                Anchor(scrollbarObject.GetComponent<RectTransform>(), new Vector2(1f, 0f), Vector2.one,
                    new Vector2(-10f, 5f), new Vector2(-4f, -5f));
                var scrollbar = scrollbarObject.AddComponent<Scrollbar>();
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                var handle = Image(scrollbarObject.transform, "Handle", Accent, true).GetComponent<Image>();
                Round(handle);
                SetOffsets(handle.rectTransform, 1f, 1f, 1f, 1f);
                scrollbar.handleRect = handle.rectTransform;
                scrollbar.targetGraphic = handle;
                scroll.verticalScrollbar = scrollbar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            }
            var item = New(content.transform, "Item", typeof(Toggle));
            var itemRect = item.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, .5f); itemRect.anchorMax = new Vector2(1f, .5f);
            itemRect.sizeDelta = new Vector2(0f, 32f);
            var itemBackground = Image(item.transform, "Item Background", Control, true).GetComponent<Image>();
            Stretch(itemBackground.rectTransform);
            var itemCheck = Image(item.transform, "Item Checkmark", Accent).GetComponent<Image>();
            Circle(itemCheck);
            Anchor(itemCheck.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, -7f), new Vector2(22f, 7f));
            var itemLabel = Text(item.transform, string.Empty);
            SetOffsets(itemLabel.rectTransform, 30f, 0f, 4f, 0f);
            var itemToggle = item.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBackground;
            itemToggle.graphic = itemCheck;
            var itemColors = itemToggle.colors;
            itemColors.normalColor = Color.white;
            itemColors.highlightedColor = new Color(.75f, .68f, .9f, 1f);
            itemColors.selectedColor = itemColors.highlightedColor;
            itemToggle.colors = itemColors;
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            template.SetActive(false);
            dropdown.value = Mathf.Clamp(selected, 0, values.Length - 1);
            dropdown.RefreshShownValue();
            if (changed != null) dropdown.onValueChanged.AddListener(v => changed(v));
            return dropdown;
        }

        internal static GameObject Toolbar(Transform parent, string[] values, int selected, Action<int> changed)
        {
            var row = Row(parent, 32f);
            row.GetComponent<HorizontalLayoutGroup>().spacing = 4f;
            var buttons = new Button[values.Length];
            Action<int> refresh = value => {
                for (var i = 0; i < buttons.Length; i++)
                {
                    buttons[i].GetComponent<Image>().color = i == value ? Accent : Control;
                    buttons[i].GetComponentInChildren<Text>().color = i == value
                        ? new Color(0.10f, 0.09f, 0.12f, 1f) : Foreground;
                }
            };
            for (var i = 0; i < values.Length; i++)
            {
                var index = i;
                buttons[i] = Button(row.transform, values[i], () => {
                    refresh(index);
                    if (changed != null) changed(index);
                });
                var colors = buttons[i].colors;
                colors.selectedColor = Color.white;
                buttons[i].colors = colors;
                Preferred(buttons[i], 0f);
            }
            refresh(Mathf.Clamp(selected, 0, values.Length - 1));
            return row;
        }

        internal static Slider Slider(Transform parent, float min, float max, float value, Action<float> changed)
        {
            // Receive pointer events across the whole control, including the
            // track and padding around the small handle.
            var go = Image(parent, "Slider", Color.clear, true);
            var slider = go.AddComponent<Slider>();
            var background = Image(go.transform, "Background", Backdrop).GetComponent<Image>();
            background.sprite = SliderTrackSprite;
            background.type = UnityEngine.UI.Image.Type.Sliced;
            Anchor(background.rectTransform, new Vector2(0f, .5f), new Vector2(1f, .5f),
                new Vector2(6f, -2f), new Vector2(-6f, 2f));
            var fillArea = New(go.transform, "Fill Area", typeof(RectTransform));
            Anchor(fillArea.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(1f, .5f),
                new Vector2(6f, -2f), new Vector2(-6f, 2f));
            var fill = Image(fillArea.transform, "Fill", Accent).GetComponent<Image>();
            fill.sprite = SliderTrackSprite;
            fill.type = UnityEngine.UI.Image.Type.Sliced;
            Stretch(fill.rectTransform);
            // Slider stretches the handle in its non-moving axis. A zero-height
            // slide area keeps its diameter independent of the layout row height.
            var handleArea = New(go.transform, "Handle Slide Area", typeof(RectTransform));
            Anchor(handleArea.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(1f, .5f),
                new Vector2(6f, 0f), new Vector2(-6f, 0f));
            var handle = Image(handleArea.transform, "Handle", Foreground, true).GetComponent<Image>();
            Circle(handle);
            var handleRect = handle.rectTransform;
            handleRect.sizeDelta = new Vector2(12f, 12f);
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (changed != null) slider.onValueChanged.AddListener(v => changed(v));
            return slider;
        }

        internal static GameObject Row(Transform parent, float height = 30f)
        {
            var row = New(parent, "Row", typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
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

        internal static void Round(Image image, bool large = false)
        {
            if (image == null) return;
            image.sprite = large ? LargeRoundedSprite : RoundedSprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
        }

        private static void Circle(Image image)
        {
            image.sprite = CircleSprite;
            image.type = UnityEngine.UI.Image.Type.Simple;
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

        private static Sprite RoundedSprite => roundedSprite != null ? roundedSprite
            : (roundedSprite = CreateRoundedSprite(32, 32, 7));
        private static Sprite LargeRoundedSprite => largeRoundedSprite != null ? largeRoundedSprite
            : (largeRoundedSprite = CreateRoundedSprite(48, 48, 12));
        private static Sprite ToggleTrackSprite => toggleTrackSprite != null ? toggleTrackSprite
            : (toggleTrackSprite = CreateRoundedSprite(38, 20, 10));
        private static Sprite CircleSprite => circleSprite != null ? circleSprite
            : (circleSprite = CreateRoundedSprite(14, 14, 7));
        private static Sprite SliderTrackSprite => sliderTrackSprite != null ? sliderTrackSprite
            : (sliderTrackSprite = CreateRoundedSprite(16, 4, 2));

        private static Sprite CreateRoundedSprite(int width, int height, int radius)
        {
            // Four texels per logical pixel preserve curves when the canvas grows.
            // Mipmaps filter that coverage when displayed at smaller screen sizes.
            const int density = 4;
            width *= density;
            height *= density;
            radius *= density;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) {
                name = "OrbitRender uGUI rounded mask",
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var px = x + .5f;
                    var py = y + .5f;
                    var dx = Mathf.Max(Mathf.Max(radius - px, 0f), px - (width - radius));
                    var dy = Mathf.Max(Mathf.Max(radius - py, 0f), py - (height - radius));
                    var alpha = Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy));
                    // Keep transparent texels white too, so filtering does not
                    // introduce dark fringes before the UI shader applies tint.
                    pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            texture.SetPixels(pixels);
            texture.Apply(true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(.5f, .5f),
                100f * density, 0u, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("OrbitRender.EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
    }
}
