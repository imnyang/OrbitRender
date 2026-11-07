using System;
using OrbitRender.Renderer;

internal static class RenderPerformancePolicyTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
        // Exhaustive capability/fence combinations: only an enabled, fully
        // completed host with a stable legacy fence may avoid the refresh.
        for (int flags = 0; flags < 32; flags++)
        {
            bool enabled = (flags & 1) != 0, host = (flags & 2) != 0;
            bool before = (flags & 4) != 0, after = (flags & 8) != 0, stable = (flags & 16) != 0;
            Check(ReplaySettlePolicy.CanSkipRefresh(enabled, host, before, after, 20, stable ? 20 : 21)
                == (flags == 31), "Unsafe settle combination " + flags);
        }
        var cadence = new ProgressUpdateCadence(1000);
        Check(cadence.ShouldPublish(0, false), "First snapshot was delayed.");
        Check(!cadence.ShouldPublish(99, false), "Unchanged snapshot exceeded 10 Hz.");
        Check(cadence.ShouldPublish(100, false), "Scheduled snapshot was delayed.");
        Check(cadence.ShouldPublish(101, true), "State transition was delayed.");
        Check(!cadence.ShouldPublish(200, false), "Transition did not reset cadence.");
        Check(cadence.ShouldPublish(201, false), "Progress did not resume.");
        Console.WriteLine("Render performance policy tests passed (" + checks + " checks).");
    }
}
