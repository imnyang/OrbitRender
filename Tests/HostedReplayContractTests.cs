using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OrbitRender.Renderer;
using HostedReplayFixtures;

namespace HostedReplayFixtures
{
    // Stand-in for UnityEngine.Canvas. The binder is generic over the canvas
    // type, so the contract can be checked without Unity.
    public sealed class Canvas { }

    // Every required member except the four the refusal cases vary.
    public abstract class HostCore
    {
        public readonly List<string> Calls = new List<string>();
        public readonly Canvas Overlay = new Canvas();
        public Func<bool> IsCancelled;
        public Action<double> SetEndTime, RequestStop;
        public void Begin(int width, int height, int videoFps, Func<bool> isCancelled, Action<double> setEndTimeSeconds,
            Action<double> requestStopAfterSeconds)
        {
            Calls.Add("begin " + width + "x" + height + "@" + videoFps);
            IsCancelled = isCancelled;
            SetEndTime = setEndTimeSeconds;
            RequestStop = requestStopAfterSeconds;
        }
        public void AfterSimulationFrame(long frameIndex, double videoSeconds)
            => Calls.Add("after " + frameIndex + " " + videoSeconds.ToString(CultureInfo.InvariantCulture));
        public bool TerminalReached { get; set; }
        public long? GameplayStartVideoTimeUs { get; set; }
        public double GameplayRate { get; set; } = 1;
        public IEnumerable<Canvas> GetCaptureCanvases() { yield return Overlay; }
        public void OnBeforeCapture() => Calls.Add("capture-before");
        public void OnAfterCapture() => Calls.Add("capture-after");
        public void End(string finalState) => Calls.Add("end " + finalState);
    }

    public class FakeHost : HostCore
    {
        public void PrepareBeforeRender() => Calls.Add("prepare");
        public void BeforeSimulationFrame(long frameIndex, double videoSeconds)
            => Calls.Add("before " + frameIndex + " " + videoSeconds.ToString(CultureInfo.InvariantCulture));
        public long CurrentVideoTimeUs { get; set; }
        public void RestoreAfterRender() => Calls.Add("restore");
    }

    public sealed class PerformanceHost : FakeHost
    {
        public long CompletedFrame;
        public double CompletedSeconds;
        public bool Ready;
        public bool TryCompleteFrame(long frameIndex, double videoSeconds)
        { CompletedFrame = frameIndex; CompletedSeconds = videoSeconds; return Ready; }
        public long CaptureHierarchyRevision { get; set; }
        public long CaptureLayoutRevision { get; set; }
    }

    public sealed class WrongOptionalHost : FakeHost
    {
        public bool TryCompleteFrame(int frameIndex, double videoSeconds) => true;
        public int CaptureHierarchyRevision => 1;
        public long CaptureLayoutRevision { private get; set; }
    }

    public sealed class MissingRestoreHost : HostCore
    {
        public void PrepareBeforeRender() { }
        public void BeforeSimulationFrame(long frameIndex, double videoSeconds) { }
        public long CurrentVideoTimeUs { get; set; }
    }

    public sealed class WrongClockHost : HostCore
    {
        public void PrepareBeforeRender() { }
        public void BeforeSimulationFrame(long frameIndex, double videoSeconds) { }
        public int CurrentVideoTimeUs { get; set; }
        public void RestoreAfterRender() { }
    }

    public sealed class WrongFrameHost : HostCore
    {
        public void PrepareBeforeRender() { }
        public void BeforeSimulationFrame(int frameIndex, double videoSeconds) { }
        public long CurrentVideoTimeUs { get; set; }
        public void RestoreAfterRender() { }
    }

    // A host built against an earlier draft that passed an overlay-work delegate.
    public sealed class DraftHost : HostCore
    {
        public void PrepareBeforeRender(Func<double, IDisposable> overlayWork) { }
        public void BeforeSimulationFrame(long frameIndex, double videoSeconds) { }
        public long CurrentVideoTimeUs { get; set; }
        public void RestoreAfterRender() { }
    }
}

internal static class HostedReplayContractTests
{
    internal static void Run()
    {
        CheckHostBinding();
        CheckHostRefusals();
        CheckOptionalCapabilities();
        CheckRequestValidation();
        Console.WriteLine("Hosted replay contract tests passed.");
    }

