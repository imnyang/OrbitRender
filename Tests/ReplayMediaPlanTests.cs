using System;
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
        Console.WriteLine("Replay media composition planning tests passed.");
    }
}
