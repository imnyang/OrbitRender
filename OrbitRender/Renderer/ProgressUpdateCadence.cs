using System;

namespace OrbitRender.Renderer
{
    // Wall-clock presentation cadence only. It never gates simulation, capture,
    // cancellation polling or replay callbacks.
    internal sealed class ProgressUpdateCadence
    {
        private readonly long interval;
        private long next;
        private bool published;

        internal ProgressUpdateCadence(long frequency) { interval = Math.Max(1, frequency / 10); }

        internal bool ShouldPublish(long now, bool changed)
        {
            if (published && !changed && now < next) return false;
            published = true;
            next = now + interval;
            return true;
        }
    }
}
