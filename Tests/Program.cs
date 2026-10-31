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
            if (args.Length == 1 && args[0] == "--process-stress")
            {
                Console.Error.Write(new string('e', 256 * 1024));
                Console.Write("done");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--process-delay")
            {
                Thread.Sleep(5000);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--tuf-replay-parser") { TUFReplayRenderer.Tests.ParserTests.Run(); Console.WriteLine("TUF replay parser tests passed."); return 0; }
            if (args.Length == 1 && args[0] == "--tuf-media-plan") { ReplayMediaPlanTests.Run(); return 0; }
            if (args.Length == 1 && args[0] == "--tuf-korean-key") { KoreanReplayKeyTests.Run(); return 0; }
            if (args.Length == 1 && args[0] == "--jrp-guard") { JrpGuardTests.Run(); return 0; }
            RenderResourceTests.Run();
            if (args.Length == 2 && args[0] == "--encoder-probe")
            {
                var available = EncoderAvailability.Get(args[1], VideoCodec.FFV1, VideoBitDepth.Eight, VideoContainer.Auto);
                var timeout = Stopwatch.StartNew();
                while (!available.Complete && timeout.Elapsed.TotalSeconds < 120) Thread.Sleep(50);
                Assert(available.Complete && available.Encoders.Contains(VideoEncoder.Software), "FFV1 software encoder was not found.");
                Assert(!available.Encoders.Contains(VideoEncoder.Vaapi), "FFV1 incorrectly advertised VAAPI.");
                Console.WriteLine("FFV1 encoder discovery passed.");
                return 0;
            }
            if (args.Length == 5 && args[0] == "--recovery")
            {
                RenderRecoveryTests.Run(args[1], args[2], args[3], args[4]);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--resources")
            {
                Console.WriteLine("Render resource tests passed.");
                return 0;
            }
            if (args.Length == 3 && args[0] == "--user-presets")
            {
                UserPresetTests.Run(args[1], args[2]);
                return 0;
            }
            UiLayoutTests.Run();
            if (args.Length == 1 && args[0] == "--ui-layout")
            {
                Console.WriteLine("UI layout tests passed.");
                return 0;
            }
            FileNameTemplateTests.Run();
            if (args.Length == 1 && args[0] == "--filename-template")
            {
                Console.WriteLine("Filename template tests passed.");
                return 0;
            }
            if (args.Length != 2) throw new ArgumentException("Pass ffmpeg.exe and a test output directory.");
            Directory.CreateDirectory(args[1]);
            TestOutputFormats(args[0], args[1]);
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
            Assert(VideoCodecCatalog.IsHardwareEncoder("hevc_vaapi"), "VAAPI failures did not use hardware fallback handling.");
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
            if (!string.Equals(Environment.GetEnvironmentVariable("ORBIT_RENDER_SKIP_TRANSPORT_TEST"), "1",
                StringComparison.Ordinal))
            {
                var rgbaTransport = Path.Combine(args[1], "transport-rgba.mp4");
                var rgbTransport = Path.Combine(args[1], "transport-rgb.mp4");
                EncodeTransportVariant(args[0], rgbaTransport, RawVideoPixelFormat.Rgba32);
                EncodeTransportVariant(args[0], rgbTransport, RawVideoPixelFormat.Rgb24);
                AssertTransportEquivalent(args[0], rgbaTransport, rgbTransport, args[1]);
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
            var rawTone = Path.Combine(args[1], "tone.f32le");
            var concurrentAudio = Path.Combine(args[1], "tone-concurrent.m4a");
            var concurrentMuxed = Path.Combine(args[1], "with-concurrent-audio.mp4");
            Probe(args[0], "-v error -i \"" + wav + "\" -f f32le \"" + rawTone + "\"");
            var rawSamples = File.ReadAllBytes(rawTone);
            using (var concurrent = new ConcurrentAudioEncoder(args[0], concurrentAudio, 48000, 2, 3.0))
            {
                for (var offset = 0; offset < rawSamples.Length; offset += 6400)
                {
                    var count = Math.Min(6400, rawSamples.Length - offset);
                    var block = new byte[count];
                    Buffer.BlockCopy(rawSamples, offset, block, 0, count);
                    concurrent.Write(block, count);
                }
                concurrent.Finish();
            }
            FFmpegEncoder.MuxPreencodedAudio(args[0], fast, concurrentAudio, concurrentMuxed);
            Assert(Probe(args[0], "-v error -i \"" + concurrentMuxed + "\" -map 0:v:0 -f framemd5 -") == fastHash,
                "Concurrent audio mux changed video frames or timestamps.");
            var concurrentMetadata = Probe(ResolveProbe(args[0]),
                "-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels,duration -of default=noprint_wrappers=1 \""
                + concurrentMuxed + "\"");
            Assert(concurrentMetadata.Contains("codec_name=aac") && concurrentMetadata.Contains("sample_rate=48000")
                && concurrentMetadata.Contains("channels=2") && concurrentMetadata.Contains("duration=1.000000"),
                "Concurrent audio format/duration mismatch.");
            Assert(Math.Abs(MeanVolume(args[0], concurrentMuxed) - boostedVolume) < 0.2,
                "Concurrent audio gain differs from final-mux gain.");
            var longWav = Path.Combine(args[1], "long-tone.wav");
            var offsetMuxed = Path.Combine(args[1], "with-offset-audio.mp4");
            Probe(args[0], "-v error -f lavfi -i sine=frequency=440:sample_rate=48000:duration=2 -ac 2 -c:a pcm_f32le \"" + longWav + "\"");
            FFmpegEncoder.MuxAudio(args[0], fast, longWav, offsetMuxed, 1.0);
            var offsetMetadata = Probe(ResolveProbe(args[0]),
                "-v error -select_streams a:0 -show_entries stream=duration -of default=noprint_wrappers=1 \""
                + offsetMuxed + "\"");
            Assert(offsetMetadata.Contains("duration=1.000000"), "Selection audio offset/duration mismatch.");
            TestVideoCodecs(args[0], args[1]);
            TestProRes(args[0], args[1]);
            TestFFV1(args[0], args[1]);
            TestOpusAudio(args[0], args[1]);
            TestFlacAudio(args[0], args[1]);
            Console.WriteLine("PASS: filename tokens/sanitization/collisions, MP4/TS/MKV/MOV video and AAC mux/copy, encoder availability and Vulkan FFV1, four-hour clock, DSP anchoring, pitch/offset, 1080p60/60 frames, repeated-frame grouping, frame order, identical fast/slow video, RGB24/RGBA transport equivalence, failure, cancellation, AAC/Opus/FLAC mux, concurrent AAC gain/mux, bit-exact lossless FFV1, selection audio offset and H.264/H.265/VP9/AV1/ProRes/FFV1 codec support.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void TestOpusAudio(string ffmpeg, string directory)
    {
        Assert(AudioCodecCatalog.Resolve(AudioCodec.Auto, ".mp4") == AudioCodec.AAC
            && AudioCodecCatalog.Resolve(AudioCodec.Auto, ".webm") == AudioCodec.Opus,
            "Automatic audio selection changed.");
        Assert(AudioCodecCatalog.CompatibleSelection(AudioCodec.Opus, ".mov") == AudioCodec.Auto
            && AudioCodecCatalog.CompatibleSelection(AudioCodec.AAC, ".webm") == AudioCodec.Auto,
            "Container changes retained an incompatible audio selection.");
        foreach (var extension in new[] { ".ts", ".mov" })
        {
            bool rejected = false;
            try { FFmpegEncoder.MuxAudio(ffmpeg, "unused", "unused", "unused" + extension,
                audioCodec: AudioCodec.Opus); }
            catch (NotSupportedException) { rejected = true; }
            Assert(rejected, "Unsupported Opus container was accepted: " + extension);
        }
        bool aacRejected = false;
        try { FFmpegEncoder.MuxAudio(ffmpeg, "unused", "unused", "unused.webm", audioCodec: AudioCodec.AAC); }
        catch (NotSupportedException) { aacRejected = true; }
        Assert(aacRejected, "AAC was allowed in WebM.");

        // Unity can capture 44.1 kHz PCM; both encoding paths must resample it
        // consistently and preserve gain and the one-second output timeline.
        var wav = Path.Combine(directory, "opus-44100.wav");
        var raw = Path.Combine(directory, "opus-44100.f32le");
        var encoded = Path.Combine(directory, "concurrent.opus");
        var webmVideo = Path.Combine(directory, "opus-video.webm");
        Probe(ffmpeg, "-v error -f lavfi -i testsrc2=size=160x90:rate=30:duration=1 -an -c:v libvpx-vp9 -deadline realtime -cpu-used 8 \"" + webmVideo + "\"");
        Probe(ffmpeg, "-v error -f lavfi -i sine=frequency=440:sample_rate=44100:duration=1 -ac 2 -c:a pcm_f32le \"" + wav + "\"");
        Probe(ffmpeg, "-v error -i \"" + wav + "\" -f f32le \"" + raw + "\"");
        var samples = File.ReadAllBytes(raw);
        using (var encoder = new ConcurrentAudioEncoder(ffmpeg, encoded, 44100, 2, 3.0, AudioCodec.Opus))
        {
            encoder.Write(samples, samples.Length);
            encoder.Finish();
        }
        foreach (var extension in new[] { ".mp4", ".mkv", ".webm" })
        {
            var video = extension == ".webm" ? webmVideo : Path.Combine(directory, "fast.mp4");
            var fallback = Path.Combine(directory, "opus-final" + extension);
            var concurrent = Path.Combine(directory, "opus-concurrent" + extension);
            FFmpegEncoder.MuxAudio(ffmpeg, video, wav, fallback, 0, 3.0, AudioCodec.Opus);
            FFmpegEncoder.MuxPreencodedAudio(ffmpeg, video, encoded, concurrent, AudioCodec.Opus);
            var videoHash = Probe(ffmpeg, "-v error -i \"" + video + "\" -map 0:v:0 -c copy -f hash -");
            foreach (var output in new[] { fallback, concurrent })
            {
                var metadata = Probe(ResolveProbe(ffmpeg),
                    "-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels -of default=noprint_wrappers=1 \"" + output + "\"");
                Assert(metadata.Contains("codec_name=opus") && metadata.Contains("sample_rate=48000")
                    && metadata.Contains("channels=2"), "Opus output format mismatch: " + output);
                Assert(Probe(ffmpeg, "-v error -i \"" + output + "\" -map 0:v:0 -c copy -f hash -") == videoHash,
                    "Opus mux changed compressed video: " + output);
                var decoded = output + ".f32le";
                Probe(ffmpeg, "-v error -i \"" + output + "\" -map 0:a:0 -f f32le \"" + decoded + "\"");
                var seconds = new FileInfo(decoded).Length / (48000.0 * 2 * sizeof(float));
                Assert(Math.Abs(seconds - 1.0) < 0.025, "Opus duration drift: " + seconds + " in " + output);
            }
            Assert(Math.Abs(MeanVolume(ffmpeg, concurrent) - MeanVolume(ffmpeg, fallback)) < 0.2,
                "Concurrent Opus gain differs from WAV fallback: " + extension);
            Assert(Math.Abs(MeanVolume(ffmpeg, fallback) - MeanVolume(ffmpeg, wav) - 3.0) < 0.3,
                "Opus gain was not applied: " + extension);
        }
        var offset = Path.Combine(directory, "opus-offset.mp4");
        FFmpegEncoder.MuxAudio(ffmpeg, Path.Combine(directory, "fast.mp4"), Path.Combine(directory, "long-tone.wav"),
            offset, 1.0, audioCodec: AudioCodec.Opus);
        var duration = Probe(ResolveProbe(ffmpeg), "-v error -select_streams a:0 -show_entries stream=duration -of default=noprint_wrappers=1 \"" + offset + "\"");
        Assert(duration.Contains("duration=1.000000"), "Opus selection offset/duration mismatch.");
        Console.WriteLine("PASS: Opus MP4/MKV/WebM final mux and concurrent copy, 44.1 kHz resampling, gain, duration, selection offset and incompatible containers.");
    }

    private static void TestOutputFormats(string ffmpeg, string directory)
    {
        var now = new DateTime(2026, 10, 5, 12, 34, 56);
        Assert(OutputFormat.FileName("{level}_{date}_{time}_{id}", "Test/Level", now, "abc123")
            == "Test_Level_2026-10-05_12-34-56_abc123", "Filename tokens or sanitization failed.");
        Assert(OutputFormat.FileName("../CON", "Level", now, "abc123") == "_CON", "Unsafe filename escaped sanitization.");
        Assert(OutputFormat.FileName("NUL", "Level", now, "abc123") == "_NUL", "Reserved filename was not escaped.");
        var name = OutputFormat.FileName("", "Level", now, "abc123");
        Assert(name == Path.Combine("Level", "2026-10-05_12-34-56_abc123"), "Default filename migration failed.");
        var original = OutputFormat.UniquePath(directory, name, ".mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(original));
        File.WriteAllText(original, "keep");
        Assert(OutputFormat.UniquePath(directory, name, ".mp4") != original && File.ReadAllText(original) == "keep",
            "Duplicate filename overwrote an existing render.");
        var audio = Path.Combine(directory, "container-tone.wav");
        Probe(ffmpeg, "-v error -f lavfi -i sine=duration=0.2 -c:a pcm_s16le \"" + audio + "\"");
        foreach (var container in new[] { VideoContainer.Mp4, VideoContainer.Ts, VideoContainer.Mkv, VideoContainer.Mov })
        {
            var extension = OutputFormat.Extension(container, VideoCodec.H264);
            var video = Path.Combine(directory, "container-video" + extension);
            Assert(FFmpegEncoder.TryValidateVideo(ffmpeg, 2, "ultrafast", "libx264", "yuv420p", extension, false, out var error),
                "Container preflight failed: " + container + " " + error);
            EncodeTransportVariant(ffmpeg, video, RawVideoPixelFormat.Rgba32);
            var mux = Path.Combine(directory, "container-audio" + extension);
            FFmpegEncoder.MuxAudio(ffmpeg, video, audio, mux);
            var metadata = Probe(ResolveProbe(ffmpeg), "-v error -show_entries stream=codec_name -of csv=p=0 \"" + mux + "\"");
            Assert(metadata.Contains("h264") && metadata.Contains("aac"), "Container lost video or audio: " + container);
            var m4a = Path.Combine(directory, "container-tone" + container + ".m4a");
            Probe(ffmpeg, "-v error -i \"" + audio + "\" -c:a aac \"" + m4a + "\"");
            FFmpegEncoder.MuxPreencodedAudio(ffmpeg, video, m4a, Path.Combine(directory, "container-copy" + extension));
        }
        Assert(!FFmpegEncoder.TryValidateVideo(ffmpeg, 2, "ultrafast", "libaom-av1", "yuv420p", ".ts", false, out _),
            "Unsupported TS/AV1 was advertised as playable.");
        var available = EncoderAvailability.Get(ffmpeg, VideoCodec.H264, VideoBitDepth.Eight, VideoContainer.Mkv);
        var timeout = Stopwatch.StartNew();
        while (!available.Complete && timeout.Elapsed.TotalSeconds < 120) Thread.Sleep(50);
        Assert(available.Complete && available.Encoders.Contains(VideoEncoder.Software), "Working software encoder was not discovered.");
        var missing = EncoderAvailability.Get(Path.Combine(directory, "missing-ffmpeg.exe"), VideoCodec.H264,
            VideoBitDepth.Eight, VideoContainer.Mp4);
        timeout.Restart();
        while (!missing.Complete && timeout.Elapsed.TotalSeconds < 5) Thread.Sleep(10);
        Assert(missing.Complete && missing.Encoders.Length == 0, "Missing FFmpeg advertised usable encoders.");
        var automatic = EncoderAvailability.GetCombined(ffmpeg, VideoBitDepth.Eight, VideoContainer.Auto);
        timeout.Restart();
        while (!automatic.Complete && timeout.Elapsed.TotalSeconds < 120) Thread.Sleep(50);
        Assert(automatic.Complete && automatic.Choices.Any(c => c.Codec == VideoCodec.AV1 && c.Encoder == VideoEncoder.Software),
            "Combined Auto dropdown lost the supported AOM AV1 encoder.");
        Assert(automatic.Choices.Any(c => c.Codec == VideoCodec.ProRes && c.Encoder == VideoEncoder.Software),
            "Combined Auto dropdown lost ProRes.");
        Assert(automatic.Choices.Where(c => c.Codec == VideoCodec.ProRes)
            .All(c => c.Encoder == VideoEncoder.Auto || c.Encoder == VideoEncoder.Software),
            "ProRes advertised an unsupported hardware backend.");
        Assert(automatic.Choices.Any(c => c.Codec == VideoCodec.FFV1 && c.Encoder == VideoEncoder.Software),
            "Combined Auto dropdown lost FFV1.");
        Assert(automatic.Choices.Where(c => c.Codec == VideoCodec.FFV1)
            .All(c => c.Encoder == VideoEncoder.Auto || c.Encoder == VideoEncoder.Software
                || c.Encoder == VideoEncoder.Vulkan),
            "FFV1 advertised an unsupported hardware backend.");
        Assert(automatic.Choices.Where(c => c.Codec != VideoCodec.FFV1)
            .All(c => c.Encoder != VideoEncoder.Vulkan),
            "Non-FFV1 codecs advertised the Vulkan backend.");
        Console.WriteLine("Available Auto-format encoders: " + string.Join(", ", automatic.Choices.Select(c => EncoderAvailability.Label(c.Encoder, c.Codec))));
        var vaapiChoices = automatic.Choices.Where(c => c.Encoder == VideoEncoder.Vaapi).ToArray();
        foreach (var choice in vaapiChoices)
        {
            var codec = VideoCodecCatalog.Get(choice.Codec).ResolveEncoder(choice.Encoder, false, false, false);
            var video = Path.Combine(directory, codec + ".mkv");
            EncodeTransportVariant(ffmpeg, video, RawVideoPixelFormat.Rgb24, codec);
            var metadata = Probe(ResolveProbe(ffmpeg), "-v error -show_entries stream=codec_name -of csv=p=0 \"" + video + "\"");
            var expectedCodec = choice.Codec == VideoCodec.H265 ? "hevc" : choice.Codec.ToString().ToLowerInvariant();
            Assert(metadata.Contains(expectedCodec), "VAAPI output used the wrong codec: " + codec);
            var decoded = Probe(ffmpeg, "-v error -i \"" + video + "\" -f framemd5 -")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Count(line => !line.StartsWith("#"));
            Assert(decoded == 6, "VAAPI lost frames during hardware upload: " + codec);
            FFmpegEncoder.MuxAudio(ffmpeg, video, audio, Path.Combine(directory, codec + "-audio.mkv"));
        }
        Console.WriteLine(vaapiChoices.Length == 0 ? "VAAPI hardware unavailable; hardware integration skipped."
            : "VAAPI hardware encoding, decode and audio mux passed.");
        var vulkanChoices = automatic.Choices.Where(c => c.Encoder == VideoEncoder.Vulkan).ToArray();
        foreach (var choice in vulkanChoices)
        {
            var codec = VideoCodecCatalog.Get(choice.Codec).ResolveEncoder(choice.Encoder, false, false, false);
            Assert(codec.EndsWith("_vulkan", StringComparison.Ordinal),
                "Vulkan availability resolved to a non-Vulkan encoder: " + codec);
            var video = Path.Combine(directory, codec + ".mkv");
            EncodeTransportVariant(ffmpeg, video, RawVideoPixelFormat.Rgb24, codec);
            var metadata = Probe(ResolveProbe(ffmpeg), "-v error -show_entries stream=codec_name -of csv=p=0 \"" + video + "\"");
            Assert(metadata.Contains("ffv1"), "Vulkan output used the wrong codec: " + codec);
            var decoded = Probe(ffmpeg, "-v error -i \"" + video + "\" -f framemd5 -")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Count(line => !line.StartsWith("#"));
            Assert(decoded == 6, "Vulkan FFV1 lost frames during hardware upload: " + codec);
            FFmpegEncoder.MuxAudio(ffmpeg, video, audio, Path.Combine(directory, codec + "-audio.mkv"));
        }
        Console.WriteLine(vulkanChoices.Length == 0 ? "Vulkan encoding unavailable; Vulkan FFV1 skipped."
            : "Vulkan lossless FFV1 encoding, decode and audio mux passed.");
        var combined = EncoderAvailability.GetCombined(ffmpeg, VideoBitDepth.Eight, VideoContainer.Ts);
        timeout.Restart();
        while (!combined.Complete && timeout.Elapsed.TotalSeconds < 120) Thread.Sleep(50);
        Assert(combined.Complete, "Combined encoder discovery did not finish.");
        var choices = combined.Choices;
        Assert(choices.Any(c => c.Codec == VideoCodec.H264 && c.Encoder == VideoEncoder.Software)
            && choices.Any(c => c.Codec == VideoCodec.H265 && c.Encoder == VideoEncoder.Software),
            "Combined dropdown lost H.264/H.265 software choices.");
        Assert(choices.All(c => c.Codec != VideoCodec.AV1 && c.Codec != VideoCodec.VP9 && c.Codec != VideoCodec.FFV1),
            "Combined dropdown exposed incompatible TS codecs.");
        var selected = choices[EncoderAvailability.SelectionIndex(choices, VideoCodec.H265, VideoEncoder.Software)];
        Assert(selected.Codec == VideoCodec.H265 && selected.Encoder == VideoEncoder.Software,
            "A shared backend selected the wrong codec in the combined dropdown.");
        selected = choices[EncoderAvailability.SelectionIndex(choices, VideoCodec.H265, VideoEncoder.Auto)];
        Assert(selected.Codec == VideoCodec.H265 && selected.Encoder == VideoEncoder.Auto,
            "Combined dropdown lost the saved automatic HEVC selection.");
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

    private static void AssertTransportEquivalent(string ffmpeg, string rgbaVideo, string rgbVideo, string directory)
    {
        var rgbaTiming = FrameMd5(ffmpeg, "-i \"" + rgbaVideo + "\" -f framemd5 -")
            .Where(line => !line.StartsWith("#"))
            .Select(line => string.Join(",", line.Split(',').Take(5))).ToArray();
        var rgbTiming = FrameMd5(ffmpeg, "-i \"" + rgbVideo + "\" -f framemd5 -")
            .Where(line => !line.StartsWith("#"))
            .Select(line => string.Join(",", line.Split(',').Take(5))).ToArray();
        Assert(rgbaTiming.Length == 6 && rgbaTiming.SequenceEqual(rgbTiming),
            "RGB24 raw transport changed frame count or timestamps.");

        var rgbaRaw = Path.Combine(directory, "transport-rgba.rgb");
        var rgbRaw = Path.Combine(directory, "transport-rgb.rgb");
        Probe(ffmpeg, "-v error -i \"" + rgbaVideo + "\" -pix_fmt rgb24 -f rawvideo \"" + rgbaRaw + "\"");
        Probe(ffmpeg, "-v error -i \"" + rgbVideo + "\" -pix_fmt rgb24 -f rawvideo \"" + rgbRaw + "\"");
        var rgba = File.ReadAllBytes(rgbaRaw);
        var rgb = File.ReadAllBytes(rgbRaw);
        Assert(rgba.Length == 320 * 180 * 3 * 6 && rgba.Length == rgb.Length,
            "RGB24 raw transport changed decoded dimensions or frame count.");
        long totalDifference = 0;
        var maxDifference = 0;
        for (var i = 0; i < rgba.Length; i++)
        {
            var difference = Math.Abs(rgba[i] - rgb[i]);
            totalDifference += difference;
            maxDifference = Math.Max(maxDifference, difference);
        }
        Assert(maxDifference <= 4 && totalDifference <= rgba.Length,
            "RGB24 raw transport changed decoded pixels: max difference " + maxDifference
            + ", mean difference " + ((double)totalDifference / rgba.Length).ToString(CultureInfo.InvariantCulture));
    }

    private static void EncodeTransportVariant(string ffmpeg, string output, RawVideoPixelFormat format,
        string codec = "libx264")
    {
        using (var encoder = new FFmpegEncoder(ffmpeg, output, 320, 180, 30, 4, "ultrafast", false,
            codec, "yuv420p", format, deferStart: format == RawVideoPixelFormat.Rgb24))
        {
            var bytesPerPixel = format == RawVideoPixelFormat.Rgb24 ? 3 : 4;
            for (var frameIndex = 0; frameIndex < 6; frameIndex++)
            {
                var frame = encoder.Rent();
                frame.Index = frameIndex;
                for (var pixel = 0; pixel < frame.Bytes.Length; pixel += bytesPerPixel)
                {
                    frame.Bytes[pixel] = (byte)(frameIndex * 19);
                    frame.Bytes[pixel + 1] = (byte)((pixel / (320 * bytesPerPixel)) * 31);
                    frame.Bytes[pixel + 2] = (byte)(255 - frameIndex * 19);
                    if (bytesPerPixel == 4) frame.Bytes[pixel + 3] = 255;
                }
                encoder.Submit(frame);
            }
            encoder.Finish(6);
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

    private static void TestProRes(string ffmpeg, string directory)
    {
        Assert(VideoCodecCatalog.TryParse("ProRes", out var parsed) && parsed == VideoCodec.ProRes,
            "RPC ProRes name was not recognized.");
        Assert(OutputFormat.Extension(VideoContainer.Auto, parsed) == ".mov", "ProRes Auto must use MOV.");
        Assert(VideoCodecCatalog.PixelFormat(parsed, VideoBitDepth.Eight) == "yuv422p10le",
            "ProRes must use 10-bit 4:2:2 regardless of saved bit depth.");
        foreach (var extension in new[] { ".mp4", ".ts" })
            Assert(!FFmpegEncoder.TryValidateVideo(ffmpeg, 2, "fast", "prores_ks", "yuv422p10le",
                extension, false, out _), "Unsupported ProRes container was accepted: " + extension);
        foreach (var extension in new[] { ".mov", ".mkv" })
        {
            var video = Path.Combine(directory, "prores" + extension);
            EncodeTransportVariant(ffmpeg, video, RawVideoPixelFormat.Rgb24, "prores_ks");
            var muxed = Path.Combine(directory, "prores-audio" + extension);
            FFmpegEncoder.MuxAudio(ffmpeg, video, Path.Combine(directory, "codec-tone.wav"), muxed);
            var metadata = Probe(ResolveProbe(ffmpeg),
                "-v error -count_frames -show_entries stream=codec_name,profile,pix_fmt,nb_read_frames -of default=noprint_wrappers=1 \"" + muxed + "\"");
            Assert(metadata.Contains("codec_name=prores") && metadata.Contains("profile=HQ")
                && metadata.Contains("pix_fmt=yuv422p10le") && metadata.Contains("nb_read_frames=6")
                && metadata.Contains("codec_name=aac"), "ProRes HQ video/audio metadata mismatch: " + metadata);
            var copied = Path.Combine(directory, "prores-copy" + extension);
            FFmpegEncoder.MuxPreencodedAudio(ffmpeg, video, muxed, copied);
            var copy = Probe(ResolveProbe(ffmpeg),
                "-v error -count_frames -select_streams v:0 -show_entries stream=codec_name,nb_read_frames -of default=noprint_wrappers=1 \"" + copied + "\"");
            Assert(copy.Contains("codec_name=prores") && copy.Contains("nb_read_frames=6"),
                "ProRes stream-copy mux lost frames.");
        }
        Console.WriteLine("PASS: ProRes 422 HQ 10-bit MOV/MKV, Auto selection, software backend, AAC mux/copy and frame counts.");
    }

    private static void TestFFV1(string ffmpeg, string directory)
    {
        Assert(VideoCodecCatalog.TryParse("FFV1", out var parsed) && parsed == VideoCodec.FFV1,
            "RPC FFV1 name was not recognized.");
        Assert(VideoCodecCatalog.TryParse("lossless", out parsed) && parsed == VideoCodec.FFV1,
            "FFV1 lossless alias was not recognized.");
        Assert(OutputFormat.Extension(VideoContainer.Auto, parsed) == ".mkv", "FFV1 Auto must use MKV.");
        Assert(VideoCodecCatalog.PixelFormat(parsed, VideoBitDepth.Eight) == "yuv420p"
            && VideoCodecCatalog.PixelFormat(parsed, VideoBitDepth.Ten) == "yuv420p10le",
            "FFV1 pixel format selection changed.");
        Assert(VideoCodecCatalog.Get(VideoCodec.FFV1)
                .ResolveEncoder(VideoEncoder.Vulkan, false, false, false) == "ffv1_vulkan",
            "FFV1 Vulkan backend did not resolve to ffv1_vulkan.");
        Assert(VideoCodecCatalog.Get(VideoCodec.H264)
                .ResolveEncoder(VideoEncoder.Vulkan, false, false, false) == "libx264",
            "H.264 must ignore the Vulkan backend.");
        Assert(VideoCodecCatalog.IsHardwareEncoder("ffv1_vulkan"),
            "ffv1_vulkan was not treated as a hardware encoder.");
        Assert(FFmpegEncoder.BuildEncoderOptions("ffv1_vulkan", "ultrafast", "-b:v 4M", true, "yuv420p")
            == "-c:v ffv1_vulkan", "Vulkan FFV1 encoder options were wrong.");
        Assert(FFmpegEncoder.BuildVideoFilter("ffv1_vulkan", "yuv420p") == "vflip,format=bgra,hwupload",
            "Vulkan FFV1 video filter was wrong.");
        foreach (var extension in new[] { ".mp4", ".ts", ".mov" })
            Assert(!FFmpegEncoder.TryValidateVideo(ffmpeg, 2, "fast", "ffv1", "yuv420p",
                extension, false, out _), "Unsupported FFV1 container was accepted: " + extension);

        // The decoded FFV1 stream must reproduce the converted source pixels
        // exactly. Feed the same raw frames through the transport pipeline and
        // through a direct reference conversion; lossless output means both
        // framemd5 checksums match.
        var source = Path.Combine(directory, "ffv1-source.rgb24");
        Probe(ffmpeg, "-v error -f lavfi -i testsrc2=size=320x180:rate=30:duration=1 -pix_fmt rgb24 -f rawvideo \"" + source + "\"");
        var samples = File.ReadAllBytes(source);
        var frames = samples.Length / (320 * 180 * 3);
        var video = Path.Combine(directory, "ffv1.mkv");
        using (var encoder = new FFmpegEncoder(ffmpeg, video, 320, 180, 30, 4, "ultrafast", false,
            "ffv1", "yuv420p", RawVideoPixelFormat.Rgb24))
        {
            for (var index = 0; index < frames; index++)
            {
                var frame = encoder.Rent();
                frame.Index = index;
                Buffer.BlockCopy(samples, index * frame.Bytes.Length, frame.Bytes, 0, frame.Bytes.Length);
                encoder.Submit(frame);
            }
            encoder.Finish(frames);
        }
        var metadata = Probe(ResolveProbe(ffmpeg),
            "-v error -count_frames -show_entries stream=codec_name,pix_fmt,nb_read_frames -of default=noprint_wrappers=1 \"" + video + "\"");
        Assert(metadata.Contains("codec_name=ffv1") && metadata.Contains("pix_fmt=yuv420p")
            && metadata.Contains("nb_read_frames=" + frames), "FFV1 video metadata mismatch: " + metadata);
        var decodedHash = FrameMd5(ffmpeg, "-an -i \"" + video + "\" -f framemd5 -");
        var referenceHash = FrameMd5(ffmpeg,
            "-an -f rawvideo -pixel_format rgb24 -video_size 320x180 -framerate 30 -i \"" + source
            + "\" -vf vflip -pix_fmt yuv420p -f framemd5 -");
        Assert(decodedHash == referenceHash, "FFV1 output was not bit-exact lossless.");
        Console.WriteLine("PASS: FFV1 lossless MKV, Auto selection, container guard and bit-exact decode.");

        // When the machine provides a working Vulkan FFV1 encoder, the same
        // source must decode bit-exactly through the GPU RGB path as well.
        if (FFmpegEncoder.TryValidateVideo(ffmpeg, 2, "ultrafast", "ffv1_vulkan", "yuv420p",
                OutputFormat.Extension(VideoContainer.Auto, VideoCodec.FFV1), false, out _))
        {
            var vulkanVideo = Path.Combine(directory, "ffv1-vulkan.mkv");
            using (var encoder = new FFmpegEncoder(ffmpeg, vulkanVideo, 320, 180, 30, 4, "ultrafast", false,
                "ffv1_vulkan", "yuv420p", RawVideoPixelFormat.Rgb24))
            {
                for (var index = 0; index < frames; index++)
                {
                    var frame = encoder.Rent();
                    frame.Index = index;
                    Buffer.BlockCopy(samples, index * frame.Bytes.Length, frame.Bytes, 0, frame.Bytes.Length);
                    encoder.Submit(frame);
                }
                encoder.Finish(frames);
            }
            var vulkanMetadata = Probe(ResolveProbe(ffmpeg),
                "-v error -count_frames -show_entries stream=codec_name,pix_fmt,nb_read_frames -of default=noprint_wrappers=1 \"" + vulkanVideo + "\"");
            Assert(vulkanMetadata.Contains("codec_name=ffv1") && vulkanMetadata.Contains("pix_fmt=bgra")
                && vulkanMetadata.Contains("nb_read_frames=" + frames), "Vulkan FFV1 video metadata mismatch: " + vulkanMetadata);
            var vulkanHash = FrameMd5(ffmpeg, "-an -i \"" + vulkanVideo + "\" -f framemd5 -");
            var vulkanReferenceHash = FrameMd5(ffmpeg,
                "-an -f rawvideo -pixel_format rgb24 -video_size 320x180 -framerate 30 -i \"" + source
                + "\" -vf vflip,format=bgra -f framemd5 -");
            Assert(vulkanHash == vulkanReferenceHash, "Vulkan FFV1 output was not bit-exact lossless.");
            Console.WriteLine("PASS: Vulkan FFV1 lossless MKV and bit-exact RGB decode.");
        }
        else Console.WriteLine("Note: ffv1_vulkan unavailable; Vulkan lossless path skipped.");
    }

    private static void TestFlacAudio(string ffmpeg, string directory)
    {
        Assert(AudioCodecCatalog.Resolve(AudioCodec.Flac, ".mkv") == AudioCodec.Flac,
            "FLAC was not accepted in MKV.");
        Assert(AudioCodecCatalog.CompatibleSelection(AudioCodec.Flac, ".mp4") == AudioCodec.Auto
            && AudioCodecCatalog.CompatibleSelection(AudioCodec.Flac, ".webm") == AudioCodec.Auto,
            "Container changes retained an incompatible FLAC selection.");
        bool flacRejected = false;
        try { FFmpegEncoder.MuxAudio(ffmpeg, "unused", "unused", "unused.mp4", audioCodec: AudioCodec.Flac); }
        catch (NotSupportedException) { flacRejected = true; }
        Assert(flacRejected, "FLAC was allowed in MP4.");

        var video = Path.Combine(directory, "flac-video.mkv");
        EncodeTransportVariant(ffmpeg, video, RawVideoPixelFormat.Rgb24, "ffv1");
        var wav = Path.Combine(directory, "flac-tone.wav");
        var raw = Path.Combine(directory, "flac.f32le");
        var encoded = Path.Combine(directory, "flac-concurrent.flac");
        Probe(ffmpeg, "-v error -f lavfi -i sine=frequency=440:sample_rate=44100:duration=1 -ac 2 -c:a pcm_f32le \"" + wav + "\"");
        Probe(ffmpeg, "-v error -i \"" + wav + "\" -f f32le \"" + raw + "\"");
        var samples = File.ReadAllBytes(raw);
        using (var encoder = new ConcurrentAudioEncoder(ffmpeg, encoded, 44100, 2, 3.0, AudioCodec.Flac))
        {
            encoder.Write(samples, samples.Length);
            encoder.Finish();
        }
        var boostedVolume = MeanVolume(ffmpeg, encoded);
        Assert(Math.Abs(boostedVolume - (MeanVolume(ffmpeg, wav) + 3.0)) < 0.2,
            "Concurrent FLAC gain differs from +3 dB.");
        var muxed = Path.Combine(directory, "flac-copied.mkv");
        FFmpegEncoder.MuxPreencodedAudio(ffmpeg, video, encoded, muxed);
        var metadata = Probe(ResolveProbe(ffmpeg),
            "-v error -select_streams a:0 -show_entries stream=codec_name,sample_rate,channels -of default=noprint_wrappers=1 \"" + muxed + "\"");
        Assert(metadata.Contains("codec_name=flac") && metadata.Contains("sample_rate=44100")
            && metadata.Contains("channels=2"), "Copied FLAC mux changed the audio stream: " + metadata);
        var finalMux = Path.Combine(directory, "flac-final.mkv");
        FFmpegEncoder.MuxAudio(ffmpeg, video, wav, finalMux, 0.0, 3.0, AudioCodec.Flac);
        Assert(Math.Abs(MeanVolume(ffmpeg, finalMux) - boostedVolume) < 0.2,
            "Final FLAC mux gain differs from the concurrent encoder.");
        Console.WriteLine("PASS: FLAC lossless audio in MKV, concurrent and final mux paths, container guard.");
    }

    private static string FrameMd5(string ffmpeg, string arguments)
    {
        var lines = Probe(ffmpeg, arguments)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", lines.Where(line => !line.StartsWith("#")));
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
            + "\" -map 0:a:0 -af volumedetect -f null -");
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
