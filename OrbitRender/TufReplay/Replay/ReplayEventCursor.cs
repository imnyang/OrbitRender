using System;

namespace TUFReplayRenderer.Replay;

public sealed class ReplayEventCursor
{
  private readonly RecordedKeyEvent[] inputs;
  private readonly RecordedHitEvent[] hits;
  private int inputIndex;
  private int hitIndex;
  private long previousTimeUs = long.MinValue;

  public ReplayEventCursor(RecordedKeyEvent[] inputs, RecordedHitEvent[] hits)
  {
    this.inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    this.hits = hits ?? throw new ArgumentNullException(nameof(hits));
  }

  public int InputsConsumed => inputIndex;
  public int HitsConsumed => hitIndex;
  public bool AllHitsConsumed => hitIndex == hits.Length;

  // Countdown keys drive visual input before the player can accept gameplay hits.
  public void AdvanceInputsTo(long timeUs, Action<RecordedKeyEvent> input)
  {
    if (timeUs < previousTimeUs) throw new InvalidOperationException("An export timeline cannot move backwards.");
    previousTimeUs = timeUs;
    while (inputIndex < inputs.Length && inputs[inputIndex].TimeUs <= timeUs) {
      input(inputs[inputIndex]);
      inputIndex++;
    }
  }

  public void AdvanceTo(long timeUs, Action<RecordedKeyEvent> input, Action<RecordedHitEvent> hit)
  {
    if (timeUs < previousTimeUs) throw new InvalidOperationException("An export timeline cannot move backwards.");
    previousTimeUs = timeUs;
    while (true)
    {
      bool hasInput = inputIndex < inputs.Length && inputs[inputIndex].TimeUs <= timeUs;
      bool hasHit = hitIndex < hits.Length && hits[hitIndex].TimeUs <= timeUs;
      if (!hasInput && !hasHit) return;
      if (hasInput && (!hasHit || inputs[inputIndex].TimeUs <= hits[hitIndex].TimeUs))
      {
        input(inputs[inputIndex]);
        inputIndex++;
      }
      else
      {
        hit(hits[hitIndex]);
        hitIndex++;
      }
    }
  }
}
