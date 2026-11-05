namespace OrbitRender.Renderer
{
    internal sealed class AudioDeviceRecovery
    {
        private double deadline;
        private double lastTime;
        private double lastDsp;
        private double elapsed;
        private double advanced;
        private bool active;

        internal void Begin(double time, double dsp)
        {
            active = true;
            deadline = time + 6.0;
            ResetWindow(time, dsp);
        }

        internal void Cancel() => active = false;

        internal void Rebase(double time, double dsp) => ResetWindow(time, dsp);

        internal bool Update(double time, double dsp, bool paused, int bufferSize)
        {
            if (!active) return false;
            if (time >= deadline || bufferSize >= 2048)
            {
                active = false;
                return false;
            }
            var delta = time - lastTime;
            var dspDelta = dsp - lastDsp;
            lastTime = time;
            lastDsp = dsp;
            // Device resets, listener pauses and loading stalls are not underruns.
            if (paused || delta <= 0 || delta > 0.2 || dspDelta < 0)
            {
                ResetWindow(time, dsp);
                return false;
            }
            elapsed += delta;
            advanced += dspDelta;
            if (elapsed < 1.0) return false;
            var broken = advanced < elapsed * 0.75;
            ResetWindow(time, dsp);
            return broken;
        }

        private void ResetWindow(double time, double dsp)
        {
            lastTime = time;
            lastDsp = dsp;
            elapsed = advanced = 0;
        }
    }
}
