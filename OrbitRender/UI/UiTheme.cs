using UnityEngine;

namespace OrbitRender.UI
{
    // Styles shared by the export dialog and the small in-game prompts.
    internal static class UiTheme
    {
        private static GUIStyle window, windowBackground, button, primaryButton, section, textFieldStyle, toolbar, label, title, toggleLabel;
        private static Texture2D switchOff, switchOn, switchKnobOff, switchKnobOn;
        private static GUISkin sourceScrollSkin, scrollSkin;

        internal static GUIStyle Window { get { Ensure(); return window; } }
        internal static GUIStyle Button { get { Ensure(); return button; } }
        internal static GUIStyle PrimaryButton { get { Ensure(); return primaryButton; } }
        internal static GUIStyle Section { get { Ensure(); return section; } }
        internal static GUIStyle Field { get { Ensure(); return textFieldStyle; } }
        internal static GUIStyle Toolbar { get { Ensure(); return toolbar; } }
        internal static GUIStyle Label { get { Ensure(); return label; } }
        internal static GUIStyle Title { get { Ensure(); return title; } }

        internal static void DrawWindowBackground(Rect rect)
        {
            Ensure();
            if (Event.current.type == EventType.Repaint)
                windowBackground.Draw(rect, GUIContent.none, false, false, false, false);
        }

        internal static GUISkin ScrollSkin(GUISkin source)
        {
            if (scrollSkin != null && sourceScrollSkin == source
                && scrollSkin.verticalScrollbar.normal.background != null
                && scrollSkin.verticalScrollbarThumb.normal.background != null)
                return scrollSkin;
            sourceScrollSkin = source;
            scrollSkin = Object.Instantiate(source);
            // The editor reloads level assets after a render; keep this static IMGUI cache alive.
            scrollSkin.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var track = Rounded(new Color(0.12f, 0.10f, 0.14f), 12, 6);
            var thumb = Rounded(new Color(0.40f, 0.36f, 0.43f), 12, 6);
            var thumbHover = Rounded(new Color(0.54f, 0.49f, 0.57f), 12, 6);
            var transparent = new GUIStyle { fixedWidth = 12f, fixedHeight = 0f };

            scrollSkin.verticalScrollbar = new GUIStyle(source.verticalScrollbar) {
                fixedWidth = 12f,
                border = new RectOffset(6, 6, 6, 6)
            };
            SetStates(scrollSkin.verticalScrollbar, track, track, track, Color.clear);
            scrollSkin.verticalScrollbarThumb = new GUIStyle(source.verticalScrollbarThumb) {
                fixedWidth = 12f,
                border = new RectOffset(6, 6, 6, 6)
            };
            SetStates(scrollSkin.verticalScrollbarThumb, thumb, thumbHover, thumbHover, Color.clear);
            scrollSkin.verticalScrollbarUpButton = transparent;
            scrollSkin.verticalScrollbarDownButton = transparent;
            scrollSkin.scrollView = new GUIStyle(source.scrollView);
            scrollSkin.scrollView.normal.background = null;
            return scrollSkin;
        }

        internal static bool DrawToggle(bool value, string caption)
        {
            Ensure();
            var row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(36f), GUILayout.ExpandWidth(true));
            value = GUI.Toggle(row, value, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(new Rect(row.x + 2f, row.y, Mathf.Max(0f, row.width - 56f), row.height),
                    caption, toggleLabel);
                var track = new Rect(row.xMax - 42f, row.y + 8f, 38f, 20f);
                GUI.DrawTexture(track, value ? switchOn : switchOff);
                GUI.DrawTexture(new Rect(track.x + (value ? 21f : 3f), track.y + 3f, 14f, 14f),
                    value ? switchKnobOn : switchKnobOff);
            }
            return value;
        }

