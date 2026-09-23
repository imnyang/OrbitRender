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
