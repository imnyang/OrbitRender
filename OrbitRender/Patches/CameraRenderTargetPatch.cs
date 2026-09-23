using HarmonyLib;
using OrbitRender.Renderer;
using UnityEngine;

namespace OrbitRender.Patches
{
    // ResetScene calls SetCustomFrameRate(false), which can release camRT while
    // leaving its width and height unchanged. The game's recreation check only
    // compares dimensions, so the next editor playback can display a black quad.
    [HarmonyPatch(typeof(scrCamera), "get_camRTNeedsRecreation")]
    internal static class CameraRenderTargetPatch
    {
        private static readonly System.Reflection.FieldInfo CameraTargetField =
            AccessTools.Field(typeof(scrCamera), "camRT");
        private static bool logged;

        private static void Postfix(scrCamera __instance, ref bool __result)
        {
            if (__result || !Main.Enabled || __instance == null) return;
            var renderer = RendererController.Instance;
            if (renderer == null || renderer.Busy || renderer.State == RenderState.Idle) return;

            var target = CameraTargetField?.GetValue(__instance) as RenderTexture;
            if (target == null || target.IsCreated()) return;

            // Let scrCamera.Update allocate a fresh texture and rebind both the
            // gameplay cameras and presentation quad through SetupRTCam(true).
            __result = true;
            if (!logged)
            {
                logged = true;
                Main.Entry?.Logger.Log("Recreating released game camera render texture before playback.");
            }
        }
    }

    [HarmonyPatch(typeof(scnEditor), "Play")]
    internal static class EditorPlaybackCameraPatch
    {
        private static readonly System.Reflection.FieldInfo CameraTargetField =
            AccessTools.Field(typeof(scrCamera), "camRT");
        private static readonly System.Reflection.FieldInfo QuadMeshField =
            AccessTools.Field(typeof(scrCamera), "camQuadMesh");

        private static void Postfix()
        {
            if (!Main.Enabled) return;
            var renderer = RendererController.Instance;
            if (renderer == null || renderer.Busy || renderer.State == RenderState.Idle) return;

            var camera = scrCamera.instance;
            if (camera == null) return;
            var target = CameraTargetField?.GetValue(camera) as RenderTexture;
            var quadMesh = QuadMeshField?.GetValue(camera) as MeshRenderer;
            if (target == null || quadMesh == null) return;

            if (!target.IsCreated() && !target.Create())
            {
                Main.Entry?.Logger.Error("The game camera render texture could not be recreated for editor playback.");
                return;
            }

            if (!camera.enableCustomFPS && quadMesh.material.mainTexture != target)
            {
                quadMesh.material.mainTexture = target;
                Main.Entry?.Logger.Log("Rebound the game camera presentation quad for editor playback.");
            }
            if (camera.quad != null && camera.Overlaycam != null && Screen.height > 0)
            {
                var quadHeight = camera.Overlaycam.orthographicSize * 2f;
                var quadWidth = quadHeight * Screen.width / Screen.height;
                var scale = camera.quad.transform.localScale;
                if (!Mathf.Approximately(scale.x, quadWidth)
                    || !Mathf.Approximately(scale.y, quadHeight))
                {
                    camera.quad.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);
                    Main.Entry?.Logger.Log("Restored the game camera presentation quad size for editor playback.");
                }
            }
            var uiCanvas = scrUIController.instance != null ? scrUIController.instance.canvas : null;
            Main.Entry?.Logger.Log("Editor playback camera: rtCreated=" + target.IsCreated()
                + ", sourceTargetsRT=" + (camera.camobj != null && camera.camobj.targetTexture == target)
                + ", quadActive=" + (camera.quad != null && camera.quad.activeInHierarchy)
                + ", quadUsesRT=" + (quadMesh.material.mainTexture == target)
                + ", quadScale=" + (camera.quad != null ? camera.quad.transform.localScale.ToString() : "missing")
                + ", uiCanvasEnabled=" + (uiCanvas != null && uiCanvas.enabled)
                + ", uiCanvasMode=" + (uiCanvas != null ? uiCanvas.renderMode.ToString() : "missing") + ".");
        }
    }
}
