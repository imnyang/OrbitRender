using System;

namespace OrbitRender
{
    public enum VideoCodec
    {
        H264,
        H265,
        VP9,
        AV1,
        ProRes,
        FFV1
    }

    public enum VideoBitDepth
    {
        Eight,
        Ten
    }

    public enum VideoEncoder
    {
        Auto,
        NvidiaNvenc,
        // Keep Software at value 2 so existing 1.1.x/1.2.x settings files
        // do not silently change from software encoding to another backend.
        Software = 2,
        IntelQsv = 3,
        AmdAmf = 4,
        Vaapi = 5,
        Vulkan = 6,
        AppleVideoToolbox = 7
    }

    public enum ProResProfile
    {
        Proxy = 0,
        LT = 1,
        Standard = 2,
        HQ = 3,
        FourFourFourFour = 4,
        FourFourFourFourXQ = 5
    }

    internal static class ProResProfiles
    {
        internal static ProResProfile Normalize(ProResProfile profile)
            => Enum.IsDefined(typeof(ProResProfile), profile) ? profile : ProResProfile.HQ;

        internal static bool HasAlpha(ProResProfile profile)
            => profile == ProResProfile.FourFourFourFour || profile == ProResProfile.FourFourFourFourXQ;

        internal static string DisplayName(ProResProfile profile)
        {
            switch (Normalize(profile))
            {
                case ProResProfile.Proxy: return "Proxy";
                case ProResProfile.LT: return "LT";
                case ProResProfile.Standard: return "Standard";
                case ProResProfile.FourFourFourFour: return "4444";
                case ProResProfile.FourFourFourFourXQ: return "4444 XQ";
                default: return "HQ";
            }
        }

