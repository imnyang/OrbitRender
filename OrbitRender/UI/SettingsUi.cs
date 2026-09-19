using UnityEngine;

namespace OrbitRender.UI
{
    // Shared controls for the UMM settings page and the in-game export dialog.
    // Keeping these in one place prevents the two entry points from slowly
    // drifting into different layouts and interaction patterns.
    internal static class SettingsUi
    {
        internal static string LabeledField(string label, string value, float width)
        {
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            return GUILayout.TextField(value ?? string.Empty, GUILayout.Width(width));
        }

        internal static bool DrawSectionHeader(string title, ref bool expanded)
        {
            var marker = expanded ? "▼ " : "▶ ";
            if (GUILayout.Button(marker + title, GUI.skin.button, GUILayout.ExpandWidth(true)))
                expanded = !expanded;
            return expanded;
        }

        internal static RendererPreset DrawPreset(RendererPreset value)
        {
            return (RendererPreset)GUILayout.Toolbar((int)value, new[] {
                Localization.Get("custom"),
                Localization.Get("preview"),
                "FullHD", "QHD", "UHD 4K"
            });
        }

        internal static EncoderSpeed DrawEncoding(EncoderSpeed value)
        {
            GUILayout.Label(Localization.Get("encoding-speed"));
            return (EncoderSpeed)GUILayout.Toolbar((int)value, new[] {
                Localization.Get("maximum"),
                Localization.Get("balanced"),
                Localization.Get("quality")
            });
        }

        internal static VideoEncoder DrawEncoder(VideoEncoder value)
        {
            GUILayout.Label(Localization.Get("video-encoder"));
            var selected = value == VideoEncoder.Auto ? 0
                : value == VideoEncoder.NvidiaNvenc ? 1
                : value == VideoEncoder.IntelQsv ? 2
                : value == VideoEncoder.AmdAmf ? 3 : 4;
            selected = GUILayout.Toolbar(selected, new[] {
                Localization.Get("auto"),
                "NVIDIA NVENC", "Intel QSV", "AMD AMF",
                Localization.Get("software")
            });
            switch (selected)
            {
                case 1: return VideoEncoder.NvidiaNvenc;
                case 2: return VideoEncoder.IntelQsv;
                case 3: return VideoEncoder.AmdAmf;
                case 4: return VideoEncoder.Software;
                default: return VideoEncoder.Auto;
            }
        }

        internal static VideoCodec DrawCodec(VideoCodec value)
        {
            GUILayout.Label(Localization.Get("video-codec"));
            return (VideoCodec)GUILayout.Toolbar((int)value,
                new[] { "H.264", "H.265", "VP9", "AV1" });
        }

        internal static VideoBitDepth DrawBitDepth(VideoBitDepth value)
        {
            GUILayout.Label(Localization.Get("video-bit-depth"));
            return (VideoBitDepth)GUILayout.Toolbar((int)value,
                new[] { "8-bit", "10-bit" });
        }
    }
}
