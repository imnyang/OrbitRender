using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OrbitRender.Renderer;

internal static class RenderPerformanceDiagnosticsTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void Run()
    {
        Assert(RenderPerformanceDiagnostics.Current == null, "Diagnostic collection is enabled by default.");
        Assert(RenderPerformanceDiagnostics.ResolveReportStatus("Rendering", true) == "Cancelled",
            "Force-cancel cleanup reported a render still running.");
        Assert(RenderPerformanceDiagnostics.ResolveReportStatus("Preparing", true) == "Cancelled",
            "Cancellation during preparation did not reach the report.");
        Assert(RenderPerformanceDiagnostics.ResolveReportStatus("Completed", true) == "Completed",
            "A later cancellation changed a completed report.");
        Assert(RenderPerformanceDiagnostics.ResolveReportStatus("Failed", true) == "Failed",
            "Cancellation hid a failed render.");
        Assert(RenderPerformanceDiagnostics.ResolveReportStatus("Rendering", false) == "Rendering",
            "The report invented a cancellation.");
        var timestamp = typeof(RenderPerformanceDiagnostics).GetMethod("Timestamp", BindingFlags.NonPublic | BindingFlags.Static);
        Assert((timestamp.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0,
            "The real clock helper can be copied into virtualized overlay callers.");
        var clock = new RenderClock();
        var frozenFrame = clock.FrameIndex;
        long start = RenderPerformanceDiagnostics.Timestamp();
        Thread.Sleep(20);
        Assert(clock.FrameIndex == frozenFrame && RenderPerformanceDiagnostics.Timestamp() > start,
            "Diagnostic time did not advance independently of the frozen render clock.");

        var diagnostics = new RenderPerformanceDiagnostics();
        Task[] workers = new Task[4];
        for (int i = 0; i < workers.Length; i++)
            workers[i] = Task.Run(() => {
                for (int sample = 0; sample < 1000; sample++)
                {
                    diagnostics.RecordTicks(RenderPerformanceStage.EncoderPipeWrite, Stopwatch.Frequency);
                    diagnostics.Increment(RenderPerformanceCounter.OutputFrames);
                }
            });
        Task.WaitAll(workers);
        Assert(diagnostics.Count(RenderPerformanceStage.EncoderPipeWrite) == 4000,
            "Concurrent writer samples were lost.");
        Assert(diagnostics.Counter(RenderPerformanceCounter.OutputFrames) == 4000,
            "Concurrent counter increments were lost.");
        diagnostics.RecordTicks(RenderPerformanceStage.Simulation, -1);
        Assert(diagnostics.Count(RenderPerformanceStage.Simulation) == 1, "Zero-duration samples were dropped.");
        diagnostics.RecordTicks(RenderPerformanceStage.WebcamCompose, checked(Stopwatch.Frequency * 7200));
        foreach (int depth in new[] { 0, 1, 1, 5, 40 }) diagnostics.ObserveQueue(RenderPerformanceQueue.Readback, depth);
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        string report;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            report = diagnostics.BuildReport("Completed", 120, 60, "test metadata");
        }
        finally { Thread.CurrentThread.CurrentCulture = previousCulture; }
        Assert(report.Contains("EncoderPipeWrite,4000,4000.000000,1000.000000,1048.576000,1048.576000"),
            "Stage aggregate, duration percentile or invariant number formatting changed.");
        Assert(report.Contains("Readback,5,1,32,40"), "Queue percentiles or the uncapped maximum changed.");
        var longStage = Array.Find(report.Split('\n'), line => line.StartsWith("WebcamCompose,", StringComparison.Ordinal)).Split(',');
        Assert(double.Parse(longStage[5], CultureInfo.InvariantCulture) >= 7200000,
            "The duration histogram stopped being an upper bound for a two-hour composition.");
        Assert(report.Contains("encoded frames: 120; video: 2.000000 s") && report.Contains("do not sum them"),
            "The report omitted video duration or overlapping-stage guidance.");
        var path = Path.Combine(Path.GetTempPath(), "orbit-performance-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            diagnostics.WriteReport(path, "Cancelled", 12, 60, "test metadata");
            Assert(File.ReadAllText(path).Contains("Status: Cancelled"), "A partial-job report could not be read.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("Performance diagnostics: 16 assertions passed.");
    }
}
