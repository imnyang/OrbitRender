using System;
using System.Collections.Generic;
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

        // Old hosts use the legacy fence and an extra stability frame unchanged.
        public bool OverlayWorkSettled => OptionalModClock.Settled;
        public long OverlayWorkRevision => OptionalModClock.WorkRevision;
        public string OverlayPendingWork => OptionalModClock.PendingWork;
        public void OverlayRefreshFrameCompleted() { }

        internal System.Func<long> CaptureHierarchyRevision => host.CaptureHierarchyRevision;
        internal System.Func<long> CaptureLayoutRevision => host.CaptureLayoutRevision;

        internal bool CanSkipOverlayRefresh(bool enabled, long frameIndex, double videoSeconds)
        {
            if (!enabled || host.TryCompleteFrame == null || !OptionalModClock.Settled) return false;
            var revision = OptionalModClock.WorkRevision;
            var diagnostics = RenderPerformanceDiagnostics.Current;
            var start = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            try
            {
                var completed = host.TryCompleteFrame(frameIndex, videoSeconds);
                var skip = ReplaySettlePolicy.CanSkipRefresh(enabled, completed, true,
                    OptionalModClock.Settled, revision, OptionalModClock.WorkRevision);
                if (skip && diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.SettleRefreshesSkipped);
                return skip;
            }
            catch
            {
                if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.HostCallbackFailures);
                throw;
            }
            finally { if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.HostCompleteFrame, start); }
        }

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

        public void BeforeSimulationFrame(RenderReplayFrame frame)
        {
            var diagnostics = RenderPerformanceDiagnostics.Current;
            var start = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            try { host.BeforeSimulationFrame(frame.FrameIndex, frame.TimeSeconds); }
            catch
            {
                if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.HostCallbackFailures);
                throw;
            }
            finally { if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.HostBeforeSimulation, start); }
        }

        public void AfterSimulationFrame(RenderReplayFrame frame)
        {
            var diagnostics = RenderPerformanceDiagnostics.Current;
            var start = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            try { host.AfterSimulationFrame(frame.FrameIndex, frame.TimeSeconds); }
            catch
            {
                if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.HostCallbackFailures);
                throw;
            }
            finally { if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.HostAfterSimulation, start); }
        }
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
            var diagnostics = RenderPerformanceDiagnostics.Current;
            var start = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            try { host.OnBeforeCapture(); }
            catch (Exception exception)
            {
                if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.HostCallbackFailures);
                LogError("OnBeforeCapture", exception);
            }
            finally { if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.HostBeforeCapture, start); }
        }

        internal void AfterCapture()
        {
            var diagnostics = RenderPerformanceDiagnostics.Current;
            var start = diagnostics != null ? RenderPerformanceDiagnostics.Timestamp() : 0;
            try { host.OnAfterCapture(); }
            catch (Exception exception)
            {
                if (diagnostics != null) diagnostics.Increment(RenderPerformanceCounter.HostCallbackFailures);
                LogError("OnAfterCapture", exception);
            }
            finally { if (diagnostics != null) diagnostics.Record(RenderPerformanceStage.HostAfterCapture, start); }
        }

        private static void LogError(string member, Exception error)
        {
            try { Main.Entry?.Logger?.Error("[Replay] Host " + member + ": " + error); }
            catch (Exception) { }
        }
    }
}
