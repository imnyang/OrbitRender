namespace TUFReplayRenderer.Replay;

public readonly struct RecordedKeyEvent
{
  public RecordedKeyEvent(long timeUs, string key, bool down, long sequence)
  { TimeUs = timeUs; Key = key; Down = down; Sequence = sequence; }
  public long TimeUs { get; }
  public string Key { get; }
  public bool Down { get; }
  public long Sequence { get; }
}

public readonly struct RecordedHitEvent
{
  public RecordedHitEvent(long timeUs, int floorId, double angle, float overloadCounter, bool noFailHit,
    bool isAuto, bool nextFloorAuto, double cachedAngle, double targetExitAngle,
    bool midspinInfiniteMargin, bool rdcAuto, int freeRoamSection, string margin)
  {
    TimeUs = timeUs; FloorId = floorId; Angle = angle; OverloadCounter = overloadCounter;
    NoFailHit = noFailHit; IsAuto = isAuto; NextFloorAuto = nextFloorAuto; CachedAngle = cachedAngle;
    TargetExitAngle = targetExitAngle; MidspinInfiniteMargin = midspinInfiniteMargin;
    RdcAuto = rdcAuto; FreeRoamSection = freeRoamSection; Margin = margin;
  }
  public long TimeUs { get; }
  public int FloorId { get; }
  public double Angle { get; }
  public float OverloadCounter { get; }
  public bool NoFailHit { get; }
  public bool IsAuto { get; }
  public bool NextFloorAuto { get; }
  public double CachedAngle { get; }
  public double TargetExitAngle { get; }
  public bool MidspinInfiniteMargin { get; }
  public bool RdcAuto { get; }
  public int FreeRoamSection { get; }
  public string Margin { get; }
}
