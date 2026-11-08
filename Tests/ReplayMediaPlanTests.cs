using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TUFReplayRenderer.Media;

internal static class ReplayMediaPlanTests
{
    internal static void Run()
    {
        var timeline = new RenderTimeline(1000000, 2, 4000000);
        CheckOverlayPlan(timeline);
        CheckOverlayList();
        if (Math.Abs(timeline.ReplayToOutput(4000000) - 3000000) > 1 ||
            Math.Abs(timeline.OutputToReplay(3500000) - 4500000) > 1)
            throw new Exception("Replay timing must preserve pitch and the post-clear tail.");
        var camera = JObject.Parse(@"{""durationUs"":10000000,""captureStartOffsetUs"":0,""gameplayRate"":1,""offsetMs"":125}");
        var mic = JObject.Parse(@"{""captureStartOffsetUs"":0,""latencyUs"":52000,""volume"":2}");
        var adjusted = MediaCompositionPlan.Create("game.mp4", "out.mp4", 1920, 1080, 60,
            3000000, new RenderTimeline(1000000, 1, null), camera, "camera.mp4", mic, "mic.wav");
        if (!adjusted.FilterGraph.Contains("+0.875/TB") || !adjusted.FilterGraph.Contains("volume=2,adelay=948:all=1"))
            throw new Exception("Recorded webcam offset and microphone latency/gain must be applied to the output timeline.");
        var delayed = MediaCompositionPlan.Create("game.mp4", "out.mp4", 1920, 1080, 60,
            3000000, new RenderTimeline(1000000, 1, null), camera, "camera.mp4", mic, "mic.wav", audioDelayUs: 40000);
        if (!delayed.FilterGraph.Contains("+0.875/TB") || !delayed.FilterGraph.Contains("volume=2,adelay=988:all=1"))
            throw new Exception("A delayed game audio track must move the microphone, not the webcam.");
        // Recorded microseconds follow conductor songposition, which already includes original pitch.
        var originalPitch = new RenderTimeline(1_000_000, 1.5, 6_000_000);
        if (originalPitch.ReplayToOutput(3_000_000) != 3_000_000) throw new Exception("Original pitch must be applied exactly once.");
        if (originalPitch.ReplayToOutput(6_500_000) != 5_500_000) throw new Exception("The clear tail must return to rate one.");
        if (originalPitch.OutputToReplay(5_500_000) != 6_500_000) throw new Exception("The clear tail inverse mapping is wrong.");
        var doubleSpeed = new RenderTimeline(1_000_000, 3, 6_000_000);
        if (doubleSpeed.ReplayToOutput(3_000_000) != 2_000_000) throw new Exception("Requested speed must multiply original pitch.");
        if (doubleSpeed.OutputToReplay(500_000) != -1_500_000) throw new Exception("The countdown must be before recording zero.");
        Console.WriteLine("Replay media composition planning tests passed.");
    }

