using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OrbitRender.Renderer;
using TUFReplayRenderer.Contracts;
using TUFReplayRenderer.Media;
using UnityEngine;

namespace TUFReplayRenderer.Replay;

public sealed class RecordedReplayDriver : IRenderReplayDriver, IRenderOverlaySynchronization, IDisposable
{
  private readonly RecordingBundle bundle;
  private readonly ReplayEventCursor cursor;
  private readonly double endDelaySeconds;
  private readonly Dictionary<string, KeyCode> keyCodes = new(StringComparer.Ordinal);
  private readonly Dictionary<string, HitMargin> margins = new(StringComparer.Ordinal);
  private readonly HashSet<KeyCode> heldKeys = new();
  private SharedOverlayInput overlays;
  public bool OverlayWorkSettled => OptionalModClock.Settled;
  public long OverlayWorkRevision => OptionalModClock.WorkRevision;
  public string OverlayPendingWork => OptionalModClock.PendingWork;
  public void OverlayRefreshFrameCompleted() => overlays?.BeforeFrame();
  private RenderReplayContext context;
  private GameSettingsSnapshot settings;
  private RenderTimeline timeline;
  private bool prepared;
  private bool begun;
  private bool ended;
  private bool won;
  private bool terminalReached;
  internal bool TerminalReached => terminalReached;
  private bool deathAnimationCompleted;
  private long? deathStartedVideoTimeUs;
  private RecordedHitEvent? activeHit;
  private long currentVideoTimeUs;
  private static readonly FieldInfo RawSongPosition = AccessTools.Field(typeof(scrConductor), "_songposition_minusi");
  private static readonly FieldInfo FreeroamUpTime = AccessTools.Field(typeof(scrController), "freeroamUpTime");

  public RecordedReplayDriver(RecordingBundle bundle, double gameplayRate = 1, double endDelaySeconds = 2)
  {
    this.bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
    if (double.IsNaN(gameplayRate) || double.IsInfinity(gameplayRate) || gameplayRate <= 0)
      throw new ArgumentOutOfRangeException(nameof(gameplayRate));
    PlaybackRateMultiplier = gameplayRate;
    if (double.IsNaN(endDelaySeconds) || double.IsInfinity(endDelaySeconds) || endDelaySeconds < 0 || endDelaySeconds > 30)
      throw new ArgumentOutOfRangeException(nameof(endDelaySeconds));
    this.endDelaySeconds = endDelaySeconds;
    GameplayRate = bundle.Manifest.Replay.EffectivePitch * gameplayRate;
    cursor = new ReplayEventCursor(bundle.Inputs, bundle.Hits);
    foreach (RecordedKeyEvent input in bundle.Inputs)
    {
      if (keyCodes.ContainsKey(input.Key)) continue;
      if (!Enum.TryParse(input.Key, false, out KeyCode code) || !Enum.IsDefined(typeof(KeyCode), code) || code == KeyCode.None)
        throw new RecordingFormatException("render_input_key_unsupported", $"The recording uses an unsupported key: {input.Key}.", "key");
      keyCodes.Add(input.Key, code);
    }
    foreach (RecordedHitEvent hit in bundle.Hits)
    {
      if (margins.ContainsKey(hit.Margin)) continue;
      if (!Enum.TryParse(hit.Margin, false, out HitMargin margin) || !Enum.IsDefined(typeof(HitMargin), margin))
        throw new RecordingFormatException("render_hit_judgment_unsupported", $"The recording uses an unsupported judgment: {hit.Margin}.", "margin");
      margins.Add(hit.Margin, margin);
    }
  }

