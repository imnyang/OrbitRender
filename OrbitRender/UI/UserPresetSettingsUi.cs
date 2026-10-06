using System;
using System.Linq;
using UnityEngine;

namespace OrbitRender.UI
{
    internal static class UserPresetSettingsUi
    {
        private static UserRenderPreset selected;
        private static bool choosing;
        private static Vector2 listScroll;

        internal static void Draw(RendererSettings settings, Action applied)
        {
            var presets = UserRenderPresets.Items(settings).Where(p => p != null).ToArray();
            if (selected != null && !presets.Contains(selected)) selected = null;
            var builtins = new[] { Localization.Get("custom"), Localization.Get("preview"), "FullHD", "QHD", "UHD 4K" };
            var choices = builtins.Concat(presets.Select(p => p.Name)).ToArray();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Localization.Get("preset"), GUILayout.Width(80f));
            var caption = selected != null ? selected.Name : builtins[Mathf.Clamp((int)settings.Preset, 0, builtins.Length - 1)];
            if (GUILayout.Button(caption + "  ▼", GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true)))
                choosing = !choosing;
            if (GUILayout.Button("Preset Save", GUILayout.Width(120f)))
            {
                choosing = false;
                // Capture now, so changing the main settings cannot affect a
                // save dialog that is already open.
                var source = new RendererSettings();
                UserRenderPreset.Capture("", settings).ApplyTo(source);
                PresetSaveModal.Open(name => {
                    var failure = UserRenderPresets.Save(settings, name, source);
                    if (failure != null) return Localization.Get(failure);
                    settings.Save(Main.Entry);
                    selected = UserRenderPresets.Items(settings).First(p => p != null
                        && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                    return null;
                });
            }
            GUILayout.EndHorizontal();
            if (!choosing) return;
            listScroll = GUILayout.BeginScrollView(listScroll,
                GUILayout.Height(Math.Min(168f, choices.Length * 28f)));
            for (var index = 0; index < choices.Length; index++)
            {
                if (!GUILayout.Button(choices[index], GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true))) continue;
                choosing = false;
                AudioPreview.Stop();
                if (index < builtins.Length)
                {
                    selected = null;
                    settings.Preset = (RendererPreset)index;
                    settings.OnChange();
                }
                else
                {
                    selected = presets[index - builtins.Length];
                    selected.ApplyTo(settings);
                }
                applied();
            }
            GUILayout.EndScrollView();
        }
    }
}
