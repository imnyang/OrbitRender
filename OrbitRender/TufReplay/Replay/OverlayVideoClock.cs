using System;

namespace TUFReplayRenderer.Replay;

// Portable kernel for the proposed shared overlay runtime. It never changes
// Unity, system clocks, other assemblies or a running render by itself.
internal sealed class OverlayVideoClock
{
    private readonly Func<double> videoTime;
    private readonly DateTime utcOrigin;
    internal OverlayWorkFence Work { get; } = new();
    internal OverlayVideoClock(DateTime utcOrigin, Func<double> videoTime)
    { this.utcOrigin = utcOrigin; this.videoTime = videoTime ?? throw new ArgumentNullException(nameof(videoTime)); }
    internal double Seconds => Work.EventTime ?? videoTime();
    internal DateTime UtcAt(double seconds) => utcOrigin.AddTicks(checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond)));
    internal DateTime UtcNow => UtcAt(Seconds);
    internal double Delta(int fps, bool refreshOnly)
    {
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
        return refreshOnly || Work.EventTime.HasValue ? 0 : 1d / fps;
    }
}