    // Host overlay videos: one input each, cropped to the source, scaled only when the
    // target differs, held at their last frame, and under the webcam.
    private static void CheckOverlayPlan(RenderTimeline timeline)
    {
        // Two overlays show the same file, as two key viewers of a kind share one page.
        var overlays = new[] {
            new MediaOverlay("hand.mov", new MediaRect(30, 30, 585, 180), new MediaRect(40, 860, 585, 180)),
            new MediaOverlay("foot.mov", new MediaRect(0, 0, 400, 100), new MediaRect(-20, 900, 400, 100)),
            new MediaOverlay("hand.mov", new MediaRect(30, 30, 585, 180), new MediaRect(1200, 860, 390, 120))
        };
        var camera = JObject.Parse(@"{""durationUs"":10000000,""captureStartOffsetUs"":0,""gameplayRate"":1}");
        var plan = MediaCompositionPlan.Create("game.mp4", "out.mp4", 1920, 1080, 60,
            4000000, timeline, camera, "camera.mp4", null, null, overlays: overlays);
        Check(plan.Arguments.Count(argument => argument == "hand.mov") == 2 && plan.Arguments.Count(argument => argument == "foot.mov") == 1,
            "Each overlay must be its own input.");
        int foot = plan.Arguments.IndexOf("foot.mov"), webcam = plan.Arguments.IndexOf("camera.mp4");
        Check(plan.Arguments.IndexOf("hand.mov") < foot && foot < plan.Arguments.LastIndexOf("hand.mov")
            && plan.Arguments.LastIndexOf("hand.mov") < webcam, "Overlay videos must be inputs in order, before the webcam.");
        string graph = plan.FilterGraph;
        Check(graph.Contains("[1:v]crop=585:180:30:30[overlaypicture0]") && graph.Contains("[2:v]crop=400:100:0:0[overlaypicture1]"),
            "A same-size overlay must only be cropped: " + graph);
        Check(graph.Contains("[3:v]crop=585:180:30:30,premultiply=inplace=1,scale=390:120:flags=lanczos[overlaypicture2]")
            && graph.Contains("[overlay1][overlaypicture2]overlay=x=1200:y=860:format=yuv420:alpha=premultiplied:eof_action=repeat[overlay2]"),
            "A smaller target must be premultiplied, scaled after the crop and blended as premultiplied: " + graph);
        Check(graph.Contains("[0:v][overlaypicture0]overlay=x=40:y=860:format=yuv420:eof_action=repeat[overlay0]")
            && graph.Contains("[overlay0][overlaypicture1]overlay=x=-20:y=900:format=yuv420:eof_action=repeat[overlay1]"),
            "Overlays must be chained on the game picture in the host's order and keep their last frame: " + graph);
        Check(graph.IndexOf("[overlay2][camera0]", StringComparison.Ordinal) >= 0,
            "The webcam must be drawn over the overlay videos: " + graph);
        int copy = plan.Arguments.IndexOf("-c:v");
        Check(copy >= 0 && plan.Arguments[copy + 1] != "copy", "Overlay videos must be re-encoded with the picture.");
        var none = MediaCompositionPlan.Create("game.mp4", "out.mp4", 1920, 1080, 60, 4000000, timeline, null, null, null, null);
        Check(string.IsNullOrEmpty(none.FilterGraph) && !none.Arguments.Contains("-filter_complex"),
            "No media must mean no filter graph.");
    }

