namespace OrbitRender.Renderer
{
    internal static class ReplaySettlePolicy
    {
        // A stable legacy fence alone cannot certify a host's Unity queue or
        // presentation work. Both the explicit host guarantee and fence are required.
        internal static bool CanSkipRefresh(bool enabled, bool hostCompleted,
            bool settledBefore, bool settledAfter, long revisionBefore, long revisionAfter)
            => enabled && hostCompleted && settledBefore && settledAfter && revisionBefore == revisionAfter;
    }
}