  public double GameplayRate { get; }
  public double PlaybackRateMultiplier { get; }
  internal bool CompetitiveMode => bundle.Manifest.Replay.JudgmentSystem == "ModernCompetitive";
  internal float InputCalibrationSeconds => (float)(bundle.Manifest.Replay.GameInputOffsetMs / 1000d);
  public long? GameplayStartVideoTimeUs { get; private set; }
  public long CurrentReplayTimeUs { get; private set; }
  public long CurrentVideoTimeUs => currentVideoTimeUs;
  public long CurrentInputSequence { get; private set; } = -1;
  public bool ApplyingRecordedHit => activeHit.HasValue;
  internal bool ApplyingTerminalDeath { get; private set; }
  internal void DeathStarted()
  {
    if (ADOBase.controller?.playerOne?.alive == false && !deathStartedVideoTimeUs.HasValue)
      deathStartedVideoTimeUs = currentVideoTimeUs;
  }
  internal void DeathAnimationCompleted() => deathAnimationCompleted = true;
  internal bool ApplyingInputEvent { get; private set; }
  internal bool ApplyingFreeroam { get; private set; }
  internal RecordedHitEvent? ActiveHit => activeHit;
  internal HitMargin ActiveMargin => margins[activeHit.Value.Margin];
  internal bool HasHeldKey(KeyCode code) => heldKeys.Contains(code);
  internal bool HasHeldKeys => heldKeys.Count != 0;
  public event Action<RecordedKeyEvent> InputEvent;
  public event Action<string> CompatibilityWarning;

  public void PrepareBeforeRender()
  {
    if (prepared) return;
    if (scrController.coopMode) throw new RecordingFormatException("This recording format supports one player. Turn off co-op before rendering.");
    if (ADOBase.controller == null || ADOBase.conductor == null || ADOBase.conductor.song == null)
      throw new InvalidOperationException("The level is not ready to render. Open the recorded level and try again.");
    settings = new GameSettingsSnapshot();
    ReplayHooks.Activate(this);
    overlays = new SharedOverlayInput(this);
    overlays.Begin();


    prepared = true;
    ApplyPreparedSettings();
  }

  internal void ApplyPreparedSettings()
  {
    if (!prepared) return;
    float pitch = checked((float)GameplayRate);
    if (float.IsInfinity(pitch) || pitch <= 0) throw new RecordingFormatException("The requested playback speed is outside the game's supported range.");
    scrConductor conductor = ADOBase.conductor;
    if (conductor.song != null) conductor.song.pitch = pitch;
    if (conductor.song2 != null) conductor.song2.pitch = pitch;
    if (conductor.song3 != null) conductor.song3.pitch = pitch;
    if (scnEditor.instance != null) scnEditor.instance.playbackSpeed = (float)PlaybackRateMultiplier;
    ADOBase.controller.noFail = bundle.Manifest.Replay.NoFailMode;
    if (!string.IsNullOrEmpty(bundle.Manifest.Replay.JudgmentDifficulty))
    {
      if (!Enum.TryParse(bundle.Manifest.Replay.JudgmentDifficulty, out Difficulty difficulty)
        || !Enum.IsDefined(typeof(Difficulty), difficulty))
        throw new RecordingFormatException("The recording uses an unsupported judgment difficulty.");
      GCS.difficulty = difficulty;
    }
    RDC.auto = false;
  }

  public void Begin(RenderReplayContext renderContext)
  {
    if (begun || ended) throw new InvalidOperationException("The recorded render driver has already been used.");
    if (!prepared) throw new InvalidOperationException("PrepareBeforeRender must run before Orbit schedules its audio.");
    context = renderContext ?? throw new ArgumentNullException(nameof(renderContext));
    begun = true;
    // Orbit has already scheduled music and anchored conductor.songposition_minusi to frame zero.
    double initialReplayUs = (ADOBase.conductor.songposition_minusi - bundle.Manifest.Replay.GameplayStartSongPosition) * 1_000_000d;
    long initialOriginUs = checked(-(long)Math.Round(initialReplayUs / GameplayRate));
    timeline = new RenderTimeline(initialOriginUs, GameplayRate, bundle.Manifest.Replay.WonTimeUs);
    ReplayHitErrorMeter.Activate(this);

    // Input-event effects deduplicate by the original game frame, which v1 does not store.
    if (HasInputEventEffects(ADOBase.controller))
      CompatibilityWarning?.Invoke("Custom input-event effects need runtime verification; this recording does not contain the original game frame IDs.");
  }

