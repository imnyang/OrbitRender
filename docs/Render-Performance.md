# Render performance diagnostics

Enable **Save render stage timings** in the export dialog's Advanced tab or OrbitRender's UMM Advanced settings. Hosted callers can pass `"diagnosticTiming": true` in `options`. The default is off. Settings, queued requests and user presets preserve the choice; explicit request `false` overrides saved `true`.

After completion, cancellation or failure, open `<output video>.performance.txt`. The Unity Mod Manager log prints its exact path as `[Render] Performance diagnostics: ...`. If scene preparation fails before an output name exists, the report goes to a uniquely named file in the system temporary directory. No report is uploaded. The measured scope starts at render preparation and ends after cleanup; level loading, encoder availability/preflight and user confirmation before that scope are excluded.

The off path creates no diagnostics object, clocks, histograms, log entries or per-frame strings. It adds only null guards around the optional measurements. Existing timeout and summary timers remain in use.

## Reading a report

- `wall/video-second` is the elapsed render scope divided by encoded duration. Lower is faster. Cancelled or failed runs may include work that never became an encoded frame.
- `Simulation` includes the normal Unity frame and coroutine wait. `Settle` covers extra frozen-clock Unity frames; counters distinguish simulated frames, settle frames, initial fence readiness and refreshes skipped by an optional host.
- Host before/after simulation, before/after capture and completion callbacks include provider work performed inside the host. OrbitRender cannot identify individual providers; provider IDs and internal IPC/upload/layout timers require TUFReplay/mod instrumentation.
- Camera stages measure OrbitRender's manual CPU submission time, not GPU execution time. Automatic and game custom-FPS passes require the game's profiler or a frame debugger. `CanvasCapture` includes output composition, while layout and hierarchy stages expose their nested work.
- Readback issue, wait, copy and latency are separate. Latencies overlap other frames. Encoder buffer wait indicates pool back-pressure; pipe writes include FFmpeg consumption stalls. Queue p50/p95/max describe observed occupancy, not frame duration.
- Audio stages separate sample query/render/copy/peak scan/WAV write/pipe write/drain and realtime fallback pacing. Metadata records the capture mode and sample count. Webcam composition and final mux are separate finalization stages.
- Stage durations overlap, especially inclusive capture/audio stages, readback latency and worker pipe writes. **Do not add stage totals to obtain job wall time.** p50/p95 values are power-of-two microsecond upper bounds, not exact percentiles. Zero counts mean unused or unavailable stages.

Timers use a non-inlined monotonic `Stopwatch.GetTimestamp` helper in OrbitRender's assembly, which is excluded from OptionalModClock rewriting. Replay time and Unity time do not drive the diagnostic clock. Fixed arrays collect measurements in memory; the report is written once after drain/cleanup.

## Optional render behavior

The following switches are off by default in settings and available as optional booleans in hosted request JSON:

| Setting / request option | Behavior and limits |
| --- | --- |
| `SettleFastPath` / `settleFastPath` | May remove the stability refresh only when a host implements the completion contract below. Removing a Unity update can change RNG/timing, so compare judgments, frames and audio before using it. Older hosts retain the original settle loop even when the switch is on. |
| `SampleGameplayAtOutputFrames` / `sampleGameplayAtOutputFrames` | Keeps every simulation tick and host callback, but omits intermediate manual gameplay camera passes. Known Hall of Mirrors and custom-FPS scenes retain all passes. Other temporal shaders, render callbacks and feedback may still change: disable it for those scenes unless validated. At 240/60 FPS, eligible camera passes can approach a 75% reduction; that is a call-count bound, not a measured whole-render speedup. |

Both switches are exposed in Advanced with an experimental warning and are preserved in user presets and queued renders. Neither changes default FPS, resolution, codec, bitrate or preset. UI and RPC progress refresh at up to 10 Hz, with state changes immediate; cancellation processing and preview texture updates remain independent.

