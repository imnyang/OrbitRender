using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OrbitRender.Patches
{
    // A zero FastFilter causes division by zero during the filter's OnRenderImage.
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
            // Negative values are intentional for some levels and produce a
            // distinct filter effect. Only zero needs the fallback divisor.
            if (__instance == null || __instance.FastFilter != 0) return;
            // Level events can omit this value; use the filter's default divisor.
            __instance.FastFilter = 2;
            if (logged) return;
            logged = true;
            Debug.Log("[OrbitRender] [Patch] Repaired CameraFilterPack_Blur_Movie FastFilter=0 with the default value 2.");
        }
    }
}