  public void BeforeSimulationFrame(RenderReplayFrame frame)
  {
    currentVideoTimeUs = ToMicroseconds(frame.TimeSeconds);
    if (context.IsCancellationRequested) return;
    overlays?.BeforeFrame();

  }

  public void AfterSimulationFrame(RenderReplayFrame frame)
  {
    if (ended || context.IsCancellationRequested) return;
    currentVideoTimeUs = ToMicroseconds(frame.TimeSeconds);
    scrController controller = ADOBase.controller;
    scrConductor conductor = ADOBase.conductor;
    if (controller == null || conductor == null) throw new InvalidOperationException("The game left the recorded level while rendering.");
    if (!GameplayStartVideoTimeUs.HasValue)
    {
      if (controller.state != States.PlayerControl) {
        CurrentReplayTimeUs = checked((long)Math.Floor(timeline.OutputToReplay(currentVideoTimeUs)));
        cursor.AdvanceInputsTo(CurrentReplayTimeUs, ApplyInput);
        return;
      }
      double recordingUs = (conductor.songposition_minusi - bundle.Manifest.Replay.GameplayStartSongPosition) * 1_000_000d;
      GameplayStartVideoTimeUs = checked(currentVideoTimeUs - (long)Math.Round(recordingUs / GameplayRate));
      timeline = new RenderTimeline(GameplayStartVideoTimeUs.Value, GameplayRate, bundle.Manifest.Replay.WonTimeUs);
      // Leave room for the native death callback; its actual completion below
      // sets the final end time. A missing callback must fail instead of cutting
      // away the death animation or silently rendering indefinitely.
      context.SetEndTime(ReplayToVideoTimeUs(bundle.Manifest.Replay.TerminalTimeUs) / 1_000_000d
        + endDelaySeconds + (bundle.Manifest.Replay.Result == "failed" ? 10 : 0) + 1d / context.VideoFps);
    }
    long elapsedVideoUs = currentVideoTimeUs - GameplayStartVideoTimeUs.Value;
    long timeUs = VideoElapsedToReplayTime(elapsedVideoUs);
    CurrentReplayTimeUs = timeUs;
    cursor.AdvanceTo(timeUs, ApplyInput, ApplyHit);
    if (controller.state == States.PlayerControl)
    {
      ApplyingFreeroam = true;
      try { controller.UpdateFreeroam(); }
      finally { ApplyingFreeroam = false; }
    }
    if (!won && bundle.Manifest.Replay.WonTimeUs.HasValue && timeUs >= bundle.Manifest.Replay.WonTimeUs.Value)
    {
      won = true;
      if (controller.state != States.Won)
      {
        using (SongPositionScope(bundle.Manifest.Replay.WonTimeUs.Value))
          controller.OnLandOnPortal(controller.chosenPlanet, Portal.EndOfLevel, null);
      }
    }
    if (timeUs >= bundle.Manifest.Replay.TerminalTimeUs)
    {
      if (!terminalReached)
      {
        if (!cursor.AllHitsConsumed) throw new InvalidOperationException("The render ended before all recorded judgments were applied.");
        terminalReached = true;
        if (bundle.Manifest.Replay.Result == "failed" && controller.playerOne.alive)
        {
          // Missed/late inputs need not have an accepted Hit row. The recorded
          // outcome is authoritative, including failures on safe/no-fail tiles.
          ApplyingTerminalDeath = true;
          try
          {
            using (SongPositionScope(bundle.Manifest.Replay.TerminalTimeUs))
              controller.playerOne.Die(hitbox: true);
          }
          finally { ApplyingTerminalDeath = false; }
          DeathStarted();
        }
      }
      if (bundle.Manifest.Replay.Result != "failed" || deathAnimationCompleted)
        context.RequestStop(endDelaySeconds);
      else if (!deathStartedVideoTimeUs.HasValue || currentVideoTimeUs - deathStartedVideoTimeUs.Value > 5_000_000)
        throw new InvalidOperationException("The game's death animation did not finish. Check the installed gameplay mods and try rendering again.");
    }
  }

