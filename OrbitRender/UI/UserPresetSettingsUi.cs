using System;
using System.Linq;
using UnityEngine;

namespace OrbitRender.UI
{
    internal static class UserPresetSettingsUi
    {
        private static UserRenderPreset selected;
        private static string name = "";
        private static string error;
        private static bool deleting;

        internal static void Draw(RendererSettings settings, Action applied)
        {
            GUILayout.Label(Localization.Get("user-presets"));
            var presets = UserRenderPresets.Items(settings).Where(p => p != null).ToArray();
            if (selected != null && !presets.Contains(selected)) selected = null;
            var choices = new[] { Localization.Get("user-preset-select") }.Concat(presets.Select(p => p.Name)).ToArray();
            var current = Array.IndexOf(presets, selected) + 1;
            var next = GUILayout.SelectionGrid(current, choices, Math.Min(3, choices.Length));
            if (next != current)
            {
                selected = next > 0 ? presets[next - 1] : null;
                deleting = false;
                error = null;
                if (selected != null)
                {
                    name = selected.Name;
                    AudioPreview.Stop();
                    selected.ApplyTo(settings);
                    applied();
                }
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Localization.Get("user-preset-name"), GUILayout.Width(100f));
            name = GUILayout.TextField(name, 64);
            GUILayout.EndHorizontal();
            GUILayout.Label(Localization.Get("user-preset-hint"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Localization.Get("user-preset-save-new")))
                Save(settings, null);
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && selected != null;
            if (GUILayout.Button(Localization.Get("user-preset-update")))
                Save(settings, selected);
            if (GUILayout.Button(Localization.Get("user-preset-rename")))
            {
                error = UserRenderPresets.Rename(settings, selected, name);
                if (error == null) settings.Save(Main.Entry);
            }
            if (GUILayout.Button(Localization.Get("user-preset-delete"))) deleting = true;
            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
            if (deleting && selected != null)
            {
                GUILayout.Label(Localization.Format("user-preset-delete-question", selected.Name));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Localization.Get("user-preset-delete-confirm")))
                {
                    UserRenderPresets.Items(settings).Remove(selected);
                    settings.Save(Main.Entry);
                    selected = null;
                    name = "";
                    deleting = false;
                    error = null;
                }
                if (GUILayout.Button(Localization.Get("cancel"))) deleting = false;
                GUILayout.EndHorizontal();
            }
            if (error != null) GUILayout.Label(Localization.Get(error));
            GUILayout.Space(8f);
        }

        private static void Save(RendererSettings settings, UserRenderPreset replacement)
        {
            var savedName = replacement != null ? replacement.Name : name;
            error = UserRenderPresets.Save(settings, savedName, settings, replacement);
            if (error != null) return;
            settings.Save(Main.Entry);
            selected = UserRenderPresets.Items(settings).First(p => p != null
                && string.Equals(p.Name, savedName.Trim(), StringComparison.OrdinalIgnoreCase));
            name = selected.Name;
            deleting = false;
        }
    }
}
