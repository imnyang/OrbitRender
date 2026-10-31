using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TUFReplayRenderer.Replay;

// Discover ownership from actual overlay components, not a list of mod names.
internal static class OverlayAssemblyDiscovery
{
    internal static Assembly[] Discover()
        => DiscoverComponents().Select(t => t.Assembly).Distinct().ToArray();

    internal static Type[] DiscoverComponents()
    {
        var result = new HashSet<Type>();
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas == null || !canvas.isRootCanvas || !canvas.enabled
                || canvas.renderMode != RenderMode.ScreenSpaceOverlay
                || canvas.gameObject.scene.name != "DontDestroyOnLoad" || IsControlCanvas(canvas)) continue;
            foreach (MonoBehaviour component in canvas.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                Assembly assembly = component.GetType().Assembly;
                string name = assembly.GetName().Name;
                if (assembly == typeof(OverlayAssemblyDiscovery).Assembly || assembly == typeof(scrController).Assembly
                    || !OverlayRuntimeScope.AllowsAssembly(name)) continue;
                result.Add(component.GetType());
            }
        }
        return result.ToArray();
    }
    internal static bool IsControlCanvas(Canvas canvas)
    {
        for (Transform parent = canvas.transform; parent != null; parent = parent.parent)
            if (OverlayRuntimeScope.IsControlName(parent.name)) return true;
        return false;
    }
}
