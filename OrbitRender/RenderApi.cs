using OrbitRender.Renderer;

namespace OrbitRender
{
    /// <summary>Entry point for other mods. Call from Unity's main thread.</summary>
    public static class RenderApi
    {
        /// <summary>Returns the current renderer, or null when OrbitRender is unavailable.</summary>
        public static RendererController Controller => Main.Enabled ? RendererController.Instance : null;

        /// <summary>
        /// Starts rendering the level currently open in the editor. Returns false if the
        /// renderer is unavailable, busy, or cannot begin preparation. Check Controller.State
        /// and Controller.Message for the subsequent result.
        /// </summary>
        public static bool TryStart(RenderRequestOptions options = null)
        {
            var controller = Controller;
            if (controller == null || controller.Busy) return false;
            controller.StartRender(options?.Snapshot());
            return controller.State == RenderState.Preparing || controller.State == RenderState.Rendering;
        }

        /// <summary>Requests cancellation of the active render.</summary>
        public static bool TryCancel()
        {
            var controller = Controller;
            if (controller == null || !controller.Busy) return false;
            controller.Cancel();
            return true;
        }
    }
}