        internal static bool TryParse(string value, out ProResProfile profile)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", ""))
            {
                case "proxy": profile = ProResProfile.Proxy; return true;
                case "lt": profile = ProResProfile.LT; return true;
                case "standard": profile = ProResProfile.Standard; return true;
                case "hq": profile = ProResProfile.HQ; return true;
                case "4444": profile = ProResProfile.FourFourFourFour; return true;
                case "4444xq": case "xq": profile = ProResProfile.FourFourFourFourXQ; return true;
                default: profile = ProResProfile.HQ; return false;
            }
        }
    }

    internal sealed class VideoCodecDefinition
    {
        internal VideoCodecDefinition(VideoCodec codec, string displayName, string softwareEncoder,
            string nvidiaEncoder, string intelEncoder, string amdEncoder, string extension,
            string vulkanEncoder = null, string appleEncoder = null)
        {
            Codec = codec;
            DisplayName = displayName;
            SoftwareEncoder = softwareEncoder;
            NvidiaEncoder = nvidiaEncoder;
            IntelEncoder = intelEncoder;
            AmdEncoder = amdEncoder;
            ContainerExtension = extension;
            VulkanEncoder = vulkanEncoder;
            AppleEncoder = appleEncoder;
        }

        internal VideoCodec Codec { get; }
        internal string DisplayName { get; }
        internal string SoftwareEncoder { get; }
        internal string NvidiaEncoder { get; }
        internal string IntelEncoder { get; }
        internal string AmdEncoder { get; }
        internal string VulkanEncoder { get; }
        internal string AppleEncoder { get; }
        internal string ContainerExtension { get; }

        internal string ResolveEncoder(VideoEncoder backend, bool nvidiaGpu, bool intelGpu, bool amdGpu,
            bool appleVideoToolbox = false)
        {
            if ((backend == VideoEncoder.AppleVideoToolbox || (backend == VideoEncoder.Auto && appleVideoToolbox))
                && !string.IsNullOrEmpty(AppleEncoder)) return AppleEncoder;
            if (Codec == VideoCodec.ProRes || (Codec == VideoCodec.FFV1 && backend == VideoEncoder.Vaapi)) return SoftwareEncoder;
            if (backend == VideoEncoder.Vulkan)
            {
                if (!string.IsNullOrEmpty(VulkanEncoder)) return VulkanEncoder;
            }
            if (backend == VideoEncoder.Vaapi)
            {
                switch (Codec)
                {
                    case VideoCodec.H265: return "hevc_vaapi";
                    case VideoCodec.VP9: return "vp9_vaapi";
                    case VideoCodec.AV1: return "av1_vaapi";
                    default: return "h264_vaapi";
                }
            }
            if (backend == VideoEncoder.NvidiaNvenc || (backend == VideoEncoder.Auto && nvidiaGpu))
            {
                if (!string.IsNullOrEmpty(NvidiaEncoder)) return NvidiaEncoder;
            }
            if (backend == VideoEncoder.IntelQsv || (backend == VideoEncoder.Auto && intelGpu))
            {
                if (!string.IsNullOrEmpty(IntelEncoder)) return IntelEncoder;
            }
            if (backend == VideoEncoder.AmdAmf || (backend == VideoEncoder.Auto && amdGpu))
            {
                if (!string.IsNullOrEmpty(AmdEncoder)) return AmdEncoder;
            }
            return SoftwareEncoder;
        }
    }

    internal static class VideoCodecCatalog
    {
        private static readonly VideoCodecDefinition H264 = new VideoCodecDefinition(
            VideoCodec.H264, "H.264 / AVC", "libx264", "h264_nvenc", "h264_qsv", "h264_amf",
            ".mp4");
        private static readonly VideoCodecDefinition H265 = new VideoCodecDefinition(
            VideoCodec.H265, "H.265 / HEVC", "libx265", "hevc_nvenc", "hevc_qsv", "hevc_amf",
            ".mp4");
        private static readonly VideoCodecDefinition VP9 = new VideoCodecDefinition(
            VideoCodec.VP9, "VP9", "libvpx-vp9", null, null, null,
            ".webm");
        private static readonly VideoCodecDefinition AV1 = new VideoCodecDefinition(
            VideoCodec.AV1, "AV1", "libaom-av1", "av1_nvenc", "av1_qsv", "av1_amf",
            ".mp4");

        private static readonly VideoCodecDefinition ProRes = new VideoCodecDefinition(
            VideoCodec.ProRes, "Apple ProRes", "prores_ks", null, null, null,
            ".mov", appleEncoder: "prores_videotoolbox");

        private static readonly VideoCodecDefinition FFV1 = new VideoCodecDefinition(
            VideoCodec.FFV1, "FFV1 (Lossless)", "ffv1", null, null, null,
            ".mkv", "ffv1_vulkan");

        internal static string PixelFormat(VideoCodec codec, VideoBitDepth depth,
            ProResProfile proResProfile = ProResProfile.HQ, string encoder = null)
            => codec == VideoCodec.ProRes
                ? (string.Equals(encoder, "prores_videotoolbox", StringComparison.OrdinalIgnoreCase)
                    ? (ProResProfiles.HasAlpha(proResProfile) ? "bgra" : "p210le")
                    : (ProResProfiles.HasAlpha(proResProfile) ? "yuva444p10le" : "yuv422p10le"))
                : depth == VideoBitDepth.Ten ? "yuv420p10le" : "yuv420p";

        internal static VideoCodecDefinition Get(VideoCodec codec)
        {
            switch (codec)
            {
                case VideoCodec.H265: return H265;
                case VideoCodec.VP9: return VP9;
                case VideoCodec.AV1: return AV1;
                case VideoCodec.ProRes: return ProRes;
                case VideoCodec.FFV1: return FFV1;
                case VideoCodec.H264:
                default: return H264;
            }
        }

        internal static VideoCodec Normalize(VideoCodec codec)
        {
            return Enum.IsDefined(typeof(VideoCodec), codec) ? codec : VideoCodec.H264;
        }

        internal static VideoBitDepth Normalize(VideoBitDepth bitDepth)
        {
            return Enum.IsDefined(typeof(VideoBitDepth), bitDepth) ? bitDepth : VideoBitDepth.Eight;
        }

        internal static bool IsHardwareEncoder(string codec)
        {
            return !string.IsNullOrEmpty(codec)
                && (codec.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase)
                    || codec.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase)
                    || codec.EndsWith("_amf", StringComparison.OrdinalIgnoreCase)
                    || codec.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase)
                    || codec.EndsWith("_videotoolbox", StringComparison.OrdinalIgnoreCase)
                    || codec.EndsWith("_vulkan", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryParse(string value, out VideoCodec codec)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "h264":
                case "h.264":
                case "avc":
                case "avc1":
                    codec = VideoCodec.H264;
                    return true;
                case "h265":
                case "h.265":
                case "hevc":
                case "hev1":
                    codec = VideoCodec.H265;
                    return true;
                case "vp9":
                case "vp09":
                    codec = VideoCodec.VP9;
                    return true;
                case "prores":
                case "prores_ks":
                case "prores422hq":
                case "prores422":
                    codec = VideoCodec.ProRes;
                    return true;
                case "ffv1":
                case "ffv1.3":
                case "lossless":
                    codec = VideoCodec.FFV1;
                    return true;
                case "av1":
                case "av01":
                    codec = VideoCodec.AV1;
                    return true;
                default:
                    codec = VideoCodec.H264;
                    return false;
            }
        }
    }
}
