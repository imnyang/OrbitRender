using System;
using OrbitRender.Renderer;

internal static class AudioDeviceRecoveryTests
{
    internal static void Run()
    {
        var recovery = new AudioDeviceRecovery();
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        bool Sample(double start, double rate, bool paused = false, int buffer = 64)
        {
            bool broken = false;
            for (int i = 1; i <= 11; i++)
                broken |= recovery.Update(start + i * 0.1, start + i * 0.1 * rate, paused, buffer);
            return broken;
        }
        Check(!Sample(0, 0), "Inactive recovery changed the buffer.");
        recovery.Begin(0, 0);
        Check(!Sample(0, 1), "Healthy DSP clock was rejected.");
        recovery.Begin(0, 0);
        Check(Sample(0, 0), "Stopped DSP clock was missed.");
        recovery.Begin(0, 0);
        Check(Sample(0, 0.5), "Slow DSP clock was missed.");
        recovery.Begin(0, 0);
        Check(!Sample(0, 0.8), "DSP progress above the threshold was rejected.");
        recovery.Begin(0, 0);
        Check(Sample(0, 0), "First recovery attempt was missed.");
        recovery.Rebase(1.1, 1.1);
        Check(Sample(1.1, 0, false, 128), "Still-broken device did not request another buffer increase.");
        recovery.Begin(0, 0);
        Check(!Sample(0, 0, true), "Paused listener was treated as broken.");
        recovery.Begin(0, 0);
        Check(!recovery.Update(0.5, 0, false, 64), "Loading stall triggered recovery.");
        Check(!recovery.Update(0.6, -1, false, 64), "DSP reset triggered recovery.");
        recovery.Begin(0, 0);
        Check(!Sample(0, 0, false, 2048), "Maximum buffer was exceeded.");
        recovery.Begin(0, 0);
        recovery.Rebase(5.9, 5.9);
        Check(!Sample(6, 0), "Rebase extended the recovery deadline.");
        recovery.Begin(0, 0);
        recovery.Cancel();
        Check(!Sample(0, 0), "New render did not cancel recovery.");
        Console.WriteLine("Audio device recovery tests passed.");
    }
}
