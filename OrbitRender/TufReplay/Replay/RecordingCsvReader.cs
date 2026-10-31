using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TUFReplayRenderer.Contracts;

namespace TUFReplayRenderer.Replay;

public static class RecordingCsvReader
{
  public const string InputsHeader = "timeUs,key,down,sequence";
  public const string HitsHeader = "timeUs,floorId,angle,overloadCounter,noFailHit,isAuto,nextFloorAuto,cachedAngle,targetExitAngle,midspinInfiniteMargin,rdcAuto,freeRoamSection,margin";
  private const int MaximumEvents = 5_000_000;

  public static RecordedKeyEvent[] ReadInputs(TextReader reader) => ReadInputEvents(reader).ToArray();

  // The caller owns the reader. Enumeration validates each row before yielding it.
  public static IEnumerable<RecordedKeyEvent> ReadInputEvents(TextReader reader)
  {
    ReadHeader(reader, InputsHeader);
    var names = new Dictionary<string, string>(StringComparer.Ordinal);
    int count = 0;
    RecordedKeyEvent previous = default;
    int lineNumber = 1;
    string line;
    while ((line = reader.ReadLine()) != null)
    {
      lineNumber++;
      if (string.IsNullOrWhiteSpace(line)) continue;
      string[] row = Split(line, 4, lineNumber);
      long timeUs = Integer(row[0], lineNumber, "timeUs");
      if (!IsSymbol(row[1])) Fail(lineNumber, "The key must be a symbolic Unity key name.", "render_input_key_unsupported", "key");
      if (!names.TryGetValue(row[1], out string key)) names.Add(row[1], key = row[1]);
      bool down = Flag(row[2], lineNumber, "down");
      long sequence = Integer(row[3], lineNumber, "sequence");
      if (sequence < 0) Fail(lineNumber, "The input sequence must be non-negative.", field: "sequence");
      if (count > 0)
      {
        if (timeUs < previous.TimeUs || (timeUs == previous.TimeUs && sequence <= previous.Sequence))
          Fail(lineNumber, "Inputs must be ordered by time and a unique sequence at each timestamp.", "render_timeline_out_of_order", "timeUs");
      }
      if (count == MaximumEvents) Fail(lineNumber, "The recording has too many input events.");
      previous = new RecordedKeyEvent(timeUs, key, down, sequence);
      count++;
      yield return previous;
    }
  }

  public static RecordedHitEvent[] ReadHits(TextReader reader)
  {
    ReadHeader(reader, HitsHeader);
    var result = new List<RecordedHitEvent>();
    int lineNumber = 1;
    string line;
    while ((line = reader.ReadLine()) != null)
    {
      lineNumber++;
      if (string.IsNullOrWhiteSpace(line)) continue;
      string[] row = Split(line, 13, lineNumber);
      long timeUs = Integer(row[0], lineNumber, "timeUs");
      int floorId = SmallInteger(row[1], lineNumber, "floorId");
      double overload = Number(row[3], lineNumber, "overloadCounter");
      int freeRoamSection = SmallInteger(row[11], lineNumber, "freeRoamSection");
      if (floorId < 0 || freeRoamSection < 0) Fail(lineNumber, "The recorded tile or freeroam section is invalid.", field: floorId < 0 ? "floorId" : "freeRoamSection");
      if (overload < 0 || overload > float.MaxValue) Fail(lineNumber, "The overload counter is invalid.", field: "overloadCounter");
      if (!IsSymbol(row[12])) Fail(lineNumber, "This recording is missing a resolved judgment. Export it again from a compatible recorder.", "render_hit_judgment_unsupported", "margin");
      if (result.Count > 0 && timeUs < result[result.Count - 1].TimeUs) Fail(lineNumber, "Judgment events must be ordered by time.", "render_timeline_out_of_order", "timeUs");
      if (result.Count == MaximumEvents) Fail(lineNumber, "The recording has too many judgment events.");
      result.Add(new RecordedHitEvent(timeUs, floorId, Number(row[2], lineNumber, "angle"), (float)overload,
        Flag(row[4], lineNumber, "noFailHit"), Flag(row[5], lineNumber, "isAuto"), Flag(row[6], lineNumber, "nextFloorAuto"), Number(row[7], lineNumber, "cachedAngle"),
        Number(row[8], lineNumber, "targetExitAngle"), Flag(row[9], lineNumber, "midspinInfiniteMargin"), Flag(row[10], lineNumber, "rdcAuto"), freeRoamSection, row[12]));
    }
    return result.ToArray();
  }

  private static void ReadHeader(TextReader reader, string expected)
  {
    if (reader == null) throw new ArgumentNullException(nameof(reader));
    string header = reader.ReadLine()?.TrimStart('\uFEFF');
    if (!string.Equals(header, expected, StringComparison.Ordinal))
      throw new RecordingFormatException("render_csv_header_invalid", "The recording CSV header does not match bundle version 1. Export the recording again.", line: 1);
  }
  private static string[] Split(string line, int columns, int lineNumber)
  {
    if (line.Length > 4096) Fail(lineNumber, "The recording row is too long.");
    string[] row = line.Split(',');
    if (row.Length != columns) Fail(lineNumber, "The recording row has the wrong number of columns.", "render_csv_row_invalid");
    return row;
  }
  private static long Integer(string text, int lineNumber, string field)
  {
    if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result))
      Fail(lineNumber, "The recording contains an invalid integer.", field: field);
    return result;
  }
  private static int SmallInteger(string text, int lineNumber, string field)
  {
    long result = Integer(text, lineNumber, field);
    if (result < int.MinValue || result > int.MaxValue) Fail(lineNumber, "The recording integer is out of range.", field: field);
    return (int)result;
  }
  private static double Number(string text, int lineNumber, string field)
  {
    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
      || double.IsNaN(result) || double.IsInfinity(result)) Fail(lineNumber, "The recording contains an invalid angle or counter.", field: field);
    return result;
  }
  private static bool Flag(string text, int lineNumber, string field)
  {
    if (text == "1") return true;
    if (text == "0") return false;
    Fail(lineNumber, "A recording flag must be 0 or 1.", field: field);
    return false;
  }
  private static bool IsSymbol(string value)
  {
    if (string.IsNullOrEmpty(value) || value.Length > 64 || !char.IsLetter(value[0])) return false;
    foreach (char character in value) if (!char.IsLetterOrDigit(character) && character != '_') return false;
    return true;
  }
  private static void Fail(int lineNumber, string message, string code = "render_csv_value_invalid", string field = null) => throw new RecordingFormatException(code, message, field, lineNumber);
}
