using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TUFReplayRenderer.Replay;

namespace TUFReplayRenderer.Contracts;

public sealed class RecordingBundle
{
  private RecordingBundle(string path, RecordingManifest manifest, RecordedKeyEvent[] inputs, RecordedHitEvent[] hits)
  { ManifestPath = path; Manifest = manifest; Inputs = inputs; Hits = hits; }
  public string ManifestPath { get; }
  public string DirectoryPath => Path.GetDirectoryName(ManifestPath);
  public RecordingManifest Manifest { get; }
  public RecordedKeyEvent[] Inputs { get; }
  public RecordedHitEvent[] Hits { get; }

  public static RecordingBundle Load(string manifestPath)
  {
    if (string.IsNullOrWhiteSpace(manifestPath)) throw new RecordingFormatException("render_manifest_missing", "The recording manifest is missing. Export the recording again before rendering.", "manifestPath", file: "manifest.json");
    string fullPath;
    try { fullPath = Path.GetFullPath(manifestPath); }
    catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException) {
      throw new RecordingFormatException("render_file_path_invalid", "The recording manifest path is invalid. Export the recording again.", "manifestPath", file: "manifest.json", innerException: exception);
    }
    RecordingManifest manifest;
    try {
      JObject data = JObject.Parse(ReadManifest(fullPath));
      foreach (string field in RequiredFields)
        if (data.SelectToken(field) is not JToken token || token.Type == JTokenType.Null)
          throw new RecordingFormatException("render_metadata_field_missing", "A required recording setting is missing. Export the recording again with the current recorder.", field, file: "manifest.json");
      manifest = data.ToObject<RecordingManifest>();
    }
    catch (JsonException exception) {
      throw new RecordingFormatException("render_metadata_invalid", "The recording manifest contains invalid data. Export the recording again.", (exception as JsonSerializationException)?.Path, file: "manifest.json", innerException: exception);
    }
    if (manifest == null) throw new RecordingFormatException("render_metadata_missing", "The recording manifest is empty. Export the recording again.", file: "manifest.json");
    try { manifest.Validate(); }
    catch (RecordingFormatException exception) { throw WithFile(exception, "manifest.json"); }
    string directory = Path.GetDirectoryName(fullPath);
    RecordedKeyEvent[] inputs;
    RecordedHitEvent[] hits;
    inputs = ReadCsv(directory, manifest.InputsFile, "inputsFile", "render_input_file_missing", RecordingCsvReader.ReadInputs);
    hits = ReadCsv(directory, manifest.HitsFile, "hitsFile", "render_hit_file_missing", RecordingCsvReader.ReadHits);
    if ((inputs.Length > 0 && inputs[inputs.Length - 1].TimeUs > manifest.Replay.TerminalTimeUs)
      || (hits.Length > 0 && hits[hits.Length - 1].TimeUs > manifest.Replay.TerminalTimeUs))
      throw new RecordingFormatException("render_event_after_terminal", "The recording contains events after its end time. Export it again or record a new run.", "timeUs");
    return new RecordingBundle(fullPath, manifest, inputs, hits);
  }
  public string ResolveFile(string relativePath) => ResolveBundleFile(DirectoryPath, relativePath);
  public string ResolveLevelPath()
  {
    try { return Path.IsPathRooted(Manifest.Level.Path) ? Path.GetFullPath(Manifest.Level.Path) : ResolveBundleFile(DirectoryPath, Manifest.Level.Path, "level.path"); }
    catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException) {
      throw new RecordingFormatException("level_file_invalid", "The recorded level path is invalid. Restore the original level and export the recording again.", "level.path", file: Manifest.Level.Path, innerException: exception);
    }
  }
  public void ValidateLevelHash()
  {
    string path = ResolveLevelPath();
    try {
      using var stream = File.OpenRead(path);
      if (Manifest.Level.FileSha256 == null) return;
      using var hash = SHA256.Create();
      string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
      if (!string.Equals(actual, Manifest.Level.FileSha256, StringComparison.OrdinalIgnoreCase))
        throw new RecordingFormatException("level_gameplay_modified", "The level has changed since this recording was made. Restore the recorded level version and render again.", "level.fileSha256", file: Manifest.Level.Path);
    }
    catch (Exception exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException) {
      throw new RecordingFormatException("level_file_missing", "The recorded level file is missing. Restore the original level file and try again.", "level.path", file: Manifest.Level.Path, innerException: exception);
    }
    catch (Exception exception) when (exception is UnauthorizedAccessException || exception is IOException) {
      throw new RecordingFormatException("level_file_unreadable", "The recorded level could not be read. Check its permissions and whether the drive is connected, then try again.", "level.path", file: Manifest.Level.Path, innerException: exception);
    }
  }
  private static string ReadManifest(string path)
  {
    try {
      if (new FileInfo(path).Length > 1_048_576)
        throw new RecordingFormatException("render_manifest_too_large", "The recording manifest exceeds the supported size. Export it again with a compatible recorder.", file: "manifest.json");
      return File.ReadAllText(path);
    }
    catch (Exception exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException) {
      throw new RecordingFormatException("render_manifest_missing", "The recording manifest is missing. Export the recording again before rendering.", "manifestPath", file: "manifest.json", innerException: exception);
    }
    catch (Exception exception) when (exception is UnauthorizedAccessException || exception is IOException) {
      throw new RecordingFormatException("render_manifest_unreadable", "The recording manifest could not be read. Check its permissions and whether the drive is connected, then export it again.", "manifestPath", file: "manifest.json", innerException: exception);
    }
  }
  private static T[] ReadCsv<T>(string directory, string relativePath, string field, string missingCode, Func<TextReader, T[]> read)
  {
    try {
      using var reader = new StreamReader(ResolveBundleFile(directory, relativePath, field));
      return read(reader);
    }
    catch (RecordingFormatException exception) { throw WithFile(exception, relativePath); }
    catch (Exception exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException) {
      throw new RecordingFormatException(missingCode, "A recorded input or judgment file is missing. Export the recording again before rendering.", field, file: relativePath, innerException: exception);
    }
    catch (Exception exception) when (exception is UnauthorizedAccessException || exception is IOException) {
      throw new RecordingFormatException("render_recording_file_unreadable", "A recording data file could not be read. Check its permissions and whether the drive is connected, then export it again.", field, file: relativePath, innerException: exception);
    }
  }
  private static RecordingFormatException WithFile(RecordingFormatException exception, string file) =>
    new(exception.Code, exception.Message, exception.Field, exception.Line, exception.File ?? file, exception);

  private static string ResolveBundleFile(string directory, string relativePath, string field = null)
  {
    if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
      throw new RecordingFormatException("render_file_path_unsafe", "A recording data file must be inside its bundle folder. Export the recording again.", field, file: relativePath);
    string path;
    try { path = Path.GetFullPath(Path.Combine(directory, relativePath)); }
    catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException) {
      throw new RecordingFormatException("render_file_path_invalid", "A recording data file path is invalid. Export the recording again.", field, file: relativePath, innerException: exception);
    }
    string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!path.StartsWith(prefix, StringComparison.Ordinal)) throw new RecordingFormatException("render_file_path_unsafe", "A recording data file points outside its bundle folder. Export the recording again.", field, file: relativePath);
    return path;
  }
  private static readonly string[] RequiredFields = {
    "schemaVersion", "recordingId", "level.path", "replay.gameplayStartSongPosition", "replay.effectivePitch",
    "replay.gameInputOffsetMs", "replay.noFailMode", "replay.judgmentSystem", "replay.terminalTimeUs", "inputsFile", "hitsFile",
  };
}
