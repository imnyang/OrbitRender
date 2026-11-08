using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TUFReplayRenderer.Media;

/// <summary>A rectangle in pixels, origin at the top left.</summary>
public readonly struct MediaRect
{
    public readonly int X, Y, Width, Height;
    public MediaRect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
}

/// <summary>
/// A video the replay host made, such as a key viewer, laid over the rendered video. Its
/// time 0 is output frame zero. <see cref="Source"/> is the part of each overlay frame to
/// use and <see cref="Target"/> where it goes in the rendered video.
/// </summary>
public sealed class MediaOverlay
{
    public string Path { get; }
    public MediaRect Source { get; }
    public MediaRect Target { get; }

    public MediaOverlay(string path, MediaRect source, MediaRect target)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Source = source;
        Target = target;
    }

    /// <summary>
    /// Reads the host's overlay list: <c>[{"path", "source":{x,y,width,height}, "target":{…}}]</c>.
    /// An entry without an existing file or with a non-positive size is left out and
    /// <paramref name="skip"/> says so. Anything FFmpeg still can't use fails the composition,
    /// which then runs again without the overlays. Null or blank JSON is an empty list.
    /// </summary>
    public static List<MediaOverlay> ParseList(string json, Action<string> skip)
    {
        var overlays = new List<MediaOverlay>();
        if (string.IsNullOrWhiteSpace(json)) return overlays;
        JArray entries;
        try { entries = JArray.Parse(json); }
        catch (JsonException exception)
        {
            skip?.Invoke("The overlay video list is not a JSON array: " + exception.Message);
            return overlays;
        }
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] as JObject;
            string path = entry?["path"]?.Type == JTokenType.String ? (string)entry["path"] : null;
            MediaRect? source = Rect(entry?["source"]), target = Rect(entry?["target"]);
            if (path == null || !File.Exists(path) || source == null || target == null)
                skip?.Invoke("Overlay video " + index + " left out: it needs an existing file and positive source and target sizes.");
            else overlays.Add(new MediaOverlay(path, source.Value, target.Value));
        }
        return overlays;
    }

    private static MediaRect? Rect(JToken token)
    {
        if (!(token is JObject rect)) return null;
        try
        {
            var value = new MediaRect((int)rect["x"], (int)rect["y"], (int)rect["width"], (int)rect["height"]);
            return value.Width > 0 && value.Height > 0 ? value : (MediaRect?)null;
        }
        catch (Exception exception) when (exception is ArgumentException || exception is FormatException
            || exception is InvalidCastException || exception is OverflowException)
        {
            return null;
        }
    }
}
