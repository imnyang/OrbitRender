using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OrbitRender.Renderer
{
    internal static partial class RenderErrorCodes
    {
        internal const string LevelPathMissing = "render_level_path_missing";
        internal const string LevelPathInvalid = "render_level_path_invalid";
        internal const string OptionsInvalid = "render_options_invalid";
        internal const string RequestInvalid = "render_request_invalid";
        internal const string HostInvalid = "render_host_invalid";
        internal const string LevelFingerprintInvalid = "level_fingerprint_invalid";
        internal const string StartTileInvalid = "render_start_tile_invalid";
        internal const string MediaFileInvalid = "render_media_file_invalid";
        internal const string MediaFileMissing = "render_media_file_missing";
        internal const string MediaFileUnreadable = "render_media_file_unreadable";
    }

    /// <summary>A refused render request with the structured fields RenderApi returns.</summary>
    internal sealed class RenderRequestException : Exception
    {
        internal RenderRequestException(string code, string message, string field = null, string file = null,
            Exception innerException = null) : base(message, innerException)
        {
            Code = code;
            Field = field;
            File = file;
        }

        internal string Code { get; }
        internal string Field { get; }
        internal string File { get; }
    }

    /// <summary>
    /// The request JSON that TUFReplay passes to <c>RenderApi.StartHostedReplay</c>.
    /// OrbitRender reads only what it needs to open the level, name the output
    /// and compose recorded media; the replay itself belongs to the host.
    /// </summary>
    internal sealed class HostedReplayRequest
    {
        private const string InvalidCode = RenderErrorCodes.RequestInvalid;

        private HostedReplayRequest() { }

        internal string LevelPath { get; private set; }
        /// <summary>Used only for the output file name. Null when the host did not send one.</summary>
        internal string LevelFileSha256 { get; private set; }
        internal int StartTile { get; private set; }
        /// <summary>Used only as the output audio delay.</summary>
        internal double GameInputOffsetMs { get; private set; }
        /// <summary>Used only for the media timeline.</summary>
        internal long? WonTimeUs { get; private set; }
        /// <summary>Same shape as the recording's media block, with absolute paths. Null when absent.</summary>
        internal JObject Media { get; private set; }
        internal string[] Warnings { get; private set; } = Array.Empty<string>();
        /// <summary>Render options in the RPC payload shape. Null when absent.</summary>
        internal JObject Options { get; private set; }

        internal bool HasMedia => MediaEntry("webcam") != null || MediaEntry("microphone") != null;

        internal JObject MediaEntry(string kind) => Media?[kind] as JObject;

        /// <summary>The validated absolute path of one media entry, or null when it is absent.</summary>
        internal string MediaPath(string kind)
        {
            var entry = MediaEntry(kind);
            return entry == null ? null : Path.GetFullPath((string)entry["path"]);
        }

        internal static HostedReplayRequest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new RenderRequestException(InvalidCode, "The replay request is empty.", "requestJson");
            JObject data;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    var token = JToken.ReadFrom(reader);
                    data = token as JObject;
                    if (data == null || reader.Read())
                        throw new RenderRequestException(InvalidCode, "The replay request must be one JSON object.", "requestJson");
                }
            }
            catch (JsonException exception)
            {
                throw new RenderRequestException(InvalidCode, "The replay request is not valid JSON: " + exception.Message,
                    "requestJson", innerException: exception);
            }

            var request = new HostedReplayRequest
            {
                LevelPath = ReadLevelPath(data),
                LevelFileSha256 = ReadLevelHash(data),
                StartTile = ReadStartTile(data),
                GameInputOffsetMs = ReadFinite(data, "gameInputOffsetMs"),
                WonTimeUs = ReadWonTime(data),
                Warnings = ReadWarnings(data),
                Options = ReadOptions(data),
            };
            request.Media = ReadMedia(data);
            request.ValidateMedia();
            return request;
        }

        /// <summary>Checks that every selected media file is still a readable, non-empty file.</summary>
        internal void ValidateMedia()
        {
            ValidateMediaFile("webcam");
            ValidateMediaFile("microphone");
        }

        private void ValidateMediaFile(string kind)
        {
            var entry = MediaEntry(kind);
            if (entry == null) return;
            var field = "media." + kind + ".path";
            var path = entry["path"]?.Type == JTokenType.String ? (string)entry["path"] : null;
            if (string.IsNullOrWhiteSpace(path) || !IsAbsolute(path)) throw InvalidMedia(field, path);
            try { path = Path.GetFullPath(path); }
            catch (Exception exception) when (IsPathError(exception)) { throw InvalidMedia(field, path); }
            if (Directory.Exists(path)) throw InvalidMedia(field, path);
            try
            {
                using (var stream = System.IO.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length == 0) throw InvalidMedia(field, path);
                    stream.ReadByte();
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException)
            {
                throw new RenderRequestException(RenderErrorCodes.MediaFileMissing,
                    "A selected recorded media file is missing. Export the recording again or disable that media before rendering.",
                    field, path, exception);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new RenderRequestException(RenderErrorCodes.MediaFileUnreadable,
                    "A selected recorded media file could not be read. Check its permission and drive connection, or disable that media before rendering.",
                    field, path, exception);
            }
        }

        private static string ReadLevelPath(JObject data)
        {
            var token = data["levelPath"];
            if (token == null || token.Type == JTokenType.Null
                || (token.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)token)))
                throw new RenderRequestException(RenderErrorCodes.LevelPathMissing,
                    "The replay request is missing levelPath.", "levelPath");
            if (token.Type != JTokenType.String)
                throw new RenderRequestException(RenderErrorCodes.LevelPathInvalid,
                    "levelPath must be an absolute level file path.", "levelPath");
            var path = ((string)token).Trim();
            if (!IsAbsolute(path))
                throw new RenderRequestException(RenderErrorCodes.LevelPathInvalid,
                    "levelPath must be an absolute level file path.", "levelPath", path);
            try { return Path.GetFullPath(path); }
            catch (Exception exception) when (IsPathError(exception))
            {
                throw new RenderRequestException(RenderErrorCodes.LevelPathInvalid,
                    "Invalid levelPath: " + exception.Message, "levelPath", path, exception);
            }
        }

        private static string ReadLevelHash(JObject data)
        {
            var token = data["levelFileSha256"];
            if (token == null || token.Type == JTokenType.Null) return null;
            var value = token.Type == JTokenType.String ? (string)token : null;
            if (value == null || value.Length != 64 || !IsHex(value))
                throw new RenderRequestException(RenderErrorCodes.LevelFingerprintInvalid,
                    "levelFileSha256 must be a 64-character hexadecimal SHA-256.", "levelFileSha256");
            return value;
        }

        private static int ReadStartTile(JObject data)
        {
            var token = data["startTile"];
            if (token == null || token.Type == JTokenType.Null) return 0;
            if (token.Type != JTokenType.Integer)
                throw new RenderRequestException(InvalidCode, "startTile must be a whole number.", "startTile");
            long value = (long)token;
            if (value < 0)
                throw new RenderRequestException(RenderErrorCodes.StartTileInvalid, "startTile must not be negative.", "startTile");
            if (value > int.MaxValue)
                throw new RenderRequestException(InvalidCode, "startTile is too large.", "startTile");
            return (int)value;
        }

        private static double ReadFinite(JObject data, string name)
        {
            var token = data[name];
            if (token == null || token.Type == JTokenType.Null)
                throw new RenderRequestException(InvalidCode, "The replay request is missing " + name + ".", name);
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new RenderRequestException(InvalidCode, name + " must be a number.", name);
            double value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new RenderRequestException(InvalidCode, name + " must be a finite number.", name);
            return value;
        }

        private static long? ReadWonTime(JObject data)
        {
            var token = data["wonTimeUs"];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Integer)
                throw new RenderRequestException(InvalidCode, "wonTimeUs must be a whole number of microseconds or null.", "wonTimeUs");
            long value;
            try { value = (long)token; }
            catch (OverflowException exception)
            {
                throw new RenderRequestException(InvalidCode, "wonTimeUs is out of range.", "wonTimeUs", innerException: exception);
            }
            if (value < 0)
                throw new RenderRequestException(InvalidCode, "wonTimeUs must not be negative.", "wonTimeUs");
            return value;
        }

        private static string[] ReadWarnings(JObject data)
        {
            var token = data["warnings"];
            if (token == null || token.Type == JTokenType.Null) return Array.Empty<string>();
            if (!(token is JArray list))
                throw new RenderRequestException(InvalidCode, "warnings must be an array of strings.", "warnings");
            var result = new List<string>();
            foreach (var item in list)
            {
                if (item.Type == JTokenType.Null) continue;
                if (item.Type != JTokenType.String)
                    throw new RenderRequestException(InvalidCode, "warnings must be an array of strings.", "warnings");
                var text = (string)item;
                if (!string.IsNullOrEmpty(text)) result.Add(text);
            }
            return result.ToArray();
        }

        private static JObject ReadOptions(JObject data)
        {
            var token = data["options"];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JObject options))
                throw new RenderRequestException(RenderErrorCodes.OptionsInvalid, "options must be a JSON object.", "options");
            return options;
        }

        private static JObject ReadMedia(JObject data)
        {
            var token = data["media"];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JObject media))
                throw new RenderRequestException(InvalidCode, "media must be a JSON object.", "media");
            foreach (var kind in new[] { "webcam", "microphone" })
            {
                var entry = media[kind];
                if (entry == null || entry.Type == JTokenType.Null) continue;
                if (!(entry is JObject))
                    throw InvalidMedia("media." + kind + ".path", null);
            }
            return media;
        }

        private static RenderRequestException InvalidMedia(string field, string file) =>
            new RenderRequestException(RenderErrorCodes.MediaFileInvalid,
                "A selected recorded media file must be an absolute path to a non-empty file. Export the recording again or disable that media before rendering.",
                field, file);

        private static bool IsAbsolute(string path)
        {
            try
            {
                if (!Path.IsPathRooted(path)) return false;
                // On Windows a rooted path can still be drive- or root-relative ("C:a", "\\a").
                if (Path.DirectorySeparatorChar != '\\') return true;
                var root = Path.GetPathRoot(path);
                return !string.IsNullOrEmpty(root) && (root.StartsWith(@"\\", StringComparison.Ordinal)
                    || (root.Length >= 3 && root[1] == ':'));
            }
            catch (ArgumentException) { return false; }
        }

        private static bool IsPathError(Exception exception) =>
            exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException;

        private static bool IsHex(string value)
        {
            foreach (var c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }
    }
}
