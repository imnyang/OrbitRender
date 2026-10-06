using System;
using System.IO;
#if UMM
using UnityModManagerNet;
#endif

namespace OrbitRender
{
    public enum RendererPreset
    {
        Custom,
        Preview,
        FullHD,
        QHD,
        UHD4K
    }

    public enum EncoderSpeed
    {
        Maximum,
        Balanced,
        Quality
    }

    internal sealed class RenderProfile
    {
        public RenderProfile(int width, int height, int targetFps, int videoFps, int bitrateMbps, string ffmpegPreset,
            float endDelaySeconds = 2f, string ffmpegCodec = "libx264", VideoCodec videoCodec = VideoCodec.H264,
            VideoBitDepth bitDepth = VideoBitDepth.Eight, VideoContainer container = VideoContainer.Auto,
            AudioCodec audioCodec = AudioCodec.Auto)
        {
            Width = width;
            Height = height;
            TargetFps = targetFps;
            VideoFps = videoFps;
            BitrateMbps = bitrateMbps;
            FfmpegPreset = ffmpegPreset;
            EndDelaySeconds = endDelaySeconds;
            FfmpegCodec = ffmpegCodec;
            VideoCodec = VideoCodecCatalog.Normalize(videoCodec);
            BitDepth = VideoCodecCatalog.Normalize(bitDepth);
            Container = container;
            AudioCodec = AudioCodecCatalog.Resolve(audioCodec, ContainerExtension);
        }

        public int Width { get; }
        public int Height { get; }
        public int TargetFps { get; }
        public int VideoFps { get; }
        public int BitrateMbps { get; }
        public string FfmpegPreset { get; }
        public float EndDelaySeconds { get; }
        public string FfmpegCodec { get; }
        public VideoCodec VideoCodec { get; }
        public VideoBitDepth BitDepth { get; }
        public string PixelFormat => BitDepth == VideoBitDepth.Ten ? "yuv420p10le" : "yuv420p";
        public VideoContainer Container { get; }
        public string ContainerExtension => OutputFormat.Extension(Container, VideoCodec);
        public string ContainerMimeType => OutputFormat.MimeType(ContainerExtension);
        public AudioCodec AudioCodec { get; }
        public string AudioEncoder => AudioCodecCatalog.Encoder(AudioCodec);
        public string AudioBitrate => AudioCodecCatalog.Bitrate(AudioCodec);
    }

