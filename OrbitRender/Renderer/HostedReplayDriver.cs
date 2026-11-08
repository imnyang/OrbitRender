using System;
using System.Collections.Generic;
using TUFReplayRenderer.Media;
using TUFReplayRenderer.Replay;
using UnityEngine;

namespace OrbitRender.Renderer
{
    /// <summary>
    /// Adapts TUFReplay's replay host to the controller's replay seam. The host
    /// owns the recorded inputs, hits, settings and overlay mods; OrbitRender
    /// keeps the clock, the frame loop, capture and encoding.
    /// </summary>
    internal sealed class HostedReplayDriver : IRenderReplayDriver, IRenderOverlaySynchronization
    {
        private readonly HostedReplayHost<Canvas> host;

        internal HostedReplayDriver(HostedReplayRequest request, HostedReplayHost<Canvas> host)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        internal HostedReplayRequest Request { get; }

        // The overlay settle loop reads OptionalModClock's work fence; the host takes no part in it.
        public bool OverlayWorkSettled => OptionalModClock.Settled;
        public long OverlayWorkRevision => OptionalModClock.WorkRevision;
        public string OverlayPendingWork => OptionalModClock.PendingWork;
        public void OverlayRefreshFrameCompleted() { }

        /// <summary>The host's video clock, in microseconds from output frame zero.</summary>
        internal Func<long> VideoTimeSource => host.CurrentVideoTimeUs;
        internal bool TerminalReached => host.TerminalReached();
        internal long? GameplayStartVideoTimeUs => host.GameplayStartVideoTimeUs();
        internal double GameplayRate => host.GameplayRate();

        /// <summary>Runs after the level is ready and OptionalModClock has begun, before the song is prepared.</summary>
        internal void PrepareBeforeRender() => host.PrepareBeforeRender();

        public void Begin(RenderReplayContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            host.Begin(context.Width, context.Height, context.VideoFps,
                () => context.IsCancellationRequested, context.SetEndTime, tail => context.RequestStop(tail));
        }

        public void BeforeSimulationFrame(RenderReplayFrame frame) => host.BeforeSimulationFrame(frame.FrameIndex, frame.TimeSeconds);
        public void AfterSimulationFrame(RenderReplayFrame frame) => host.AfterSimulationFrame(frame.FrameIndex, frame.TimeSeconds);
        public void End(RenderReplayContext context, RenderState finalState) => host.End(finalState.ToString());
        internal void RestoreAfterRender() => host.RestoreAfterRender();

        /// <summary>Overlay and key-viewer canvases the host collected from mods. Host failures are logged.</summary>
        internal IEnumerable<Canvas> CaptureCanvases()
        {
            var result = new List<Canvas>();
            try
            {
                var canvases = host.GetCaptureCanvases();
                if (canvases != null)
                    foreach (var canvas in canvases)
                        if (canvas != null) result.Add(canvas.rootCanvas ?? canvas);
            }
            catch (Exception exception) { LogError("GetCaptureCanvases", exception); }
            return result;
        }

        internal void BeforeCapture()
        {
            try { host.OnBeforeCapture(); }
            catch (Exception exception) { LogError("OnBeforeCapture", exception); }
        }

        internal void AfterCapture()
        {
            try { host.OnAfterCapture(); }
            catch (Exception exception) { LogError("OnAfterCapture", exception); }
        }

        /// <summary>
        /// The host's videos to lay over the finished video, such as key viewers. Called once,
        /// after the last frame of a completed render; the host may block while it finishes
        /// them. A host failure or an unusable entry is logged and left out, never failing the render.
        /// </summary>
        internal List<MediaOverlay> OverlayVideos()
        {
            if (host.GetOverlayVideos == null) return new List<MediaOverlay>();
            var started = System.Diagnostics.Stopwatch.StartNew();
            string json;
            try { json = host.GetOverlayVideos(); }
            catch (Exception exception)
            {
                LogError("GetOverlayVideos", exception);
                return new List<MediaOverlay>();
            }
            var overlays = MediaOverlay.ParseList(json, problem => Log("[Replay] " + problem));
            if (overlays.Count > 0 || started.Elapsed.TotalSeconds >= 1)
                Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[Replay] Host overlay videos: {0} in {1:F2} s.", overlays.Count, started.Elapsed.TotalSeconds));
            return overlays;
        }

        private static void Log(string message)
        {
            try { Main.Entry?.Logger?.Log(message); }
            catch (Exception) { }
        }

        private static void LogError(string member, Exception error)
        {
            try { Main.Entry?.Logger?.Error("[Replay] Host " + member + ": " + error); }
            catch (Exception) { }
        }
    }
}
