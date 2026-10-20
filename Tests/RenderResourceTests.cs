using System;
using OrbitRender;
using OrbitRender.Renderer;

internal static class RenderResourceTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run()
    {
        foreach (FrameBufferPolicy policy in Enum.GetValues(typeof(FrameBufferPolicy)))
        {
            long budget = (policy == FrameBufferPolicy.LowMemory ? 64L : policy == FrameBufferPolicy.Balanced ? 256L : 512L) * 1024 * 1024;
            foreach (int frame in new[] { 1920 * 1080 * 3, 3840 * 2160 * 4, 3840 * 2160 * 3, 100000000 })
            {
                int count = RenderResources.BufferCount(frame, policy);
                Assert(count >= 1 && count <= 16, "Invalid buffer count.");
                Assert(frame > budget ? count == 1 : (long)frame * count <= budget, "Frame pool exceeded budget.");
            }
        }
        var video = RenderResources.VideoBytes(60, 1920, 1080, 60, 36, VideoCodec.H264);
        Assert(video == 283500000, "Bitrate size estimate changed.");
        var shortAudio = RenderResources.RequiredBytes(video, 60, true, 48000, 2);
        var longAudio = RenderResources.RequiredBytes(video, 600, true, 48000, 2);
        Assert(longAudio > shortAudio, "Selection warmup audio was not counted.");
        Assert(shortAudio > video * 2, "Mux and temporary audio were not counted.");
        Assert(RenderResources.VideoBytes(double.NaN, 1920, 1080, 60, 36, VideoCodec.H264) == 0, "Invalid duration accepted.");
        Assert(RenderResources.RequiredBytes(long.MaxValue, 1, true) == long.MaxValue, "Large estimate overflowed.");
        Assert(RenderResources.VideoBytes(float.MaxValue, 3840, 2160, 240, 200, VideoCodec.ProRes) == long.MaxValue, "Large duration overflowed.");
        Assert(RenderResources.VideoBytes(60, 3840, 2160, 60, 36, VideoCodec.ProRes)
            == RenderResources.VideoBytes(60, 1920, 1080, 30, 36, VideoCodec.ProRes) * 8, "ProRes scaling changed.");
        Assert(RenderResources.VideoBytes(60, 1920, 1080, 60, 36, VideoCodec.FFV1)
            == RenderResources.VideoBytes(60, 1920, 1080, 30, 36, VideoCodec.FFV1) * 2, "Lossless scaling changed.");
        Assert(RenderResources.VideoBytes(60, 1920, 1080, 60, 36, VideoCodec.FFV1)
            > RenderResources.VideoBytes(60, 1920, 1080, 60, 36, VideoCodec.H264),
            "Lossless estimate fell below the bitrate estimate.");
        using (var encoder = new FFmpegEncoder("ffmpeg", "unused.mp4", 3840, 2160, 60, 36,
            "fast", false, "libx264", "yuv420p", RawVideoPixelFormat.Rgba32,
            deferStart: true, bufferPolicy: FrameBufferPolicy.LowMemory))
            Assert(encoder.BufferCapacity == 2, "Encoder ignored low-memory policy.");
    }
}
