using System;

namespace OrbitRender.UI
{
    // Logical pixel sizes shared with the original IMGUI presentation.
    internal static class UiLayout
    {
        internal const int LabelFontSize = 12;
        internal const int TitleFontSize = 17;
        internal const float ExportWidth = 760f;
        internal const float ExportHeight = 680f;
        internal const float ExportMargin = 18f;

        internal static DialogLayout ExportDialog(float screenWidth, float screenHeight, float dpi)
        {
            var availableWidth = Math.Max(1f, screenWidth - ExportMargin * 2f);
            var availableHeight = Math.Max(1f, screenHeight - ExportMargin * 2f);
            var resolutionScale = Math.Max(1f, screenHeight / 1080f);
            var dpiScale = dpi > 0f ? Math.Max(1f, dpi / 96f) : 1f;
            var fitScale = Math.Min(availableWidth / ExportWidth, availableHeight / ExportHeight);
            var scale = Math.Min(Math.Min(Math.Max(resolutionScale, dpiScale), 2f),
                Math.Max(1f, fitScale));
            return new DialogLayout(scale, Math.Min(ExportWidth, availableWidth / scale),
                Math.Min(ExportHeight, availableHeight / scale));
        }

        internal readonly struct DialogLayout
        {
            internal readonly float Scale, Width, Height;

            internal DialogLayout(float scale, float width, float height)
            {
                Scale = scale;
                Width = width;
                Height = height;
            }
        }
    }
}
