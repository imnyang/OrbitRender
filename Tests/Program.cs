using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using OrbitRender;
using OrbitRender.Renderer;

internal static class Program
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Pass ffmpeg.exe and a test output directory.");
            Directory.CreateDirectory(args[1]);
            var clock = new RenderClock();
            for (int i = 0; i < 60 * 60 * 4 * 60; i++) clock.Advance();
            Assert(clock.Time == 14400, "Four-hour clock drift.");
            Assert(Math.Abs(clock.SongPosition(1001, 1.5, 0.2, 0.03) - ((14399 - 0.03) * 1.5 - 0.2)) < 1e-9, "Pitch/offset mapping failed.");
            var anchored = new RenderClock();
            anchored.AnchorDsp(12345.5);
            for (int i = 0; i < 60; i++) anchored.Advance();
            Assert(anchored.DspTime == 12346.5 && anchored.Time == 1, "Audio DSP anchoring changed virtual frame time.");
            bool anchorRejected = false;
            try { anchored.AnchorDsp(0); } catch (InvalidOperationException) { anchorRejected = true; }
            Assert(anchorRejected, "DSP clock was allowed to reanchor during rendering.");
            Assert(VideoCodecCatalog.Get(VideoCodec.H264).ResolveEncoder(VideoEncoder.IntelQsv, false, false, true) == "h264_qsv", "Intel QSV mapping failed.");
            Assert(VideoCodecCatalog.Get(VideoCodec.H265).ResolveEncoder(VideoEncoder.AmdAmf, true, false, false) == "hevc_amf", "AMD AMF mapping failed.");
            Assert(VideoCodecCatalog.Get(VideoCodec.AV1).ResolveEncoder(VideoEncoder.NvidiaNvenc, false, true, false) == "av1_nvenc", "NVIDIA NVENC mapping failed.");
            Assert(VideoCodecCatalog.Get(VideoCodec.VP9).ResolveEncoder(VideoEncoder.IntelQsv, false, true, false) == "libvpx-vp9", "VP9 software fallback failed.");
            Assert(FFmpegEncoder.TryValidateVideo(args[0], 2, "ultrafast", "libx265", "yuv420p10le", ".mp4", false, out var preflightError),
                "10-bit encoder preflight failed: " + preflightError);
            var fast = Path.Combine(args[1], "fast.mp4");
            var slow = Path.Combine(args[1], "slow.mp4");
            const int targetFps = 60;
            Encode(args[0], fast, false, targetFps);
            Encode(args[0], slow, true, targetFps);
            var fastHash = Probe(args[0], "-v error -i \"" + fast + "\" -f framemd5 -");
            var slowHash = Probe(args[0], "-v error -i \"" + slow + "\" -f framemd5 -");
            Assert(fastHash == slowHash, "Different wall-clock delays changed decoded frames.");
            Assert(fastHash.Contains("#tb 0: 1/" + targetFps) && fastHash.Contains("#dimensions 0: 1920x1080"), "Wrong frame rate or resolution.");
            var decoded = fastHash.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith("#")).ToArray();
            Assert(decoded.Length == targetFps, "Decoded frame count mismatch.");
            Assert(decoded.Select(line => line.Split(',').Last().Trim()).Distinct().Count() == targetFps, "Duplicate decoded frames.");
            for (int i = 0; i < decoded.Length; i++)
            {
                var columns = decoded[i].Split(',');
                Assert(long.Parse(columns[2]) == i && int.Parse(columns[3]) == 1, "Frame timestamp or duration mismatch.");
            }
            var customFpsVideo = Path.Combine(args[1], "video-24.mp4");
            const int customVideoFps = 24;
            Encode(args[0], customFpsVideo, false, customVideoFps);
            var customMetadata = Probe(ResolveProbe(args[0]),
                "-v error -select_streams v:0 -show_entries stream=r_frame_rate,avg_frame_rate,nb_frames -of default=noprint_wrappers=1 \""
                + customFpsVideo + "\"");
            Assert(customMetadata.Contains("r_frame_rate=24/1")
                && customMetadata.Contains("avg_frame_rate=24/1")
                && customMetadata.Contains("nb_frames=24"), "Custom Video FPS was not preserved in the output stream.");
            var duplicateBaseline = Path.Combine(args[1], "duplicate-baseline.mp4");
            var duplicateGrouped = Path.Combine(args[1], "duplicate-grouped.mp4");
            EncodeDuplicateFrames(args[0], duplicateBaseline, false);
            EncodeDuplicateFrames(args[0], duplicateGrouped, true);
            Assert(Probe(args[0], "-v error -i \"" + duplicateBaseline + "\" -f framemd5 -")
                == Probe(args[0], "-v error -i \"" + duplicateGrouped + "\" -f framemd5 -"),
                "Grouped repeated frames changed decoded pixels or timestamps.");
            using (var encoder = new FFmpegEncoder(args[0], Path.Combine(args[1], "bad-order.mp4")))
            {
                var frame = encoder.Rent(); frame.Index = 1; encoder.Submit(frame);
                bool failed = false;
                try { encoder.Finish(1); } catch (IOException) { failed = true; }
                Assert(failed, "Out-of-order frames were silently accepted.");
            }
            using (var encoder = new FFmpegEncoder(args[0], Path.Combine(args[1], "cancelled.mp4")))
            {
                var frame = encoder.Rent(); frame.Index = 0; encoder.Submit(frame);
            }
            bool rejected = false;
            try { using (var encoder = new FFmpegEncoder(args[0], Path.Combine(args[1], "missing", "failure.mp4"))) {
                var frame = encoder.Rent(); frame.Index = 0; encoder.Submit(frame); encoder.Finish(1);
            }} catch (IOException) { rejected = true; }
            Assert(rejected, "FFmpeg nonzero exit was ignored.");
            var wav = Path.Combine(args[1], "tone.wav");
            var muxed = Path.Combine(args[1], "with-audio.mp4");
            Probe(args[0], "-v error -f lavfi -i sine=frequency=440:sample_rate=48000:duration=1 -ac 2 -c:a pcm_f32le \"" + wav + "\"");
            FFmpegEncoder.MuxAudio(args[0], fast, wav, muxed);
            var muxedVideoHash = Probe(args[0], "-v error -i \"" + muxed + "\" -map 0:v:0 -f framemd5 -");
            Assert(muxedVideoHash == fastHash, "Audio mux changed video frames or timestamps.");
            var metadata = Probe(ResolveProbe(args[0]),
                "-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,duration -of default=noprint_wrappers=1 \"" + muxed + "\"");
            Assert(metadata.Contains("codec_name=aac") && metadata.Contains("sample_rate=48000") && metadata.Contains("channels=2") && metadata.Contains("duration=1.000000"), "Muxed audio format/duration mismatch.");
            var boostedMuxed = Path.Combine(args[1], "with-boosted-audio.mp4");
            FFmpegEncoder.MuxAudio(args[0], fast, wav, boostedMuxed, 0.0, 3.0);
            var baseVolume = MeanVolume(args[0], muxed);
            var boostedVolume = MeanVolume(args[0], boostedMuxed);
            Assert(boostedVolume > baseVolume + 2.0 && boostedVolume < baseVolume + 4.0,
                "Audio gain did not apply approximately +3 dB: " + baseVolume.ToString(CultureInfo.InvariantCulture)
                + " -> " + boostedVolume.ToString(CultureInfo.InvariantCulture));
            var longWav = Path.Combine(args[1], "long-tone.wav");
            var offsetMuxed = Path.Combine(args[1], "with-offset-audio.mp4");
            Probe(args[0], "-v error -f lavfi -i sine=frequency=440:sample_rate=48000:duration=2 -ac 2 -c:a pcm_f32le \"" + longWav + "\"");
            FFmpegEncoder.MuxAudio(args[0], fast, longWav, offsetMuxed, 1.0);
            var offsetMetadata = Probe(ResolveProbe(args[0]),
                "-v error -select_streams a:0 -show_entries stream=duration -of default=noprint_wrappers=1 \""
                + offsetMuxed + "\"");
            Assert(offsetMetadata.Contains("duration=1.000000"), "Selection audio offset/duration mismatch.");
            TestVideoCodecs(args[0], args[1]);
            Console.WriteLine("PASS: four-hour clock, DSP anchoring, pitch/offset, 1080p60/60 frames, repeated-frame grouping, frame order, identical fast/slow video, failure, cancellation, AAC/Opus mux, +3 dB audio gain, selection audio offset and H.264/H.265/VP9/AV1 codec support.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Encode(string ffmpeg, string output, bool slow, int fps)
    {
        var encoder = fps == 60
            ? new FFmpegEncoder(ffmpeg, output)
            : new FFmpegEncoder(ffmpeg, output, 1920, 1080, fps, 18, "veryfast");
        using (encoder)
        {
            for (int i = 0; i < fps; i++)
            {
                var frame = encoder.Rent(); frame.Index = i;
                for (int j = 0; j < frame.Bytes.Length; j += 4) {
                    frame.Bytes[j] = (byte)(i * 4); frame.Bytes[j+1] = (byte)((j / (1920 * 4)) % 256);
                    frame.Bytes[j+2] = (byte)(255 - i * 4); frame.Bytes[j+3] = 255;
                }
                if (slow && i % 10 == 0) Thread.Sleep(100);
                encoder.Submit(frame);
            }
            encoder.Finish(fps);
        }
    }

    private static void EncodeDuplicateFrames(string ffmpeg, string output, bool grouped)
    {
        using (var encoder = new FFmpegEncoder(ffmpeg, output, 320, 180, 60, 4, "veryfast"))
        {
            for (var index = 0; index < 60; index += grouped ? 2 : 1)
            {
                var frame = encoder.Rent();
                frame.Index = index;
                frame.RepeatCount = grouped ? 2 : 1;
                var sample = index / 2;
                for (var pixel = 0; pixel < frame.Bytes.Length; pixel += 4)
                {
                    frame.Bytes[pixel] = (byte)(sample * 7);
                    frame.Bytes[pixel + 1] = (byte)((pixel / (320 * 4)) & 255);
                    frame.Bytes[pixel + 2] = (byte)(255 - sample * 7);
                    frame.Bytes[pixel + 3] = 255;
                }
                encoder.Submit(frame);
            }
            encoder.Finish(60);
            Assert(encoder.RepeatedFrames == (grouped ? 30 : 0), "Repeated-frame counter mismatch.");
        }
    }

    private static void TestVideoCodecs(string ffmpeg, string directory)
    {
        var codecs = new[] {
            new CodecCase("libx264", ".mp4", "h264"),
            new CodecCase("libx265", ".mp4", "hevc"),
            new CodecCase("libvpx-vp9", ".webm", "vp9"),
            new CodecCase("libaom-av1", ".mp4", "av1")
        };
        foreach (var codec in codecs)
        {
            var output = Path.Combine(directory, "codec-" + codec.Name.Replace("-", "") + codec.Extension);
            using (var encoder = new FFmpegEncoder(ffmpeg, output, 160, 90, 30, 2, "ultrafast", false, codec.Name))
            {
                for (int i = 0; i < 8; i++)
                {
                    var frame = encoder.Rent();
                    frame.Index = i;
                    for (int j = 0; j < frame.Bytes.Length; j += 4)
                    {
                        frame.Bytes[j] = (byte)(i * 24);
                        frame.Bytes[j + 1] = (byte)((j / (160 * 4)) * 40);
                        frame.Bytes[j + 2] = (byte)(255 - i * 24);
                        frame.Bytes[j + 3] = 255;
                    }
                    encoder.Submit(frame);
                }
                encoder.Finish(8);
            }

            var metadata = Probe(ResolveProbe(ffmpeg),
                "-v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1 \"" + output + "\"");
            Assert(metadata.Contains("codec_name=" + codec.ProbeName), "Wrong codec for " + codec.Name + ".");
            Assert(Path.GetExtension(output).Equals(codec.Extension, StringComparison.OrdinalIgnoreCase), "Wrong container for " + codec.Name + ".");
        }

        var vp9 = Path.Combine(directory, "codec-libvpxvp9.webm");
        var tone = Path.Combine(directory, "codec-tone.wav");
        var muxed = Path.Combine(directory, "codec-vp9-audio.webm");
        Probe(ffmpeg, "-v error -f lavfi -i sine=frequency=440:sample_rate=48000:duration=1 -ac 2 -c:a pcm_f32le \"" + tone + "\"");
        FFmpegEncoder.MuxAudio(ffmpeg, vp9, tone, muxed);
        var audio = Probe(ResolveProbe(ffmpeg),
            "-v error -select_streams a:0 -show_entries stream=codec_name -of default=noprint_wrappers=1 \"" + muxed + "\"");
        Assert(audio.Contains("codec_name=opus"), "VP9 audio was not muxed as Opus.");
    }

    private sealed class CodecCase
    {
        internal CodecCase(string name, string extension, string probeName)
        {
            Name = name;
            Extension = extension;
            ProbeName = probeName;
        }

        internal string Name { get; }
        internal string Extension { get; }
        internal string ProbeName { get; }
    }
    private static string Probe(string ffmpeg, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo(ffmpeg, arguments) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        })) {
            string result = process.StandardOutput.ReadToEnd(); process.WaitForExit();
            Assert(process.ExitCode == 0, "Video decode failed."); return result;
        }
    }
    private static double MeanVolume(string ffmpeg, string input)
    {
        var output = ProbeStderr(ffmpeg, "-v info -i \"" + input
            + "\" -map 0:a:0 -af volumedetect -f null NUL");
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = "mean_volume:";
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var value = line.Substring(index + marker.Length).Trim().TrimEnd(' ', 'd', 'B');
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        throw new Exception("FFmpeg did not report mean audio volume.");
    }
    private static string ProbeStderr(string ffmpeg, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo(ffmpeg, arguments) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        })) {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            stdout.GetAwaiter().GetResult();
            var result = stderr.GetAwaiter().GetResult();
            Assert(process.ExitCode == 0, "FFmpeg audio analysis failed: " + result);
            return result;
        }
    }
    private static string ResolveProbe(string ffmpeg)
    {
        var directory = Path.GetDirectoryName(ffmpeg);
        if (!string.IsNullOrEmpty(directory))
        {
            var sibling = Path.Combine(directory, "ffprobe.exe");
            if (File.Exists(sibling)) return sibling;
            sibling = Path.Combine(directory, "ffprobe");
            if (File.Exists(sibling)) return sibling;
        }
        return "ffprobe";
    }
}
