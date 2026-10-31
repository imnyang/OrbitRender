using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using TUFReplayRenderer.Contracts;
using TUFReplayRenderer.Media;
using TUFReplayRenderer.Replay;

namespace TUFReplayRenderer.Tests;

internal static class ParserTests
{
  internal static void Run()
  {
    CultureInfo previous = CultureInfo.CurrentCulture;
    try
    {
      CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
      var inputs = ReadInputs("-2,A,1,0\n-2,A,0,1\n100,KeypadEnter,1,2\n");
      Assert(inputs.Length == 3 && inputs[0].Down && !inputs[1].Down && inputs[2].Key == "KeypadEnter", "input parsing");
      using (var streamReader = new StringReader(RecordingCsvReader.InputsHeader + "\n-2,A,1,0\n-2,A,0,1\nwrong,A,1,2\n"))
      using (var events = RecordingCsvReader.ReadInputEvents(streamReader).GetEnumerator()) {
        Assert(events.MoveNext() && events.Current.TimeUs == -2 && events.Current.Down, "streaming inputs validates one row at a time");
        Assert(events.MoveNext() && !events.Current.Down, "streaming preserves same-time short taps");
        ExactError(() => events.MoveNext(), "render_csv_value_invalid", "timeUs", 4);
      }
      var hits = ReadHits("0,0,1.25,0.5,0,0,0,1.24,1.26,0,0,0,XPerfect\n");
      Assert(hits.Length == 1 && hits[0].Angle == 1.25 && hits[0].OverloadCounter == 0.5f, "invariant hit parsing and fractional overload");
      Reject(() => ReadInputs("0,A,1,2\n0,A,0,2\n"));
      Reject(() => ReadInputs("3,A,1,0\n2,A,0,1\n"));
      Reject(() => ReadInputs("0,A,true,0\n"));
      Reject(() => ReadInputs("0,65,1,0\n"));
      Reject(() => ReadHits("0,0,NaN,0,0,0,0,1,1,0,0,0,XPerfect\n"));
      Reject(() => ReadHits("0,0,1,0,0,0,0,1,1,0,0,0,\n"));
      Reject(() => ReadHits("0,0,1,0,0,0,0,1,1,0,0,0,9\n"));
      ExactError(() => ReadInputs("oops,A,1,0\n"), "render_csv_value_invalid", "timeUs", 2);
      ExactError(() => ReadInputs("0,A,true,0\n"), "render_csv_value_invalid", "down", 2);
      ExactError(() => ReadHits("0,0,1,0,0,0,0,1,1,0,0,0,\n"), "render_hit_judgment_unsupported", "margin", 2);
      var countdownInputs = ReadInputs("-2000000,A,1,0\n-2000000,A,0,1\n-500000,B,1,2\n0,B,0,3\n");
      var countdownHits = ReadHits("-1,0,1,0,0,0,0,1,1,0,0,0,XPerfect\n");
      var countdownCursor = new ReplayEventCursor(countdownInputs, countdownHits);
      var countdownOrder = new List<string>();
      countdownCursor.AdvanceInputsTo(-2000001, value => countdownOrder.Add("key" + value.Sequence));
      Assert(countdownOrder.Count == 0, "countdown keys do not run before their timestamp");
      countdownCursor.AdvanceInputsTo(-2000000, value => countdownOrder.Add("key" + value.Sequence));
      Assert(string.Join(",", countdownOrder) == "key0,key1" && countdownCursor.HitsConsumed == 0, "countdown short pulse is consumed in sequence before gameplay");
      countdownCursor.AdvanceInputsTo(-500000, value => countdownOrder.Add("key" + value.Sequence));
      countdownCursor.AdvanceTo(0, value => countdownOrder.Add("key" + value.Sequence), value => countdownOrder.Add("hit" + value.FloorId));
      Assert(string.Join(",", countdownOrder) == "key0,key1,key2,hit0,key3" && countdownCursor.InputsConsumed == 4, "countdown events are not replayed twice at gameplay start");
      var orderedInputs = ReadInputs("0,A,1,0\n0,A,0,1\n1,B,1,2\n");
      var orderedHits = ReadHits("0,0,1,0,0,0,0,1,1,0,0,0,XPerfect\n1,1,1,0,0,0,0,1,1,0,0,0,Auto\n");
      var cursor = new ReplayEventCursor(orderedInputs, orderedHits);
      var ordering = new List<string>();
      cursor.AdvanceTo(0, value => ordering.Add("key" + value.Sequence), value => ordering.Add("hit" + value.FloorId));
      Assert(string.Join(",", ordering) == "key0,key1,hit0", "same-time ordering keeps every short tap");
      cursor.AdvanceTo(1, value => ordering.Add("key" + value.Sequence), value => ordering.Add("hit" + value.FloorId));
      Assert(cursor.AllHitsConsumed && cursor.InputsConsumed == 3, "all events consumed once");
      try { cursor.AdvanceTo(0, _ => { }, _ => { }); throw new Exception("Backwards seek accepted"); }
      catch (InvalidOperationException) { }
      CheckBundle();
      // Recorded microseconds follow conductor songposition, which already includes original pitch.
      var originalPitch = new RenderTimeline(1_000_000, 1.5, 6_000_000);
      Assert(originalPitch.ReplayToOutput(3_000_000) == 3_000_000, "original pitch is applied exactly once");
      Assert(originalPitch.ReplayToOutput(6_500_000) == 5_500_000, "clear tail returns to rate one");
      Assert(originalPitch.OutputToReplay(5_500_000) == 6_500_000, "clear tail inverse mapping");
      var doubleSpeed = new RenderTimeline(1_000_000, 3, 6_000_000);
      Assert(doubleSpeed.ReplayToOutput(3_000_000) == 2_000_000, "requested speed multiplies original pitch");
      Assert(doubleSpeed.OutputToReplay(500_000) == -1_500_000, "countdown is before recording zero");
    }
    finally { CultureInfo.CurrentCulture = previous; }
  }

