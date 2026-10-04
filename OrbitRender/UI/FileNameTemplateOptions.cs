using System.Linq;

namespace OrbitRender.UI
{
    internal static class FileNameTemplateOptions
    {
        internal static readonly string[] Tokens = { "{level}", "{artist}", "{date:yyyyMMdd}", "{time:HHmmss}", "{id}",
            "{width}", "{height}", "{videoFps}", "{ingameFps}", "{bitrate}", "{codec}", "{bitDepth}", "{bgaMode}",
            "{level|replace:\"/\",\"_\"|truncate:40}", "{artist|default:\"Unknown\"}", "{if:bgaMode,\"BGA\",\"Gameplay\"}" };

        internal static string[] Labels => Tokens.Take(13).Concat(new[] {
            Localization.Get("filename-insert-transform"), Localization.Get("filename-insert-default"),
            Localization.Get("filename-insert-condition") }).ToArray();
    }
}