        private static void Ensure()
        {
            if (window != null && windowBackground != null
                && windowBackground.normal.background != null
                && button != null && button.normal.background != null
                && primaryButton != null && primaryButton.normal.background != null
                && textFieldStyle != null && textFieldStyle.normal.background != null
                && toolbar != null && toolbar.onNormal.background != null
                && switchOff != null && switchOn != null
                && switchKnobOff != null && switchKnobOn != null)
                return;
            var surface = Rounded(new Color(0.16f, 0.13f, 0.17f), 48, 11);
            var resting = Rounded(new Color(0.23f, 0.20f, 0.25f), 32, 7);
            var hovering = Rounded(new Color(0.31f, 0.27f, 0.33f), 32, 7);
            var pressed = Rounded(new Color(0.38f, 0.33f, 0.40f), 32, 7);
            var accent = Rounded(new Color(0.78f, 0.74f, 0.84f), 32, 7);
            var accentHover = Rounded(new Color(0.88f, 0.84f, 0.93f), 32, 7);
            var accentPressed = Rounded(new Color(0.68f, 0.64f, 0.75f), 32, 7);
            var input = Rounded(new Color(0.10f, 0.09f, 0.12f), 32, 7);
            var white = new Color(0.98f, 0.98f, 0.98f);

            window = new GUIStyle(GUI.skin.window) {
                border = new RectOffset(11, 11, 11, 11),
                padding = new RectOffset(22, 22, 20, 20)
            };
            SetStates(window, null, null, null, white);
            window.onNormal.background = window.onHover.background =
                window.onActive.background = window.onFocused.background = null;
            windowBackground = new GUIStyle {
                border = new RectOffset(11, 11, 11, 11)
            };
            SetStates(windowBackground, surface, surface, surface, white);

            button = new GUIStyle(GUI.skin.button) {
                border = new RectOffset(7, 7, 7, 7),
                padding = new RectOffset(12, 12, 7, 7),
                fixedHeight = 34,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            SetStates(button, resting, hovering, pressed, white);
            primaryButton = new GUIStyle(button);
            SetStates(primaryButton, accent, accentHover, accentPressed, new Color(0.10f, 0.09f, 0.12f));

            section = new GUIStyle(button) {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 7, 7)
            };
            textFieldStyle = new GUIStyle(GUI.skin.textField) {
                border = new RectOffset(7, 7, 7, 7),
                padding = new RectOffset(9, 9, 6, 6),
                fixedHeight = 30,
                fontSize = 12
            };
            SetStates(textFieldStyle, input, resting, input, white);
            toolbar = new GUIStyle(button) { fixedHeight = 32, padding = new RectOffset(5, 5, 5, 5) };
            toolbar.onNormal.background = accent;
            toolbar.onHover.background = accentHover;
            toolbar.onActive.background = accentPressed;
            toolbar.onFocused.background = accent;
            toolbar.onNormal.textColor = toolbar.onHover.textColor =
                toolbar.onActive.textColor = toolbar.onFocused.textColor = new Color(0.10f, 0.09f, 0.12f);
            label = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            label.normal.textColor = white;
            title = new GUIStyle(label) { fontSize = 17, fontStyle = FontStyle.Bold };
            toggleLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft };
            switchOff = Rounded(new Color(0.35f, 0.32f, 0.38f), 38, 20, 10);
            switchOn = Rounded(new Color(0.68f, 0.63f, 0.74f), 38, 20, 10);
            switchKnobOff = Rounded(new Color(0.90f, 0.88f, 0.92f), 14, 14, 7);
            switchKnobOn = Rounded(new Color(0.15f, 0.12f, 0.18f), 14, 14, 7);
        }

        private static void SetStates(GUIStyle style, Texture2D normal, Texture2D hover,
            Texture2D active, Color text)
        {
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = hover;
            style.normal.textColor = style.hover.textColor =
                style.active.textColor = style.focused.textColor = text;
        }

        private static Texture2D Rounded(Color color, int size, int radius)
        {
            return Rounded(color, size, size, radius);
        }

        private static Texture2D Rounded(Color color, int width, int height, int radius)
        {
            // These textures are referenced by static IMGUI styles, not scene objects.
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
                name = "OrbitRender UI style",
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var dx = Mathf.Max(Mathf.Max(radius - x - 0.5f, 0f), x + 0.5f - (width - radius));
                    var dy = Mathf.Max(Mathf.Max(radius - y - 0.5f, 0f), y + 0.5f - (height - radius));
                    texture.SetPixel(x, y, dx * dx + dy * dy <= radius * radius ? color : Color.clear);
                }
            texture.Apply();
            return texture;
        }
    }
}
