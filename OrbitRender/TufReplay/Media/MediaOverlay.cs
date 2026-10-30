using Newtonsoft.Json.Linq;

namespace TUFReplayRenderer.Media;

public sealed class MediaOverlay
{
    public string Path { get; }
    public JObject Layout { get; }
    public MediaOverlay(string path, JObject layout) { Path = path; Layout = layout; }
}