  public long ReplayToVideoTimeUs(long replayTimeUs)
  {
    if (timeline == null) throw new InvalidOperationException("The render timeline has not started yet.");
    return checked((long)Math.Round(timeline.ReplayToOutput(replayTimeUs)));
  }

  private long VideoElapsedToReplayTime(long elapsedVideoUs)
  {
    return checked((long)Math.Floor(timeline.OutputToReplay(elapsedVideoUs + GameplayStartVideoTimeUs.Value)));
  }

  private void ApplyInput(RecordedKeyEvent input)
  {
    KeyCode code = keyCodes[input.Key];
    if (input.Down) heldKeys.Add(code); else heldKeys.Remove(code);
    CurrentInputSequence = input.Sequence;
    long outputTime = currentVideoTimeUs;
    try
    {
      ApplyingInputEvent = true;
      currentVideoTimeUs = ReplayToVideoTimeUs(input.TimeUs);
      overlays?.Apply(input, code);
      InputEvent?.Invoke(input);
    }
    finally { ApplyingInputEvent = false; currentVideoTimeUs = outputTime; }
  }

  private void ApplyHit(RecordedHitEvent hit)
  {
    scrController controller = ADOBase.controller;
    scrPlayer player = controller.playerOne;
    using (SongPositionScope(hit.TimeUs))
    {
      SynchronizeFreeroam(hit.FreeRoamSection);
      scrPlanet planet = player.planetarySystem.chosenPlanet;
      if (planet == null || planet.currfloor == null || planet.currfloor.seqID != hit.FloorId)
        throw new InvalidOperationException("The recorded level no longer matches tile " + hit.FloorId + ". Check that the original level and required mods are installed.");
      bool previousAuto = RDC.auto;
      bool previousInfinite = controller.noFailInfiniteMargin;
      HitMarginLimit previousLimit = GCS.hitMarginLimit;
      scrFloor nextFloor = planet.currfloor.nextfloor;
      bool previousNextAuto = nextFloor != null && nextFloor.auto;
      try
      {
        activeHit = hit;
        // Accepted recorded hits must not inherit live multipress penalties.
        // Match normal replay playback before restoring the recorded hit state.
        player.consecMultipressCounter = 0;
        controller.multipressPenalty = false;
        RDC.auto = hit.RdcAuto;
        controller.noFailInfiniteMargin = hit.NoFailHit;
        // The resolved accepted hit is authoritative; a user's current perfect-only preference is unrelated.
        GCS.hitMarginLimit = HitMarginLimit.None;
        player.midspinInfiniteMargin = hit.MidspinInfiniteMargin;
        if (player.failBar != null) player.failBar.overloadCounter = hit.OverloadCounter;
        // Angle is the signed judgment offset; CachedAngle stores the planet's
        // absolute angle captured immediately before the original hit.
        planet.angle = hit.CachedAngle;
        planet.cachedAngle = hit.CachedAngle;
        planet.SetTargetExitAngle(hit.TargetExitAngle);
        if (nextFloor != null) nextFloor.auto = hit.NextFloorAuto;
        if (planet.currfloor.holdLength > -1 && planet.currfloor.holdRenderer != null)
          planet.currfloor.holdRenderer.Hit();
        player.Hit(null, hit.IsAuto);
      }
      finally
      {
        activeHit = null;
        RDC.auto = previousAuto;
        controller.noFailInfiniteMargin = previousInfinite;
        GCS.hitMarginLimit = previousLimit;
        if (nextFloor != null) nextFloor.auto = previousNextAuto;
      }
    }
    player.planetarySystem.chosenPlanet?.Update_RefreshAngles();
  }