    private static void CheckOptionalCapabilities()
    {
        var old = HostedReplayHost<Canvas>.Bind(new FakeHost());
        Assert(old.TryCompleteFrame == null && old.CaptureHierarchyRevision == null
            && old.CaptureLayoutRevision == null, "existing 13-member hosts need no new members");
        var wrong = HostedReplayHost<Canvas>.Bind(new WrongOptionalHost());
        Assert(wrong.TryCompleteFrame == null && wrong.CaptureHierarchyRevision == null
            && wrong.CaptureLayoutRevision == null, "incompatible optional signatures are ignored");
        var host = new PerformanceHost { Ready = true, CaptureHierarchyRevision = 7, CaptureLayoutRevision = 9 };
        var bound = HostedReplayHost<Canvas>.Bind(host);
        Assert(ReferenceEquals(bound.TryCompleteFrame.Target, host), "completion is a bound delegate");
        Assert(bound.TryCompleteFrame(42, .7) && host.CompletedFrame == 42 && host.CompletedSeconds == .7,
            "completion receives the exact simulation frame and time");
        host.Ready = false;
        Assert(!bound.TryCompleteFrame(43, .8), "host can decline the fast path per frame");
        Assert(bound.CaptureHierarchyRevision() == 7 && bound.CaptureLayoutRevision() == 9,
            "capture revision getters bind by exact signature");
        host.CaptureHierarchyRevision = -1;
        Assert(bound.CaptureHierarchyRevision() == -1, "host can withdraw cache support");
    }

    private static void CheckHostBinding()
    {
        var host = new FakeHost();
        var bound = HostedReplayHost<Canvas>.Bind(host);
        Assert(ReferenceEquals(bound.Instance, host), "bound host keeps the instance");
        // Each delegate targets the host method directly, so per-frame calls never use MethodInfo.Invoke.
        foreach (var pair in new Dictionary<string, Delegate> {
            ["PrepareBeforeRender"] = bound.PrepareBeforeRender, ["Begin"] = bound.Begin,
            ["BeforeSimulationFrame"] = bound.BeforeSimulationFrame, ["AfterSimulationFrame"] = bound.AfterSimulationFrame,
            ["get_CurrentVideoTimeUs"] = bound.CurrentVideoTimeUs, ["get_TerminalReached"] = bound.TerminalReached,
            ["get_GameplayStartVideoTimeUs"] = bound.GameplayStartVideoTimeUs, ["get_GameplayRate"] = bound.GameplayRate,
            ["GetCaptureCanvases"] = bound.GetCaptureCanvases, ["OnBeforeCapture"] = bound.OnBeforeCapture,
            ["OnAfterCapture"] = bound.OnAfterCapture, ["End"] = bound.End, ["RestoreAfterRender"] = bound.RestoreAfterRender })
        {
            Assert(pair.Value != null, pair.Key + " is bound");
            Assert(ReferenceEquals(pair.Value.Target, host) && pair.Value.Method.Name == pair.Key
                && pair.Value.Method.DeclaringType.IsAssignableFrom(host.GetType()), pair.Key + " is a direct delegate to the host");
        }

        // Drive the delegates in the controller's order.
        double endTime = -1, stopTail = -1;
        bool cancelled = false;
        bound.PrepareBeforeRender();
        bound.Begin(1920, 1080, 60, () => cancelled, seconds => endTime = seconds, tail => stopTail = tail);
        bound.BeforeSimulationFrame(3, 0.05);
        bound.AfterSimulationFrame(3, 0.05);
        bound.OnBeforeCapture();
        bound.OnAfterCapture();
        bound.End("Completed");
        bound.RestoreAfterRender();
        var expected = "prepare,begin 1920x1080@60,before 3 0.05,after 3 0.05,capture-before,capture-after,end Completed,restore";
        Assert(string.Join(",", host.Calls) == expected, "host calls arrive in order with their arguments: " + string.Join(",", host.Calls));
        host.SetEndTime(12.5);
        host.RequestStop(2);
        cancelled = true;
        Assert(endTime == 12.5 && stopTail == 2 && host.IsCancelled(), "Begin hands the host working end-time, stop and cancellation callbacks");

        host.CurrentVideoTimeUs = 1_234_567;
        host.TerminalReached = true;
        host.GameplayStartVideoTimeUs = 250_000;
        host.GameplayRate = 1.5;
        Assert(bound.CurrentVideoTimeUs() == 1_234_567 && bound.TerminalReached() && bound.GameplayStartVideoTimeUs() == 250_000
            && bound.GameplayRate() == 1.5, "property getters read live host state");
        host.GameplayStartVideoTimeUs = null;
        Assert(bound.GameplayStartVideoTimeUs() == null, "a null gameplay start survives binding");
        Assert(bound.GetCaptureCanvases().Single() == host.Overlay, "capture canvases come from the host");
    }

