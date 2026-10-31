using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TUFReplayRenderer.Contracts;

public sealed class RecordingManifest
{
  [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion { get; set; }
  [JsonProperty("recordingId", Required = Required.Always)] public string RecordingId { get; set; }
  [JsonProperty("level", Required = Required.Always)] public LevelManifest Level { get; set; }
  [JsonProperty("replay", Required = Required.Always)] public ReplayManifest Replay { get; set; }
  [JsonProperty("inputsFile", Required = Required.Always)] public string InputsFile { get; set; }
  [JsonProperty("hitsFile", Required = Required.Always)] public string HitsFile { get; set; }
  [JsonProperty("media")] public JObject Media { get; set; }
  [JsonProperty("warnings")] public string[] Warnings { get; set; } = Array.Empty<string>();

  public void Validate()
  {
    if (SchemaVersion != 1) throw new RecordingFormatException("render_recording_version_unsupported", "This recording bundle version is not supported. Export it with a compatible recorder.", "schemaVersion");
    if (string.IsNullOrWhiteSpace(RecordingId) || RecordingId.Length > 256)
      throw new RecordingFormatException("render_metadata_field_invalid", "The recording ID is empty or too long. Export the recording again.", "recordingId");
    if (Level == null || string.IsNullOrWhiteSpace(Level.Path)) throw new RecordingFormatException("render_metadata_field_missing", "The recording is missing its level file path. Restore the original level and export again.", "level.path");
    Level.Validate();
    if (Replay == null) throw new RecordingFormatException("render_metadata_field_missing", "The recording is missing its replay settings. Export it with a compatible recorder.", "replay");
    if (string.IsNullOrWhiteSpace(InputsFile) || string.IsNullOrWhiteSpace(HitsFile))
      throw new RecordingFormatException("render_metadata_field_missing", "The recording is missing its input or judgment file path. Export it again.", string.IsNullOrWhiteSpace(InputsFile) ? "inputsFile" : "hitsFile");
    Replay.Validate();
  }
}

public sealed class LevelManifest
{
  [JsonProperty("path", Required = Required.Always)] public string Path { get; set; }
  [JsonProperty("fileSha256")] public string FileSha256 { get; set; }
  public void Validate()
  {
    if (FileSha256 == null) return;
    if (FileSha256.Length != 64) throw InvalidFingerprint();
    foreach (char value in FileSha256)
      if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F')))
        throw InvalidFingerprint();
  }
  private static RecordingFormatException InvalidFingerprint() => new("level_fingerprint_invalid", "The recorded level fingerprint is invalid. Export the recording again.", "level.fileSha256");
}

public sealed class ReplayManifest
{
  [JsonProperty("gameplayStartSongPosition", Required = Required.Always)] public double GameplayStartSongPosition { get; set; }
  [JsonProperty("effectivePitch", Required = Required.Always)] public double EffectivePitch { get; set; }
  [JsonProperty("gameInputOffsetMs", Required = Required.Always)] public double GameInputOffsetMs { get; set; }
  [JsonProperty("noFailMode", Required = Required.Always)] public bool NoFailMode { get; set; }
  [JsonProperty("judgmentSystem", Required = Required.Always)] public string JudgmentSystem { get; set; }
  [JsonProperty("judgmentDifficulty")] public string JudgmentDifficulty { get; set; }
  [JsonProperty("startTile")] public int StartTile { get; set; }
  [JsonProperty("result")] public string Result { get; set; }
  [JsonProperty("wonTimeUs")] public long? WonTimeUs { get; set; }
  [JsonProperty("terminalTimeUs", Required = Required.Always)] public long TerminalTimeUs { get; set; }

  public void Validate()
  {
    if (StartTile < 0) throw new RecordingFormatException("render_start_tile_invalid", "The recording has a negative start tile. Record a new run before rendering.", "startTile");
    if (Result != null && Result != "cleared" && Result != "failed" && Result != "aborted") throw Invalid("result");
    if (Result == "failed" && WonTimeUs.HasValue) throw Invalid("result");
    if (!IsFinite(GameplayStartSongPosition)) throw Invalid("gameplayStartSongPosition");
    if (!IsFinite(EffectivePitch) || EffectivePitch <= 0) throw Invalid("effectivePitch");
    if (!IsFinite(GameInputOffsetMs)) throw Invalid("gameInputOffsetMs");
    if (string.IsNullOrWhiteSpace(JudgmentSystem) || JudgmentSystem.Length > 128)
      throw new RecordingFormatException("render_metadata_field_missing", "The recording is missing its judgment system. Record a new run with the current recorder.", "judgmentSystem");
    if (JudgmentSystem != "ModernClassic" && JudgmentSystem != "ModernCompetitive" && JudgmentSystem != "Legacy")
      throw new RecordingFormatException("render_judgment_system_unsupported", "The recording uses an unsupported judgment system. Use a compatible recorder and game version.", "judgmentSystem");
    if (TerminalTimeUs < 0 || (WonTimeUs.HasValue && (WonTimeUs.Value < 0 || WonTimeUs.Value > TerminalTimeUs)))
      throw Invalid(TerminalTimeUs < 0 ? "terminalTimeUs" : "wonTimeUs");
  }

  private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
  private static RecordingFormatException Invalid(string field) => new("render_metadata_field_invalid", "A recording setting has an invalid value. Export the recording again or record a new run.", field);
}

public sealed class RecordingFormatException : Exception
{
  public string Code { get; }
  public string Field { get; }
  public int? Line { get; }
  public string File { get; }
  public RecordingFormatException(string message) : this("render_recording_invalid", message) { }
  public RecordingFormatException(string message, Exception innerException) : this("render_recording_invalid", message, innerException: innerException) { }
  public RecordingFormatException(string code, string message, string field = null, int? line = null, string file = null, Exception innerException = null) : base(message, innerException)
  { Code = code; Field = field; Line = line; File = file; }
}