## Optional host capabilities

RenderApi 3's 13 existing required members are unchanged. All optional members bind once by exact public instance signature. Missing or mismatched optional members are ignored.

`bool TryCompleteFrame(long frameIndex, double videoSeconds)` is called at the end of the normal Unity frame, after `AfterSimulationFrame`, only with `settleFastPath` enabled and the legacy fence settled. It must synchronously finish all current-frame provider work, background-to-Unity handoffs and required presentation updates before returning `true`. A `true` result guarantees no additional Unity frame is needed to expose that frame's inputs/presentation. Providers that need future Update/LateUpdate/coroutine work must cause `false`; do not treat an empty queue alone as proof. OrbitRender also requires the legacy fence to remain settled with the same revision after the callback. A changed fence or `false` result falls back to the existing settle loop, and callback exceptions fail the render. No callback is made when the setting is off.

`long CaptureHierarchyRevision { get; }` may return a nonnegative generation only if it covers **every capture-relevant canvas, including game HUD**, every descendant insertion/removal/reparent, activation change and layer mutation from all Unity callbacks and mods. Increment/change it whenever any such state changes. OrbitRender caches the transform list and layer assignments per root at that revision. Unchanged revision skips the scan; absent/negative revision performs the original full discovery using reusable lists, including inactive descendants. Switching back to negative invalidates prior cached state.

`long CaptureLayoutRevision { get; }` similarly certifies all capture-relevant text, geometry and layout state. It is read after `OnBeforeCapture`. An unchanged nonnegative revision allows reuse of the last successful forced canvas update; changed, absent or negative revisions force the final layout flush. The guarantee includes side effects of `Canvas.preWillRenderCanvases` and `Canvas.willRenderCanvases`: if those callbacks need to run even with unchanged geometry, return `-1` instead of certifying reuse. Layout callbacks that mutate hierarchy must also change the hierarchy revision. If the host cannot observe all game/mod changes, omit these capabilities or return `-1`. They are correctness guarantees, not approximate dirty hints.

TUFReplay dev `87382e9` supplies none of these optional members and continues to work unchanged. Its counterpart would need a provider completion fence and comprehensive revision aggregation; providers should explicitly opt into synchronous completion instead of being assumed synchronous.

## Encoding choices

Webcam composition preserves the selected codec-specific preset, bitrate/rate-control, profile and pixel format. Microphone-only composition still copies video. The webcam remains a separate final pass: the installed host's gameplay epoch is known only during rendering, and final duration is known at the end. Folding it into the initial encode requires a validated streaming timeline/EOF contract before it can preserve all offsets and post-clear behavior.

HEVC Apple VideoToolbox is an explicit macOS option after a successful real encode probe, including the selected bit depth. It uses native VideoToolbox options and `p010le` for 10-bit input; software fallback uses `yuv420p10le`. Unavailable encoders use the existing fallback confirmation. HEVC Auto selection remains unchanged. H.264 VideoToolbox is not added because this machine's report measured it slower than x264. ProRes support is unchanged.

## Compare in the game

Render a fixed recording with the same settings, first with behavior switches off, then change only one switch. Use a simple map, an overlay/key-burst map and a decoration/feedback/custom-FPS map. Separate a cold run from at least three warmed runs. Compare whole-scope wall/video-second, stage counts and queue waits rather than summing overlapping timers.

Before accepting a change, compare lossless decoded frame hashes, frame count and PTS, judgments/event order, audio sample count and waveform alignment, and webcam/microphone offsets. Check long renders and pitch/BPM changes for drift, dynamic key-rain creation/removal for missed layers, and cancel/failure cleanup. Hardware codec comparisons also need text/gradient/alpha visual review and actual bitrate/file-size measurements; equal target bitrate does not imply equal quality. Windows, D3D, Metal readback and VideoToolbox inside Unity require separate game tests.
