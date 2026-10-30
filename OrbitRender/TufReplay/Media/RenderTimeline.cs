using System;
using System.Collections.Generic;

namespace TUFReplayRenderer.Media;

// Replay time follows the original pitch until clear, then advances at wall-clock speed.
public sealed class RenderTimeline
{
    public double GameplayStartUs { get; }
    public double GameplayRate { get; }
    public long? WonTimeUs { get; }

    public RenderTimeline(double gameplayStartUs, double gameplayRate, long? wonTimeUs)
    {
        if (double.IsNaN(gameplayStartUs) || double.IsInfinity(gameplayStartUs)
            || gameplayRate <= 0 || double.IsNaN(gameplayRate) || double.IsInfinity(gameplayRate))
            throw new ArgumentOutOfRangeException(nameof(gameplayRate));
        GameplayStartUs = gameplayStartUs;
        GameplayRate = gameplayRate;
        WonTimeUs = wonTimeUs;
    }

    public double ReplayToOutput(double replayUs) => GameplayStartUs
        + (WonTimeUs.HasValue && replayUs >= WonTimeUs.Value
            ? WonTimeUs.Value / GameplayRate + replayUs - WonTimeUs.Value
            : replayUs / GameplayRate);

    public double OutputToReplay(double outputUs)
    {
        double elapsed = outputUs - GameplayStartUs;
        double wonOutput = WonTimeUs.HasValue ? WonTimeUs.Value / GameplayRate : double.PositiveInfinity;
        return elapsed >= wonOutput ? WonTimeUs.Value + elapsed - wonOutput : elapsed * GameplayRate;
    }

    public double RateAtOutput(double outputUs) => WonTimeUs.HasValue && outputUs >= ReplayToOutput(WonTimeUs.Value)
        ? 1 : GameplayRate;

    public object[] ToSegments()
    {
        var segments = new List<object> { new { outputTimeUs = 0L, replayTimeUs = (long)Math.Round(-GameplayStartUs * GameplayRate), rate = GameplayRate } };
        if (WonTimeUs.HasValue)
            segments.Add(new { outputTimeUs = (long)Math.Round(ReplayToOutput(WonTimeUs.Value)), replayTimeUs = WonTimeUs.Value, rate = 1d });
        return segments.ToArray();
    }
}
