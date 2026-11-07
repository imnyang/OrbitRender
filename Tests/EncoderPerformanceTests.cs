using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OrbitRender;
using OrbitRender.Renderer;
using Newtonsoft.Json.Linq;
using TUFReplayRenderer.Media;

internal static class EncoderPerformanceTests
{
    private static int assertions;
    private static void Assert(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    internal static void Run(string ffmpeg, string directory)
    {
        assertions = 0;
        Directory.CreateDirectory(directory);
        var hevc = VideoCodecCatalog.Get(VideoCodec.H265);
        Assert(hevc.ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false) == "hevc_videotoolbox",
            "Explicit HEVC Apple selection did not map to VideoToolbox.");
        Assert(hevc.ResolveEncoder(VideoEncoder.Auto, false, false, false, true) == "libx265",
            "Adding HEVC VideoToolbox changed Auto's macOS default.");
        Assert(hevc.ResolveEncoder(VideoEncoder.Software, false, false, false, true) == "libx265",
            "HEVC software fallback did not remain available.");
        Assert(VideoCodecCatalog.Get(VideoCodec.H264).ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false)
            == "libx264", "H.264 VideoToolbox was enabled without a demonstrated speed benefit.");
        Assert(VideoCodecCatalog.Get(VideoCodec.ProRes).ResolveEncoder(VideoEncoder.Auto, false, false, false, true)
            == "prores_videotoolbox", "ProRes Auto changed.");
        Assert(VideoCodecCatalog.PixelFormat(VideoCodec.H265, VideoBitDepth.Ten, encoder: "hevc_videotoolbox")
            == "p010le", "HEVC VideoToolbox must receive supported 10-bit input.");
        Assert(VideoCodecCatalog.PixelFormat(VideoCodec.H265, VideoBitDepth.Ten, encoder: "libx265")
            == "yuv420p10le", "Software fallback must use its own 10-bit input format.");
        Assert(VideoCodecCatalog.PixelFormat(VideoCodec.H265, VideoBitDepth.Eight, encoder: "hevc_videotoolbox")
            == "yuv420p", "HEVC VideoToolbox 8-bit input changed.");

        foreach (var preset in new[] { "fast", "veryfast", "ultrafast" })
        {
            var arguments = FFmpegEncoder.BuildCompositionVideoArguments("libx264", preset, 18, "yuv420p");
            Assert(Option(arguments, "-preset") == preset, "Webcam composition dropped the selected preset.");
            Assert(Option(arguments, "-b:v") == "18M" && Option(arguments, "-maxrate") == "18M"
                && Option(arguments, "-bufsize") == "36M", "Webcam rate-control options differ from the first encode.");
            Assert((Option(arguments, "-tune") == "zerolatency") == (preset == "ultrafast"),
                "Webcam x264 preset semantics differ from the first encode.");
        }
        Assert(Option(FFmpegEncoder.BuildCompositionVideoArguments("h264_nvenc", "veryfast", 18, "yuv420p"), "-preset")
            == "p4", "Webcam NVENC preset was not translated.");
        Assert(Option(FFmpegEncoder.BuildCompositionVideoArguments("hevc_qsv", "veryfast", 18, "yuv420p10le"), "-preset")
            == "faster", "Webcam QSV preset was not translated.");
        Assert(Option(FFmpegEncoder.BuildCompositionVideoArguments("hevc_amf", "ultrafast", 18, "yuv420p10le"), "-quality")
            == "speed", "Webcam AMF preset was not translated.");
        Assert(Option(FFmpegEncoder.BuildCompositionVideoArguments("libvpx-vp9", "veryfast", 18, "yuv420p"), "-cpu-used")
            == "6", "Webcam VP9 preset was not translated.");
        var aom = FFmpegEncoder.BuildCompositionVideoArguments("libaom-av1", "fast", 18, "yuv420p");
        Assert(Option(aom, "-cpu-used") == "4" && Option(aom, "-maxrate") == null,
            "Webcam AOM must use its supported rate-control options.");
        var ffv1 = FFmpegEncoder.BuildCompositionVideoArguments("ffv1", "fast", 18, "yuv420p");
        Assert(Option(ffv1, "-level") == "3" && Option(ffv1, "-b:v") == null && Option(ffv1, "-preset") == null,
            "Webcam FFV1 must retain lossless encoding without bitrate/preset flags.");
        var alpha = FFmpegEncoder.BuildCompositionVideoArguments("prores_videotoolbox", "fast", 18,
            "bgra", ProResProfile.FourFourFourFour);
        Assert(Option(alpha, "-pix_fmt") == "bgra" && Option(alpha, "-profile:v") == "4"
            && Option(alpha, "-allow_sw") == "0", "Webcam ProRes alpha profile changed.");

