using System;
using System.Linq;
using OrbitRender.Renderer;
using System.Globalization;
using UnityEngine;

namespace OrbitRender.UI
{
    // Shared controls for the UMM settings page and the in-game export dialog.
    // Keeping these in one place prevents the two entry points from slowly
    // drifting into different layouts and interaction patterns.
    internal static class SettingsUi
    {
        internal static string LabeledField(string label, string value, float width, bool dark = false)
        {
            GUILayout.Label(label, dark ? UiTheme.Label : GUI.skin.label, GUILayout.ExpandWidth(false));
            return GUILayout.TextField(value ?? string.Empty, dark ? UiTheme.Field : GUI.skin.textField,
                GUILayout.Width(width));
        }

        internal static string AudioPreviewCaption => AudioPreview.IsLoading ? "Loading…"
            : AudioPreview.IsActive ? "Stop" : "Preview";

        internal static float DrawAudioGainSlider(float gainDb, ref string gainText,
            ref float syncedGainDb, bool dark = false, Action preview = null)
        {
            gainDb = RendererSettings.ClampAudioGainDb(gainDb);
            if (gainText == null || float.IsNaN(syncedGainDb)
                || Mathf.Abs(syncedGainDb - gainDb) > 0.0001f)
            {
                gainText = gainDb.ToString("0.##", CultureInfo.InvariantCulture);
                syncedGainDb = gainDb;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Localization.Get("audio-volume-db"), dark ? UiTheme.Label : GUI.skin.label,
                GUILayout.Width(dark ? 175f : 190f));
            if (preview != null && GUILayout.Button(AudioPreviewCaption, dark ? UiTheme.Label : GUI.skin.label,
                GUILayout.Width(70f))) preview();
            var sliderPosition = gainDb <= 0f
                ? (gainDb - RendererSettings.MinAudioGainDb) / -RendererSettings.MinAudioGainDb * 0.75f
                : 0.75f + gainDb / RendererSettings.MaxAudioGainDb * 0.25f;
            var newPosition = GUILayout.HorizontalSlider(sliderPosition, 0f, 1f,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(true));
            var newGainDb = newPosition <= 0.75f
                ? RendererSettings.MinAudioGainDb + newPosition / 0.75f * -RendererSettings.MinAudioGainDb
                : (newPosition - 0.75f) / 0.25f * RendererSettings.MaxAudioGainDb;
            // Preserve the exact saved value during layout/repaint to avoid
            // introducing floating-point drift when the slider has not moved.
            if (Mathf.Abs(newPosition - sliderPosition) > 0.000001f)
            {
                gainDb = RendererSettings.ClampAudioGainDb(Mathf.Round(newGainDb * 10f) / 10f);
                gainText = gainDb.ToString("0.##", CultureInfo.InvariantCulture);
                syncedGainDb = gainDb;
            }
            var percent = gainDb <= RendererSettings.MinAudioGainDb
                ? 0f : Mathf.Pow(10f, gainDb / 20f) * 100f;
            GUILayout.Label(percent.ToString("0.#", CultureInfo.InvariantCulture) + "%",
                dark ? UiTheme.Label : GUI.skin.label, GUILayout.Width(55f));
            var edited = GUILayout.TextField(gainText, dark ? UiTheme.Field : GUI.skin.textField,
                GUILayout.Width(65f));
            if (edited != gainText)
            {
                gainText = edited;
                if ((float.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    || float.TryParse(edited, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                    && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    gainDb = RendererSettings.ClampAudioGainDb(parsed);
                    syncedGainDb = gainDb;
                    if (parsed != gainDb)
                        gainText = gainDb.ToString("0.##", CultureInfo.InvariantCulture);
                }
            }
            GUILayout.Label("dB", dark ? UiTheme.Label : GUI.skin.label, GUILayout.Width(25f));
            GUILayout.EndHorizontal();
            return gainDb;
        }

        internal static bool DrawSectionHeader(string title, ref bool expanded, bool dark = false)
        {
            var marker = expanded ? "▼ " : "▶ ";
            if (GUILayout.Button(marker + title, dark ? UiTheme.Section : GUI.skin.button,
                GUILayout.ExpandWidth(true)))
                expanded = !expanded;
            return expanded;
        }

        internal static RendererPreset DrawPreset(RendererPreset value, bool dark = false)
        {
            return (RendererPreset)DrawRadioGroup((int)value, new[] {
                Localization.Get("custom"),
                Localization.Get("preview"),
                "FullHD", "QHD", "UHD 4K"
            }, dark);
        }

        internal static EncoderSpeed DrawEncoding(EncoderSpeed value, bool dark = false)
        {
            GUILayout.Label(Localization.Get("encoding-speed"), dark ? UiTheme.Label : GUI.skin.label);
            return (EncoderSpeed)DrawRadioGroup((int)value, new[] {
                Localization.Get("maximum"),
                Localization.Get("balanced"),
                Localization.Get("quality")
            }, dark);
        }

        private static bool encoderDropdownOpen;
        internal static EncoderAvailability.CombinedResult AvailableEncoders(VideoBitDepth depth, VideoContainer container)
        {
            return EncoderAvailability.GetCombined(RendererController.ResolveFfmpegExecutable(Main.Settings), depth, container);
        }
        internal static string EncoderLabel(EncoderAvailability.Choice choice)
        {
            return choice.Encoder == VideoEncoder.Auto
                ? Localization.Get("auto") + " (" + VideoCodecCatalog.Get(choice.Codec).DisplayName + ")"
                : EncoderAvailability.Label(choice.Encoder, choice.Codec);
        }
        internal static void DrawEncoder(ref VideoEncoder encoder, ref VideoCodec codec,
            VideoBitDepth depth, VideoContainer container)
        {
            GUILayout.Label(Localization.Get("video-encoder"));
            if (GUILayout.Button(Localization.Get("refresh-encoders"), GUILayout.Width(180f)))
                EncoderAvailability.Refresh();
            var result = AvailableEncoders(depth, container);
            if (!result.Complete) { GUILayout.Label(Localization.Get("checking-available-encoders")); return; }
            var choices = result.Choices;
            if (choices.Length == 0) { GUILayout.Label(Localization.Get("no-compatible-encoders")); return; }
            var selected = EncoderAvailability.SelectionIndex(choices, codec, encoder);
            encoder = choices[selected].Encoder; codec = choices[selected].Codec;
            if (GUILayout.Button(EncoderLabel(choices[selected]) + "  ▼", GUILayout.Width(340f)))
                encoderDropdownOpen = !encoderDropdownOpen;
            if (encoderDropdownOpen)
                for (var i = 0; i < choices.Length; i++)
                {
                    var choice = choices[i];
                    if (i > 0 && choices[i - 1].Codec != choice.Codec)
                        GUILayout.Box(GUIContent.none, GUILayout.Width(340f), GUILayout.Height(1f));
                    if (GUILayout.Button(EncoderLabel(choice), GUILayout.Width(340f)))
                    { encoder = choice.Encoder; codec = choice.Codec; encoderDropdownOpen = false; }
                }
        }
        private static bool containerDropdownOpen;
        internal static VideoContainer DrawContainer(VideoContainer value, bool showLabel = true)
        {
            if (showLabel) GUILayout.Label(Localization.Get("output-format"));
            var options = new[] { Localization.Get("auto"), ".mp4", ".ts", ".mkv", ".mov" };
            if (GUILayout.Button(options[Mathf.Clamp((int)value, 0, options.Length - 1)] + "  ▼", GUILayout.Height(30f)))
                containerDropdownOpen = !containerDropdownOpen;
            if (containerDropdownOpen)
                for (var i = 0; i < options.Length; i++)
                    if (GUILayout.Button(options[i], GUILayout.Height(26f)))
                    { value = (VideoContainer)i; containerDropdownOpen = false; }
            return value;
        }

        internal static VideoBitDepth DrawBitDepth(VideoBitDepth value, bool dark = false)
        {
            GUILayout.Label(Localization.Get("video-bit-depth"), dark ? UiTheme.Label : GUI.skin.label);
            return (VideoBitDepth)DrawRadioGroup((int)value,
                new[] { "8-bit", "10-bit" }, dark);
        }

        private static Texture2D radioOffTexture;
        private static Texture2D radioOnTexture;
        private static int DrawRadioGroup(int selected, string[] options, bool dark)
        {
            var style = new GUIStyle(dark ? UiTheme.Label : GUI.skin.label) {
                padding = new RectOffset(24, 4, 0, 0), alignment = TextAnchor.MiddleLeft
            };
            foreach (var state in new[] { style.normal, style.onNormal, style.hover, style.onHover,
                style.active, style.onActive, style.focused, style.onFocused }) state.background = null;
            if (radioOffTexture == null) radioOffTexture = CreateRadioTexture(false);
            if (radioOnTexture == null) radioOnTexture = CreateRadioTexture(true);
            GUILayout.BeginHorizontal();
            for (var i = 0; i < options.Length; i++)
            {
                var caption = new GUIContent(options[i]);
                var rect = GUILayoutUtility.GetRect(caption, style, GUILayout.Width(style.CalcSize(caption).x), GUILayout.Height(30f));
                if (GUI.Toggle(rect, selected == i, caption, style)) selected = i;
                var color = GUI.color;
                GUI.color = selected == i ? UguiFactory.Accent : UguiFactory.Muted;
                GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + (rect.height - 16f) / 2f, 16f, 16f),
                    selected == i ? radioOnTexture : radioOffTexture);
                GUI.color = color;
                GUILayout.Space(12f);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return selected;
        }

        private static Texture2D CreateRadioTexture(bool selected)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = selected ? "OrbitRender radio selected" : "OrbitRender radio",
                hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = x + .5f - size / 2f;
                    var dy = y + .5f - size / 2f;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var ring = Mathf.Clamp01(31.5f - distance) * Mathf.Clamp01(distance - 23.5f);
                    var dot = selected ? Mathf.Clamp01(13.5f - distance) : 0f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(ring, dot));
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
