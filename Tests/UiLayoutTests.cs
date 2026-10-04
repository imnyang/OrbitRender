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
