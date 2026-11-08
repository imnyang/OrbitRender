using System;
using OrbitRender.Renderer;

internal static class BitrateTests
{
    internal static void Run()
    {
        // FFmpeg reads k as 1000, so whole Mbps keep the rate they had as M.
        Equal("-b:v 18000k -maxrate 18000k -bufsize 36000k", FFmpegEncoder.RateControl("libx264", 18000, false),
            "whole Mbps");
        Equal("-b:v 18500k -maxrate 18500k -bufsize 37000k", FFmpegEncoder.RateControl("h264_videotoolbox", 18500, false),
            "exact Kbps");
        Equal("-b:v 1250k", FFmpegEncoder.RateControl("libaom-av1", 1250, false), "AV1 target");
        Equal("-crf 30 -b:v 0", FFmpegEncoder.RateControl("libaom-av1", 1250, true), "AV1 capped CRF");
        Equal("-crf 18", FFmpegEncoder.RateControl("libx264", 18500, true), "x264 CRF");
    }

    private static void Equal(string expected, string actual, string name)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception(name + ": expected '" + expected + "', got '" + actual + "'.");
    }
}
