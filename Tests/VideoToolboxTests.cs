using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OrbitRender;
using OrbitRender.Renderer;

// H.264 and H.265 through Apple VideoToolbox on macOS.
internal static class VideoToolboxTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void Run(string ffmpeg, string directory)
    {
        var h264 = VideoCodecCatalog.Get(VideoCodec.H264);
        var hevc = VideoCodecCatalog.Get(VideoCodec.H265);
        Assert(h264.ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false) == "h264_videotoolbox"
            && hevc.ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false) == "hevc_videotoolbox",
            "Choosing Apple VideoToolbox must encode H.264 and H.265 with VideoToolbox.");
        Assert(h264.ResolveEncoder(VideoEncoder.Auto, false, true, false, true) == "h264_videotoolbox"
            && hevc.ResolveEncoder(VideoEncoder.Auto, false, false, false, true) == "hevc_videotoolbox",
            "Auto on macOS, Intel Macs included, must use VideoToolbox.");
        Assert(h264.ResolveEncoder(VideoEncoder.Auto, false, false, false) == "libx264"
            && h264.ResolveEncoder(VideoEncoder.Auto, true, false, false) == "h264_nvenc",
            "Auto elsewhere must not change.");
        // VideoToolbox H.264 has no 10-bit profile; FFmpeg would quietly encode 8-bit.
        Assert(h264.ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false, true, VideoBitDepth.Ten) == "libx264"
            && h264.ResolveEncoder(VideoEncoder.Auto, false, false, false, true, VideoBitDepth.Ten) == "libx264",
            "10-bit H.264 must fall back to software.");
        Assert(hevc.ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false, true, VideoBitDepth.Ten) == "hevc_videotoolbox",
            "10-bit H.265 must stay on VideoToolbox.");
        Assert(FFmpegEncoder.EncoderPixelFormat("hevc_videotoolbox", "yuv420p10le") == "p010le"
            && FFmpegEncoder.EncoderPixelFormat("hevc_videotoolbox", "yuv420p") == "yuv420p"
            && FFmpegEncoder.EncoderPixelFormat("libx265", "yuv420p10le") == "yuv420p10le",
            "VideoToolbox HEVC takes 10-bit frames as p010le; other encoders keep their formats.");
        Assert(FFmpegEncoder.BuildEncoderOptions("h264_videotoolbox", "fast", "-b:v 18M", true, "yuv420p")
                == "-c:v h264_videotoolbox -allow_sw 0 -b:v 18M"
            && FFmpegEncoder.BuildEncoderOptions("hevc_videotoolbox", "fast", "-b:v 18M", true, "yuv420p10le")
                == "-c:v hevc_videotoolbox -allow_sw 0 -b:v 18M -profile:v main10",
            "VideoToolbox options must refuse the software fallback and set Main 10 for 10-bit HEVC.");
        Assert(VideoCodecCatalog.Get(VideoCodec.VP9).ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false) == "libvpx-vp9"
            && VideoCodecCatalog.Get(VideoCodec.AV1).ResolveEncoder(VideoEncoder.AppleVideoToolbox, false, false, false) == "libaom-av1",
            "Codecs without a VideoToolbox encoder must stay in software.");
        if (Environment.GetEnvironmentVariable("ORBIT_RENDER_TEST_VIDEOTOOLBOX") == "1")
        {
            EncodeAndProbe(ffmpeg, directory, "h264_videotoolbox", "yuv420p", "codec_name=h264", "pix_fmt=yuv420p");
            EncodeAndProbe(ffmpeg, directory, "hevc_videotoolbox", "yuv420p", "codec_name=hevc", "pix_fmt=yuv420p");
            EncodeAndProbe(ffmpeg, directory, "hevc_videotoolbox", "yuv420p10le", "profile=Main 10", "pix_fmt=yuv420p10le");
            // The export dialog and Auto pick among probed encoders.
            Assert(Probed(ffmpeg, VideoCodec.H264, VideoBitDepth.Eight).Contains(VideoEncoder.AppleVideoToolbox)
                && Probed(ffmpeg, VideoCodec.H265, VideoBitDepth.Ten).Contains(VideoEncoder.AppleVideoToolbox),
                "VideoToolbox must be listed for H.264 and 10-bit H.265.");
            Assert(!Probed(ffmpeg, VideoCodec.H264, VideoBitDepth.Ten).Contains(VideoEncoder.AppleVideoToolbox),
                "VideoToolbox must not be listed for 10-bit H.264.");
        }
        Console.WriteLine("PASS: H.264/H.265 VideoToolbox mapping, Auto on macOS, 10-bit rules and encoder options.");
    }

    private static VideoEncoder[] Probed(string ffmpeg, VideoCodec codec, VideoBitDepth depth)
    {
        var result = EncoderAvailability.Get(ffmpeg, codec, depth, VideoContainer.Auto);
        var timeout = Stopwatch.StartNew();
        while (!result.Complete && timeout.Elapsed.TotalSeconds < 120) System.Threading.Thread.Sleep(50);
        Assert(result.Complete, "The encoder probe for " + codec + " did not finish.");
        return result.Encoders;
    }

    private static void EncodeAndProbe(string ffmpeg, string directory, string codec, string pixelFormat,
        string expected, string expectedFormat)
    {
        var output = Path.Combine(directory, codec + "-" + pixelFormat + ".mp4");
        using (var encoder = new FFmpegEncoder(ffmpeg, output, 320, 180, 30, 2, "fast", false,
            codec, pixelFormat, RawVideoPixelFormat.Rgba32))
        {
            for (var index = 0; index < 4; index++)
            {
                var frame = encoder.Rent();
                frame.Index = index;
                for (var pixel = 0; pixel < frame.Bytes.Length; pixel += 4)
                {
                    frame.Bytes[pixel] = (byte)(index * 40);
                    frame.Bytes[pixel + 1] = 96;
                    frame.Bytes[pixel + 2] = 160;
                    frame.Bytes[pixel + 3] = 255;
                }
                encoder.Submit(frame);
            }
            encoder.Finish(4);
        }
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe" + Path.GetExtension(ffmpeg));
        var metadata = RunProcess(probe, "-v error -count_frames -select_streams v:0"
            + " -show_entries stream=codec_name,profile,pix_fmt,nb_read_frames -of default=noprint_wrappers=1 \"" + output + "\"");
        Assert(metadata.Contains(expected) && metadata.Contains(expectedFormat) && metadata.Contains("nb_read_frames=4"),
            "Wrong " + codec + " output from " + pixelFormat + ": " + metadata);
    }

    private static string RunProcess(string executable, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo(executable, arguments) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        }))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("VideoToolbox test process stalled."); }
            Assert(process.ExitCode == 0, error.GetAwaiter().GetResult());
            return output.GetAwaiter().GetResult();
        }
    }
}
