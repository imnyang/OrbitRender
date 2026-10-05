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
        internal const float CompletionControlsHeight = 52f;
        internal const float CompletionSidebarWidth = 248f;
        internal const float CompletionFooterHeight = 56f;

        // Size the viewport itself to 16:9; fitting the video texture alone
        // leaves a differently shaped player with unnecessary black space.
        internal static CompletionPlayerLayout CompletionPlayer(float width, float height, bool expanded)
        {
            var left = expanded ? 0f : CompletionSidebarWidth;
            var bottom = expanded ? 0f : CompletionFooterHeight;
            var availableWidth = Math.Max(1f, width - left);
            var availableHeight = Math.Max(1f, height - bottom - (expanded ? 0 : CompletionControlsHeight));
            var videoWidth = Math.Min(availableWidth, availableHeight * 16f / 9f);
            var videoHeight = videoWidth * 9f / 16f;
            return new CompletionPlayerLayout(left + (availableWidth - videoWidth) / 2f,
                expanded ? (availableHeight - videoHeight) / 2f : 0f, videoWidth, videoHeight);
        }

        internal readonly struct CompletionPlayerLayout
        {
            internal readonly float Left, Top, Width, VideoHeight;

            internal CompletionPlayerLayout(float left, float top, float width, float videoHeight)
            {
                Left = left; Top = top; Width = width; VideoHeight = videoHeight;
            }
        }

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
