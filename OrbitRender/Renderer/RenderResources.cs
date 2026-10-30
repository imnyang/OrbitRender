using System;
using System.IO;

namespace OrbitRender.Renderer
{
    public enum FrameBufferPolicy { LowMemory, Balanced, Throughput }

    internal static class RenderResources
    {
        internal static int BufferCount(int frameBytes, FrameBufferPolicy policy)
        {
            if (frameBytes <= 0) throw new ArgumentOutOfRangeException(nameof(frameBytes));
            long budget = (policy == FrameBufferPolicy.LowMemory ? 64L
                : policy == FrameBufferPolicy.Throughput ? 512L : 256L) * 1024 * 1024;
            return (int)Math.Max(1L, Math.Min(16L, budget / frameBytes));
        }

        internal static long VideoBytes(double seconds, int width, int height, int fps, int bitrate, VideoCodec codec)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return 0;
            double bitsPerSecond;
            switch (codec)
            {
                // ProRes HQ is variable bitrate: use twice the approximate
                // 1080p30 target rate for conservative planning, never as an
                // exact size.
                case VideoCodec.ProRes:
                    bitsPerSecond = 440e6 * width / 1920d * height / 1080d * fps / 30d;
                    break;
                // Estimate FFV1 from the raw 8-bit 4:2:0 rate, rounding per frame.
                // Flat game art often compresses further; this is a conservative estimate.
                case VideoCodec.FFV1:
                    return Saturate(Math.Ceiling(seconds * fps)
                        * Math.Ceiling(width * (double)height * 12d * 0.55 * 1.05 / 8d));
                default:
                    bitsPerSecond = bitrate * 1e6;
                    break;
            }
            return Saturate(seconds * bitsPerSecond / 8 * 1.05);
        }

        internal static long RequiredBytes(long videoBytes, double audioSeconds, bool audio, int sampleRate = 48000, int channels = 8)
        {
            return Saturate(videoBytes * (audio ? 2d : 1d)
                + (audio ? audioSeconds * (sampleRate * (double)channels * 4 + 64000) : 0d)
                + 256L * 1024 * 1024);
        }

        private static long Saturate(double bytes) => bytes >= long.MaxValue || double.IsInfinity(bytes)
            ? long.MaxValue : double.IsNaN(bytes) || bytes <= 0 ? 0 : (long)Math.Ceiling(bytes);

        internal static long? FreeBytes(string directory)
        {
            try
            {
                var path = Path.GetFullPath(directory);
                DriveInfo best = null;
                foreach (var drive in DriveInfo.GetDrives())
                {
                    var root = drive.RootDirectory.FullName;
                    if (path.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                        && (best == null || root.Length > best.RootDirectory.FullName.Length)) best = drive;
                }
                return best != null && best.IsReady ? (long?)best.AvailableFreeSpace : null;
            }
            catch { return null; }
        }

        internal static string Size(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("0.##") + " GiB";
    }
}
