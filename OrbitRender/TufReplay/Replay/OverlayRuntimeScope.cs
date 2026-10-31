using System;

namespace TUFReplayRenderer.Replay;

// Host services and control UI must retain real input and wall-clock time.
internal static class OverlayRuntimeScope
{
    internal static bool AllowsAssembly(string name) => !string.IsNullOrEmpty(name)
        && !name.Equals("TUFReplay", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("AdofaiIpc", StringComparison.OrdinalIgnoreCase)
        && !name.StartsWith("AdofaiIpc.", StringComparison.OrdinalIgnoreCase)
        && !name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("DOTween", StringComparison.OrdinalIgnoreCase);

    internal static bool IsControlName(string name) => name != null
        && (name.StartsWith("TUFReplay", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("UnityModManager", StringComparison.OrdinalIgnoreCase)
            || name.Contains("CameraSetup") || name.Contains("ReplayTimeline"));
}
