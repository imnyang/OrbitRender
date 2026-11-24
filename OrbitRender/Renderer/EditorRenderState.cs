using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace OrbitRender.Renderer
{
    internal static class EditorRenderState
    {
        internal static void Rebuild(scnEditor targetEditor, scnGame targetLevel)
        {
            targetLevel.ResetScene();
            targetEditor.SwitchToEditMode();
            targetLevel.ReloadAssets(force: true, reloadDecorations: true);
            targetEditor.UpdateDecorationObjects();
            RepairInternalCameraTarget();
        }

        private static void RepairInternalCameraTarget()
        {
            var camera = scrCamera.instance;
            if (camera == null) return;
            SyncBackgroundCameras(camera);

            // SetCustomFrameRate(false) releases the texture currently shown by
            // the camera quad. After a custom-FPS event that texture is camRT,
            // so a later ResetScene can leave the same object assigned but no
            // longer created. Recreate it without replacing level/editor data.
            var field = AccessTools.Field(typeof(scrCamera), "camRT");
            var target = field?.GetValue(camera) as RenderTexture;
            if (target == null && field != null)
            {
                target = new RenderTexture(Math.Max(1, Screen.width), Math.Max(1, Screen.height), 24) {
                    name = "ADOFAI Camera RT"
                };
                field.SetValue(camera, target);
            }
            if (target != null && !target.IsCreated() && !target.Create())
                throw new InvalidOperationException("The game camera render texture could not be recreated.");

            if (target != null)
            {
                // Use the same presentation mesh that scrCamera uses for the
                // full-screen quad, with a child lookup as a fallback.
                var renderer = AccessTools.Field(typeof(scrCamera), "camQuadMesh")?.GetValue(camera) as MeshRenderer;
                if (renderer == null && camera.quad != null)
                    renderer = camera.quad.GetComponentInChildren<MeshRenderer>(true);
                if (renderer != null && renderer.material.mainTexture != target)
                    renderer.material.mainTexture = target;
            }
            if (camera.quad != null && camera.Overlaycam != null && Screen.height > 0)
            {
                // scrCamera.Update normally sizes this presentation quad when
                // camRT is first allocated. We allocate camRT in edit mode, so
                // that size-change branch no longer runs on the next Play.
                var quadHeight = camera.Overlaycam.orthographicSize * 2f;
                var quadWidth = quadHeight * Screen.width / Screen.height;
                camera.quad.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);
            }
            camera.SetupRTCam(false);
        }

        internal static void SyncBackgroundCameras(scrCamera camera)
        {
            if (camera == null || camera.camobj == null) return;
            var position = camera.camobj.transform.position;
            var rotation = camera.camobj.transform.rotation;
            if (camera.Bgcamstatic != null)
                camera.Bgcamstatic.transform.SetPositionAndRotation(position, rotation);
            if (camera.BGcam != null)
                camera.BGcam.transform.SetPositionAndRotation(position, rotation);
        }

        internal static bool IsValid(scnGame targetLevel, out string reason)
        {
            var camera = scrCamera.instance;
            if (camera == null || camera.Bgcamstatic == null || camera.BGcam == null || camera.camobj == null)
            {
                reason = "camera chain is missing";
                return false;
            }
            foreach (var item in new[] { camera.Bgcamstatic, camera.BGcam, camera.camobj })
            {
                if (!item.enabled || !item.gameObject.activeInHierarchy || item.cullingMask == 0)
                {
                    reason = item.name + " is disabled, inactive, or has an empty culling mask";
                    return false;
                }
                if (item.targetTexture != null)
                {
                    reason = item.name + " still targets a render texture in edit mode";
                    return false;
                }
            }
            if ((camera.Bgcamstatic.transform.position - camera.camobj.transform.position).sqrMagnitude > 0.0001f
                || (camera.BGcam.transform.position - camera.camobj.transform.position).sqrMagnitude > 0.0001f)
            {
                reason = "background camera transform does not match the gameplay camera";
                return false;
            }

            var target = AccessTools.Field(typeof(scrCamera), "camRT")?.GetValue(camera) as RenderTexture;
            if (target == null || !target.IsCreated())
            {
                reason = "internal camera render texture is released";
                return false;
            }
            var quadMesh = AccessTools.Field(typeof(scrCamera), "camQuadMesh")?.GetValue(camera) as MeshRenderer;
            if (quadMesh != null && quadMesh.material.mainTexture != target)
            {
                reason = "camera presentation quad is not bound to the internal render texture";
                return false;
            }
            if (camera.quad != null && camera.Overlaycam != null && Screen.height > 0)
            {
                var expectedHeight = camera.Overlaycam.orthographicSize * 2f;
                var expectedWidth = expectedHeight * Screen.width / Screen.height;
                var scale = camera.quad.transform.localScale;
                if (!Mathf.Approximately(scale.x, expectedWidth)
                    || !Mathf.Approximately(scale.y, expectedHeight))
                {
                    reason = "camera presentation quad has the wrong size";
                    return false;
                }
            }
            if (camera.flashPlusRendererBg == null || camera.flashPlusRendererFg == null
                || !camera.flashPlusRendererBg.enabled || !camera.flashPlusRendererFg.enabled)
            {
                reason = "Flash Plus background/foreground renderer is unavailable";
                return false;
            }

            // The editor may intentionally keep its default background hidden.
            // Bgcamstatic clears to SolidColor in that configuration, so the
            // absence of a visible default-background renderer is not a broken
            // render state and must not prevent export.
            reason = null;
            return true;
        }

        private static GameObject FindDefaultBackground(scnGame targetLevel)
        {
            if (targetLevel == null) return null;
            var custom = AccessTools.Field(typeof(scnGame), "customEditorBG")?.GetValue(targetLevel) as GameObject;
            if (custom != null) return custom;
            return AccessTools.Field(typeof(scnGame), "editorBG")?.GetValue(targetLevel) as GameObject;
        }

        internal static void Log(string stage, scnGame targetLevel)
        {
            var camera = scrCamera.instance;
            if (camera == null)
            {
                Main.Entry.Logger.Log("[Render] Render chain [" + stage + "]: <missing>");
                return;
            }
            var target = AccessTools.Field(typeof(scrCamera), "camRT")?.GetValue(camera) as RenderTexture;
            var quadMesh = AccessTools.Field(typeof(scrCamera), "camQuadMesh")?.GetValue(camera) as MeshRenderer;
            var quadTexture = quadMesh != null ? quadMesh.material.mainTexture : null;
            var background = FindDefaultBackground(targetLevel);
            var backgroundRenderers = background != null
                ? background.GetComponentsInChildren<UnityEngine.Renderer>(true)
                : new UnityEngine.Renderer[0];
            Main.Entry.Logger.Log("[Render] Render chain [" + stage + "]: "
                + DescribeCamera("Bgcamstatic", camera.Bgcamstatic) + "; "
                + DescribeCamera("BGcam", camera.BGcam) + "; "
                + DescribeCamera("camobj", camera.camobj) + "; camRT="
                + (target == null ? "missing" : target.width + "x" + target.height + "/created=" + target.IsCreated())
                + "; useRTCam=" + ReadBoolField(camera, "useRTCam")
                + ", customFPS=" + camera.enableCustomFPS
                + ", forceRTCam=" + camera.forceRTCam
                + ", lockCustomFrameUpdate=" + camera.lockCustomFrameUpdate
                + ", quadActive=" + (camera.quad != null && camera.quad.activeInHierarchy)
                + ", overlayActive=" + (camera.Overlaycam != null && camera.Overlaycam.gameObject.activeInHierarchy)
                + ", quadTexture=" + (quadTexture != null ? quadTexture.name : "missing")
                + ", quadUsesCamRT=" + (quadTexture == target)
                + ", quadScale=" + (camera.quad != null ? camera.quad.transform.localScale.ToString() : "missing")
                + ", hom=" + (ADOBase.controller != null && ADOBase.controller.homEnabled)
                + "; " + DescribeRenderer("flashBg", camera.flashPlusRendererBg)
                + "; " + DescribeRenderer("flashFg", camera.flashPlusRendererFg)
                + "; defaultBG(active=" + (background != null && background.activeInHierarchy)
                + ", renderers=" + backgroundRenderers.Length
                + ", visible=" + backgroundRenderers.Count(item => item.enabled && item.gameObject.activeInHierarchy) + ")");
        }

        private static bool ReadBoolField(object instance, string name)
        {
            var field = instance != null ? AccessTools.Field(instance.GetType(), name) : null;
            return field != null && (bool)field.GetValue(instance);
        }

        private static string DescribeCamera(string name, Camera camera)
        {
            if (camera == null) return name + "=<missing>";
            return name + "(active=" + camera.gameObject.activeInHierarchy + ", target="
                + (camera.targetTexture == null ? "screen" : camera.targetTexture.name)
                + ", enabled=" + camera.enabled + ", mask=0x" + camera.cullingMask.ToString("X8")
                + ", clear=" + camera.clearFlags + ", depth=" + camera.depth.ToString("F1")
                + ", ortho=" + camera.orthographicSize.ToString("F2")
                + ", pos=" + camera.transform.position
                + ", rect=" + camera.rect
                + ", effects=" + string.Join(",", camera.GetComponents<MonoBehaviour>()
                    .Where(item => item != null && item.enabled).Select(item => item.GetType().Name).ToArray()) + ")";
        }

        private static string DescribeRenderer(string name, UnityEngine.Renderer renderer)
        {
            if (renderer == null) return name + "=<missing>";
            return name + "(active=" + renderer.gameObject.activeInHierarchy + ", enabled=" + renderer.enabled
                + ", layer=" + renderer.gameObject.layer + ", color=" + renderer.material.color + ")";
        }
    }
}