    private static void CheckHostRefusals()
    {
        Refused(() => HostedReplayHost<Canvas>.Bind(null), "render_host_invalid", "host");
        Refused(() => HostedReplayHost<Canvas>.Bind(new MissingRestoreHost()), "render_host_invalid",
            "RestoreAfterRender", "void RestoreAfterRender()");
        Refused(() => HostedReplayHost<Canvas>.Bind(new WrongClockHost()), "render_host_invalid",
            "CurrentVideoTimeUs", "long CurrentVideoTimeUs { get; }");
        Refused(() => HostedReplayHost<Canvas>.Bind(new WrongFrameHost()), "render_host_invalid",
            "BeforeSimulationFrame", "void BeforeSimulationFrame(long frameIndex, double videoSeconds)");
        Refused(() => HostedReplayHost<Canvas>.Bind(new DraftHost()), "render_host_invalid",
            "PrepareBeforeRender", "void PrepareBeforeRender()");
        // Every missing member is named, the first one in the error field.
        var all = Refused(() => HostedReplayHost<Canvas>.Bind(new object()), "render_host_invalid",
            "PrepareBeforeRender", "void PrepareBeforeRender()");
        foreach (var member in new[] { "Begin(", "BeforeSimulationFrame(", "AfterSimulationFrame(", "CurrentVideoTimeUs",
            "TerminalReached", "GameplayStartVideoTimeUs", "GameplayRate", "GetCaptureCanvases(", "OnBeforeCapture(",
            "OnAfterCapture(", "End(", "RestoreAfterRender(" })
            Assert(all.Message.Contains(member), "the refusal names " + member);
    }

    private static void CheckRequestValidation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "orbitrender-hosted-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string level = Path.Combine(directory, "level.adofai");
            string webcam = Path.Combine(directory, "webcam.mkv");
            string microphone = Path.Combine(directory, "microphone.wav");
            string empty = Path.Combine(directory, "empty.wav");
            File.WriteAllText(level, "{\"angleData\":[0,180]}");
            File.WriteAllText(webcam, "video");
            File.WriteAllText(microphone, "audio");
            File.WriteAllText(empty, "");
            string hash = new string('a', 64);

            var request = HostedReplayRequest.Parse(Json(new {
                levelPath = level, levelFileSha256 = hash, startTile = 4, gameInputOffsetMs = 12.5, wonTimeUs = 123456,
                media = new { webcam = new { path = webcam, offsetMs = 125 }, microphone = new { path = microphone, latencyUs = 52000 } },
                warnings = new[] { "check overlays" }, options = new { videoFps = 60, outputDirectory = directory } }));
            Assert(request.LevelPath == Path.GetFullPath(level) && request.LevelFileSha256 == hash && request.StartTile == 4
                && request.GameInputOffsetMs == 12.5 && request.WonTimeUs == 123456, "request fields are read");
            Assert(request.Warnings.SequenceEqual(new[] { "check overlays" }) && (int)request.Options["videoFps"] == 60, "warnings and options are read");
            Assert(request.HasMedia && request.MediaPath("webcam") == Path.GetFullPath(webcam)
                && request.MediaPath("microphone") == Path.GetFullPath(microphone)
                && (int)request.MediaEntry("webcam")["offsetMs"] == 125, "absolute media paths and settings are kept");