    public sealed class RendererSettings
#if UMM
        : UnityModManager.ModSettings, IDrawable
#endif
    {
#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public System.Collections.Generic.List<UserRenderPreset> UserPresets =
            new System.Collections.Generic.List<UserRenderPreset>();

        private const int MinWidth = 320;
        private const int MaxWidth = 3840;
        private const int MinHeight = 180;
        private const int MaxHeight = 2160;
        private const int MinFps = 15;
        private const int MaxTargetFps = 1024;
        private const int MaxVideoFps = 240;
        private const int MinBitrate = 1;
        private const int MaxBitrate = 200;
        internal const float MinAudioGainDb = -60f;
        internal const float MaxAudioGainDb = 12f;

#if UMM
        [Draw("Preset", DrawType.PopupList)]
#endif
        public RendererPreset Preset = RendererPreset.FullHD;

#if UMM
        [Draw("Width", DrawType.Field, Min = MinWidth, Max = MaxWidth, VisibleOn = "Preset|Custom")]
#endif
        public int Width = 1920;

#if UMM
        [Draw("Height", DrawType.Field, Min = MinHeight, Max = MaxHeight, VisibleOn = "Preset|Custom")]
#endif
        public int Height = 1080;

        // Do not pass Min/Max to UMM for a text field. UMM clamps each parsed
        // keystroke, so typing a value such as 120 would turn the first "1"
        // into 15 before the remaining digits can be entered.
#if UMM
        [Draw("InGame FPS", DrawType.Field, VisibleOn = "Preset|Custom")]
#endif
        public int Fps = 60;

#if UMM
        [Draw("Video FPS", DrawType.Field, VisibleOn = "Preset|Custom")]
#endif
        public int VideoFps = 60;

#if UMM
        [Draw("Video bitrate (Mbps)", DrawType.Field, Min = MinBitrate, Max = MaxBitrate, VisibleOn = "Preset|Custom")]
#endif
        public int BitrateMbps = 18;

#if UMM
        [Draw("End delay (seconds)", DrawType.Field, Min = 0, Max = 30, Precision = 2)]
#endif
        public float EndDelaySeconds = 2f;

#if UMM
        [Draw("Capture audio", DrawType.Toggle)]
#endif
        public bool CaptureAudio = true;

#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public float AudioGainDb = 0f;

#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public AudioCodec AudioCodec = AudioCodec.Auto;

#if UMM
        [Draw("Show render preview", DrawType.Toggle)]
#endif
        public bool ShowRenderPreview = true;

#if UMM
        [Draw("BGA mode (hide tiles, planets & hit sounds)", DrawType.Toggle)]
#endif
        public bool BgaMode = false;

#if UMM
        [Draw("Show planet rings", DrawType.Toggle)]
#endif
        public bool ShowPlanetRings = true;

#if UMM
        [Draw("Show song title", DrawType.Toggle)]
#endif
        public bool ShowSongTitle = true;

#if UMM
        [Draw("Show countdown", DrawType.Toggle)]
#endif
        public bool ShowCountdown = true;

#if UMM
        [Draw("Show result text (hit judgments stay hidden)", DrawType.Toggle)]
#endif
        public bool ShowResultText = true;

#if UMM
        [Draw("Show hit judgments", DrawType.Toggle)]
#endif
        public bool ShowHitJudgments = false;

#if UMM
        [Draw("Encoding speed", DrawType.PopupList)]
#endif
        public EncoderSpeed Encoding = EncoderSpeed.Quality;

#if UMM
        [Draw("Video encoder", DrawType.PopupList)]
#endif
        public VideoEncoder Encoder = VideoEncoder.Auto;

#if UMM
        [Draw("Video codec", DrawType.PopupList)]
#endif
        public VideoCodec Codec = VideoCodec.H264;

#if UMM
        [Draw("Video bit depth", DrawType.PopupList)]
#endif
        public VideoBitDepth BitDepth = VideoBitDepth.Eight;

        // These paths are rendered by Main.OnGUI so a Browse button can sit
        // next to each field. UMM's stock Field drawer cannot add a button
        // to the same row.
#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public string OutputDirectory = "";

#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public string FileNameFormat = OutputFormat.DefaultFileName;

#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public VideoContainer Container = VideoContainer.Auto;

#if UMM
        [Draw("Open output folder after render", DrawType.Toggle)]
#endif
        public bool OpenOutputFolder = true;

#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public string FfmpegExecutable = "";

        // FFmpeg is downloaded only after the user explicitly approves it on
        // the first launch. This is intentionally hidden from the UMM form;
        // the install prompt is shown in the game UI instead.
#if UMM
        [Draw(DrawType.Ignore)]
#endif
        public bool FfmpegInstallPrompted = false;

        public void OnChange()
        {
            // Selecting a built-in preset also copies its resolution and
            // bitrate into the fields, so switching to Custom starts from a
            // useful baseline. InGame FPS is deliberately independent from
            // the preset and must not be overwritten here.
            if (Preset != RendererPreset.Custom)
            {
                var profile = GetPresetProfile(Preset);
                Width = profile.Width;
                Height = profile.Height;
                BitrateMbps = profile.BitrateMbps;
            }
            Width = EvenClamp(Width, MinWidth, MaxWidth);
            Height = EvenClamp(Height, MinHeight, MaxHeight);
            Fps = Clamp(Fps, MinFps, MaxTargetFps);
            VideoFps = Clamp(VideoFps, MinFps, MaxVideoFps);
            BitrateMbps = Clamp(BitrateMbps, MinBitrate, MaxBitrate);
            if (float.IsNaN(EndDelaySeconds) || float.IsInfinity(EndDelaySeconds)) EndDelaySeconds = 2f;
            EndDelaySeconds = Math.Max(0f, Math.Min(30f, EndDelaySeconds));
            AudioGainDb = ClampAudioGainDb(AudioGainDb);
        }

        internal void ResetToDefaults()
        {
            Preset = RendererPreset.FullHD;
            Width = 1920;
            Height = 1080;
            Fps = 60;
            VideoFps = 60;
            BitrateMbps = 18;
            EndDelaySeconds = 2f;
            CaptureAudio = true;
            AudioGainDb = 0f;
            AudioCodec = AudioCodec.Auto;
            ShowRenderPreview = true;
            BgaMode = false;
            ShowPlanetRings = true;
            ShowSongTitle = true;
            ShowCountdown = true;
            ShowResultText = true;
            ShowHitJudgments = false;
            Encoding = EncoderSpeed.Quality;
            Encoder = VideoEncoder.Auto;
            Codec = VideoCodec.H264;
            BitDepth = VideoBitDepth.Eight;
            OutputDirectory = string.Empty;
            FileNameFormat = OutputFormat.DefaultFileName;
            Container = VideoContainer.Auto;
            OpenOutputFolder = true;
            FfmpegExecutable = string.Empty;
        }

        internal RenderProfile ResolveProfile()
        {
            return ResolveProfile(null, null, null, null, null, null, null, null, null);
        }

        internal RenderProfile ResolveProfile(RendererPreset? presetOverride, int? widthOverride,
            int? heightOverride, int? targetFpsOverride, int? videoFpsOverride, int? bitrateOverride, float? endDelayOverride,
            VideoCodec? codecOverride, VideoBitDepth? bitDepthOverride,
            EncoderSpeed? encodingOverride = null, VideoEncoder? encoderOverride = null, VideoContainer? containerOverride = null,
            AudioCodec? audioCodecOverride = null)
        {
            var preset = presetOverride ?? Preset;
            // A target-FPS override does not turn a built-in resolution preset
            // into Custom. Width/height/bitrate overrides without an explicit
            // preset still use the saved Custom values as their base.
            var useCustomBase = preset == RendererPreset.Custom
                || (!presetOverride.HasValue
                    && (widthOverride.HasValue || heightOverride.HasValue || bitrateOverride.HasValue));
            var baseProfile = useCustomBase
                ? new RenderProfile(
                    EvenClamp(Width, MinWidth, MaxWidth),
                    EvenClamp(Height, MinHeight, MaxHeight),
                    Clamp(Fps, MinFps, MaxTargetFps),
                    Clamp(VideoFps, MinFps, MaxVideoFps),
                    Clamp(BitrateMbps, MinBitrate, MaxBitrate),
                    "fast", EndDelaySeconds)
                : GetPresetProfile(preset);
            var endDelay = endDelayOverride.HasValue
                ? Clamp(endDelayOverride.Value, 0f, 30f)
                : baseProfile.EndDelaySeconds;
            var encoding = encodingOverride ?? Encoding;
            var encoder = encoderOverride ?? Encoder;
            // RPC requests naming a preset retain that preset's default rates
            // unless they explicitly provide target/video FPS. Normal settings
            // and the export dialog use the independent saved values.
            var targetFps = targetFpsOverride.HasValue
                ? Clamp(targetFpsOverride.Value, MinFps, MaxTargetFps)
                : presetOverride.HasValue && preset != RendererPreset.Custom
                    ? baseProfile.TargetFps
                    : Clamp(Fps, MinFps, MaxTargetFps);
            var videoFps = videoFpsOverride.HasValue
                ? Clamp(videoFpsOverride.Value, MinFps, MaxVideoFps)
                : presetOverride.HasValue && preset != RendererPreset.Custom
                    ? baseProfile.VideoFps
                    : Clamp(VideoFps, MinFps, MaxVideoFps);
            return new RenderProfile(
                widthOverride.HasValue ? EvenClamp(widthOverride.Value, MinWidth, MaxWidth) : baseProfile.Width,
                heightOverride.HasValue ? EvenClamp(heightOverride.Value, MinHeight, MaxHeight) : baseProfile.Height,
                targetFps,
                videoFps,
                bitrateOverride.HasValue ? Clamp(bitrateOverride.Value, MinBitrate, MaxBitrate) : baseProfile.BitrateMbps,
                GetEncoderPreset(encoding), endDelay, GetEncoderCodec(codecOverride ?? Codec, encoder,
                    bitDepthOverride ?? BitDepth, containerOverride ?? Container), codecOverride ?? Codec,
                bitDepthOverride ?? BitDepth, containerOverride ?? Container,
                audioCodecOverride ?? AudioCodecCatalog.CompatibleSelection(AudioCodec,
                    OutputFormat.Extension(containerOverride ?? Container, codecOverride ?? Codec)));
        }

#if UMM
        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Normalize();
            Save(this, modEntry);
        }

