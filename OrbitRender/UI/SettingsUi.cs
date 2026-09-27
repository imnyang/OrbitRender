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

        internal static float DrawAudioGainSlider(float gainDb, ref string gainText,
            ref float syncedGainDb, bool dark = false)
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
            return (RendererPreset)DrawToolbar((int)value, new[] {
                Localization.Get("custom"),
                Localization.Get("preview"),
                "FullHD", "QHD", "UHD 4K"
            }, dark);
        }

        internal static EncoderSpeed DrawEncoding(EncoderSpeed value, bool dark = false)
        {
            GUILayout.Label(Localization.Get("encoding-speed"), dark ? UiTheme.Label : GUI.skin.label);
            return (EncoderSpeed)DrawToolbar((int)value, new[] {
                Localization.Get("maximum"),
                Localization.Get("balanced"),
                Localization.Get("quality")
            }, dark);
        }

        internal static VideoEncoder DrawEncoder(VideoEncoder value, bool dark = false)
        {
            GUILayout.Label(Localization.Get("video-encoder"), dark ? UiTheme.Label : GUI.skin.label);
            var selected = value == VideoEncoder.Auto ? 0
                : value == VideoEncoder.NvidiaNvenc ? 1
                : value == VideoEncoder.IntelQsv ? 2
                : value == VideoEncoder.AmdAmf ? 3 : 4;
            selected = DrawToolbar(selected, new[] {
                Localization.Get("auto"),
                "NVIDIA NVENC", "Intel QSV", "AMD AMF",
                Localization.Get("software")
            }, dark);
            switch (selected)
            {
                case 1: return VideoEncoder.NvidiaNvenc;
                case 2: return VideoEncoder.IntelQsv;
                case 3: return VideoEncoder.AmdAmf;
                case 4: return VideoEncoder.Software;
                default: return VideoEncoder.Auto;
            }
        }

        internal static VideoCodec DrawCodec(VideoCodec value, bool dark = false)
        {
            GUILayout.Label(Localization.Get("video-codec"), dark ? UiTheme.Label : GUI.skin.label);
            return (VideoCodec)DrawToolbar((int)value,
                new[] { "H.264", "H.265", "VP9", "AV1" }, dark);
        }

        internal static VideoBitDepth DrawBitDepth(VideoBitDepth value, bool dark = false)
        {
            GUILayout.Label(Localization.Get("video-bit-depth"), dark ? UiTheme.Label : GUI.skin.label);
            return (VideoBitDepth)DrawToolbar((int)value,
                new[] { "8-bit", "10-bit" }, dark);
        }

        private static int DrawToolbar(int selected, string[] options, bool dark)
        {
            return dark ? GUILayout.Toolbar(selected, options, UiTheme.Toolbar)
                : GUILayout.Toolbar(selected, options);
        }
    }
}
