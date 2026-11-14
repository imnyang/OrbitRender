using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace OrbitRender.Renderer
{
    internal enum RawVideoPixelFormat
    {
        Rgba32,
        Rgb24
    }

    internal sealed class FFmpegEncoder : IDisposable
    {
        private const double MinAudioGainDb = -60.0;
        private const double MaxAudioGainDb = 12.0;
        private const int HardwareFilterThreads = 2;
        // Always deliver HD video with the BT.709 matrix and limited range, and
        // tag both the frames and the container. Without this, swscale converts
        // the captured sRGB frames with BT.601 coefficients while players
        // assume BT.709 for HD, so the same file looks redder/oranger on
        // different machines. Tagging is what makes the render deterministic.
        private const string Bt709VideoFilter =
            "scale=out_color_matrix=bt709:out_range=tv,setparams=range=tv:colorspace=bt709:color_primaries=bt709:color_trc=bt709";
        internal sealed class Frame
        {
            public readonly byte[] Bytes;
            public long Index;
            public int RepeatCount = 1;

            public Frame(int byteCount) { Bytes = new byte[byteCount]; }
        }
        private readonly BlockingCollection<Frame> free;
        private readonly BlockingCollection<Frame> work;
        private readonly StringBuilder stderr = new StringBuilder();
        private readonly Process process;
        private Thread writer;
        private bool started;
        private readonly RawVideoPixelFormat rawPixelFormat;
        private readonly string executable, output;
        private readonly int fps;
        internal NativeNvencEncoder NativeBackend { get; private set; }
        internal string NativeBitstreamPath => output + ".nvenc.h264";
        private volatile Exception failure;
        private bool disposed;
        public long WrittenFrames => NativeBackend != null ? NativeBackend.WrittenFrames : Interlocked.Read(ref written);
        public double WriteSeconds => NativeBackend != null ? NativeBackend.WriteSeconds : Interlocked.Read(ref writeTicks) / (double)Stopwatch.Frequency;
        public int QueueDepth => NativeBackend != null ? NativeBackend.QueueDepth : work.Count;
        public int PeakQueueDepth => NativeBackend != null ? NativeBackend.PeakQueueDepth : Volatile.Read(ref peakQueueDepth);
        public int BufferCapacity => work.BoundedCapacity;
        public long RepeatedFrames => NativeBackend != null ? NativeBackend.RepeatedFrames : Interlocked.Read(ref repeatedFrames);
        internal RawVideoPixelFormat RawPixelFormat => rawPixelFormat;
        internal int BytesPerPixel => rawPixelFormat == RawVideoPixelFormat.Rgb24 ? 3 : 4;
        internal int FrameByteCount { get; }
        private long written;
        private long repeatedFrames;
        private long writeTicks;
        private int peakQueueDepth;

        // Kept for the standalone encoder tests and for callers that use the
        // original API. RendererController uses the configurable overload.
        public FFmpegEncoder(string executable, string output)
            : this(executable, output, 1920, 1080, 60, 18000, "veryfast", true, true, "libx264", "yuv420p",
                RawVideoPixelFormat.Rgba32) { }

        public FFmpegEncoder(string executable, string output, int width, int height, int fps, int bitrateMbps, string preset)
            : this(executable, output, width, height, fps, bitrateMbps * 1000, preset, false, true, "libx264", "yuv420p",
                RawVideoPixelFormat.Rgba32) { }

        public FFmpegEncoder(string executable, string output, int width, int height, int fps, int bitrateMbps,
            string preset, bool fastStart)
            : this(executable, output, width, height, fps, bitrateMbps * 1000, preset, false, fastStart, "libx264", "yuv420p",
                RawVideoPixelFormat.Rgba32) { }

        public FFmpegEncoder(string executable, string output, int width, int height, int fps, int bitrateMbps,
            string preset, bool fastStart, string codec)
            : this(executable, output, width, height, fps, bitrateMbps * 1000, preset, false, fastStart, codec, "yuv420p",
                RawVideoPixelFormat.Rgba32) { }

        public FFmpegEncoder(string executable, string output, int width, int height, int fps, int bitrateMbps,
            string preset, bool fastStart, string codec, string pixelFormat)
            : this(executable, output, width, height, fps, bitrateMbps * 1000, preset, false, fastStart, codec, pixelFormat,
                RawVideoPixelFormat.Rgba32) { }

        internal FFmpegEncoder(string executable, string output, int width, int height, int fps, int bitrateMbps,
            string preset, bool fastStart, string codec, string pixelFormat, RawVideoPixelFormat rawPixelFormat,
            bool deferStart = false, FrameBufferPolicy bufferPolicy = FrameBufferPolicy.Balanced,
            ProResProfile proResProfile = ProResProfile.HQ, int? bitrateKbps = null)
            : this(executable, output, width, height, fps, bitrateKbps ?? bitrateMbps * 1000, preset, false, fastStart,
                codec, pixelFormat, rawPixelFormat, deferStart, bufferPolicy, proResProfile) { }

        private FFmpegEncoder(string executable, string output, int width, int height, int fps, int bitrateKbps,
            string preset, bool legacyCrf, bool fastStart, string codec, string pixelFormat,
            RawVideoPixelFormat rawPixelFormat, bool deferStart = false, FrameBufferPolicy bufferPolicy = FrameBufferPolicy.Balanced,
            ProResProfile proResProfile = ProResProfile.HQ)
        {
            if (string.IsNullOrWhiteSpace(executable)) throw new FileNotFoundException("FFmpeg executable was not configured.");
            if (LooksLikeFilePath(executable) && !File.Exists(executable))
                throw new FileNotFoundException("FFmpeg executable not found", executable);
            if (width <= 0 || height <= 0 || (width & 1) != 0 || (height & 1) != 0 || fps <= 0 || bitrateKbps <= 0)
                throw new ArgumentOutOfRangeException();
            if (string.IsNullOrEmpty(preset)) preset = "fast";
            if (string.IsNullOrEmpty(codec)) codec = "libx264";
            var isProRes = string.Equals(codec, "prores_ks", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "prores_videotoolbox", StringComparison.OrdinalIgnoreCase);
            proResProfile = ProResProfiles.Normalize(proResProfile);
            if (isProRes
                && !string.Equals(Path.GetExtension(output), ".mov", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetExtension(output), ".mkv", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("ProRes output requires MOV or MKV.");
            if (codec.StartsWith("ffv1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetExtension(output), ".mkv", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("FFV1 output requires MKV.");
            // FFmpeg can put VP9/AV1 into MPEG-TS as opaque private data and
            // report success, although normal players cannot identify video.
            if (string.Equals(Path.GetExtension(output), ".ts", StringComparison.OrdinalIgnoreCase)
                && (codec.IndexOf("vp9", StringComparison.OrdinalIgnoreCase) >= 0
                    || codec.IndexOf("av1", StringComparison.OrdinalIgnoreCase) >= 0))
                throw new NotSupportedException("MPEG-TS output requires H.264 or H.265 video.");
            if (!string.Equals(pixelFormat, "yuv420p10le", StringComparison.OrdinalIgnoreCase)) pixelFormat = "yuv420p";
            if (!Enum.IsDefined(typeof(RawVideoPixelFormat), rawPixelFormat))
                rawPixelFormat = RawVideoPixelFormat.Rgba32;
            this.rawPixelFormat = rawPixelFormat;
            this.executable = executable;
            this.output = output;
            this.fps = fps;
            bool isHardwareEncoder = VideoCodecCatalog.IsHardwareEncoder(codec);
            if (isProRes) pixelFormat = VideoCodecCatalog.PixelFormat(VideoCodec.ProRes,
                VideoBitDepth.Ten, proResProfile, codec);
            bool isVaapi = codec.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase);
            bool isVulkan = codec.EndsWith("_vulkan", StringComparison.OrdinalIgnoreCase);
            bool fullRangeOutput = isVulkan || IsRgbPixelFormat(pixelFormat);
            var frameByteCount = checked(width * height * BytesPerPixel);
            FrameByteCount = frameByteCount;
            // Readback and encoding share this pool. Respect the selected CPU
            // memory budget; one frame is the minimum even above the budget.
            var bufferCount = RenderResources.BufferCount(frameByteCount, bufferPolicy);
            free = new BlockingCollection<Frame>(bufferCount);
            work = new BlockingCollection<Frame>(bufferCount);
            var rateControl = RateControl(codec, bitrateKbps, legacyCrf);
            var encoderOptions = BuildEncoderOptions(codec, preset, rateControl, isHardwareEncoder, pixelFormat, proResProfile);
            process = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable,
                // RGB conversion needs a small filter pool alongside Unity.
                // Encoder presets/rate control and software encoder threads
                // remain independent of these preprocessing threads.
                Arguments = "-hide_banner -loglevel warning -nostdin -n"
                    + (isVaapi ? " -init_hw_device vaapi=orbit_vaapi -filter_hw_device orbit_vaapi" : "")
                    + (isVulkan ? " -init_hw_device vulkan=orbit_vulkan -filter_hw_device orbit_vulkan" : "")
                    + (isHardwareEncoder ? " -filter_threads " + HardwareFilterThreads : "")
                    + " -f rawvideo -pixel_format "
                    + (rawPixelFormat == RawVideoPixelFormat.Rgb24 ? "rgb24" : "rgba") + " -video_size "
                    + width + "x" + height + " -framerate " + fps + " -i pipe:0 -an -vf "
                    + BuildVideoFilter(codec, pixelFormat) + " "
                    + encoderOptions + " -pix_fmt " + (isVaapi ? "vaapi" : isVulkan ? "vulkan" : EncoderPixelFormat(codec, pixelFormat))
                    + ColorMetadataOptions(fullRangeOutput)
                    // The raw-video demuxer supplies the same target time base
                    // used by the render clock, so the encoded stream inherits
                    // the requested constant frame rate without resampling.
                    + " -fps_mode cfr"
                    + (fastStart ? ContainerOptions(output) : "") + " \"" + output + "\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true
            }};
            process.ErrorDataReceived += (sender, args) => {
                if (args.Data == null) return;
                lock (stderr) {
                    stderr.AppendLine(args.Data);
                    if (stderr.Length > 16384) stderr.Remove(0, stderr.Length - 16384);
                }
            };
            if (!deferStart) StartFallback();
        }

        private void StartFallback()
        {
            if (started) return;
            if (disposed) throw new ObjectDisposedException(nameof(FFmpegEncoder));
            try
            {
                process.Start();
                started = true;
                try { process.PriorityClass = ProcessPriorityClass.High; }
                catch { /* Restricted environments may not allow priority changes. */ }
                process.BeginErrorReadLine();
                for (int i = 0; i < free.BoundedCapacity; i++) free.Add(new Frame(FrameByteCount));
                writer = new Thread(WriteFrames) { IsBackground = true, Name = "OrbitRender FFmpeg" };
                writer.Start();
            }
            catch { AbortProcess(); process.Dispose(); throw; }
        }

        private void WriteFrames()
        {
            try
            {
                var stream = process.StandardInput.BaseStream;
                foreach (var frame in work.GetConsumingEnumerable())
                {
                    if (frame.Index != WrittenFrames) throw new InvalidDataException("Frame ordering violation.");
                    if (frame.RepeatCount < 1) throw new InvalidDataException("Invalid frame repeat count.");
                    for (var i = 0; i < frame.RepeatCount; i++)
                    {
                        var writeStart = Stopwatch.GetTimestamp();
                        stream.Write(frame.Bytes, 0, frame.Bytes.Length);
                        Interlocked.Add(ref writeTicks, Stopwatch.GetTimestamp() - writeStart);
                        Interlocked.Increment(ref written);
                    }
                    if (frame.RepeatCount > 1) Interlocked.Add(ref repeatedFrames, frame.RepeatCount - 1);
                    frame.RepeatCount = 1;
                    free.Add(frame);
                }
                stream.Flush();
                stream.Close();
            }
            catch (Exception ex) { failure = ex; }
        }

        public void Check()
        {
            if (NativeBackend != null) { var depth = NativeBackend.QueueDepth; return; }
            StartFallback();
            if (failure != null) throw new IOException("FFmpeg input failed: " + ErrorText(), failure);
            if (process.HasExited) throw new IOException("FFmpeg exited (" + process.ExitCode + "): " + ErrorText());
        }
        private string ErrorText() { lock (stderr) return stderr.ToString(); }

        internal string DiagnosticReport
        {
            get
            {
                var report = new StringBuilder();
                report.AppendLine("Encoder backend: " + (NativeBackend != null ? "native NVENC" : "FFmpeg")
                    + "; started=" + started + "; disposed=" + disposed);
                try
                {
                    report.AppendLine("FFmpeg command: " + process?.StartInfo.FileName + " " + process?.StartInfo.Arguments);
                    if (started && process != null)
                        report.AppendLine("FFmpeg process: " + (process.HasExited ? "exited; code=" + process.ExitCode : "running"));
                    report.AppendLine("Encoder frames: written=" + WrittenFrames + "; repeated=" + RepeatedFrames
                        + "; queue=" + QueueDepth + "/" + BufferCapacity + "; peak=" + PeakQueueDepth);
                }
                catch (Exception ex) { report.AppendLine("Encoder inspection unavailable: " + ex.Message); }
                report.AppendLine("Encoder worker error: " + (failure?.ToString() ?? "none"));
                report.AppendLine("FFmpeg stderr (last 16384 characters):\n" + ErrorText());
                return report.ToString();
            }
        }
        public bool TryRent(out Frame frame) { Check(); return free.TryTake(out frame); }
        internal void EnableNative(NativeNvencEncoder native)
        {
            if (NativeBackend != null || WrittenFrames != 0 || work.Count != 0)
                throw new InvalidOperationException("Native encoding must be selected before frame zero.");
            work.CompleteAdding();
            if (started)
            {
                if (!writer.Join(5000)) throw new TimeoutException("Cannot stop the unused FFmpeg input worker.");
                AbortProcess();
                if (!process.WaitForExit(5000)) throw new TimeoutException("Cannot stop the unused FFmpeg encoder.");
                if (File.Exists(output)) File.Delete(output);
            }
            while (free.TryTake(out var unused)) { }
            NativeBackend = native;
        }
        public Frame Rent()
        {
            var timeout = Stopwatch.StartNew();
            while (true)
            {
                Check();
                if (free.TryTake(out var frame, 100)) return frame;
                if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("FFmpeg stopped consuming frames.");
            }
        }

        private static string NvencPreset(string preset)
        {
            if (string.Equals(preset, "ultrafast", StringComparison.OrdinalIgnoreCase)) return "p1";
            if (string.Equals(preset, "fast", StringComparison.OrdinalIgnoreCase)) return "p6";
            return "p4";
        }

        internal static string BuildVideoFilter(string codec, string pixelFormat)
        {
            // VAAPI and Vulkan encoders consume hardware frames. Convert and
            // flip on the CPU before uploading; the raw transport can remain
            // RGB or RGBA. Vulkan FFV1 keeps the native RGB surface (no YUV
            // subsampling), which is also what avoids the FFV1 v3 alignment
            // limits on chroma-subsampled input.
            if (codec.EndsWith("_vulkan", StringComparison.OrdinalIgnoreCase))
                // FFV1's RGB coder accepts packed BGRA (AV_PIX_FMT_RGB32); RGBA
                // is not in its table. BGRA also maps to a universally regular
                // Vulkan storage format (R8G8B8A8).
                return "vflip,format=bgra,hwupload";
            if (codec.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase))
                return "vflip," + Bt709VideoFilter + ",format="
                    + (pixelFormat == "yuv420p10le" ? "p010le" : "nv12") + ",hwupload";
            // Packed RGB outputs such as ProRes 4444 with alpha must not pass
            // through a YUV matrix conversion: that would discard or flatten
            // the alpha plane. Their pixels are already full-range RGB.
            return IsRgbPixelFormat(pixelFormat) ? "vflip" : "vflip," + Bt709VideoFilter;
        }

        private static bool IsRgbPixelFormat(string pixelFormat)
        {
            return string.Equals(pixelFormat, "bgra", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pixelFormat, "rgba", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pixelFormat, "rgb24", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pixelFormat, "rgb32", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pixelFormat, "gbrp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pixelFormat, "gbrp10le", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The -pix_fmt FFmpeg gets for <paramref name="pixelFormat"/>. VideoToolbox HEVC takes
        /// 10-bit frames only as p010le; the same samples, packed differently.
        /// </summary>
        internal static string EncoderPixelFormat(string codec, string pixelFormat)
            => string.Equals(pixelFormat, "yuv420p10le", StringComparison.Ordinal)
                && string.Equals(codec, "hevc_videotoolbox", StringComparison.OrdinalIgnoreCase) ? "p010le" : pixelFormat;

        internal static string ColorMetadataOptions(bool fullRange)
        {
            // RGB surfaces (Vulkan FFV1 / ProRes alpha) are full range; encoded
            // YUV is always limited range. The matrix and primaries are BT.709
            // for both so players do not have to guess.
            return " -color_range " + (fullRange ? "pc" : "tv")
                + " -colorspace bt709 -color_primaries bt709 -color_trc bt709";
        }

        internal static string RateControl(string codec, int bitrateKbps, bool legacyCrf)
        {
            // libaom-av1 does not accept x264-style CBR flags. Use a target
            // bitrate for audio renders and capped CRF for video-only
            // renders so the existing quality/bitrate intent is retained.
            if (string.Equals(codec, "libaom-av1", StringComparison.OrdinalIgnoreCase))
                return legacyCrf ? "-crf 30 -b:v 0" : "-b:v " + bitrateKbps + "k";
            if (legacyCrf && string.Equals(codec, "libx264", StringComparison.OrdinalIgnoreCase)) return "-crf 18";
            return "-b:v " + bitrateKbps + "k -maxrate " + bitrateKbps + "k -bufsize " + bitrateKbps * 2 + "k";
        }

        internal static string BuildEncoderOptions(string codec, string preset, string rateControl,
            bool isHardwareEncoder, string pixelFormat, ProResProfile proResProfile = ProResProfile.HQ)
        {
            var normalized = (codec ?? string.Empty).Trim().ToLowerInvariant();
            // ProRes sets its own bitrate from the selected profile and frame size.
            proResProfile = ProResProfiles.Normalize(proResProfile);
            if (normalized == "prores_ks")
                return "-c:v prores_ks -profile:v " + (int)proResProfile + " -threads 0";
            if (normalized == "prores_videotoolbox")
                return "-c:v prores_videotoolbox -profile:v " + (int)proResProfile + " -allow_sw 0";
            // FFV1 is a frame-intra lossless codec: bitrate and encoder presets
            // do not apply. Level 3 with a fixed slice count enables threaded
            // slice encoding on every supported FFmpeg build.
            if (normalized == "ffv1") return "-c:v ffv1 -level 3 -slices 8 -threads 8";
            // The Vulkan FFV1 encoder slices internally (async GPU dispatch) and
            // rejects bitrate/preset/threads arguments entirely.
            if (normalized.EndsWith("_vulkan", StringComparison.Ordinal))
                return "-c:v " + normalized;
            var tenBitHevc = string.Equals(pixelFormat, "yuv420p10le", StringComparison.Ordinal)
                && (normalized == "libx265" || normalized == "hevc_nvenc" || normalized == "hevc_qsv"
                    || normalized == "hevc_amf" || normalized == "hevc_vaapi" || normalized == "hevc_videotoolbox");
            var profile = tenBitHevc ? " -profile:v main10" : string.Empty;
            // VideoToolbox has no presets. -allow_sw 0 refuses Apple's software encoder, so a
            // Mac without the hardware encoder fails the preflight instead of encoding slowly.
            if (normalized == "h264_videotoolbox" || normalized == "hevc_videotoolbox")
                return "-c:v " + normalized + " -allow_sw 0 " + rateControl + profile;
            if (normalized.EndsWith("_vaapi", StringComparison.Ordinal))
                // Leave driver-specific rate-control/quality selection on auto.
                // VAAPI does not accept x264/NVENC preset names.
                return "-c:v " + normalized + " " + rateControl + profile;
            if (isHardwareEncoder && normalized.EndsWith("_nvenc", StringComparison.Ordinal))
                return "-c:v " + normalized + " -preset " + NvencPreset(preset)
                    + " -tune hq -rc cbr " + rateControl + profile;

            if (isHardwareEncoder && normalized.EndsWith("_qsv", StringComparison.Ordinal))
                return "-c:v " + normalized + " -preset " + QsvPreset(preset)
                    + " " + rateControl + profile;

            if (isHardwareEncoder && normalized.EndsWith("_amf", StringComparison.Ordinal))
                return "-c:v " + normalized + " -quality " + AmfQuality(preset)
                    + " -rc cbr " + rateControl + profile;

            if (normalized == "libvpx-vp9")
            {
                var cpuUsed = string.Equals(preset, "ultrafast", StringComparison.OrdinalIgnoreCase) ? 8
                    : string.Equals(preset, "veryfast", StringComparison.OrdinalIgnoreCase) ? 6 : 4;
                return "-c:v libvpx-vp9 -deadline good -cpu-used " + cpuUsed
                    + " -row-mt 1 " + rateControl;
            }

            if (normalized == "libaom-av1")
            {
                var aomCpuUsed = string.Equals(preset, "ultrafast", StringComparison.OrdinalIgnoreCase) ? 8
                    : string.Equals(preset, "veryfast", StringComparison.OrdinalIgnoreCase) ? 6 : 4;
                return "-c:v libaom-av1 -cpu-used " + aomCpuUsed + " -row-mt 1 " + rateControl;
            }

            var encoderPreset = preset;
            return "-c:v " + normalized + " -threads 0 -preset " + encoderPreset
                + (string.Equals(preset, "ultrafast", StringComparison.OrdinalIgnoreCase)
                    && (normalized == "libx264" || normalized == "libx265")
                    ? " -tune zerolatency " : " ") + rateControl + profile;
        }

        private static string QsvPreset(string preset)
        {
            if (string.Equals(preset, "ultrafast", StringComparison.OrdinalIgnoreCase)) return "veryfast";
            if (string.Equals(preset, "veryfast", StringComparison.OrdinalIgnoreCase)) return "faster";
            return "fast";
        }

        private static string AmfQuality(string preset)
        {
            if (string.Equals(preset, "ultrafast", StringComparison.OrdinalIgnoreCase)) return "speed";
            if (string.Equals(preset, "veryfast", StringComparison.OrdinalIgnoreCase)) return "balanced";
            return "quality";
        }

        internal static bool TryValidateVideo(string executable, int bitrateMbps, string preset, string codec,
            string pixelFormat, string extension, bool legacyCrf, out string error,
            ProResProfile proResProfile = ProResProfile.HQ)
        {
            error = null;
            string output;
            try
            {
                output = Path.Combine(GetTemporaryDirectory(), "OrbitRender-encoder-check-"
                    + Guid.NewGuid().ToString("N") + extension);
            }
            catch (Exception ex)
            {
                error = "Could not create a temporary FFmpeg output path: " + ex.Message;
                return false;
            }
            try
            {
                // Keep the probe at the renderer's minimum profile size. Some
                // hardware encoders, including NVENC on supported drivers,
                // reject tiny 128x128 surfaces before testing any real frame.
                using (var encoder = new FFmpegEncoder(executable, output, 320, 180, 30,
                    Math.Max(1, bitrateMbps) * 1000, preset, legacyCrf, false, codec, pixelFormat,
                    RawVideoPixelFormat.Rgba32, proResProfile: proResProfile))
                {
                    var frame = encoder.Rent();
                    frame.Index = 0;
                    for (int i = 0; i < frame.Bytes.Length; i += 4)
                    {
                        frame.Bytes[i] = 32;
                        frame.Bytes[i + 1] = 96;
                        frame.Bytes[i + 2] = 160;
                        frame.Bytes[i + 3] = 255;
                    }
                    encoder.Submit(frame);
                    encoder.Finish(1);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(output)) File.Delete(output); } catch { }
            }
        }

        private static string GetTemporaryDirectory()
        {
            string configured;
            try { configured = Path.GetTempPath(); }
            catch { configured = null; }

            if (TryCreateDirectory(configured)) return configured;

            // On macOS, Path.GetTempPath() commonly comes from TMPDIR and can
            // point at a stale /var/folders/... directory after a shell or
            // login session has outlived the directory owner. /tmp is the
            // portable Unix fallback and is recreated by the OS as needed.
            if (Path.DirectorySeparatorChar == '/' && TryCreateDirectory("/tmp")) return "/tmp";

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var fallback = string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "Temp");
            if (TryCreateDirectory(fallback)) return fallback;

            throw new IOException("No writable temporary directory is available.");
        }

        private static bool TryCreateDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return false;
            try
            {
                Directory.CreateDirectory(directory);
                return Directory.Exists(directory);
            }
            catch { return false; }
        }

        private static bool LooksLikeFilePath(string executable)
        {
            return Path.IsPathRooted(executable)
                || executable.IndexOf(Path.DirectorySeparatorChar) >= 0
                || executable.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
        }
        public void Submit(Frame frame)
        {
            Check();
            work.Add(frame);
            var depth = work.Count;
            while (true)
            {
                var previous = Volatile.Read(ref peakQueueDepth);
                if (depth <= previous || Interlocked.CompareExchange(ref peakQueueDepth, depth, previous) == previous)
                    break;
            }
        }
        public void Finish(long expectedFrames)
        {
            if (NativeBackend != null) { NativeBackend.Finish(expectedFrames, executable, output, fps); return; }
            Check();
            work.CompleteAdding();
            if (!writer.Join(30000)) { AbortProcess(); throw new TimeoutException("FFmpeg input did not finish."); }
            if (failure != null) throw new IOException("FFmpeg input failed: " + ErrorText(), failure);
            if (!process.WaitForExit(30000)) { AbortProcess(); throw new TimeoutException("FFmpeg did not finalize the video."); }
            process.WaitForExit(); // Drain asynchronous stderr events after process exit.
            if (process.ExitCode != 0) throw new IOException("FFmpeg exited (" + process.ExitCode + "): " + ErrorText());
            if (WrittenFrames != expectedFrames) throw new IOException("Encoded frame count does not match the render clock.");
        }
        private void AbortProcess() { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }
        internal static string ContainerOptions(string output)
        {
            var extension = Path.GetExtension(output).ToLowerInvariant();
            return extension == ".mp4" || extension == ".mov" ? " -movflags +faststart"
                : extension == ".webm" ? " -f webm" : string.Empty;
        }
        // audioOffsetSeconds skips the start of the WAV (selection renders).
        // audioDelaySeconds starts the WAV that much after the first video
        // frame, or earlier when negative (replay renders).
        public static void MuxAudio(string executable, string video, string audio, string output,
            double audioOffsetSeconds = 0.0, double audioGainDb = 0.0, AudioCodec audioCodec = AudioCodec.Auto,
            double audioDelaySeconds = 0.0)
        {
            if (double.IsNaN(audioGainDb) || double.IsInfinity(audioGainDb)
                || audioGainDb < MinAudioGainDb
                || audioGainDb > MaxAudioGainDb)
                throw new ArgumentOutOfRangeException(nameof(audioGainDb));
            if (double.IsNaN(audioDelaySeconds) || double.IsInfinity(audioDelaySeconds))
                throw new ArgumentOutOfRangeException(nameof(audioDelaySeconds));
            audioCodec = AudioCodecCatalog.Resolve(audioCodec, Path.GetExtension(output));
            var containerOptions = ContainerOptions(output);
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var audioSeek = audioOffsetSeconds > 0
                ? "-ss " + audioOffsetSeconds.ToString("0.########", culture) + " "
                : string.Empty;
            var filters = new System.Collections.Generic.List<string>();
            // A later start runs past the last video frame, where -shortest
            // ends it. An earlier start is padded with the same length of
            // silence, so a WAV as long as the video keeps every frame.
            if (audioDelaySeconds > 0)
                filters.Add("adelay=" + (audioDelaySeconds * 1000).ToString("0.######", culture) + ":all=1");
            else if (audioDelaySeconds < 0)
                filters.Add("atrim=start=" + (-audioDelaySeconds).ToString("0.########", culture)
                    + ",asetpts=PTS-STARTPTS,apad=pad_dur=" + (-audioDelaySeconds).ToString("0.########", culture));
            if (audioGainDb <= MinAudioGainDb) filters.Add("volume=0");
            else if (Math.Abs(audioGainDb) > 0.000001) filters.Add("volume=" + audioGainDb.ToString("0.########", culture) + "dB");
            var audioFilter = filters.Count == 0 ? string.Empty : " -af \"" + string.Join(",", filters) + "\"";
            using (var mux = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
                Arguments = "-hide_banner -loglevel error -nostdin -progress pipe:1 -nostats -n -i \"" + video + "\" " + audioSeek + "-i \"" + audio
                    + "\" -map 0:v:0 -map 1:a:0 -c:v copy" + audioFilter + AudioCodecCatalog.EncodingArguments(audioCodec)
                    + " " + containerOptions + " -shortest \"" + output + "\""
            }})
            {
                RunMux(mux);
            }
        }
        public static void MuxPreencodedAudio(string executable, string video, string audio, string output,
            AudioCodec audioCodec = AudioCodec.Auto)
        {
            audioCodec = AudioCodecCatalog.Resolve(audioCodec, Path.GetExtension(output));
            // Both inputs already cover the render timeline. Audio packet
            // priming/rounding can make -shortest drop the last video frame,
            // including ProRes with AAC. Preserve every rendered frame.
            using (var mux = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
                Arguments = "-hide_banner -loglevel error -nostdin -progress pipe:1 -nostats -n -i \"" + video + "\" -i \"" + audio
                    + "\" -map 0:v:0 -map 1:a:0 -c:v copy -c:a copy" + ContainerOptions(output) + " \""
                    + output + "\""
            }})
            {
                RunMux(mux);
            }
        }
        private static void RunMux(Process mux)
        {
            var lastProgress = Stopwatch.GetTimestamp();
            var errors = new StringBuilder();
            mux.OutputDataReceived += (sender, args) => {
                if (args.Data != null)
                    Interlocked.Exchange(ref lastProgress, Stopwatch.GetTimestamp());
            };
            mux.ErrorDataReceived += (sender, args) => {
                if (args.Data == null) return;
                lock (errors)
                {
                    errors.AppendLine(args.Data);
                    if (errors.Length > 16384) errors.Remove(0, errors.Length - 16384);
                }
            };
            mux.Start();
            mux.BeginOutputReadLine();
            mux.BeginErrorReadLine();
            // AAC encoding scales with song length, and faststart can spend
            // minutes moving a large MP4 without emitting progress.
            while (!mux.WaitForExit(1000))
            {
                if ((Stopwatch.GetTimestamp() - Interlocked.Read(ref lastProgress))
                    / (double)Stopwatch.Frequency < 600) continue;
                mux.Kill();
                mux.WaitForExit();
                throw new TimeoutException("Audio/video mux produced no progress for 10 minutes. " + errors);
            }
            mux.WaitForExit(); // Drain redirected output before inspecting errors.
            if (mux.ExitCode != 0) throw new IOException("Audio/video mux failed: " + errors);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            NativeBackend?.Dispose();
            work.CompleteAdding();
            AbortProcess();
            // Killing the child is not synchronous. Wait for it to release the
            // output file before cleanup attempts to delete a failed partial.
            try { if (!process.HasExited) process.WaitForExit(5000); } catch { }
            if (writer != null && !writer.Join(5000)) return; // Do not dispose collections still used by a worker.
            process.Dispose(); work.Dispose(); free.Dispose();
        }
    }
}
