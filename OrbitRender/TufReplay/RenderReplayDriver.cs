using System;

namespace OrbitRender.Renderer
{

    /// <summary>Feeds recorded gameplay into a render without enabling autoplay or OS input.</summary>
    public interface IRenderReplayDriver
    {
        void Begin(RenderReplayContext context);
        void BeforeSimulationFrame(RenderReplayFrame frame);
        void AfterSimulationFrame(RenderReplayFrame frame);
        void End(RenderReplayContext context, RenderState finalState);
    }
    public interface IRenderOverlaySynchronization
    {
        bool OverlayWorkSettled { get; }
        long OverlayWorkRevision { get; }
        string OverlayPendingWork { get; }
        void OverlayRefreshFrameCompleted();
    }

    public readonly struct RenderReplayFrame
    {
        public long FrameIndex { get; }
        public double TimeSeconds { get; }
        public double DspTime { get; }
        public double DeltaTime { get; }

        internal RenderReplayFrame(RenderClock clock)
        {
            FrameIndex = clock.FrameIndex;
            TimeSeconds = clock.Time;
            DspTime = clock.DspTime;
            DeltaTime = 1.0 / clock.Fps;
        }
    }

    /// <summary>All times are seconds from video frame zero, including countdown and ending animations.</summary>
    public sealed class RenderReplayContext
    {
        private readonly Func<bool> cancellationRequested;
        private bool stopRequested;
        public RenderClock Clock { get; }
        public int Width { get; }
        public int Height { get; }
        public int VideoFps { get; }
        public bool IsCancellationRequested => cancellationRequested();
        public double EndTimeSeconds { get; private set; }

        internal RenderReplayContext(RenderClock clock, int width, int height, int videoFps,
            double endTimeSeconds, Func<bool> cancellationRequested)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Width = width;
            Height = height;
            if (videoFps <= 0) throw new ArgumentOutOfRangeException(nameof(videoFps));
            VideoFps = videoFps;
            this.cancellationRequested = cancellationRequested ?? throw new ArgumentNullException(nameof(cancellationRequested));
            SetEndTime(endTimeSeconds);
        }

        public void SetEndTime(double videoSeconds)
        {
            if (double.IsNaN(videoSeconds) || double.IsInfinity(videoSeconds) || videoSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(videoSeconds));
            EndTimeSeconds = videoSeconds;
        }

        /// <summary>Includes the current frame, then keeps simulating the requested ending delay.</summary>
        public void RequestStop(double tailSeconds = 0)
        {
            if (double.IsNaN(tailSeconds) || double.IsInfinity(tailSeconds) || tailSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(tailSeconds));
            if (stopRequested) return;
            SetEndTime(Clock.Time + tailSeconds + 1.0 / VideoFps);
            stopRequested = true;
        }
    }

    // Independent of Unity so lifecycle order, failure cleanup and terminal timing can be tested.
    internal sealed class RenderReplaySession
    {
        private readonly IRenderReplayDriver driver;
        private bool begun;
        private bool ended;
        private bool pendingFrame;
        private RenderReplayFrame frame;
        internal RenderReplayContext Context { get; }
        internal bool OverlayWorkSettled => driver is not IRenderOverlaySynchronization sync || sync.OverlayWorkSettled;
        internal long OverlayWorkRevision => driver is IRenderOverlaySynchronization sync ? sync.OverlayWorkRevision : 0;
        internal string OverlayPendingWork => driver is IRenderOverlaySynchronization sync ? sync.OverlayPendingWork : "";
        internal void OverlayRefreshFrameCompleted() { if (driver is IRenderOverlaySynchronization sync) sync.OverlayRefreshFrameCompleted(); }

        internal RenderReplaySession(IRenderReplayDriver driver, RenderReplayContext context)
        {
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        internal void Begin()
        {
            if (begun || ended) throw new InvalidOperationException("Replay render has already begun.");
            begun = true; // End must release partial setup even when Begin throws.
            driver.Begin(Context);
        }

        internal void BeforeFrame()
        {
            if (!begun || ended || pendingFrame)
                throw new InvalidOperationException("Replay render frame lifecycle is out of order.");
            frame = new RenderReplayFrame(Context.Clock);
            pendingFrame = true;
            driver.BeforeSimulationFrame(frame);
        }

        internal void AfterFrame()
        {
            if (!pendingFrame || ended) return;
            pendingFrame = false;
            driver.AfterSimulationFrame(frame);
        }

        internal void End(RenderState finalState)
        {
            if (!begun || ended) return;
            ended = true;
            pendingFrame = false;
            driver.End(Context, finalState);
        }
    }
}
