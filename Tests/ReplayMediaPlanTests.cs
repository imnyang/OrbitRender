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
        var media = new[] {
            new MediaOverlay("hand.mkv", JObject.FromObject(new { left = .1, top = .2, scale = 1.0 })),
            new MediaOverlay("foot.mkv", JObject.FromObject(new { left = .4, top = .5, scale = 1.0 }))
        };
        var plan = MediaCompositionPlan.Create("game.mp4", "out.mp4", 1920, 1080, 60,
            4000000, timeline, null, null, null, null, dmnoteOverlays: media);
        if (!plan.Arguments.Contains("hand.mkv") || !plan.Arguments.Contains("foot.mkv"))
            throw new Exception("Both ImplDmNote viewer streams must be inputs.");
        if (plan.FilterGraph == null || plan.FilterGraph.Split(new[] { "overlay=" }, StringSplitOptions.None).Length != 3)
            throw new Exception("Both ImplDmNote viewers must be composited.");
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