            var minimal = HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, wonTimeUs = (long?)null,
                media = new { webcam = (object)null } }));
            Assert(minimal.StartTile == 0 && minimal.WonTimeUs == null && !minimal.HasMedia && minimal.MediaPath("webcam") == null
                && minimal.LevelFileSha256 == null && minimal.Options == null && minimal.Warnings.Length == 0, "optional members default");

            Refused(() => HostedReplayRequest.Parse(Json(new { gameInputOffsetMs = 0 })), "render_level_path_missing", "levelPath");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = " ", gameInputOffsetMs = 0 })), "render_level_path_missing", "levelPath");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = "level.adofai", gameInputOffsetMs = 0 })), "render_level_path_invalid", "levelPath");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = 5, gameInputOffsetMs = 0 })), "render_level_path_invalid", "levelPath");
            Refused(() => HostedReplayRequest.Parse(null), "render_request_invalid", "requestJson");
            Refused(() => HostedReplayRequest.Parse("{"), "render_request_invalid", "requestJson");
            Refused(() => HostedReplayRequest.Parse("[]"), "render_request_invalid", "requestJson");
            Refused(() => HostedReplayRequest.Parse("{} {}"), "render_request_invalid", "requestJson");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level })), "render_request_invalid", "gameInputOffsetMs");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = "12" })), "render_request_invalid", "gameInputOffsetMs");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, startTile = -1 })), "render_start_tile_invalid", "startTile");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, startTile = 1.5 })), "render_request_invalid", "startTile");
            Refused(() => HostedReplayRequest.Parse("{\"levelPath\":" + JsonConvert.ToString(level) + ",\"gameInputOffsetMs\":0,\"startTile\":99999999999999999999999}"),
                "render_request_invalid", "startTile");
            Refused(() => HostedReplayRequest.Parse("{\"levelPath\":" + JsonConvert.ToString(level) + ",\"gameInputOffsetMs\":0,\"wonTimeUs\":99999999999999999999999}"),
                "render_request_invalid", "wonTimeUs");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, wonTimeUs = -1 })), "render_request_invalid", "wonTimeUs");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, levelFileSha256 = "bad" })), "level_fingerprint_invalid", "levelFileSha256");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, warnings = "x" })), "render_request_invalid", "warnings");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, options = "x" })), "render_options_invalid", "options");
            Refused(() => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, media = "x" })), "render_request_invalid", "media");

            // Bad media paths: relative, missing, empty, a folder, or not an object.
            Refused(() => Media(level, new { webcam = new { path = "webcam.mkv" } }), "render_media_file_invalid", "media.webcam.path");
            Refused(() => Media(level, new { webcam = new { path = 3 } }), "render_media_file_invalid", "media.webcam.path");
            Refused(() => Media(level, new { webcam = new { offsetMs = 0 } }), "render_media_file_invalid", "media.webcam.path");
            Refused(() => Media(level, new { webcam = "webcam.mkv" }), "render_media_file_invalid", "media.webcam.path");
            Refused(() => Media(level, new { microphone = new { path = Path.Combine(directory, "missing.wav") } }), "render_media_file_missing", "media.microphone.path");
            Refused(() => Media(level, new { microphone = new { path = empty } }), "render_media_file_invalid", "media.microphone.path");
            Refused(() => Media(level, new { webcam = new { path = directory } }), "render_media_file_invalid", "media.webcam.path");

            // The controller checks the media again when the job starts.
            File.Delete(webcam);
            Refused(request.ValidateMedia, "render_media_file_missing", "media.webcam.path");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static HostedReplayRequest Media(string level, object media)
        => HostedReplayRequest.Parse(Json(new { levelPath = level, gameInputOffsetMs = 0, media }));

    private static string Json(object value) => JsonConvert.SerializeObject(value);

    private static RenderRequestException Refused(Action action, string code, string field, string mention = null)
    {
        try { action(); }
        catch (RenderRequestException exception)
        {
            Assert(exception.Code == code, "expected " + code + " but got " + exception.Code + ": " + exception.Message);
            Assert(exception.Field == field, "expected field " + field + " but got " + exception.Field + " for " + code);
            if (mention != null) Assert(exception.Message.Contains(mention), "the error names " + mention + ": " + exception.Message);
            return exception;
        }
        throw new Exception("Accepted a request or host that should be refused with " + code + " (" + field + ").");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Hosted replay contract: " + message);
    }
}
