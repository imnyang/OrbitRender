using System;

namespace OrbitRender.Renderer
{
    // Mirrors scrConductor.ScrubMusicToTime's chart origin and 100 ms lead-in.
    // Kept independent of Unity so offset/pitch/negative-seek cases can be checked.
    internal readonly struct CheckpointAudioSchedule
    {
        internal double ConductorStartDsp { get; }
        internal double SourceStartDsp { get; }
        internal double SourceTimeSeconds { get; }
        private CheckpointAudioSchedule(double conductorStart, double sourceStart, double sourceTime)
        { ConductorStartDsp = conductorStart; SourceStartDsp = sourceStart; SourceTimeSeconds = sourceTime; }

        internal static CheckpointAudioSchedule Create(double dspTime, double songTime,
            double pitch, double offset, double countdownSongSeconds)
        {
            if (!Finite(dspTime) || !Finite(songTime) || !Finite(pitch) || pitch <= 0
                || !Finite(offset) || !Finite(countdownSongSeconds) || countdownSongSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(songTime));
            double seek = songTime + offset - countdownSongSeconds;
            double start = dspTime + 0.1;
            return new CheckpointAudioSchedule(start - (songTime + offset) / pitch,
                start + Math.Max(0, -seek) / pitch, Math.Max(0, seek));
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