  private static void CheckBundle()
  {
    string directory = Path.Combine(Path.GetTempPath(), "renderer-bundle-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
      File.WriteAllText(Path.Combine(directory, "inputs.csv"), RecordingCsvReader.InputsHeader + "\n0,A,1,0\n");
      File.WriteAllText(Path.Combine(directory, "hits.csv"), RecordingCsvReader.HitsHeader + "\n0,0,1,0.5,0,0,0,1,1,0,0,0,XPerfect\n");
      var manifest = new RecordingManifest { SchemaVersion = 1, RecordingId = "test", Level = new LevelManifest { Path = "level.adofai" },
        Replay = new ReplayManifest { GameplayStartSongPosition = -0.25, EffectivePitch = 1.5, GameInputOffsetMs = -30,
          NoFailMode = false, JudgmentSystem = "ModernClassic", WonTimeUs = 5, TerminalTimeUs = 10 }, InputsFile = "inputs.csv", HitsFile = "hits.csv" };
      string path = Path.Combine(directory, "manifest.json");
      ExactError(() => RecordingBundle.Load(path), "render_manifest_missing", "manifestPath", null, "manifest.json");
      File.WriteAllText(path, JsonConvert.SerializeObject(manifest));
      RecordingBundle bundle = RecordingBundle.Load(path);
      Assert(bundle.Manifest.Replay.EffectivePitch == 1.5 && bundle.Hits.Length == 1, "complete bundle");
      ExactError(() => bundle.ValidateLevelHash(), "level_file_missing", "level.path", null, "level.adofai");
      File.Move(Path.Combine(directory, "inputs.csv"), Path.Combine(directory, "inputs.saved"));
      ExactError(() => RecordingBundle.Load(path), "render_input_file_missing", "inputsFile", null, "inputs.csv");
      File.Move(Path.Combine(directory, "inputs.saved"), Path.Combine(directory, "inputs.csv"));
      File.Move(Path.Combine(directory, "hits.csv"), Path.Combine(directory, "hits.saved"));
      ExactError(() => RecordingBundle.Load(path), "render_hit_file_missing", "hitsFile", null, "hits.csv");
      File.Move(Path.Combine(directory, "hits.saved"), Path.Combine(directory, "hits.csv"));
      File.WriteAllText(Path.Combine(directory, "inputs.csv"), RecordingCsvReader.InputsHeader + "\nwrong,A,1,0\n");
      ExactError(() => RecordingBundle.Load(path), "render_csv_value_invalid", "timeUs", 2, "inputs.csv");
      File.WriteAllText(Path.Combine(directory, "inputs.csv"), RecordingCsvReader.InputsHeader + "\n0,A,1,0\n");
      var missing = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
      ((Newtonsoft.Json.Linq.JObject)missing["replay"]).Remove("effectivePitch");
      File.WriteAllText(path, missing.ToString());
      ExactError(() => RecordingBundle.Load(path), "render_metadata_field_missing", "replay.effectivePitch", null);
      File.WriteAllText(path, "{bad json}");
      ExactError(() => RecordingBundle.Load(path), "render_metadata_invalid", null, null);
      File.WriteAllText(path, JsonConvert.SerializeObject(manifest));
      manifest.Replay.StartTile = 10;
      manifest.Validate();
      manifest.Replay.Result = "failed";
      manifest.Replay.WonTimeUs = null;
      File.WriteAllText(Path.Combine(directory, "hits.csv"), RecordingCsvReader.HitsHeader + "\n0,10,1,0.5,0,0,0,1,1,0,0,0,XPerfect\n");
      File.WriteAllText(path, JsonConvert.SerializeObject(manifest));
      var checkpoint = RecordingBundle.Load(path);
      Assert(checkpoint.Manifest.Replay.StartTile == 10 && checkpoint.Hits[0].FloorId == 10, "checkpoint recording loads");
      Assert(checkpoint.Manifest.Replay.Result == "failed", "death outcome survives export");
      File.WriteAllText(Path.Combine(directory, "hits.csv"), RecordingCsvReader.HitsHeader + "\n");
      Assert(RecordingBundle.Load(path).Hits.Length == 0, "failed run without an accepted hit loads");
      File.WriteAllText(Path.Combine(directory, "hits.csv"), RecordingCsvReader.HitsHeader + "\n0,0,1,0.5,0,0,0,1,1,0,0,0,XPerfect\n");
      manifest.Replay.StartTile = -1;
      ExactError(() => manifest.Validate(), "render_start_tile_invalid", "startTile", null);
      manifest.Replay.StartTile = 0;
      manifest.Replay.Result = "unexpected";
      ExactError(() => manifest.Validate(), "render_metadata_field_invalid", "result", null);
      manifest.Replay.Result = "failed";
      manifest.Replay.WonTimeUs = 5;
      ExactError(() => manifest.Validate(), "render_metadata_field_invalid", "result", null);
      manifest.Replay.Result = null; // Older bundles remain readable.
      Reject(() => bundle.ResolveFile("../outside.csv"));
      Reject(() => bundle.ResolveFile(path));
      string level = Path.Combine(directory, "level.adofai");
      File.WriteAllText(level, "{\"angleData\":[0,180]}");
      using (var stream = File.OpenRead(level))
      using (var hash = SHA256.Create()) manifest.Level.FileSha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
      File.WriteAllText(path, JsonConvert.SerializeObject(manifest));
      RecordingBundle.Load(path).ValidateLevelHash();
      File.AppendAllText(level, "changed");
      ExactError(() => RecordingBundle.Load(path).ValidateLevelHash(), "level_gameplay_modified", "level.fileSha256", null, "level.adofai");
      manifest.Level.FileSha256 = "bad";
      Reject(() => manifest.Validate());
      manifest.Level.FileSha256 = null;
      manifest.InputsFile = "../outside.csv";
      File.WriteAllText(path, JsonConvert.SerializeObject(manifest));
      Reject(() => RecordingBundle.Load(path));
      manifest.InputsFile = "inputs.csv";
      manifest.Replay.TerminalTimeUs = 0;
      manifest.Replay.WonTimeUs = null;
      File.WriteAllText(path, JsonConvert.SerializeObject(manifest));
      File.WriteAllText(Path.Combine(directory, "inputs.csv"), RecordingCsvReader.InputsHeader + "\n1,A,1,0\n");
      Reject(() => RecordingBundle.Load(path));
    }
    finally { Directory.Delete(directory, true); }
  }
  private static RecordedKeyEvent[] ReadInputs(string rows) => RecordingCsvReader.ReadInputs(new StringReader(RecordingCsvReader.InputsHeader + "\n" + rows));
  private static RecordedHitEvent[] ReadHits(string rows) => RecordingCsvReader.ReadHits(new StringReader(RecordingCsvReader.HitsHeader + "\n" + rows));
  private static void Reject(Action action)
  {
    try { action(); throw new Exception("Malformed recording accepted"); }
    catch (RecordingFormatException) { }
  }
  private static void ExactError(Action action, string code, string field, int? line, string file = null)
  {
    try { action(); throw new Exception("Invalid recording accepted"); }
    catch (RecordingFormatException exception) {
      Assert(exception.Code == code && exception.Field == field && exception.Line == line, "precise format error survives parsing");
      if (file != null) Assert(exception.File == file, "precise file survives validation");
    }
  }
  private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Failed: " + message); }
}