        public static RendererSettings Load(UnityModManager.ModEntry modEntry)
        {
            var settings = UnityModManager.ModSettings.Load<RendererSettings>(modEntry) ?? new RendererSettings();
            settings.Normalize();
            return settings;
        }

#endif
        internal void Save(ModContext context)
        {
#if UMM
            Save(context.UmmEntry);
#else
            Normalize();
            SettingsStorage.Save(context.SettingsPath, this);
#endif
        }

        internal static RendererSettings Load(ModContext context)
        {
#if UMM
            return Load(context.UmmEntry);
#else
            var settings = SettingsStorage.Load(context.SettingsPath, context.Logger);
            settings.Normalize();
            return settings;
#endif
        }

        internal string ResolveOutputDirectory()
        {
            var dataPath = Path.GetFullPath(UnityEngine.Application.dataPath);
            var dataDirectory = new DirectoryInfo(dataPath);
            // A macOS Unity player reports its Contents directory as
            // Application.dataPath, while Windows/Linux report <game>_Data.
            var gameRoot = (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXPlayer
                || UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor)
                && string.Equals(dataDirectory.Name, "Contents", StringComparison.OrdinalIgnoreCase)
                ? dataDirectory.FullName
                : dataDirectory.Parent.FullName;
            var configured = Environment.ExpandEnvironmentVariables((OutputDirectory ?? string.Empty).Trim());
            if (string.IsNullOrEmpty(configured)) return Path.Combine(gameRoot, "Renders");
            if (!Path.IsPathRooted(configured)) configured = Path.Combine(gameRoot, configured);
            return Path.GetFullPath(configured);
        }

