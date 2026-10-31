using System;
using System.IO;
using Newtonsoft.Json.Linq;
using TUFReplayRenderer.Contracts;

namespace TUFReplayRenderer.Media;

internal static class SelectedMediaFiles
{
    internal static void Validate(RecordingBundle bundle, JObject options)
    {
        ValidateOne("webcam", "includeWebcam");
        ValidateOne("microphone", "includeMicrophone");
        void ValidateOne(string kind, string option)
        {
            if ((bool?)options?[option] == false) return;
            JToken token = bundle.Manifest.Media?[kind];
            if (token == null || token.Type == JTokenType.Null) return;
            string field = "media." + kind + ".path";
            if (token is not JObject media || media["path"]?.Type != JTokenType.String)
                throw Invalid(field, null);
            string relative = (string)media["path"];
            string path;
            try { path = bundle.ResolveFile(relative); }
            catch (RecordingFormatException) { throw Invalid(field, relative); }
            if (Directory.Exists(path)) throw Invalid(field, relative);
            try {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length == 0) throw Invalid(field, relative);
                stream.ReadByte();
            }
            catch (Exception error) when (error is FileNotFoundException || error is DirectoryNotFoundException) {
                throw new RecordingFormatException("render_media_file_missing", "A selected recorded media file is missing. Export the recording again or disable that media before rendering.", field, file: relative);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) {
                throw new RecordingFormatException("render_media_file_unreadable", "A selected recorded media file could not be read. Check its permission and drive connection, or disable that media before rendering.", field, file: relative);
            }
        }
    }

    private static RecordingFormatException Invalid(string field, string file) => new("render_media_file_invalid",
        "A selected recorded media file has an invalid path or is empty. Export the recording again or disable that media before rendering.", field, file: file);
}
