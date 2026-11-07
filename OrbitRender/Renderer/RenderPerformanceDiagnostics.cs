using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace OrbitRender.Renderer
{
    internal enum RenderPerformanceStage
    {
        Preparation, Simulation, Settle, HostCompleteFrame, HostBeforeSimulation, HostAfterSimulation,
        HostBeforeCapture, HostAfterCapture, CameraGameplay, CameraBackground,
        CameraBackgroundStatic, CameraHud, CanvasCapture, CanvasLayout, CanvasHierarchy,
        ReadbackIssue, ReadbackWait, ReadbackCopy, ReadbackLatency, EncoderBufferWait,
        EncoderPipeWrite, EncoderDrain, AudioCapture, AudioQuery, AudioRender,
        AudioCopy, AudioPeakScan, AudioWavWrite, AudioPipeWrite, AudioDrain,
        AudioRealtimePacing, WebcamCompose, MediaCompose, Mux, Cleanup, Count
    }

    internal enum RenderPerformanceCounter
    {
        SimulationFrames, SettleFrames, SettledBeforeYield, SettleRefreshesSkipped, HostCallbackFailures,
        HierarchyNodes, HierarchyRebuilds, GameplayRenders, OutputFrames,
        ReadbackRequests, DirectReadbacks, CopiedReadbacks, SynchronousReadbacks,
        AudioSampleFrames, AudioZeroReads, AudioRenderFailures, AudioSilenceFrames,
        AudioBufferAllocations, Count
    }

    internal enum RenderPerformanceQueue { Readback, Encoder, Count }

    // OrbitRender's assembly is excluded by OverlayAssemblyDiscovery, and
    // OverlayMethodDiscovery follows calls only within the discovered assemblies.
    // NoInlining also keeps these Stopwatch calls out of
    // overlay callers whose IL the virtual clock rewrites. Never use Unity time
    // or the host's replay clock to measure elapsed wall time.
    internal sealed class RenderPerformanceDiagnostics
    {
        private const int BucketCount = 48;
        private const int QueueBuckets = 33;
        private readonly long started;
        private readonly long[] ticks = new long[(int)RenderPerformanceStage.Count];
        private readonly long[] calls = new long[(int)RenderPerformanceStage.Count];
        private readonly long[] maximum = new long[(int)RenderPerformanceStage.Count];
        private readonly long[] histograms = new long[(int)RenderPerformanceStage.Count * BucketCount];
        private readonly long[] counters = new long[(int)RenderPerformanceCounter.Count];
        private readonly long[] queueHistograms = new long[(int)RenderPerformanceQueue.Count * QueueBuckets];
        private readonly long[] queueMaximum = new long[(int)RenderPerformanceQueue.Count];
        internal static RenderPerformanceDiagnostics Current { get; set; }

        internal RenderPerformanceDiagnostics() { started = Timestamp(); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static long Timestamp() => Stopwatch.GetTimestamp();

        internal void Record(RenderPerformanceStage stage, long start)
            => RecordTicks(stage, Timestamp() - start);

        internal void RecordTicks(RenderPerformanceStage stage, long elapsed)
        {
            int index = (int)stage;
            elapsed = Math.Max(0, elapsed);
            Interlocked.Add(ref ticks[index], elapsed);
            Interlocked.Increment(ref calls[index]);
            UpdateMaximum(ref maximum[index], elapsed);
            // Power-of-two microsecond buckets: fixed memory, no frame log or
            // sample allocations. Percentiles are upper bounds, not exact values.
            double microseconds = elapsed * (1000000.0 / Stopwatch.Frequency);
            int bucket = 0;
            long upper = 1;
            while (microseconds > upper && bucket < BucketCount - 1) { bucket++; upper <<= 1; }
            Interlocked.Increment(ref histograms[index * BucketCount + bucket]);
        }

        internal void Increment(RenderPerformanceCounter counter, long amount = 1)
            => Interlocked.Add(ref counters[(int)counter], amount);

        internal void ObserveQueue(RenderPerformanceQueue queue, int depth)
        {
            depth = Math.Max(0, depth);
            int index = (int)queue;
            Interlocked.Increment(ref queueHistograms[index * QueueBuckets + Math.Min(depth, QueueBuckets - 1)]);
            UpdateMaximum(ref queueMaximum[index], depth);
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long previous = Interlocked.Read(ref target);
            while (value > previous)
            {
                long observed = Interlocked.CompareExchange(ref target, value, previous);
                if (observed == previous) return;
                previous = observed;
            }
        }

        internal long Count(RenderPerformanceStage stage) => Interlocked.Read(ref calls[(int)stage]);
        internal long Counter(RenderPerformanceCounter counter) => Interlocked.Read(ref counters[(int)counter]);

        // StopCoroutine can dispose the render iterator before ForceCancel has
        // assigned its terminal state. Preserve the requested cancellation in
        // the one report written by that iterator's cleanup.
        internal static string ResolveReportStatus(string state, bool cancellationRequested)
            => cancellationRequested && state != "Completed" && state != "Failed" ? "Cancelled" : state;

        internal string BuildReport(string status, long frames, int fps, string metadata)
        {
            var text = new StringBuilder(8192);
            double wall = Math.Max(0, Timestamp() - started) / (double)Stopwatch.Frequency;
            double video = fps > 0 ? frames / (double)fps : 0;
            text.AppendLine("OrbitRender performance diagnostics v1");
            text.AppendLine("Status: " + status);
            text.AppendLine("Clock: non-inlined System.Diagnostics.Stopwatch.GetTimestamp; frequency=" + Stopwatch.Frequency);
            text.AppendLine("Scope: render preparation through cleanup; encoder availability/preflight and user confirmation are excluded.");
            text.AppendFormat(CultureInfo.InvariantCulture,
                "Job wall: {0:F6} s; encoded frames: {1}; video: {2:F6} s; wall/video-second: {3:F6}\n",
                wall, frames, video, video > 0 ? wall / video : 0);
            text.AppendLine(metadata ?? "");
            text.AppendLine("CPU wall durations only. Camera submission is not GPU execution time.");
            text.AppendLine("Stages overlap (worker pipe writes, readback latency, inclusive audio/canvas timings); do not sum them.");
            text.AppendLine("Host callbacks include provider work; provider IDs/internal timings require host-side instrumentation.");
            text.AppendLine("Camera timings count OrbitRender's manual passes; automatic/custom-FPS game passes require a GPU/game profiler.");
            text.AppendLine("p50/p95 are power-of-two microsecond upper bounds. Counts of zero mean unused or unavailable.");
            text.AppendLine();
            text.AppendLine("Stage,calls,total_seconds,max_ms,p50_upper_ms,p95_upper_ms");
            for (int i = 0; i < (int)RenderPerformanceStage.Count; i++)
            {
                long count = Interlocked.Read(ref calls[i]);
                text.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2:F6},{3:F6},{4:F6},{5:F6}\n",
                    (RenderPerformanceStage)i, count, Interlocked.Read(ref ticks[i]) / (double)Stopwatch.Frequency,
                    Interlocked.Read(ref maximum[i]) * (1000.0 / Stopwatch.Frequency),
                    DurationPercentile(i, count, 0.50), DurationPercentile(i, count, 0.95));
            }
            text.AppendLine();
            text.AppendLine("Counter,value");
            for (int i = 0; i < (int)RenderPerformanceCounter.Count; i++)
                text.AppendLine(((RenderPerformanceCounter)i) + "," + Interlocked.Read(ref counters[i]).ToString(CultureInfo.InvariantCulture));
            text.AppendLine();
            text.AppendLine("Queue event samples (not time-weighted): readback after enqueue; encoder submit/rent observations.");
            text.AppendLine("Queue,samples,p50_depth,p95_depth,max_depth (histogram caps at 32)");
            for (int i = 0; i < (int)RenderPerformanceQueue.Count; i++)
            {
                long count = 0;
                for (int bucket = 0; bucket < QueueBuckets; bucket++)
                    count += Interlocked.Read(ref queueHistograms[i * QueueBuckets + bucket]);
                text.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}\n",
                    (RenderPerformanceQueue)i, count, QueuePercentile(i, count, 0.50),
                    QueuePercentile(i, count, 0.95), Interlocked.Read(ref queueMaximum[i]));
            }
            return text.ToString();
        }

        private double DurationPercentile(int stage, long count, double percentile)
        {
            if (count == 0) return 0;
            long target = (long)Math.Ceiling(count * percentile), sum = 0;
            for (int bucket = 0; bucket < BucketCount; bucket++)
            {
                sum += Interlocked.Read(ref histograms[stage * BucketCount + bucket]);
                if (sum >= target) return (1L << bucket) / 1000.0;
            }
            return 0;
        }

        private int QueuePercentile(int queue, long count, double percentile)
        {
            if (count == 0) return 0;
            long target = (long)Math.Ceiling(count * percentile), sum = 0;
            for (int bucket = 0; bucket < QueueBuckets; bucket++)
            {
                sum += Interlocked.Read(ref queueHistograms[queue * QueueBuckets + bucket]);
                if (sum >= target) return bucket;
            }
            return 0;
        }

        internal void WriteReport(string path, string status, long frames, int fps, string metadata)
        {
            // Written once after drain/cleanup, outside the frame hot path.
            File.WriteAllText(path, BuildReport(status, frames, fps, metadata), new UTF8Encoding(false));
        }
    }
}