        internal string ResolveFfmpegExecutable(string modDirectory)
        {
            var configured = Environment.ExpandEnvironmentVariables((FfmpegExecutable ?? string.Empty).Trim());
            if (string.IsNullOrEmpty(configured)) return string.Empty;
            if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);

            // A relative path is resolved beside the mod. A bare command name
            // is returned as-is so Process.Start can resolve it through PATH.
            var local = Path.Combine(modDirectory, configured);
            return File.Exists(local) ? Path.GetFullPath(local) : configured;
        }

        internal static RenderProfile GetPresetProfile(RendererPreset preset)
        {
            switch (preset)
            {
                case RendererPreset.Preview: return new RenderProfile(1280, 720, 30, 30, 8, "veryfast");
                case RendererPreset.QHD: return new RenderProfile(2560, 1440, 60, 60, 30, "veryfast");
                case RendererPreset.UHD4K: return new RenderProfile(3840, 2160, 60, 60, 50, "fast");
                case RendererPreset.FullHD:
                default: return new RenderProfile(1920, 1080, 60, 60, 18, "veryfast");
            }
        }

        private string GetEncoderPreset(EncoderSpeed encoding)
        {
            switch (encoding)
            {
                case EncoderSpeed.Balanced: return "veryfast";
                case EncoderSpeed.Quality: return "fast";
                case EncoderSpeed.Maximum:
                default: return "ultrafast";
            }
        }

        private string GetEncoderCodec(VideoCodec codec, VideoEncoder encoder, VideoBitDepth depth, VideoContainer container)
        {
            var gpu = (UnityEngine.SystemInfo.graphicsDeviceName ?? string.Empty) + " "
                + (UnityEngine.SystemInfo.graphicsDeviceVendor ?? string.Empty);
            var nvidia = ContainsGpuName(gpu, "NVIDIA");
            var intel = ContainsGpuName(gpu, "Intel");
            var amd = ContainsGpuName(gpu, "AMD") || ContainsGpuName(gpu, "ATI")
                || ContainsGpuName(gpu, "Radeon");
            var definition = VideoCodecCatalog.Get(VideoCodecCatalog.Normalize(codec));
            if (encoder == VideoEncoder.Auto && Main.Entry != null)
            {
                var available = Renderer.EncoderAvailability.Get(Renderer.RendererController.ResolveFfmpegExecutable(this),
                    codec, depth, container);
                if (available.Complete && available.Encoders.Length > 0)
                {
                    // Prefer the active GPU when proven usable, then any usable
                    // secondary GPU, then software. Never pick a failed probe.
                    var preferred = nvidia ? VideoEncoder.NvidiaNvenc : intel ? VideoEncoder.IntelQsv
                        : amd ? VideoEncoder.AmdAmf : VideoEncoder.Software;
                    encoder = Array.IndexOf(available.Encoders, preferred) >= 0
                        ? preferred : available.Encoders[0];
                }
            }
            return definition.ResolveEncoder(encoder, nvidia, intel, amd);
        }

        private static bool ContainsGpuName(string value, string name)
        {
            return value.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void Normalize()
        {
            if (!Enum.IsDefined(typeof(RendererPreset), Preset)) Preset = RendererPreset.FullHD;
            if (!Enum.IsDefined(typeof(EncoderSpeed), Encoding)) Encoding = EncoderSpeed.Quality;
            Width = EvenClamp(Width, MinWidth, MaxWidth);
            Height = EvenClamp(Height, MinHeight, MaxHeight);
            Fps = Clamp(Fps, MinFps, MaxTargetFps);
            VideoFps = Clamp(VideoFps, MinFps, MaxVideoFps);
            BitrateMbps = Clamp(BitrateMbps, MinBitrate, MaxBitrate);
            Codec = VideoCodecCatalog.Normalize(Codec);
            BitDepth = VideoCodecCatalog.Normalize(BitDepth);
            if (!Enum.IsDefined(typeof(VideoEncoder), Encoder)) Encoder = VideoEncoder.Auto;
            if (!Enum.IsDefined(typeof(VideoContainer), Container)) Container = VideoContainer.Auto;
            AudioCodec = AudioCodecCatalog.CompatibleSelection(AudioCodec, OutputFormat.Extension(Container, Codec));
            if (string.IsNullOrWhiteSpace(FileNameFormat)) FileNameFormat = OutputFormat.DefaultFileName;
            if (float.IsNaN(EndDelaySeconds) || float.IsInfinity(EndDelaySeconds)) EndDelaySeconds = 2f;
            EndDelaySeconds = Clamp(EndDelaySeconds, 0f, 30f);
            AudioGainDb = ClampAudioGainDb(AudioGainDb);
        }

        internal static float ClampAudioGainDb(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Clamp(value, MinAudioGainDb, MaxAudioGainDb);
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static int EvenClamp(int value, int min, int max)
        {
            var result = Clamp(value, min, max);
            return (result & 1) == 0 ? result : result == max ? result - 1 : result + 1;
        }
    }
}
