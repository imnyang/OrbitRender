using System;
using OrbitRender.Renderer;

internal static class CaptureCacheTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void Run()
    {
        var cache = new CaptureRevisionCache();
        Assert(cache.NeedsRefresh(0), "The first hierarchy/layout use must refresh.");
        Assert(cache.NeedsRefresh(0), "A failed refresh must not validate the cache.");
        cache.Refreshed(0);
        Assert(!cache.NeedsRefresh(0), "An unchanged certified hierarchy/layout should be reused.");
        Assert(cache.NeedsRefresh(1), "Descendant additions must invalidate the host generation.");
        cache.Refreshed(1);
        Assert(!cache.NeedsRefresh(1), "A successful generation refresh was not cached.");
        Assert(cache.NeedsRefresh(0), "Revision reset or replacement must invalidate the cache.");
        cache.Refreshed(-1);
        Assert(cache.NeedsRefresh(-1), "Legacy hosts must retain every hierarchy/layout refresh.");
        Assert(cache.NeedsRefresh(1), "Losing the revision contract must invalidate previous state.");
        cache.Refreshed(long.MaxValue);
        Assert(!cache.NeedsRefresh(long.MaxValue), "Large revisions must be compared without truncation.");
        Assert(cache.NeedsRefresh(long.MinValue), "A negative revision must force the safe path.");

        for (var sample = 0; sample < 2; sample++)
            for (var custom = 0; custom < 2; custom++)
                for (var feedback = 0; feedback < 2; feedback++)
                    Assert(CameraSamplingPolicy.ShouldRender(sample != 0, custom != 0, feedback != 0)
                        == (sample != 0 || custom != 0 || feedback != 0),
                        "Camera sampling skipped a required output/custom-FPS/feedback pass.");
        Console.WriteLine("Capture cache and camera sampling: 18 checks passed.");
    }
}
