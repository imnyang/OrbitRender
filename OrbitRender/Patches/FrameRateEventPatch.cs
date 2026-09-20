using HarmonyLib;
using OrbitRender.Renderer;

namespace OrbitRender.Patches
{
    [HarmonyPatch(typeof(ffxSetFrameRatePlus), "StartEffect")]
    internal static class FrameRateEventPatch
    {
        internal static int Revision { get; private set; }
        internal static bool Enabled { get; private set; }
        internal static float FrameRate { get; private set; }

        static void Postfix(ffxSetFrameRatePlus __instance)
        {
            if (!RendererController.ControlsTime || __instance == null) return;
            Enabled = __instance.enableCustomFrameRate && __instance.frameRate > 0f;
            FrameRate = Enabled ? __instance.frameRate : 0f;
            Revision++;
            Main.Entry.Logger.Log(Enabled
                ? "Custom frame-rate hold enabled: " + FrameRate.ToString("0.###") + " fps."
                : "Custom frame-rate hold disabled.");
        }

        internal static void ResetRuntimeState()
        {
            Enabled = false;
            FrameRate = 0f;
            Revision++;
        }
    }
}