    // The host's JSON is read entry by entry; an entry without an existing file or with a
    // non-positive size is left out and named.
    private static void CheckOverlayList()
    {
        string directory = Path.Combine(Path.GetTempPath(), "orbitrender-overlay-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string hand = Path.Combine(directory, "hand.mov");
            File.WriteAllText(hand, "video");
            string Entry(object path, object source, object target) => Newtonsoft.Json.JsonConvert.SerializeObject(new { path, source, target });
            var good = new { x = 30, y = 30, width = 585, height = 180 };
            var json = "[" + string.Join(",",
                Entry(hand, good, new { x = -10, y = 860, width = 390, height = 120 }),
                Entry(Path.Combine(directory, "missing.mov"), good, good),
                Entry(hand, good, new { x = 0, y = 0, width = 0, height = 10 }),
                Entry(hand, null, good),
                Entry(hand, new { x = "a", y = 0, width = 10, height = 10 }, good),
                "5") + "]";
            var reasons = new System.Collections.Generic.List<string>();
            var overlays = MediaOverlay.ParseList(json, reasons.Add);
            Check(overlays.Count == 1 && overlays[0].Path == hand && overlays[0].Source.X == 30 && overlays[0].Source.Width == 585
                && overlays[0].Target.X == -10 && overlays[0].Target.Y == 860 && overlays[0].Target.Width == 390 && overlays[0].Target.Height == 120,
                "Only the valid overlay entry must be read.");
            Check(reasons.Count == 5 && reasons[0].StartsWith("Overlay video 1 left out"),
                "Each bad entry must be named: " + string.Join(" | ", reasons));
            foreach (var empty in new[] { null, "", "  ", "[]" })
                Check(MediaOverlay.ParseList(empty, reason => throw new Exception("Unexpected: " + reason)).Count == 0,
                    "An empty overlay list must be accepted quietly.");
            foreach (var bad in new[] { "{", "{}", "\"x\"" })
            {
                reasons.Clear();
                Check(MediaOverlay.ParseList(bad, reasons.Add).Count == 0 && reasons.Count == 1, "Unreadable overlay JSON must be skipped: " + bad);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Replay media plan: " + message);
    }

    // The webcam is cut into one clip per recorded timeline segment. Each clip
    // must stay on screen until the next one starts, including when the
    // camera's frames do not line up with the cut.
    internal static void RunWithFfmpeg(string ffmpeg, string directory)
    {
        string game = Path.Combine(directory, "media-game.mp4"), camera = Path.Combine(directory, "media-camera.mp4");
        string composed = Path.Combine(directory, "media-composed.mp4"), pixels = Path.Combine(directory, "media-camera.gray");
        RunFfmpeg(ffmpeg, "-v error -y -f lavfi -i color=black:s=320x180:r=60:d=3 -c:v libx264 -pix_fmt yuv420p \"" + game + "\"");
        RunFfmpeg(ffmpeg, "-v error -y -f lavfi -i color=white:s=160x120:r=30:d=5 -c:v libx264 -pix_fmt yuv420p \"" + camera + "\"");
        // Gameplay starts 1 s into the video and its first segment 5 ms later,
        // so the camera is cut there, between two 30 fps camera frames.
        var webcam = JObject.Parse(@"{""durationUs"":5000000,""captureStartOffsetUs"":-1310000,""gameplayRate"":1,
            ""timeline"":[{""timelineTimeUs"":5000,""videoTimeUs"":1315000,""gameplayRate"":1}]}");
        var plan = MediaCompositionPlan.Create(game, composed, 320, 180, 60, 3000000, new RenderTimeline(1000000, 1, null),
            webcam, camera, null, null, captureGameAudio: false);
        if (plan.FilterGraph.Split(new[] { "overlay=" }, StringSplitOptions.None).Length != 3)
            throw new Exception("The timeline segment must cut the webcam into two clips.");
        RunFfmpeg(ffmpeg, string.Join(" ", plan.Arguments.Select(argument => "\"" + argument + "\"")));
        // An 8x8 square inside the camera box, for every output frame.
        RunFfmpeg(ffmpeg, "-v error -y -i \"" + composed + "\" -vf crop=8:8:256:148 -pix_fmt gray -f rawvideo \"" + pixels + "\"");
        byte[] gray = File.ReadAllBytes(pixels);
        if (gray.Length != 180 * 64) throw new Exception("The composed webcam test video must have 180 frames.");
        for (int frame = 0; frame < 180; frame++)
            if (gray.Skip(frame * 64).Take(64).Average(value => value) < 128)
                throw new Exception("The webcam disappeared at output frame " + frame + ".");
        Console.WriteLine("Replay webcam composition tests passed.");
        RunOverlayWithFfmpeg(ffmpeg, directory, game);
    }

    // A 2 s alpha overlay (qtrle, opaque red left half, clear right half) over a 3 s black
    // video: once at its own size and once scaled down. The overlay keeps its last frame
    // after it ends, its clear half shows the game, and nothing leaks outside its target.
    private static void RunOverlayWithFfmpeg(string ffmpeg, string directory, string game)
    {
        string overlayPath = Path.Combine(directory, "media-overlay.mov");
        string composed = Path.Combine(directory, "media-overlay-composed.mp4");
        // Raw straight-alpha RGBA, as ElementNote feeds FFmpeg. The clear half is green with
        // alpha 0, so any leak of its color would show.
        string raw = Path.Combine(directory, "media-overlay.rgba");
        var frame = new byte[120 * 60 * 4];
        for (int pixel = 0; pixel < 120 * 60; pixel++)
        {
            bool opaque = pixel % 120 < 60;
            frame[pixel * 4] = (byte)(opaque ? 255 : 0);
            frame[pixel * 4 + 1] = (byte)(opaque ? 0 : 255);
            frame[pixel * 4 + 3] = (byte)(opaque ? 255 : 0);
        }
        using (var stream = File.Create(raw))
            for (int index = 0; index < 120; index++) stream.Write(frame, 0, frame.Length);
        RunFfmpeg(ffmpeg, "-v error -y -f rawvideo -pix_fmt rgba -s 120x60 -framerate 60 -i \"" + raw
            + "\" -c:v qtrle -pix_fmt argb \"" + overlayPath + "\"");
        var overlays = new[] {
            // The crop drops a 10 px border, as Element crops a page's padding.
            new MediaOverlay(overlayPath, new MediaRect(10, 10, 100, 40), new MediaRect(40, 60, 100, 40)),
            new MediaOverlay(overlayPath, new MediaRect(10, 10, 100, 40), new MediaRect(200, 120, 50, 20))
        };
        var plan = MediaCompositionPlan.Create(game, composed, 320, 180, 60, 3000000, new RenderTimeline(0, 1, null),
            null, null, null, null, captureGameAudio: false, overlays: overlays);
        RunFfmpeg(ffmpeg, string.Join(" ", plan.Arguments.Select(argument => "\"" + argument + "\"")));
        foreach (double seconds in new[] { 0.5, 2.5 })
        {
            string pixels = Path.Combine(directory, "media-overlay-" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".rgb");
            RunFfmpeg(ffmpeg, "-v error -y -i \"" + composed + "\" -ss " + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " -frames:v 1 -pix_fmt rgb24 -f rawvideo \"" + pixels + "\"");
            byte[] rgb = File.ReadAllBytes(pixels);
            if (rgb.Length != 320 * 180 * 3) throw new Exception("The composed overlay frame has the wrong size.");
            string at = " at " + seconds + " s";
            // Full-size overlay: red where the source is opaque, the game where it is clear.
            CheckColor(rgb, 60, 72, true, "The overlay's opaque half is missing" + at);
            CheckColor(rgb, 120, 72, false, "The overlay's clear half covers the game" + at);
            // Scaled overlay at half size.
            CheckColor(rgb, 208, 124, true, "The scaled overlay is missing" + at);
            CheckColor(rgb, 238, 124, false, "The scaled overlay's clear half covers the game" + at);
            // The clear pixels are green: scaling must not bleed it into the opaque edge.
            for (int x = 220; x < 232; x++)
                for (int y = 124; y < 136; y++)
                    if (rgb[(y * 320 + x) * 3 + 1] > 40)
                        throw new Exception("The scaled overlay's edge shows the clear pixels' color" + at + " at " + x + "," + y + ".");
            // Outside both targets.
            CheckColor(rgb, 280, 20, false, "The overlay leaked outside its target" + at);
            CheckColor(rgb, 20, 140, false, "The overlay leaked outside its target" + at);
        }
        Console.WriteLine("Replay overlay video composition tests passed.");
    }

    // The mean color of an 8x8 block at (x, y): red, or the black game picture.
    private static void CheckColor(byte[] rgb, int x, int y, bool red, string message)
    {
        double r = 0, g = 0, b = 0;
        for (int row = y; row < y + 8; row++)
            for (int column = x; column < x + 8; column++)
            {
                int pixel = (row * 320 + column) * 3;
                r += rgb[pixel]; g += rgb[pixel + 1]; b += rgb[pixel + 2];
            }
        r /= 64; g /= 64; b /= 64;
        bool isRed = r > 180 && g < 70 && b < 70, isBlack = r < 40 && g < 40 && b < 40;
        if (red ? !isRed : !isBlack)
            throw new Exception(message + " (mean rgb " + (int)r + "," + (int)g + "," + (int)b + ").");
    }

    private static void RunFfmpeg(string ffmpeg, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo(ffmpeg, arguments) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            output.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new Exception("FFmpeg failed: " + errors.GetAwaiter().GetResult());
        }
    }
}