        var camera = JObject.Parse(@"{""durationUs"":10000000,""captureStartOffsetUs"":0,""gameplayRate"":1,""offsetMs"":125,""mirror"":true}");
        var microphone = JObject.Parse(@"{""captureStartOffsetUs"":0,""latencyUs"":52000,""volume"":2}");
        var timeline = new RenderTimeline(1000000, 1, null);
        foreach (var pixel in new[] { "yuv420p", "p010le" })
        {
            var encoding = FFmpegEncoder.BuildCompositionVideoArguments("hevc_videotoolbox", "fast", 18, pixel);
            Assert(Option(encoding, "-allow_sw") == "0" && Option(encoding, "-preset") == null,
                "VideoToolbox must refuse implicit software fallback and unsupported presets.");
            Assert((Option(encoding, "-profile:v") == "main10") == (pixel == "p010le"), "Wrong HEVC profile.");
            var plan = MediaCompositionPlan.Create("game.mp4", "out.mp4", 1920, 1080, 60,
                3000000, timeline, camera, "camera.mp4", microphone, "microphone.wav",
                videoEncoding: encoding, audioDelayUs: 40000);
            Assert(plan.FilterGraph.Contains(pixel == "p010le" ? "format=yuv420p10:" : "format=yuv420:"),
                "Webcam overlay silently reduced the requested bit depth.");
            Assert(plan.FilterGraph.Contains("+0.875/TB") && plan.FilterGraph.Contains("volume=2,adelay=988:all=1")
                && plan.FilterGraph.Contains("hflip") && plan.FilterGraph.Contains("eof_action=pass:repeatlast=0"),
                "Webcam timing, mirroring, microphone delay or EOF behavior changed.");
            Assert(Option(plan.Arguments.ToArray(), "-t") == "3", "Composition duration changed.");
        }
        var micOnly = MediaCompositionPlan.Create("game.mp4", "out.mp4", 1920, 1080, 60,
            3000000, timeline, null, null, microphone, "microphone.wav", videoEncoding: new[] { "-c:v", "copy" });
        Assert(Option(micOnly.Arguments.ToArray(), "-c:v") == "copy", "Microphone-only composition must copy video.");
        Assert(!FFmpegEncoder.TryValidateVideo(Path.Combine(directory, "ffmpeg-does-not-exist"), 2, "fast",
            "hevc_videotoolbox", "p010le", ".mp4", false, out var missingError) && !string.IsNullOrEmpty(missingError),
            "A missing FFmpeg must fail preflight cleanly.");
        var unavailable = EncoderAvailability.Get(Path.Combine(directory, "ffmpeg-does-not-exist"),
            VideoCodec.H265, VideoBitDepth.Ten, VideoContainer.Mp4);
        var wait = Stopwatch.StartNew();
        while (!unavailable.Complete && wait.Elapsed.TotalSeconds < 10) Thread.Sleep(1);
        Assert(unavailable.Complete && unavailable.Encoders.Length == 0,
            "Failed encoder probes must not advertise hardware or software support.");
        Assert(FFmpegEncoder.TryValidateVideo(ffmpeg, 2, "fast", hevc.SoftwareEncoder,
            "yuv420p10le", ".mp4", false, out var fallbackError), "HEVC software fallback probe failed: " + fallbackError);

        if (Environment.GetEnvironmentVariable("ORBIT_RENDER_TEST_HEVC_HARDWARE") == "1")
        {
            EncodeAndProbe(ffmpeg, directory, VideoBitDepth.Eight);
            EncodeAndProbe(ffmpeg, directory, VideoBitDepth.Ten);
        }
        Console.WriteLine("PASS: encoder performance options (" + assertions + " assertions); HEVC mapping/fallback, webcam graph, preset and bit-depth preservation.");
    }

    private static string Option(string[] arguments, string name)
    {
        int index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private static void EncodeAndProbe(string ffmpeg, string directory, VideoBitDepth depth)
    {
        var output = Path.Combine(directory, "hevc-videotoolbox-" + depth + ".mp4");
        using (var encoder = new FFmpegEncoder(ffmpeg, output, 320, 180, 30, 2, "fast", false,
            "hevc_videotoolbox", VideoCodecCatalog.PixelFormat(VideoCodec.H265, depth, encoder: "hevc_videotoolbox"),
            RawVideoPixelFormat.Rgb24))
        {
            for (int index = 0; index < 3; index++)
            {
                var frame = encoder.Rent();
                frame.Index = index;
                for (int pixel = 0; pixel < frame.Bytes.Length; pixel++) frame.Bytes[pixel] = (byte)(pixel % 255);
                encoder.Submit(frame);
            }
            encoder.Finish(3);
        }
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe" + Path.GetExtension(ffmpeg));
        using (var process = Process.Start(new ProcessStartInfo(probe, "-v error -count_frames -select_streams v:0"
            + " -show_entries stream=codec_name,profile,pix_fmt,nb_read_frames,color_space,color_range"
            + " -of default=noprint_wrappers=1 \"" + output + "\"") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        }))
        {
            var result = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("HEVC metadata probe timed out."); }
            var metadata = result.GetAwaiter().GetResult();
            Assert(process.ExitCode == 0, errors.GetAwaiter().GetResult());
            Assert(metadata.Contains("codec_name=hevc") && metadata.Contains("nb_read_frames=3")
                && metadata.Contains("color_space=bt709") && metadata.Contains("color_range=tv"), "Wrong HEVC output: " + metadata);
            Assert(metadata.Contains(depth == VideoBitDepth.Ten ? "pix_fmt=yuv420p10le" : "pix_fmt=yuv420p"),
                "HEVC requested bit depth changed: " + metadata);
        }
    }
}
