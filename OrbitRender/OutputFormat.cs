using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OrbitRender
{
    public enum VideoContainer { Auto, Mp4, Ts, Mkv, Mov }

    internal static class OutputFormat
    {
        internal const string DefaultFileName = "Render_{level}_{date}_{time}_{id}";

        internal static string Extension(VideoContainer container, VideoCodec codec)
        {
            switch (container)
            {
                case VideoContainer.Mp4: return ".mp4";
                case VideoContainer.Ts: return ".ts";
                case VideoContainer.Mkv: return ".mkv";
                case VideoContainer.Mov: return ".mov";
                default: return VideoCodecCatalog.Get(codec).ContainerExtension;
            }
        }

        internal static string MimeType(string extension)
        {
            switch (extension)
            {
                case ".ts": return "video/mp2t";
                case ".mkv": return "video/x-matroska";
                case ".mov": return "video/quicktime";
                case ".webm": return "video/webm";
                default: return "video/mp4";
            }
        }

        internal static string FileName(string format, string level, DateTime now, string id)
            => FileName(format, Variables(Clean(level ?? "Level"), now, id));

        internal static Dictionary<string, object> Variables(string level, DateTime now, string id)
            => new Dictionary<string, object>(StringComparer.Ordinal) {
                { "level", level ?? "Level" }, { "artist", string.Empty },
                { "date", now }, { "time", now }, { "id", id ?? string.Empty }
            };

        internal static string FileName(string format, IDictionary<string, object> variables)
        {
            if (string.IsNullOrWhiteSpace(format)) format = DefaultFileName;
            var value = Clean(FileNameTemplate.Expand(format, variables));
            // Keep room for temporary suffixes and the container extension.
            var length = Math.Min(160, value.Length);
            if (length < value.Length && char.IsHighSurrogate(value[length - 1])) length--;
            return value.Substring(0, length).TrimEnd(' ', '.');
        }

        private static string Clean(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            value = new string(value.Select(c => invalid.Contains(c) || "<>:\"/\\|?*".Contains(c)
                || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
            if (string.IsNullOrEmpty(value)) return "Render";
            var stem = value.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                    && stem[3] >= '0' && stem[3] <= '9')) value = "_" + value;
            return value;
        }

        internal static string UniquePath(string directory, string name, string extension)
        {
            for (var i = 0; ; i++)
            {
                var path = Path.Combine(directory, name + (i == 0 ? "" : "_" + i) + extension);
                if (!File.Exists(path) && !File.Exists(Path.ChangeExtension(path, ".partial" + extension))
                    && !File.Exists(Path.ChangeExtension(path, ".mux" + extension))
                    && !File.Exists(Path.ChangeExtension(path, ".partial.wav"))
                    && !File.Exists(Path.ChangeExtension(path, ".partial.m4a"))) return path;
            }
        }
    }
}
