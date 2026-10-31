using System;
using System.Collections;
using System.Globalization;
using System.Reflection;

internal static class ExportWorkflowTests
{
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Internal).Invoke(target, args);
    private static object Get(object target, string property) => target.GetType().GetProperty(property, Internal).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Internal).SetValue(target, value);

    internal static void Run(Assembly assembly)
    {
        var validation = assembly.GetType("OrbitRender.UI.ExportValidation", true).GetMethod("Check", Internal);
        IList Validate(params object[] args) => (IList)validation.Invoke(null, args);
        var failures = Validate(true, "321", "0", "x", "14", "241", "NaN");
        Assert(failures.Count == 6, "Validation did not report each invalid input.");
        var first = failures[0];
        Assert((string)first.GetType().GetField("Field", Internal).GetValue(first) == "width", "First invalid field was not identified.");
        var last = failures[5];
        Assert((int)last.GetType().GetField("Tab", Internal).GetValue(last) == 1, "End delay should focus the game tab.");
        Assert(Validate(true, "320", "180", "1", "15", "15", "0").Count == 0, "Minimum values rejected.");
        Assert(Validate(true, "3840", "2160", "200", "1024", "240", "3600").Count == 0, "Maximum values rejected.");
        Assert(Validate(false, "bad", "bad", "bad", "60", "60", "2").Count == 0, "Hidden custom fields blocked a built-in preset.");
        Assert(Validate(true, "2147483648", "181", "201", "", "60", "Infinity").Count == 5, "Overflow/evenness/nonfinite validation failed.");
        var previous = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert(Validate(false, "", "", "", "60", "60", "1,5").Count == 0, "Local decimal separator rejected.");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        var containerCheck = assembly.GetType("OrbitRender.UI.ExportValidation", true).GetMethod("ContainerError", Internal);
        var codecType = assembly.GetType("OrbitRender.VideoCodec", true);
        var containerType = assembly.GetType("OrbitRender.VideoContainer", true);
        object Compatible(string codec, string container) => containerCheck.Invoke(null,
            new[] { Enum.Parse(codecType, codec), Enum.Parse(containerType, container) });
        Assert(Compatible("ProRes", "Mp4") != null && Compatible("ProRes", "Auto") == null
            && Compatible("ProRes", "Mkv") == null, "ProRes compatibility validation failed.");
        Assert(Compatible("FFV1", "Mp4") != null && Compatible("FFV1", "Ts") != null
            && Compatible("FFV1", "Auto") == null && Compatible("FFV1", "Mkv") == null,
            "FFV1 compatibility validation failed.");
        Assert(Compatible("AV1", "Ts") != null && Compatible("VP9", "Ts") != null
            && Compatible("H265", "Ts") == null, "MPEG-TS compatibility validation failed.");

        var pathsEqual = assembly.GetType("OrbitRender.Renderer.RendererController", true)
            .GetMethod("PathsEqual", Internal);
        var samePath = (bool)pathsEqual.Invoke(null, new object[] { "Level.adofai", "level.adofai" });
        Assert(samePath == (System.IO.Path.DirectorySeparatorChar == '\\'),
            "RPC path comparison does not match platform case sensitivity.");
        var wavLimit = assembly.GetType("OrbitRender.Renderer.GameAudioCapture", true)
            .GetMethod("ValidateWavDataSize", Internal);
        wavLimit.Invoke(null, new object[] { (long)uint.MaxValue - 36 });
        try
        {
            wavLimit.Invoke(null, new object[] { (long)uint.MaxValue - 35 });
            throw new Exception("Oversized WAV data was accepted.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is System.IO.IOException) { }
        var jobType = assembly.GetType("OrbitRender.Renderer.RpcRenderJob", true);
        var jobState = assembly.GetType("OrbitRender.Renderer.RpcJobState", true);
        var job = Activator.CreateInstance(jobType, new object[] { "job", "level", true, null });
        Call(job, "SetProgress", 1L, 1L, "finished.mp4");
        var completedOutput = jobType.GetProperty("CompletedOutputPath", Internal);
        Assert(completedOutput.GetValue(job) == null, "Unfinished job exposed its output for download.");
        Call(job, "SetState", Enum.Parse(jobState, "AwaitingConfirmation"), "hardware failed");
        var snapshotWithError = Call(job, "Snapshot");
        Assert((string)Get(snapshotWithError, "Error") == "hardware failed", "Fallback reason was not reported.");
        Call(job, "SetState", Enum.Parse(jobState, "Completed"), null);
        Assert((string)completedOutput.GetValue(job) == "finished.mp4", "Completed job output was unavailable for download.");
        Assert(Get(Call(job, "Snapshot"), "Error") == null, "Successful job retained an obsolete error.");
        // The reflection boundary in RenderApi is JSON, so the snapshot property
        // names are the contract other mods bind to.
        var errorType = assembly.GetType("OrbitRender.Renderer.RenderErrorDetails", true);
        var formatError = errorType.GetMethod("Create", Internal).Invoke(null, new object[]
            { "render_manifest_missing", "The recording manifest is missing.", "manifestPath", null, "manifest.json" });
        var failedJob = Activator.CreateInstance(jobType, new object[] { "failed", "level", true, null });
        Call(failedJob, "SetWarnings", (object)new[] { "no microphone audio" });
        jobType.GetMethod("Fail", Internal, null, new[] { errorType }, null).Invoke(failedJob, new[] { formatError });
        var contract = Newtonsoft.Json.Linq.JObject.Parse(
            Newtonsoft.Json.JsonConvert.SerializeObject(Call(failedJob, "Snapshot")));
        Assert((string)contract["state"] == "failed", "Job state was not serialized.");
        Assert((string)contract["errorCode"] == "render_manifest_missing", "Job error code was not serialized.");
        Assert((string)contract["errorDetails"]["field"] == "manifestPath", "Job error field was not serialized.");
        Assert((string)contract["errorDetails"]["file"] == "manifest.json", "Job error file was not serialized.");
        Assert((string)contract["warnings"][0] == "no microphone audio", "Job warnings were not serialized.");
        Assert(contract["waitingForGameFocus"] != null, "Job snapshot omitted the focus flag.");
        var queue = Activator.CreateInstance(assembly.GetType("OrbitRender.Renderer.RenderQueue", true), true);
        var optionsType = assembly.GetType("OrbitRender.Renderer.RenderRequestOptions", true);
        var stateType = assembly.GetType("OrbitRender.Renderer.QueueItemState", true);
        var options = Activator.CreateInstance(optionsType, true);
        var level = new object();
        Set(options, "Width", 640); Set(options, "OutputDirectory", "original-output");
        Set(options, "BgaMode", true); Set(options, "SelectionStartTile", 10); Set(options, "SelectionEndTile", 20);
        Call(queue, "Add", level, "first", options);
        Set(options, "Width", 1920); Set(options, "OutputDirectory", "changed-output");
        var items = (IList)Get(queue, "Items"); var firstItem = items[0];
        var snapshot = Get(firstItem, "Options");
        Assert((int)optionsType.GetField("Width", Internal).GetValue(snapshot) == 640, "Queued settings changed with their source.");
        Assert((string)optionsType.GetField("OutputDirectory", Internal).GetValue(snapshot) == "original-output", "Output folder was not captured.");
        Assert((bool)optionsType.GetField("BgaMode", Internal).GetValue(snapshot)
            && (int)optionsType.GetField("SelectionStartTile", Internal).GetValue(snapshot) == 10, "Selection/BGA snapshot lost.");
        Set(snapshot, "Width", 1);
        Assert((int)optionsType.GetField("Width", Internal).GetValue(Get(firstItem, "Options")) == 640, "Dispatch mutated the queued snapshot.");
        Call(queue, "Add", level, "second", options); Call(queue, "Add", level, "third", options);
        var second = items[1]; var third = items[2];
        Call(queue, "Move", third, -1); Assert(ReferenceEquals(items[1], third), "Reorder failed.");
        Call(queue, "Start"); Assert(ReferenceEquals(Call(queue, "BeginNext", level), firstItem), "Queue did not start in order.");
        Assert(Call(queue, "BeginNext", level) == null, "Two jobs ran at once.");
        Call(queue, "Remove", firstItem); Assert(items.Count == 3, "Active job was removed.");
        Call(queue, "Complete", Enum.Parse(stateType, "Failed"), "partial.mp4", "failure");
        Assert(!(bool)Get(queue, "IsRunning") && (int)Get(queue, "PendingCount") == 2, "Failure did not pause/preserve waiting jobs.");
        Call(queue, "Start"); Assert(Call(queue, "BeginNext", new object()) == null && !(bool)Get(queue, "IsRunning"), "Changed map was rendered.");
        Call(queue, "Start"); Assert(ReferenceEquals(Call(queue, "BeginNext", level), third), "Resume lost queue order.");
        Call(queue, "Pause"); Assert(ReferenceEquals(Get(queue, "Active"), third), "Pause interrupted the current render.");
        Call(queue, "Complete", Enum.Parse(stateType, "Completed"), "third.mp4", null);
        Assert(!(bool)Get(queue, "IsRunning") && (int)Get(queue, "PendingCount") == 1, "Pause-after-current was lost.");
        Call(queue, "ClearFinished"); Assert(items.Count == 1 && ReferenceEquals(items[0], second), "Clear-finished removed a waiting job.");
        Call(queue, "Start"); Call(queue, "BeginNext", level);
        Call(queue, "Complete", Enum.Parse(stateType, "Cancelled"), "", "cancelled");
        Assert(!(bool)Get(queue, "IsRunning") && Get(queue, "Active") == null, "Cancellation did not release/pause the queue.");
        Call(queue, "ClearFinished");
        Call(queue, "Add", level, "fourth", options); Call(queue, "Add", level, "fifth", options);
        Call(queue, "Start"); Call(queue, "BeginNext", level);
        Call(queue, "Complete", Enum.Parse(stateType, "Completed"), "fourth.mp4", null);
        Assert((bool)Get(queue, "IsRunning") && (int)Get(queue, "PendingCount") == 1, "Successful job did not continue the queue.");
        Call(queue, "BeginNext", level);
        Call(queue, "Complete", Enum.Parse(stateType, "Completed"), "fifth.mp4", null);
        Assert(!(bool)Get(queue, "IsRunning") && !(bool)Get(queue, "HasPending"), "Completed queue kept running.");
        Console.WriteLine("PASS: inline validation boundaries/culture/tab targets and queue snapshots, order, single dispatch, pause/resume, map changes, failure/cancellation.");
    }
}
