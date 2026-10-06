#if !UMM
using UnityEngine;

namespace OrbitRender
{
    internal sealed class LoaderSettingsWindow : MonoBehaviour
    {
        private bool visible;
        private Rect bounds = new Rect(40, 40, 760, 640);
        private void Update()
        {
            if (!Main.Enabled || !Input.GetKeyDown(KeyCode.F7)) return;
            visible = !visible;
            if (!visible) Main.Settings?.Save(Main.Entry);
        }
        private void OnGUI()
        {
            if (!visible || Main.Settings == null) return;
            bounds = GUILayout.Window(0x4f5242, bounds, Draw, "OrbitRender (F7)");
        }
        private void Draw(int id)
        {
            Main.DrawSettings();
            if (GUILayout.Button(Localization.Get("loader-settings-save-close")))
            {
                Main.Settings.Save(Main.Entry);
                visible = false;
            }
            GUI.DragWindow(new Rect(0, 0, bounds.width, 24));
        }
    }
}
#endif
