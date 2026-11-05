using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OrbitRender;
using OrbitRender.Renderer;

internal static class ProResTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void Run(string ffmpeg, string directory)
    {
        Assert((int)VideoEncoder.Vulkan == 6 && (int)VideoEncoder.AppleVideoToolbox == 7,
            "Existing Vulkan backend value was changed.");
        var definition = VideoCodecCatalog.Get(VideoCodec.ProRes);
        Assert(definition.ResolveEncoder(VideoEncoder.Auto, false, false, false, true) == "prores_videotoolbox",
            "macOS ProRes Auto did not select VideoToolbox.");
        Assert(definition.ResolveEncoder(VideoEncoder.Software, false, false, false, true) == "prores_ks"
            && definition.ResolveEncoder(VideoEncoder.Auto, false, false, false) == "prores_ks",
            "ProRes software selection regressed.");
        Assert(VideoCodecCatalog.Get(VideoCodec.FFV1).ResolveEncoder(VideoEncoder.Vulkan, false, false, false) == "ffv1_vulkan",
            "Adding VideoToolbox changed FFV1 Vulkan mapping.");
        Assert(VideoCodecCatalog.IsHardwareEncoder("prores_videotoolbox"), "VideoToolbox is not a hardware backend.");
        foreach (var alias in new[] { "prores", "prores_ks", "prores422hq", "prores422" })
            Assert(VideoCodecCatalog.TryParse(alias, out var codec) && codec == VideoCodec.ProRes, "Lost ProRes alias: " + alias);
        Assert(ProResProfiles.Normalize((ProResProfile)99) == ProResProfile.HQ, "Invalid saved profile lost HQ default.");
        var profiles = (ProResProfile[])Enum.GetValues(typeof(ProResProfile));
        var previousEstimate = 0L;
        foreach (var profile in profiles)
        {
            Assert(ProResProfiles.TryParse(ProResProfiles.DisplayName(profile), out var parsed) && parsed == profile,
                "Profile display name cannot round-trip.");
            var estimate = RenderResources.VideoBytes(60, 1920, 1080, 30, 36, VideoCodec.ProRes, profile);
            Assert(estimate > previousEstimate, "Storage estimate did not account for profile quality.");
            previousEstimate = estimate;
            EncodeAndProbe(ffmpeg, directory, "prores_ks", profile);
        }
        Assert(RenderResources.VideoBytes(60, 1920, 1080, 30, 36, VideoCodec.ProRes)
            == RenderResources.VideoBytes(60, 1920, 1080, 30, 36, VideoCodec.ProRes, ProResProfile.HQ),
            "Existing HQ estimate changed.");
        foreach (var encoder in new[] { "prores_ks", "prores_videotoolbox" })
            foreach (var extension in new[] { ".mp4", ".ts", ".webm" })
                Assert(!FFmpegEncoder.TryValidateVideo(ffmpeg, 2, "fast", encoder, "yuv420p",
                    extension, false, out _, ProResProfile.HQ), "Unsupported ProRes container accepted.");
        if (Environment.GetEnvironmentVariable("ORBIT_RENDER_TEST_PRORES_HARDWARE") == "1")
            foreach (var profile in profiles) EncodeAndProbe(ffmpeg, directory, "prores_videotoolbox", profile);
        Console.WriteLine("PASS: ProRes profiles, software/Apple mapping, Vulkan compatibility, storage estimates, containers and alpha.");
    }

    private static void EncodeAndProbe(string ffmpeg, string directory, string codec, ProResProfile profile)
    {
        var output = Path.Combine(directory, codec + "-" + (int)profile + ".mov");
        using (var encoder = new FFmpegEncoder(ffmpeg, output, 320, 180, 30, 2, "fast", false,
            codec, "yuv420p", RawVideoPixelFormat.Rgba32, proResProfile: profile))
        {
            for (var index = 0; index < 2; index++)
            {
                var frame = encoder.Rent();
                frame.Index = index;
                for (var pixel = 0; pixel < frame.Bytes.Length; pixel += 4)
                {
                    frame.Bytes[pixel] = 32;
                    frame.Bytes[pixel + 1] = 96;
                    frame.Bytes[pixel + 2] = 160;
                    frame.Bytes[pixel + 3] = 128;
                }
                encoder.Submit(frame);
            }
            encoder.Finish(2);
        }
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe" + Path.GetExtension(ffmpeg));
        var metadata = RunProcess(probe, "-v error -count_frames -select_streams v:0"
            + " -show_entries stream=codec_name,profile,pix_fmt,nb_read_frames -of default=noprint_wrappers=1 \"" + output + "\"");
        var name = profile == ProResProfile.FourFourFourFourXQ ? "XQ" : ProResProfiles.DisplayName(profile);
        Assert(metadata.Contains("codec_name=prores") && metadata.Contains("profile=" + name)
            && metadata.Contains("nb_read_frames=2"), "Wrong ProRes output: " + metadata);
        Assert(metadata.Contains(ProResProfiles.HasAlpha(profile) ? "pix_fmt=yuva444p12le" : "pix_fmt=yuv422p10le"),
            "Wrong ProRes pixel format: " + metadata);
        if (ProResProfiles.HasAlpha(profile))
        {
            var alpha = output + ".alpha";
            RunProcess(ffmpeg, "-hide_banner -loglevel error -nostdin -n -i \"" + output
                + "\" -frames:v 1 -vf alphaextract -pix_fmt gray -f rawvideo \"" + alpha + "\"");
            var bytes = File.ReadAllBytes(alpha);
            Assert(bytes.Length == 320 * 180 && bytes.All(value => Math.Abs(value - 128) <= 1),
                "ProRes 4444 lost captured alpha: " + codec + " / " + name);
        }
    }

    private static string RunProcess(string executable, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo(executable, arguments) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        }))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("ProRes test process stalled."); }
            Assert(process.ExitCode == 0, error.GetAwaiter().GetResult());
            return output.GetAwaiter().GetResult();
        }
    }
}
