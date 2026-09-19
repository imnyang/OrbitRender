using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OrbitRender.Patches
{
    // CameraFilterPack_Blur_Movie divides its shader radius and source size by
    // FastFilter without validating the serialized value first. Some level
    // filter instances arrive with FastFilter == 0, which makes every
    // OnRenderImage call throw during capture and leaves Unity with a broken
    // post-process result. One is the filter's normal full-resolution divisor.
    [HarmonyPatch(typeof(CameraFilterPack_Blur_Movie), "OnRenderImage")]
    internal static class CameraFilterPatch
    {
        private const BindingFlags InstanceFieldFlags = BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic;
        private static bool logged;

        internal static void ResetRuntimeState()
        {
            var camera = scrCamera.instance;
            if (camera == null) return;

            var seen = new HashSet<MonoBehaviour>();
            foreach (var unityCamera in new[] { camera.camobj, camera.BGcam, camera.Bgcamstatic })
            {
                if (unityCamera == null) continue;
                foreach (var component in unityCamera.GetComponents<MonoBehaviour>())
                {
                    if (component == null || !seen.Add(component)) continue;
                    ResetTime(component);
                }
            }
        }

        private static void ResetTime(MonoBehaviour component)
        {
            var timeField = component.GetType().GetField("TimeX", InstanceFieldFlags);
            if (timeField != null && timeField.FieldType == typeof(float))
                timeField.SetValue(component, 1f);
        }

        private static void Prefix(CameraFilterPack_Blur_Movie __instance)
        {
            if (__instance == null || __instance.FastFilter > 0) return;
            // The filter's own default is 2. A zero value can be produced by
            // a level event that omits the integer property; use the same
            // valid default instead of changing the filter's resolution to a
            // different full-resolution mode on the first render.
            __instance.FastFilter = 2;
            if (logged) return;
            logged = true;
            Debug.Log("[OrbitRender] Repaired CameraFilterPack_Blur_Movie FastFilter=0 with the default value 2.");
        }
    }
}
