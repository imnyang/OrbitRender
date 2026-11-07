namespace OrbitRender.Renderer
{
    // A nonnegative host revision certifies that every capture-relevant change
    // is represented. Legacy hosts use -1 and always execute the safe refresh.
    // Keep validation separate from marking success: a failed scan/layout must
    // not leave the cache looking current.
    internal sealed class CaptureRevisionCache
    {
        private bool valid;
        private long revision;

        internal bool NeedsRefresh(long currentRevision)
        {
            return currentRevision < 0 || !valid || currentRevision != revision;
        }

        internal void Refreshed(long currentRevision)
        {
            valid = currentRevision >= 0;
            revision = currentRevision;
        }
    }

    internal static class CameraSamplingPolicy
    {
        // The caller opts into intermediate-frame sampling. Known feedback and
        // custom-FPS scenes retain every camera pass even when it is requested.
        internal static bool ShouldRender(bool outputSample, bool customFrameRate, bool feedback)
        {
            return outputSample || customFrameRate || feedback;
        }
    }
}
