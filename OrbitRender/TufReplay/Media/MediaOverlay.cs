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
    public override string ToString() => X + "," + Y + " " + Width + "x" + Height;
}

/// <summary>
/// A video the replay host made, such as a key viewer, laid over the rendered video. Its
/// time 0 is output frame zero. <see cref="Source"/> is the part of each overlay frame to
/// use and <see cref="Target"/> where it goes in the rendered video.
/// </summary>
public sealed class MediaOverlay
{
    // A host hands a few videos at most; anything above this is a mistake.
    private const int MaxOverlays = 32;
    private const int MaxSize = 16384;

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
    /// An entry that can't be used is left out and <paramref name="skip"/> says why, so a bad
    /// entry never fails the render. Null or blank JSON is an empty list.
    /// </summary>
    public static List<MediaOverlay> ParseList(string json, Action<string> skip)
    {
        var overlays = new List<MediaOverlay>();
        if (string.IsNullOrWhiteSpace(json)) return overlays;
        JArray entries;
        try { entries = JToken.Parse(json) as JArray; }
        catch (JsonException exception)
        {
            skip?.Invoke("The overlay video list is not valid JSON: " + exception.Message);
            return overlays;
        }
        if (entries == null)
        {
            skip?.Invoke("The overlay video list is not a JSON array.");
            return overlays;
        }
        for (int index = 0; index < entries.Count; index++)
        {
            if (overlays.Count == MaxOverlays)
            {
                skip?.Invoke("Only the first " + MaxOverlays + " overlay videos are used; " + (entries.Count - index) + " left out.");
                break;
            }
            if (TryRead(entries[index] as JObject, out var overlay, out var problem)) overlays.Add(overlay);
            else skip?.Invoke("Overlay video " + index + " left out: " + problem);
        }
        return overlays;
    }

    private static bool TryRead(JObject entry, out MediaOverlay overlay, out string problem)
    {
        overlay = null;
        if (entry == null) { problem = "it is not an object."; return false; }
        string path = entry["path"]?.Type == JTokenType.String ? (string)entry["path"] : null;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
        {
            problem = "its path is not an absolute file path.";
            return false;
        }
        if (!File.Exists(path)) { problem = "the file " + path + " does not exist."; return false; }
        if (!TryRect(entry["source"] as JObject, true, out var source)) { problem = "its source rectangle is invalid."; return false; }
        if (!TryRect(entry["target"] as JObject, false, out var target)) { problem = "its target rectangle is invalid."; return false; }
        overlay = new MediaOverlay(path, source, target);
        problem = null;
        return true;
    }

    // A source starts inside its frame; a target may hang off the video's edges.
    private static bool TryRect(JObject rect, bool inside, out MediaRect value)
    {
        value = default(MediaRect);
        if (rect == null
            || !TryInt(rect["x"], out int x) || !TryInt(rect["y"], out int y)
            || !TryInt(rect["width"], out int width) || !TryInt(rect["height"], out int height))
            return false;
        if (width <= 0 || height <= 0 || width > MaxSize || height > MaxSize) return false;
        if (inside ? x < 0 || y < 0 || x > MaxSize || y > MaxSize : Math.Abs(x) > MaxSize || Math.Abs(y) > MaxSize) return false;
        value = new MediaRect(x, y, width, height);
        return true;
    }

    private static bool TryInt(JToken token, out int value)
    {
        value = 0;
        if (token == null || token.Type != JTokenType.Integer) return false;
        long number;
        try { number = (long)token; }
        catch (OverflowException) { return false; }
        if (number < int.MinValue || number > int.MaxValue) return false;
        value = (int)number;
        return true;
    }
}
