using System;
using OrbitRender.UI;

internal static class UiLayoutTests
{
    internal static void Run()
    {
        Check(1920, 1080, 96, 1, 760, 680);
        Check(1280, 720, 96, 1, 760, 680);
        Check(800, 600, 96, 1, 760, 564);
        Check(640, 480, 96, 1, 604, 444);
        Check(2560, 1440, 96, 4f / 3f, 760, 680);
        Check(3840, 2160, 96, 2, 760, 680);
        Check(2560, 1440, 144, 1.5f, 760, 680);
        Check(3840, 2160, 384, 2, 760, 680);

        // Extra horizontal pixels must not enlarge text on an ultrawide monitor.
        Check(3840, 1080, 96, 1, 760, 680);
        Check(1920, 1080, 0, 1, 760, 680);
        Check(1920, 1080, 72, 1, 760, 680);

        var highDpi = UiLayout.ExportDialog(1920, 1080, 192);
        Near(highDpi.Height * highDpi.Scale, 1044, "High-DPI dialog lost its vertical margin.");
        var portrait = UiLayout.ExportDialog(1080, 1920, 96);
        Near(portrait.Width * portrait.Scale, 1044, "Portrait dialog lost its horizontal margin.");

        // The player rectangle, including expanded mode, must stay 16:9 at
        // different window ratios and leave space for controls and the footer.
        foreach (var screen in new[] { (1280f, 720f), (1920f, 1080f), (2560f, 1440f),
            (3840f, 2160f), (2560f, 1080f), (800f, 600f), (1080f, 1920f) })
        {
            var scale = Math.Max(.1f, Math.Min(screen.Item2 / 900f, screen.Item1 / 1280f));
            var width = screen.Item1 / scale;
            var height = screen.Item2 / scale;
            foreach (var expanded in new[] { false, true })
            {
                var player = UiLayout.CompletionPlayer(width, height, expanded);
                Near(player.Width / player.VideoHeight, 16f / 9f, "Completion viewport lost its 16:9 ratio.");
                var sidebar = expanded ? 0 : UiLayout.CompletionSidebarWidth;
                var footer = expanded ? 0 : UiLayout.CompletionFooterHeight;
                var controls = expanded ? 0 : UiLayout.CompletionControlsHeight;
                if (player.Left < sidebar - .01f
                    || player.Top < -.01f
                    || player.Left + player.Width > width + .01f
                    || player.Top + player.VideoHeight + controls
                        > height - footer + .01f)
                    throw new Exception("Completion player overlaps sidebar, controls or footer.");
                if (player.Width < width - sidebar - .01f
                    && player.VideoHeight < height - footer - controls - .01f)
                    throw new Exception("Completion video does not fill the available width or height.");
                if (expanded && screen.Item1 / screen.Item2 == 16f / 9f)
                {
                    Near(player.Width, width, "Fullscreen controls reserve space beside the video.");
                    Near(player.VideoHeight, height, "Fullscreen controls reserve space below the video.");
                }
            }
        }

        foreach (var width in new[] { 640, 800, 1280, 1920, 2560, 3840 })
            foreach (var height in new[] { 360, 480, 600, 720, 1080, 1440, 2160 })
                foreach (var dpi in new[] { 0, 96, 144, 192, 384 })
                {
                    var layout = UiLayout.ExportDialog(width, height, dpi);
                    if (layout.Scale < 1 || layout.Scale > 2
                        || layout.Width * layout.Scale > width - 36 + .01f
                        || layout.Height * layout.Scale > height - 36 + .01f)
                        throw new Exception($"Dialog does not fit {width}x{height} at {dpi} DPI.");
                }
    }

    private static void Check(float width, float height, float dpi,
        float scale, float dialogWidth, float dialogHeight)
    {
        var layout = UiLayout.ExportDialog(width, height, dpi);
        Near(layout.Scale, scale, $"Unexpected scale at {width}x{height}, {dpi} DPI.");
        Near(layout.Width, dialogWidth, "Dialog width changed.");
        Near(layout.Height, dialogHeight, "Dialog height changed.");
    }

    private static void Near(float actual, float expected, string message)
    {
        if (Math.Abs(actual - expected) > .01f)
            throw new Exception(message + $" Expected {expected}, got {actual}.");
    }
}
