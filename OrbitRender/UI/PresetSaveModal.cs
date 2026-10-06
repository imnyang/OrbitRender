using System;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRender.UI
{
    internal static class PresetSaveModal
    {
        private static GameObject root;
        private static InputField nameInput;
        private static Text error;
        private static Func<string, string> save;
        internal static bool IsOpen => root != null;

        internal static void Open(Func<string, string> onSave)
        {
            Close();
            save = onSave;
            root = UguiFactory.Canvas("OrbitRender.PresetSave", 32766);
            root.AddComponent<PresetSaveModalInput>();
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.scaleFactor = Mathf.Min(1.5f, Mathf.Max(.5f,
                Mathf.Min(Screen.width / 520f, Screen.height / 320f)));
            var dimmer = UguiFactory.Image(root.transform, "Dimmer", new Color(0f, 0f, 0f, .72f), true);
            UguiFactory.Stretch(dimmer.GetComponent<RectTransform>());
            var panel = UguiFactory.Image(root.transform, "Panel", UguiFactory.Surface, true);
            UguiFactory.Round(panel.GetComponent<Image>(), true);
            UguiFactory.Anchor(panel.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-240f, -140f), new Vector2(240f, 140f));
            var title = UguiFactory.Text(panel.transform, Localization.Get("user-preset-save-title"), 17);
            title.fontStyle = FontStyle.Bold;
            UguiFactory.Anchor(title.rectTransform, Vector2.up, Vector2.one,
                new Vector2(24f, -50f), new Vector2(-24f, -22f));
            var label = UguiFactory.Text(panel.transform, Localization.Get("user-preset-name"));
            UguiFactory.Anchor(label.rectTransform, Vector2.up, Vector2.one,
                new Vector2(24f, -84f), new Vector2(-24f, -60f));
            nameInput = UguiFactory.Input(panel.transform, "", value => { if (error != null) error.text = ""; });
            nameInput.characterLimit = 64;
            UguiFactory.Anchor(nameInput.GetComponent<RectTransform>(), Vector2.up, Vector2.one,
                new Vector2(24f, -128f), new Vector2(-24f, -90f));
            error = UguiFactory.Text(panel.transform, "", 12, TextAnchor.UpperLeft, UguiFactory.Error);
            UguiFactory.Anchor(error.rectTransform, Vector2.up, Vector2.one,
                new Vector2(24f, -208f), new Vector2(-24f, -142f));
            var cancel = UguiFactory.Button(panel.transform, Localization.Get("cancel"), Close);
            UguiFactory.Anchor(cancel.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero,
                new Vector2(24f, 22f), new Vector2(144f, 58f));
            var confirm = UguiFactory.Button(panel.transform, Localization.Get("user-preset-save-confirm"), Submit, true);
            UguiFactory.Anchor(confirm.GetComponent<RectTransform>(), Vector2.right, Vector2.right,
                new Vector2(-144f, 22f), new Vector2(-24f, 58f));
            nameInput.ActivateInputField();
            nameInput.Select();
        }

        internal static void Submit()
        {
            if (!IsOpen || save == null) return;
            try
            {
                var message = save(nameInput.text);
                if (!string.IsNullOrEmpty(message)) { error.text = message; return; }
                Close();
            }
            catch (Exception ex) { error.text = ex.Message; }
        }

        internal static void Close()
        {
            if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            root = null;
            nameInput = null;
            error = null;
            save = null;
        }
    }

    internal sealed class PresetSaveModalInput : MonoBehaviour
    {
        private void Update()
        {
            if (!Main.Enabled || Main.Settings == null || Input.GetKeyDown(KeyCode.Escape))
                PresetSaveModal.Close();
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                PresetSaveModal.Submit();
        }
    }
}