  private void SynchronizeFreeroam(int expectedSection)
  {
    scrController controller = ADOBase.controller;
    if (controller.curFreeRoamSection > expectedSection)
      throw new InvalidOperationException("The freeroam state no longer matches the recording.");
    while (controller.curFreeRoamSection < expectedSection)
    {
      int previousSection = controller.curFreeRoamSection;
      ApplyingFreeroam = true;
      try
      {
        if (FreeroamUpTime == null) throw new MissingFieldException(typeof(scrController).FullName, "freeroamUpTime");
        FreeroamUpTime.SetValue(controller, Math.Max((float)FreeroamUpTime.GetValue(controller), 0.10001f));
        controller.UpdateFreeroam();
      }
      finally { ApplyingFreeroam = false; }
      if (controller.curFreeRoamSection == previousSection)
        throw new InvalidOperationException("The recorded freeroam transition could not be applied. Check the original level version.");
    }
  }

  private IDisposable SongPositionScope(long replayTimeUs)
  {
    scrConductor conductor = ADOBase.conductor;
    double songPosition = bundle.Manifest.Replay.GameplayStartSongPosition
      + replayTimeUs / 1_000_000d;
    if (RawSongPosition == null) throw new MissingFieldException(typeof(scrConductor).FullName, "_songposition_minusi");
    double raw = (double)RawSongPosition.GetValue(conductor);
    double correction = raw - conductor.songposition_minusi;
    RawSongPosition.SetValue(conductor, songPosition + correction);
    return new RestoreAction(() => RawSongPosition.SetValue(conductor, raw));
  }

  public void End(RenderReplayContext renderContext, RenderState finalState) => Dispose();
  public void Dispose()
  {
    if (ended) return;
    ended = true;
    try { overlays?.Dispose(); }
    finally {
      heldKeys.Clear();
      try { ReplayHitErrorMeter.Deactivate(this); }
      finally { ReplayHooks.Deactivate(this); }
    }
  }

  public void RestoreAfterRender()
  {
    Dispose();
    settings?.Restore();
  }

  private static long ToMicroseconds(double seconds) => checked((long)Math.Round(seconds * 1_000_000d));
  private static bool HasInputEventEffects(scrController controller)
  {
    if (controller.inputEventFfx == null) return false;
    foreach (var row in controller.inputEventFfx)
      if (row != null) foreach (var effect in row) if (effect != null) return true;
    return false;
  }
  private sealed class RestoreAction : IDisposable
  {
    private Action action;
    public RestoreAction(Action action) { this.action = action; }
    public void Dispose() { Action restore = action; action = null; restore?.Invoke(); }
  }

  private sealed class GameSettingsSnapshot
  {
    private readonly bool auto = RDC.auto;
    private readonly scrController controller = ADOBase.controller;
    private readonly bool noFail = ADOBase.controller.noFail;
    private readonly bool noFailInfinite = ADOBase.controller.noFailInfiniteMargin;
    private readonly Difficulty difficulty = GCS.difficulty;
    private readonly scrConductor conductor = ADOBase.conductor;
    private readonly float pitch = ADOBase.conductor.song.pitch;
    private readonly float pitch2 = ADOBase.conductor.song2 != null ? ADOBase.conductor.song2.pitch : 1f;
    private readonly float pitch3 = ADOBase.conductor.song3 != null ? ADOBase.conductor.song3.pitch : 1f;
    private readonly scnEditor editor = scnEditor.instance;
    private readonly float editorSpeed = scnEditor.instance != null ? scnEditor.instance.playbackSpeed : 1f;
    public void Restore()
    {
      RDC.auto = auto;
      GCS.difficulty = difficulty;
      if (controller != null) { controller.noFail = noFail; controller.noFailInfiniteMargin = noFailInfinite; }
      if (conductor != null)
      {
        if (conductor.song != null) conductor.song.pitch = pitch;
        if (conductor.song2 != null) conductor.song2.pitch = pitch2;
        if (conductor.song3 != null) conductor.song3.pitch = pitch3;
      }
      if (editor != null) editor.playbackSpeed = editorSpeed;
    }
  }
}
