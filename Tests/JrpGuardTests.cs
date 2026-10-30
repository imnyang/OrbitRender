using System;
using System.Runtime.CompilerServices;
using OrbitRender.Renderer;
namespace JipperResourcePack.OverlayContents
{
    public static class PlayCount
    {
        public static int Attempts, Best;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void AddAttempts() { Attempts++; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static void RemoveAttempts() { Attempts--; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static void SetBest() { Best++; }
    }
}
namespace JipperResourcePack.KeyViewerContents
{
    public sealed class KeyViewer { public static KeyViewer Instance = new KeyViewer(); public long _eventOffsetTicks = 123; }
}
internal static class JrpGuardTests
{
    internal static void Run()
    {
        var count = typeof(JipperResourcePack.OverlayContents.PlayCount);
        if (count.Assembly.GetName().Name == "JipperResourcePack") throw new Exception("Fixture must use a different assembly name.");
        var viewer = JipperResourcePack.KeyViewerContents.KeyViewer.Instance;
        var guard = JrpRenderGuard.Begin(true);
        if (guard == null || viewer._eventOffsetTicks != long.MinValue >> 1) throw new Exception("Renamed JRP assembly or replay clock was not detected.");
        JipperResourcePack.OverlayContents.PlayCount.AddAttempts();
        JipperResourcePack.OverlayContents.PlayCount.RemoveAttempts();
        JipperResourcePack.OverlayContents.PlayCount.SetBest();
        if (JipperResourcePack.OverlayContents.PlayCount.Attempts != 0 || JipperResourcePack.OverlayContents.PlayCount.Best != 0) throw new Exception("JRP statistics changed during rendering.");
        guard.RestoreInputClock();
        if (viewer._eventOffsetTicks != 123) throw new Exception("JRP input clock offset was not restored.");
        guard.Dispose();
        JipperResourcePack.OverlayContents.PlayCount.AddAttempts();
        if (JipperResourcePack.OverlayContents.PlayCount.Attempts != 1) throw new Exception("JRP attempt updates did not resume.");
        Console.WriteLine("JRP assembly detection and render guard tests passed.");
    }
}
