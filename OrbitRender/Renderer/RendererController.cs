using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using OrbitRender.Patches;

namespace OrbitRender.Renderer
{
    public enum RenderState { Idle, Preparing, Rendering, Finishing, Completed, Failed, Cancelled, AwaitingConfirmation }

    [DefaultExecutionOrder(32000)]
    public sealed class RendererController : MonoBehaviour
    {
        // Application.targetFrameRate = -1 lets Unity choose the platform's
        // default rate. On desktop that can follow the monitor refresh rate
        // (for example, exactly 200 Hz), which unintentionally caps rendering
        // even when vSync is disabled.
        private const int RenderTargetFrameRate = 6000;
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();
        public static RendererController Instance { get; private set; }
        public static bool ControlsTime => Instance != null && Instance.saved != null &&
            (Instance.State == RenderState.Preparing || Instance.State == RenderState.Rendering);
        // Block game/editor input for every render phase, including encoder
        // preflight and finalization. ControlsTime is intentionally narrower
        // because it only describes simulation ownership.
        internal static bool InputBlocked => (Instance != null && Instance.Busy)
            || OrbitRender.UI.ExportVideoDialog.IsOpen;
        internal static bool ShowHitJudgments => Instance != null && Instance.showHitJudgmentsForRun;
        internal static bool BgaModeActive => Instance != null && Instance.bgaModeForRun
            && (Instance.State == RenderState.Preparing || Instance.State == RenderState.Rendering
                || Instance.State == RenderState.Finishing);
        public RenderState State { get; private set; }
        public RenderClock Clock { get; private set; } = new RenderClock();
        public string Message { get; private set; } = Localization.Get("open-a-custom-level-then-render");
        public string ToastText { get; private set; } = Localization.Get("open-a-custom-level-then-press-f6-to-render");
        public string ProgressPercentText { get; private set; } = "";
        public string ProgressText { get; private set; } = "";
        public string EtaText { get; private set; } = "";
        public string SpeedText { get; private set; } = "";
        public string OutputPath { get; private set; } = "";
        public string FFmpegPath = "";
        internal Texture RenderPreviewTexture => capture != null ? capture.PreviewTexture : null;
        private readonly System.Diagnostics.Stopwatch renderTimer = new System.Diagnostics.Stopwatch();
        public double GenerationFps => renderTimer.Elapsed.TotalSeconds > 0 ? CapturedFrames / renderTimer.Elapsed.TotalSeconds : 0;
        public double ElapsedSeconds => renderTimer.Elapsed.TotalSeconds;
        public double EstimatedRemainingSeconds
        {
            get
            {
                if (TotalFrames <= 0 || CapturedFrames <= 0 || GenerationFps <= 0) return double.NaN;
                return Math.Max(0.0, (TotalFrames - CapturedFrames) / GenerationFps);
            }
        }
        public double RenderSpeedMultiplier => profile != null && profile.VideoFps > 0
            ? GenerationFps / profile.VideoFps : 0;
        public double CaptureWaitSeconds { get; private set; }
        public double GameFrameSeconds => gameFrameTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public double ReadbackWaitSeconds => capture != null ? capture.ReadbackWaitSeconds : 0;
        public double ReadbackCopySeconds => capture != null ? capture.ReadbackCopySeconds : 0;
        public double ReadbackLatencySeconds => capture != null ? capture.ReadbackLatencySeconds : 0;
        public int PendingReadbacks => capture != null ? capture.PendingReadbacks : 0;
        public int PeakPendingReadbacks => capture != null ? capture.PeakPendingReadbacks : 0;
        public double EncoderWriteSeconds => encoder != null ? encoder.WriteSeconds : 0;
        public int EncoderQueueDepth => encoder != null ? encoder.QueueDepth : 0;
        public int PeakEncoderQueueDepth => encoder != null ? encoder.PeakQueueDepth : 0;
        public long WrittenFrames => encoder != null ? encoder.WrittenFrames : 0;
        public double AudioCaptureSeconds => audio != null ? audio.CaptureSeconds : 0;
        public double FinalizationSeconds => finalizationTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        public long TotalFrames { get; private set; }
        public long CapturedFrames { get; private set; }
        public bool Busy => State == RenderState.Preparing || State == RenderState.Rendering || State == RenderState.Finishing
            || State == RenderState.AwaitingConfirmation
            || rpcLoadRoutine != null || editorRecoveryRoutine != null;
        internal bool EncoderFallbackPending => encoderFallbackPending;
        internal string EncoderFallbackReason => encoderFallbackReason ?? string.Empty;
        private FrameCapture capture;
        private FFmpegEncoder encoder;
        private SavedState saved;
        private Coroutine routine;
        private Coroutine editorRecoveryRoutine;
        private bool editorStateReady = true;
        private bool shuttingDown;
        private scnGame level;
        private scnEditor editor;
        private bool cancellation;
        private string partialPath;
        private string audioPath, muxPath;
        private UnityWebRequest songRequest;
        private UnityWebRequest pendingSongRequest;
        private CalibrationPreset overriddenCalibrationPreset;
        private int overriddenInputOffset;
        private bool inputOffsetOverridden;
        private GameAudioCapture audio;
        private BgaRenderState bga;
        private PlanetRingRenderState planetRings;
        private DefaultTextRenderState defaultText;
        private bool bgaModeForRun;
        private bool showPlanetRingsForRun;
        private bool showSongTitleForRun;
        private bool showCountdownForRun;
        private bool showResultTextForRun;
        private bool showHitJudgmentsForRun;
        private double scheduledMusicStartDsp;
        private double scheduledMusicLengthSeconds;
        private float toastUntil;
        private bool captureAudioForRun;
        private bool audioRealtimePacing;
        private double audioPacingOrigin;
        private long latePlaySoundSchedules;
        private bool openOutputFolderForRun;
        private RenderProfile profile;
        private float escapeHeldAt = -1f;
        private bool forceCancelTriggered;
        private bool processPriorityChanged;
        private System.Diagnostics.ProcessPriorityClass processPriorityBefore;
        private long gameFrameTicks;
        private long finalizationTicks;
        private long tailExtensionFrames;
        private readonly ConcurrentQueue<object> rpcCommands = new ConcurrentQueue<object>();
        private readonly ConcurrentQueue<EncoderPreflightResult> encoderPreflightResults =
            new ConcurrentQueue<EncoderPreflightResult>();
        private Coroutine rpcLoadRoutine;
        private RpcRenderJob activeRpcJob;
        private double nextProgressUpdateAt;
        private bool encoderFallbackPending;
        private string encoderFallbackReason;
        private int encoderPreflightRunning;

        private sealed class EncoderPreflightResult
        {
            internal readonly bool Success;
            internal readonly string Error;

            internal EncoderPreflightResult(bool success, string error)
            {
                Success = success;
                Error = error;
            }
        }

        private void Awake() { Instance = this; }
        public void StartRender()
        {
            StartRender(null);
        }

        internal void StartRender(RenderRequestOptions requestOptions)
        {
            if (Busy || !Main.Enabled) return;
            if (FfmpegInstaller.IsDownloading)
            {
                Message = FfmpegInstaller.StatusMessage;
                ShowToast(Message, 5f, false);
                return;
            }
            if (FfmpegInstaller.IsAwaitingConsent)
            {
                Message = Localization.Get("ffmpeg-installation-requires-your-confirmation-before-r");
                activeRpcJob?.Fail(Message);
                activeRpcJob = null;
                ShowToast(Message, 8f, false);
                return;
            }
            State = RenderState.Preparing;
            cancellation = false;
            CapturedFrames = TotalFrames = 0;
            OutputPath = ""; partialPath = null;
            audioPath = muxPath = null;
            renderTimer.Reset();
            nextProgressUpdateAt = 0;
            CaptureWaitSeconds = 0;
            ProgressPercentText = ProgressText = EtaText = SpeedText = "";
            gameFrameTicks = 0;
            finalizationTicks = 0;
            tailExtensionFrames = 0;
            encoderFallbackPending = false;
            encoderFallbackReason = null;
            ClearQueuedInput();
            escapeHeldAt = -1f;
            forceCancelTriggered = false;
            var settings = Main.Settings ?? new RendererSettings();
            var rpcOptions = activeRpcJob != null ? activeRpcJob.Options : null;
            bgaModeForRun = requestOptions?.BgaMode ?? rpcOptions?.BgaMode ?? settings.BgaMode;
            showPlanetRingsForRun = requestOptions?.ShowPlanetRings ?? rpcOptions?.ShowPlanetRings
                ?? settings.ShowPlanetRings;
            showSongTitleForRun = requestOptions?.ShowSongTitle ?? rpcOptions?.ShowSongTitle
                ?? settings.ShowSongTitle;
            showCountdownForRun = requestOptions?.ShowCountdown ?? rpcOptions?.ShowCountdown
                ?? settings.ShowCountdown;
            showResultTextForRun = requestOptions?.ShowResultText ?? rpcOptions?.ShowResultText
                ?? settings.ShowResultText;
            showHitJudgmentsForRun = requestOptions?.ShowHitJudgments ?? rpcOptions?.ShowHitJudgments
                ?? settings.ShowHitJudgments;
            profile = settings.ResolveProfile(
                requestOptions?.Preset ?? rpcOptions?.Preset,
                requestOptions?.Width ?? rpcOptions?.Width,
                requestOptions?.Height ?? rpcOptions?.Height,
                requestOptions?.TargetFps ?? rpcOptions?.TargetFps,
                requestOptions?.VideoFps ?? rpcOptions?.VideoFps,
                requestOptions?.BitrateMbps ?? rpcOptions?.BitrateMbps,
                requestOptions?.EndDelaySeconds ?? rpcOptions?.EndDelaySeconds,
                requestOptions?.VideoCodec ?? rpcOptions?.VideoCodec,
                requestOptions?.BitDepth ?? rpcOptions?.BitDepth,
                requestOptions?.Encoding,
                requestOptions?.Encoder);
            Clock = new RenderClock(profile.TargetFps);
            AudioSchedulePatch.ResetRuntimeState();
            Message = Localization.FormatWithCurrentCulture("preparing-render", profile.Width, profile.Height, profile.TargetFps, profile.VideoFps, profile.BitrateMbps, profile.FfmpegCodec);
            captureAudioForRun = activeRpcJob != null
                ? activeRpcJob.CaptureAudio
                : requestOptions?.CaptureAudio ?? settings.CaptureAudio;
            audioRealtimePacing = false;
            audioPacingOrigin = 0.0;
            latePlaySoundSchedules = 0;
            openOutputFolderForRun = requestOptions?.OpenOutputFolder ?? settings.OpenOutputFolder;
            FFmpegPath = ResolveFfmpegExecutable(settings);
            activeRpcJob?.SetState(RpcJobState.Preparing);
            Message = Localization.Format("checking-value-encoder", profile.FfmpegCodec);
            ShowToast(Message, 4f, false);
            BeginEncoderPreflight();
        }

        private void BeginEncoderPreflight()
        {
            if (Interlocked.CompareExchange(ref encoderPreflightRunning, 1, 0) != 0) return;
            var executable = FFmpegPath;
            var checkProfile = profile;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var success = FFmpegEncoder.TryValidateVideo(executable, checkProfile.BitrateMbps,
                        checkProfile.FfmpegPreset, checkProfile.FfmpegCodec, checkProfile.PixelFormat,
                        checkProfile.ContainerExtension, !captureAudioForRun, out var error);
                    encoderPreflightResults.Enqueue(new EncoderPreflightResult(success, error));
                }
                catch (Exception ex)
                {
                    encoderPreflightResults.Enqueue(new EncoderPreflightResult(false, ex.Message));
                }
            });
        }

        private void ProcessEncoderPreflight()
        {
            if (Interlocked.CompareExchange(ref encoderPreflightRunning, 0, 0) == 0) return;
            if (!encoderPreflightResults.TryDequeue(out var result)) return;
            Interlocked.Exchange(ref encoderPreflightRunning, 0);
            if (cancellation || State != RenderState.Preparing) return;
            if (result.Success)
            {
                Main.Entry.Logger.Log("Encoder preflight passed: " + profile.FfmpegCodec
                    + " (" + profile.PixelFormat + ").");
                StartRenderCore();
                return;
            }

            var reason = string.IsNullOrEmpty(result.Error)
                ? Localization.Get("ffmpeg-could-not-initialize-the-selected-encoder")
                : result.Error;
            if (VideoCodecCatalog.IsHardwareEncoder(profile.FfmpegCodec))
            {
                encoderFallbackPending = true;
                encoderFallbackReason = reason;
                State = RenderState.AwaitingConfirmation;
                Message = Localization.Get("hardware-encoder-failed-use-software-encoder-for-this-r");
                activeRpcJob?.SetState(RpcJobState.AwaitingConfirmation, reason);
                ShowToast(Message, 30f, false);
                Main.Entry.Logger.Error("Hardware encoder preflight failed: " + reason);
            }
            else
            {
                State = RenderState.Failed;
                Message = Localization.Format("encoder-preflight-failed-value", reason);
                activeRpcJob?.Fail(Message);
                activeRpcJob = null;
                ShowToast(Message, 10f, false);
                Main.Entry.Logger.Error(Message);
            }
        }

        private void StartRenderCore()
        {
            encoderFallbackPending = false;
            routine = StartCoroutine(GuardedRun());
        }

        internal void ConfirmEncoderFallback()
        {
            if (!encoderFallbackPending || profile == null) return;
            var definition = VideoCodecCatalog.Get(profile.VideoCodec);
            profile = new RenderProfile(profile.Width, profile.Height, profile.TargetFps, profile.VideoFps, profile.BitrateMbps,
                profile.FfmpegPreset, profile.EndDelaySeconds, definition.SoftwareEncoder,
                profile.VideoCodec, profile.BitDepth);
            encoderFallbackPending = false;
            encoderFallbackReason = null;
            State = RenderState.Preparing;
            Message = Localization.Get("checking-software-encoder-for-this-render");
            activeRpcJob?.SetState(RpcJobState.Preparing);
            Main.Entry.Logger.Log("User approved one-render software fallback: " + profile.FfmpegCodec + ".");
            ShowToast(Message, 4f, false);
            BeginEncoderPreflight();
        }

        internal void RejectEncoderFallback()
        {
            if (!encoderFallbackPending) return;
            encoderFallbackPending = false;
            encoderFallbackReason = null;
            State = RenderState.Cancelled;
            Message = Localization.Get("render-cancelled-hardware-encoder-was-unavailable");
            activeRpcJob?.Cancel();
            activeRpcJob = null;
            ShowToast(Message, 8f, false);
            Main.Entry.Logger.Log(Message);
        }

        public void Cancel()
        {
            if (encoderFallbackPending)
            {
                RejectEncoderFallback();
                return;
            }
            if (Busy) cancellation = true;
        }

        internal void EnqueueRpcRender(RpcRenderRequest request)
        {
            if (request != null && request.Job != null) rpcCommands.Enqueue(request);
        }

        internal void EnqueueRpcCancel(RpcCancelRequest request)
        {
            if (request != null && !string.IsNullOrEmpty(request.JobId)) rpcCommands.Enqueue(request);
        }

        internal bool ToastVisible => Time.unscaledTime <= toastUntil;

        internal void ShowToast(string text, float seconds, bool useGameNotification = true)
        {
            ToastText = text ?? string.Empty;
            toastUntil = Time.unscaledTime + Mathf.Max(0.5f, seconds);
            if (!useGameNotification || ADOBase.editor == null) return;
            try
            {
                // This is ADOFAI's own editor notification bar. The fallback
                // OnGUI toast below remains visible while the render canvas is
                // temporarily hidden from the captured camera.
                ADOBase.editor.ShowNotification(ToastText, null, seconds);
            }
            catch (Exception ex) { Main.Entry.Logger.Log("Game notification unavailable: " + ex.Message); }
        }

        private void ShowProgressToast()
        {
            if (TotalFrames <= 0) return;
            var progress = 100.0 * CapturedFrames / TotalFrames;
            ProgressPercentText = Localization.FormatWithCurrentCulture("progress-percent", progress);
            ProgressText = Localization.FormatWithCurrentCulture("progress-frames", CapturedFrames, TotalFrames, GenerationFps);
            EtaText = Localization.FormatWithCurrentCulture("progress-eta", FormatDuration(EstimatedRemainingSeconds), FormatFinishTime(EstimatedRemainingSeconds));
            SpeedText = Localization.FormatWithCurrentCulture("progress-speed", RenderSpeedMultiplier, FormatDuration(ElapsedSeconds));
            ToastText = Localization.FormatWithCurrentCulture("progress-toast", progress, CapturedFrames, TotalFrames, GenerationFps, FormatDuration(EstimatedRemainingSeconds));
            toastUntil = Time.unscaledTime + 1.0f;
        }
        private IEnumerator GuardedRun()
        {
            var run = Run();
            try
            {
                while (!cancellation)
                {
                    object next;
                    try { if (!run.MoveNext()) break; next = run.Current; }
                    catch (Exception ex) { Fail(ex); break; }
                    yield return next;
                }
                if (cancellation)
                {
                    State = RenderState.Cancelled;
                    Message = Localization.Get("render-cancelled");
                    ShowToast(Message, 5f);
                }
            }
            finally { (run as IDisposable)?.Dispose(); Cleanup(); routine = null; }
        }
        private IEnumerator Run()
        {
            // Start at a frame boundary; OnGUI can run several times per frame.
            yield return EndOfFrame;
            editor = ADOBase.editor;
            level = editor != null ? editor.customLevel : ADOBase.customLevel;
            ValidateLoadedLevel();
            LogRenderChain("before render preparation", level);
            string invalidReason = null;
            if (editor != null && (!editorStateReady || !IsEditorRenderStateValid(level, out invalidReason)))
            {
                Main.Entry.Logger.Log("Rebuilding editor render state before export: "
                    + (editorStateReady ? invalidReason : "previous recovery has not completed"));
                RebuildEditorRenderState(editor, level);
                yield return null;
                if (!IsEditorRenderStateValid(level, out invalidReason))
                    throw new InvalidOperationException("Editor render state could not be restored: " + invalidReason);
                editorStateReady = true;
                LogRenderChain("after pre-render rebuild", level);
            }
            if (GCS.d_oldConductor || GCS.d_webglConductor)
                throw new InvalidOperationException("The installed conductor must use its standard DSP timing mode.");
            var settings = Main.Settings ?? new RendererSettings();
            var directory = settings.ResolveOutputDirectory();
            Directory.CreateDirectory(directory);
            var name = SanitizeName(ADOBase.controller.levelName);
            OutputPath = Path.Combine(directory, name + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + profile.ContainerExtension);
            partialPath = Path.ChangeExtension(OutputPath, ".partial" + profile.ContainerExtension);
            audioPath = Path.ChangeExtension(OutputPath, ".partial.wav");
            muxPath = Path.ChangeExtension(OutputPath, ".mux" + profile.ContainerExtension);
            saved = new SavedState();
            PrepareAudioConfiguration();
            OverrideInputOffsetForRender();
            MaximizeRenderPerformance();
            encoder = new FFmpegEncoder(FFmpegPath, partialPath, profile.Width, profile.Height,
                profile.VideoFps, profile.BitrateMbps, profile.FfmpegPreset, !captureAudioForRun,
                profile.FfmpegCodec, profile.PixelFormat);
            if (editor != null)
            {
                Main.Entry.Logger.Log("Preparing editor render: playMode=" + editor.playMode
                    + ", strictlyEditing=" + editor.inStrictlyEditingMode + ", tiles=" + editor.floors.Count);
                // playMode includes paused playback. The editor's initial setup
                // does not initialize inStrictlyEditingMode, so that flag cannot
                // tell whether a freshly opened editor is ready to render.
                if (editor.playMode) editor.SwitchToEditMode();
            }
            // Unity advances the game's simulation at InGame FPS. Video FPS
            // is a separate sampling rate used by the output encoder below.
            Time.captureFramerate = profile.TargetFps;
            Time.timeScale = 1;
            DG.Tweening.DOTween.useSmoothDeltaTime = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = RenderTargetFrameRate;
            Application.runInBackground = true;
            // Keep the game's audio signal alive even for video-only renders.
            // ADOFAI uses the live song volume for TrackColorType.Volume, so
            // muting the listener here also removes a visual input from the
            // level. Video-only output simply omits the captured audio stream;
            // it must not mute the simulation that drives the visuals.
            AudioListener.pause = false;
            Persistence.skipIntroBehavior = SkipIntroBehavior.Off;
            GCS.checkpointNum = 0;
            RDC.auto = false; // Preserve the normal countdown, avoiding the editor's fast-takeoff shortcut.
            // ADOFAI's hit-text manager exits early when noHud is enabled.
            // Keep the normal HUD-hidden render path, but allow the explicit
            // hit-judgment option to reach ShowHitText.
            RDC.noHud = !showHitJudgmentsForRun;
            RDC.noAutoHud = true;
            yield return null;
            // Decode the complete clip before gameplay starts. Replacing or
            // waiting for audio after editor.Play lets camera tweens run before
            // output frame zero and changes their motion in the export.
            yield return PrepareSongClip();
            // Play Sound Effect events use lazy asynchronous loading. At a
            // high Target FPS the deterministic game clock can reach an event
            // before its external clip has finished loading; ffxPlaySound's
            // ready check then drops the event permanently. Complete the
            // level's custom-sound preload before starting gameplay timing.
            yield return PrepareCustomSoundEffects();
            if (editor != null)
            {
                editor.SelectFloor(editor.floors[0], cameraJump: false);
                editor.Play();
                editorStateReady = false;
            }
            else
            {
                level.ResetScene();
                if (!level.Play(0)) throw new InvalidOperationException("Custom Level playback could not start.");
                // The official preparation coroutine warms filters over two frames.
                int preparationFrames = 0;
                while (level.isLoading)
                {
                    if (++preparationFrames > 600) throw new TimeoutException("Custom Level preparation did not finish.");
                    yield return null;
                }
                AbortStartPrompt();
                ADOBase.conductor.Start();
                level.FinishCustomLevelLoading(0);
                ADOBase.controller.Start_Rewind(0);
            }
            // editor.Play() leaves one frame of camera setup pending. Let that
            // setup run before taking ownership of the gameplay cameras, then
            // explicitly restore the normal gameplay framing below.
            yield return null;
            RDC.auto = true;
            ADOBase.controller.noFail = true;
            ADOBase.controller.paused = false;
            ADOBase.controller.enabled = true;
            Time.timeScale = 1;
            ADOBase.conductor.dspTime = Clock.DspTime;
            ADOBase.conductor.songposition_minusi = Clock.SongPosition(ADOBase.conductor.dspTimeSong,
                ADOBase.conductor.song.pitch, ADOBase.conductor.addoffset, 0.0);
            PrepareRenderCamera();
            // Camera Filter Pack components keep their private animation clock
            // while the level is reset. Start every export from the same phase
            // so a second render is visually identical to the first one.
            CameraFilterPatch.ResetRuntimeState();
            FrameRateEventPatch.ResetRuntimeState();
            LogRenderChain("after gameplay camera setup", level);
            defaultText = DefaultTextRenderState.Capture(showSongTitleForRun,
                showCountdownForRun, showResultTextForRun, showHitJudgmentsForRun);
            capture = new FrameCapture(encoder, profile.Width, profile.Height, defaultText.CaptureCanvas,
                defaultText.HitTextContainer);
            if (!showPlanetRingsForRun)
            {
                planetRings = PlanetRingRenderState.Capture();
                Main.Entry.Logger.Log("Planet ring visibility disabled for this render.");
            }
            if (bgaModeForRun)
            {
                bga = BgaRenderState.Capture();
                Main.Entry.Logger.Log("BGA mode enabled: hidden renderers=" + bga.HiddenRendererCount);
            }
            State = RenderState.Rendering;
            renderTimer.Start();
            ApplyFramePacing();
            Message = Localization.FormatWithCurrentCulture("rendering-summary", profile.Width, profile.Height, profile.TargetFps, profile.VideoFps);
            ShowToast(Message, 2f, false);
            // Advance the game at InGame FPS, then sample the rendered state
            // at Video FPS. If Video FPS is higher, repeated samples use the
            // most recent game frame; if lower, intermediate game frames are
            // simulated but not encoded.
            var hasRenderedGameFrame = false;
            while (true)
            {
                var desiredSimulationFrame = SimulationFrameForVideoFrame(CapturedFrames);
                var sampledCurrentGameFrame = false;
                while (!hasRenderedGameFrame || Clock.FrameIndex <= desiredSimulationFrame)
                {
                    // Realtime audio fallbacks may need wall-clock pacing, but
                    // the wait must not yield extra Unity frames. Each yielded
                    // frame advances the game at InGame FPS.
                    var simulationFrameIndex = Clock.FrameIndex;
                    WaitForAudioFrame(simulationFrameIndex);
                    var gameFrameStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    yield return null;
                    yield return EndOfFrame;
                    if (level == null || (editor != null ? editor.customLevel : ADOBase.customLevel) != level || ADOBase.controller == null || ADOBase.conductor == null)
                        throw new InvalidOperationException("The level was unloaded during rendering.");
                    gameFrameTicks += System.Diagnostics.Stopwatch.GetTimestamp() - gameFrameStart;
                    hasRenderedGameFrame = true;
                    sampledCurrentGameFrame = Clock.FrameIndex == desiredSimulationFrame;
                    Clock.Advance();
                    if (sampledCurrentGameFrame) break;
                }

                // When Video FPS exceeds InGame FPS, the current render target
                // is intentionally sampled more than once for a constant-rate
                // output stream. FrameCapture keeps the sample order intact.
                // Audio follows the output timeline as well: the game may run
                // several Target FPS simulation frames between two video
                // samples, but only one audio capture block belongs to that
                // output frame.
                if (captureAudioForRun)
                {
                    if (audio == null) throw new InvalidOperationException("The game did not initialize game audio before frame zero.");
                    audio.CaptureFrame(CapturedFrames, profile.VideoFps);
                    if (audio.NeedsRealtimePacing && !audioRealtimePacing)
                    {
                        audioRealtimePacing = true;
                        audioPacingOrigin = Time.realtimeSinceStartupAsDouble
                            - CapturedFrames / (double)profile.VideoFps;
                        Main.Entry.Logger.Log("Unity AudioRenderer returned no samples; pacing the render to realtime for the AudioListener fallback.");
                    }
                }
                capture.Capture(CapturedFrames);
                CaptureWaitSeconds = capture.BackpressureSeconds;
                CapturedFrames++;
                // Throttle presentation work by wall time. At high generation
                // rates, updating this every six output frames can
                // format and rebuild the IMGUI text dozens of times per second.
                var elapsed = renderTimer.Elapsed.TotalSeconds;
                if (elapsed >= nextProgressUpdateAt)
                {
                    ShowProgressToast();
                    nextProgressUpdateAt = elapsed + 0.25;
                }
                if (TotalFrames > 0 && CapturedFrames >= TotalFrames)
                {
                    var player = ADOBase.controller.playerOne;
                    var floors = ADOBase.lm.listFloors;
                    if (HasReachedFinalTile(ADOBase.controller, player, floors))
                        break;

                    // A streamed song can report a short AudioClip.length while
                    // the conductor is still advancing the chart. Do not turn
                    // that timing discrepancy into a failed render: keep the
                    // deterministic clock moving in one-second tail steps until
                    // the final tile is actually entered, with a hard safety cap.
                    var extensionStep = Math.Max(1L, profile.VideoFps);
                    var extensionLimit = Math.Max(extensionStep, profile.VideoFps * 30L);
                    if (tailExtensionFrames >= extensionLimit)
                    {
                        var playerSeqId = player != null && player.currFloor != null
                            ? player.currFloor.seqID.ToString() : "<none>";
                        var controllerSeqId = ADOBase.controller != null
                            ? ADOBase.controller.currentSeqID.ToString() : "<none>";
                        Main.Entry.Logger.Error(string.Format(
                            "Final tile was not observed: playerFloor={0}, controllerSeqID={1}, floors={2}.",
                            playerSeqId, controllerSeqId, floors != null ? floors.Count.ToString() : "<none>"));
                        throw new InvalidOperationException("Autoplay did not reach the last tile after the render tail was extended.");
                    }
                    TotalFrames = checked(TotalFrames + extensionStep);
                    tailExtensionFrames += extensionStep;
                    Main.Entry.Logger.Log(string.Format(
                        "Final tile not reached; extending render tail by {0} frames ({1:F2}s total).",
                        extensionStep, tailExtensionFrames / (double)profile.VideoFps));
                }
                if (TotalFrames == 0 && Clock.Time > 10)
                    throw new InvalidOperationException("The game did not schedule level playback.");
            }
            State = RenderState.Finishing;
            renderTimer.Stop();
            Message = Localization.Format("finalizing-container", profile.ContainerExtension.TrimStart('.').ToUpperInvariant());
            ShowToast(Message, 8f, false);
            var finalizationStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                capture.Drain(true);
                // No Unity yields during finalization; gameplay must not progress further.
                encoder.Finish(CapturedFrames);
                if (audio != null)
                {
                    audio.Complete(CapturedFrames, profile.VideoFps);
                    audio.Dispose();
                    FFmpegEncoder.MuxAudio(FFmpegPath, partialPath, audioPath, muxPath);
                    File.Move(muxPath, OutputPath);
                    File.Delete(partialPath); File.Delete(audioPath);
                }
                else File.Move(partialPath, OutputPath);
            }
            finally { finalizationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - finalizationStart; }
            State = RenderState.Completed;
            Message = Localization.Format("completed-frames", CapturedFrames)
                + (audio != null && audio.Peak < 0.000001f
                    ? Localization.Get("audio-mix-was-silent-check-game-sound-settings")
                    : "");
            ShowToast(Message, 8f);
            Main.Entry.Logger.Log(string.Format("Completed: {0} frames in {1:F2}s, {2:F1} frames/s ({3:F2}x video). Target={4}fps Video={5}fps {6}x{7} {8}Mbps {9}/{10}. Audio={11}. Capture/encoder wait={12:F2}s. Metrics: game={13:F2}s, readbackWait={14:F2}s, readbackLatency={15:F2}s, readbackCopy={16:F2}s, pendingPeak={17}, encoderWrite={18:F2}s, encoderQueuePeak={19}, written={20}, audioCapture={21:F2}s, finalization={22:F2}s. Output={23}",
                CapturedFrames, ElapsedSeconds, GenerationFps, GenerationFps / profile.VideoFps,
                profile.TargetFps, profile.VideoFps, profile.Width, profile.Height, profile.BitrateMbps, profile.FfmpegCodec, profile.FfmpegPreset,
                audio != null, CaptureWaitSeconds,
                GameFrameSeconds, ReadbackWaitSeconds, ReadbackLatencySeconds, ReadbackCopySeconds,
                PeakPendingReadbacks, EncoderWriteSeconds, PeakEncoderQueueDepth, WrittenFrames,
                AudioCaptureSeconds, FinalizationSeconds, OutputPath));
            if (latePlaySoundSchedules > 0)
                Main.Entry.Logger.Log("Adjusted " + latePlaySoundSchedules
                    + " late Play Sound Effect schedule(s) to the next captured audio sample.");
            if (openOutputFolderForRun) OpenOutputFolder();
        }

        internal void ClampLatePlaySoundSchedule(ref double time)
        {
            if (audio == null || profile == null || Clock == null
                || double.IsNaN(time) || double.IsInfinity(time)) return;

            var renderedUntil = audio.RenderedUntilDsp(Clock.DspOrigin);
            if (double.IsNaN(renderedUntil) || double.IsInfinity(renderedUntil)
                || time >= renderedUntil) return;

            // One sample after the consumed boundary is the earliest point
            // that AudioRenderer can still include in the next block. This
            // preserves the original timestamp whenever it is still future.
            var sampleLead = 1.0 / Math.Max(1, audio.SampleRate);
            time = renderedUntil + sampleLead;
            latePlaySoundSchedules++;
        }

        private IEnumerator PrepareCustomSoundEffects()
        {
            if (level == null) yield break;
            Main.Entry.Logger.Log("Preloading Play Sound Effect clips before the Target FPS clock starts.");
            var reloadMethod = AccessTools.Method(typeof(scnGame), "ReloadCustomSoundsCo");
            if (reloadMethod == null)
                throw new MissingMethodException(typeof(scnGame).FullName, "ReloadCustomSoundsCo");
            var reload = reloadMethod.Invoke(level, new object[] { false }) as IEnumerator;
            if (reload != null) yield return reload;
            Main.Entry.Logger.Log("Play Sound Effect clip preload completed.");
        }

        private long SimulationFrameForVideoFrame(long videoFrameIndex)
        {
            if (profile == null || profile.VideoFps <= 0 || videoFrameIndex <= 0) return 0;
            var exact = videoFrameIndex * (double)profile.TargetFps / profile.VideoFps;
            if (exact >= long.MaxValue) return long.MaxValue;
            return Math.Max(0L, (long)Math.Round(exact, MidpointRounding.AwayFromZero));
        }

        private static bool HasReachedFinalTile(scrController controller, scrPlayer player,
            System.Collections.Generic.IList<scrFloor> floors)
        {
            if (controller == null || floors == null || floors.Count == 0) return false;
            var finalFloor = floors[floors.Count - 1];
            if (finalFloor == null) return false;
            var finalSeqId = finalFloor.seqID;
            if (player != null && player.currFloor != null && player.currFloor.seqID >= finalSeqId)
                return true;
            if (controller.currFloor != null && controller.currFloor.seqID >= finalSeqId)
                return true;
            // Some chart endings update scrController.currentSeqID before the
            // player object's currFloor reference catches up (or clear it).
            return controller.currentSeqID >= finalSeqId;
        }

        private IEnumerator PrepareSongClip()
        {
            if (level == null || level.levelData == null || string.IsNullOrEmpty(level.levelData.songFilename))
                yield break;

            var levelDirectory = Path.GetDirectoryName(level.levelPath);
            var songPath = string.IsNullOrEmpty(levelDirectory)
                ? level.levelData.songFilename
                : Path.Combine(levelDirectory, level.levelData.songFilename);
            if (!File.Exists(songPath))
                yield break; // Internal/bundled levels are already backed by game audio.

            var extension = Path.GetExtension(songPath).ToLowerInvariant();
            AudioType audioType;
            switch (extension)
            {
                case ".ogg": audioType = AudioType.OGGVORBIS; break;
                case ".wav": audioType = AudioType.WAV; break;
                case ".aif":
                case ".aiff": audioType = AudioType.AIFF; break;
                case ".mp3": audioType = AudioType.MPEG; break;
                default:
                    Main.Entry.Logger.Log("Song decode skipped for unsupported extension: " + extension);
                    yield break;
            }

            // Keep the request alive while its clip is attached. Disposing a
            // DownloadHandlerAudioClip can invalidate the AudioClip it owns;
            // disposing here would make clip.length become zero before the
            // render and would also leave post-render editor playback silent.
            var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(songPath).AbsoluteUri, audioType);
            pendingSongRequest = request;
            var handler = request.downloadHandler as DownloadHandlerAudioClip;
            if (handler != null) handler.streamAudio = false;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                DisposePendingSongRequest();
                throw new InvalidOperationException("Could not decode the level song: " + request.error);
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null || clip.length <= 0)
            {
                DisposePendingSongRequest();
                throw new InvalidOperationException("The level song decoded to an empty AudioClip.");
            }
            if (ADOBase.conductor == null || ADOBase.conductor.song == null)
            {
                DisposePendingSongRequest();
                throw new InvalidOperationException("ADOFAI did not provide a song source before render playback.");
            }

            ADOBase.conductor.song.Stop();
            ADOBase.conductor.song.clip = clip;
            // The old decoded request can only be released after its clip has
            // been detached from the AudioSource. Keep the new request alive
            // after rendering so normal editor playback retains a valid,
            // non-streaming clip instead of the original zero-length stream.
            DisposeSongRequest();
            songRequest = request;
            pendingSongRequest = null;
            Main.Entry.Logger.Log(string.Format(
                "Song decoded in full: {0:F2}s ({1}).", clip.length, Path.GetFileName(songPath)));
        }

        private void OverrideInputOffsetForRender()
        {
            // Some conductor setup paths cache the raw preset value before the
            // calibration getter is queried. Temporarily zero the actual field
            // so a render behaves exactly like starting playback with a 0 ms
            // input offset. This is an in-memory override only and is restored
            // by Cleanup on success, cancellation, failure, and application exit.
            var preset = scrConductor.currentPreset;
            if (inputOffsetOverridden) return;
            overriddenCalibrationPreset = preset;
            overriddenInputOffset = preset.inputOffset;
            preset.inputOffset = 0;
            scrConductor.currentPreset = preset;
            inputOffsetOverridden = true;
            Main.Entry.Logger.Log("Temporarily set input offset to 0 ms for rendering (saved "
                + overriddenInputOffset + " ms).");
        }

        private void RestoreInputOffsetAfterRender()
        {
            if (!inputOffsetOverridden) return;
            var value = overriddenInputOffset;
            var preset = scrConductor.currentPreset;
            preset.inputOffset = overriddenCalibrationPreset.inputOffset;
            scrConductor.currentPreset = preset;
            overriddenInputOffset = 0;
            inputOffsetOverridden = false;
            Main.Entry.Logger.Log("Restored input offset after rendering: " + value + " ms.");
        }

        private void DisposeSongRequest()
        {
            var request = songRequest;
            songRequest = null;
            request?.Dispose();
        }

        private void DisposePendingSongRequest()
        {
            var request = pendingSongRequest;
            pendingSongRequest = null;
            request?.Dispose();
        }

        private void OpenOutputFolder()
        {
            var directory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                Main.Entry.Logger.Log("Could not open render output folder: directory is unavailable.");
                return;
            }

            try
            {
                var windows = Application.platform == RuntimePlatform.WindowsPlayer
                    || Application.platform == RuntimePlatform.WindowsEditor;
                var mac = Application.platform == RuntimePlatform.OSXPlayer
                    || Application.platform == RuntimePlatform.OSXEditor;
                var fileName = windows ? "explorer.exe" : mac ? "open" : "xdg-open";
                var arguments = windows
                    ? "/select,\"" + OutputPath + "\""
                    : mac
                        ? "-R " + QuoteProcessArgument(OutputPath)
                        : QuoteProcessArgument(directory);

                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                }))
                {
                    if (process == null) throw new InvalidOperationException("The file manager process did not start.");
                }
            }
            catch (Exception ex)
            {
                // Opening a folder is a convenience and must not turn a
                // successfully completed render into a failed one.
                Main.Entry.Logger.Error("Could not open render output folder: " + ex.Message);
            }
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        internal void ScheduleAudio(scrConductor conductor)
        {
            if (conductor == null || conductor.song == null)
                throw new InvalidOperationException("ADOFAI did not provide a song source before StartMusic.");

            double pitch = conductor.song.pitch;
            double countdown = conductor.separateCountdownTime
                ? conductor.crotchetAtStart * conductor.adjustedCountdownTicks / pitch : 0.0;
            double songStart = conductor.dspTimeSong + countdown;
            scheduledMusicStartDsp = songStart;
            scheduledMusicLengthSeconds = LongestClipLength(conductor, pitch);
            Clock.AnchorDsp(AudioSettings.dspTime);
            conductor.dspTime = Clock.DspTime;
            conductor.dspTimeSong = conductor.dspTime + 1.0;
            countdown = conductor.separateCountdownTime
                ? conductor.crotchetAtStart * conductor.adjustedCountdownTicks / pitch : 0.0;
            songStart = conductor.dspTimeSong + countdown;
            scheduledMusicStartDsp = songStart;
            scheduledMusicLengthSeconds = LongestClipLength(conductor, pitch);
            Main.Entry.Logger.Log(string.Format(
                "Audio schedule: start={0:F3}s, level offset={1:F3}s, countdown={2:F3}s, pitch={3:F3}.",
                songStart - Clock.DspOrigin, conductor.addoffset, countdown, pitch));

            if (captureAudioForRun && audio == null)
            {
                audio = new GameAudioCapture();
                audio.Begin(audioPath);
                audioPacingOrigin = Time.realtimeSinceStartupAsDouble;
            }
            foreach (var source in new[] { conductor.song, conductor.song2, conductor.song3 })
            {
                if (source == null || source.clip == null) continue;
                source.Stop();
                source.time = 0;
                source.PlayScheduled(songStart);
            }
            conductor.PlayHitTimes();
            if (audio != null)
            {
                Main.Entry.Logger.Log("Game audio capture started: "
                    + audio.SampleRate + " Hz, " + audio.Channels + " channels.");
                AudioSchedulePatch.PreSchedulePlaySoundEffects();
            }
        }
        internal void MusicScheduled()
        {
            var conductor = ADOBase.conductor;
            double pitch = conductor.song.pitch;
            if (pitch <= 0 || double.IsNaN(pitch) || double.IsInfinity(pitch))
                throw new InvalidOperationException("Invalid song pitch.");
            var floors = ADOBase.lm.listFloors;
            double last = floors[floors.Count - 1].entryTime;
            double endDelay = profile != null ? profile.EndDelaySeconds : 2.0;
            if (double.IsNaN(endDelay) || double.IsInfinity(endDelay) || endDelay < 0) endDelay = 2.0;
            var previous = floors.Count > 1 ? floors[floors.Count - 2].entryTime : 0.0;
            var finalStep = (last - previous) / pitch;
            if (double.IsNaN(finalStep) || double.IsInfinity(finalStep) || finalStep <= 0)
                finalStep = Math.Max(conductor.crotchetAtStart / pitch, 1.0 / Clock.Fps);
            // entryTime is the instant the final tile is entered. Keep one
            // final tile interval plus one output frame so the player reaches
            // the last tile before the termination check, even when the audio
            // clip reports only a streaming-buffer length.
            double chartEnd = conductor.dspTimeSong - Clock.DspOrigin
                + (conductor.addoffset + last) / pitch + finalStep + 1.0 / Clock.Fps;
            double musicEnd = scheduledMusicStartDsp - Clock.DspOrigin + scheduledMusicLengthSeconds;
            double end = Math.Max(chartEnd, musicEnd) + endDelay;
            if (double.IsNaN(end) || double.IsInfinity(end) || end <= 0)
                throw new InvalidOperationException("Invalid final tile time.");
            TotalFrames = checked((long)Math.Ceiling(end * profile.VideoFps) + 1);
            Main.Entry.Logger.Log(string.Format("Render end: chart={0:F2}s, music={1:F2}s, delay={2:F2}s, total frames={3}",
                chartEnd, musicEnd, endDelay, TotalFrames));
        }

        private static double LongestClipLength(scrConductor conductor, double pitch)
        {
            double longest = 0.0;
            foreach (var source in new[] { conductor.song, conductor.song2, conductor.song3 })
            {
                if (source == null || source.clip == null) continue;
                longest = Math.Max(longest, source.clip.length / pitch);
            }
            return longest;
        }
        private void PrepareRenderCamera()
        {
            var camera = scrCamera.instance;
            if (camera == null) return;
            SyncBackgroundCameras(camera);
            // Do not call MoveCameraToPlayer or Refocus here. Those methods
            // overwrite the camera state/tweens created by the level's Move
            // Camera events. The game has already applied its normal camera
            // update during the preparation frames; FrameCapture only redirects
            // the existing cameras to the render target.
            var orthoSize = camera.camobj != null && camera.camobj.orthographic
                ? camera.camobj.orthographicSize : float.NaN;
            Main.Entry.Logger.Log(string.Format("Prepared render camera: zoom={0:F3}, ortho={1:F3}, player={2}",
                camera.zoomSize, orthoSize, ADOBase.controller != null && ADOBase.controller.playerOne != null));
        }
        private void ValidateLoadedLevel()
        {
            if (level == null)
                throw new InvalidOperationException("No Custom Level instance is available. Open a level in the editor or Custom Level player.");
            if (level.levelData == null)
                throw new InvalidOperationException("The Custom Level has no loaded chart data.");
            if (editor != null)
            {
                // scnEditor owns loading and the level maker while editing.
                // scnGame.isLoading is cleared by the gameplay start coroutine;
                // it can remain true for a fully loaded editor chart.
                if (editor.isLoading)
                    throw new InvalidOperationException("The editor is still loading the chart.");
                if (level.levelMaker == null || editor.floors == null || editor.floors.Count < 2)
                    throw new InvalidOperationException("The editor chart needs at least two tiles.");
            }
            else
            {
                if (level.isLoading)
                    throw new InvalidOperationException("Custom Level gameplay is still loading. Wait for the start prompt.");
                if (ADOBase.lm == null || ADOBase.lm.listFloors == null || ADOBase.lm.listFloors.Count < 2)
                    throw new InvalidOperationException("Custom Level gameplay has no playable tile path.");
            }
            if (ADOBase.controller == null || ADOBase.conductor == null)
                throw new InvalidOperationException("The gameplay controller or conductor is not ready.");
        }
        private static void AbortStartPrompt()
        {
            var controller = ADOBase.controller;
            var field = AccessTools.Field(typeof(scrController), "waitForStartCoCallCount");
            field.SetValue(controller, (int)field.GetValue(controller) + 1);
            scrUIController.instance.txtPressToStart.GetComponent<scrPressToStart>().HideText();
        }
        private void LateUpdate()
        {
            if (State != RenderState.Rendering) return;
            try { bga?.Apply(); planetRings?.Apply(); defaultText?.Apply(); ApplyFramePacing(); capture.Bind(); }
            catch (Exception ex) { Fail(ex); StopAndClean(); }
        }
        private void Update()
        {
            ProcessRpcCommands();
            ProcessEncoderPreflight();
            UpdateRpcJob();
            if (Busy)
            {
                UpdateForceCancelKey();
                return;
            }
            escapeHeldAt = -1f;
            forceCancelTriggered = false;
            if (Input.GetKeyDown(KeyCode.F6) && Main.Enabled && ADOBase.editor != null
                && !OrbitRender.UI.ExportVideoDialog.IsOpen
                && !FfmpegInstaller.IsInstallPromptVisible)
                OrbitRender.UI.ExportVideoDialog.Open(ADOBase.editor);
        }

        private void UpdateForceCancelKey()
        {
            if (!Input.GetKey(KeyCode.Escape))
            {
                escapeHeldAt = -1f;
                forceCancelTriggered = false;
                return;
            }
            if (escapeHeldAt < 0f) escapeHeldAt = Time.unscaledTime;
            if (!forceCancelTriggered && Time.unscaledTime - escapeHeldAt >= 1f)
            {
                forceCancelTriggered = true;
                ForceCancel();
            }
        }

        private void ForceCancel()
        {
            cancellation = true;
            encoderFallbackPending = false;
            encoderFallbackReason = null;
            if (rpcLoadRoutine != null) { StopCoroutine(rpcLoadRoutine); rpcLoadRoutine = null; }
            if (routine != null) { StopCoroutine(routine); routine = null; }
            State = RenderState.Cancelled;
            Message = Localization.Get("render-force-cancelled");
            ShowToast(Message, 5f);
            activeRpcJob?.Cancel();
            Cleanup();
            activeRpcJob = null;
        }
        private void OnGUI()
        {
            if (!Main.Enabled) return;
            if (Busy && Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
                Event.current.Use();
            if (State == RenderState.Idle)
            {
                Message = Localization.Get("open-a-custom-level-then-render");
                ToastText = Localization.Get("open-a-custom-level-then-press-f6-to-render");
            }
            if (FfmpegInstaller.IsInstallPromptVisible)
            {
                if (Event.current.type == EventType.Repaint)
                    OrbitRender.UI.RendererWindow.DrawBackdrop();
                OrbitRender.UI.RendererWindow.DrawFfmpegInstallPrompt();
                if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint)
                    Event.current.Use();
                return;
            }
            if (OrbitRender.UI.ExportVideoDialog.IsOpen)
            {
                OrbitRender.UI.ExportVideoDialog.Draw(this);
                return;
            }
            if (State == RenderState.Rendering && Event.current.type == EventType.Repaint)
                OrbitRender.UI.RendererWindow.DrawRenderPreview(RenderPreviewTexture);
            // Rendering temporarily owns the gameplay cameras and editor
            // overlays. Cover the presentation surface so a camera or canvas
            // target change can never flash through to the player window.
            if (Busy && State != RenderState.Rendering && Event.current.type == EventType.Repaint)
                OrbitRender.UI.RendererWindow.DrawBackdrop();
            if (EncoderFallbackPending)
            {
                OrbitRender.UI.RendererWindow.DrawEncoderFallbackPrompt(this);
                return;
            }
            if (!ToastVisible) return;
            OrbitRender.UI.RendererWindow.DrawToast(this);
        }
        private void MaximizeRenderPerformance()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                processPriorityBefore = process.PriorityClass;
                if (processPriorityBefore != System.Diagnostics.ProcessPriorityClass.High)
                {
                    process.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
                    processPriorityChanged = true;
                }
            }
            catch (Exception ex)
            {
                Main.Entry.Logger.Log("Could not raise renderer process priority: " + ex.Message);
            }
        }

        private void PrepareAudioConfiguration()
        {
            if (!captureAudioForRun) return;
            try
            {
                var configuration = AudioSettings.GetConfiguration();
                Main.Entry.Logger.Log("Unity audio configuration: " + configuration.sampleRate
                    + " Hz, DSP buffer=" + configuration.dspBufferSize + " samples, real voices="
                    + configuration.numRealVoices + ", virtual voices=" + configuration.numVirtualVoices + ".");
                var requestedBuffer = RecommendedAudioDspBufferSize(configuration.sampleRate,
                    profile != null ? profile.VideoFps : 60);
                if (requestedBuffer <= 0 || configuration.dspBufferSize <= requestedBuffer) return;

                // AudioRenderer.GetSampleCountForCaptureFrame() is driven by
                // Time.captureFramerate. The audio capture temporarily uses
                // Video FPS, so a normal 256/512 sample DSP block can be
                // larger than one capture frame, so
                // Unity reports no samples and the live listener fallback
                // would introduce map effects and realtime timing. Use the
                // largest small power-of-two buffer that fits one target
                // frame. SavedState restores the user's setting afterward.
                configuration.dspBufferSize = requestedBuffer;
                var reset = AudioSettings.Reset(configuration);
                var actual = AudioSettings.GetConfiguration();
                Main.Entry.Logger.Log("Renderer audio DSP buffer request: " + requestedBuffer
                    + " samples for InGame FPS " + (profile != null ? profile.TargetFps.ToString() : "60")
                    + ", reset=" + reset + ", actual=" + actual.dspBufferSize + ".");
            }
            catch (System.Exception ex)
            {
                Main.Entry.Logger.Log("Could not normalize the Unity audio DSP buffer: " + ex.Message);
            }
        }

        private static int RecommendedAudioDspBufferSize(int sampleRate, int targetFps)
        {
            if (sampleRate <= 0 || targetFps <= 0) return 0;
            var samplesPerTargetFrame = sampleRate / (double)targetFps;
            // Unity supports 32-sample DSP buffers on the desktop target.
            // InGame FPS can reach 1024, where one 48 kHz frame is only
            // about 47 samples; starting at 64 would make the DSP block
            // larger than a capture frame again.
            var result = 32;
            while (result < 512 && result * 2 <= samplesPerTargetFrame)
                result *= 2;
            return result;
        }

        private void RestoreRenderPerformance()
        {
            if (!processPriorityChanged) return;
            try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = processPriorityBefore; }
            catch (Exception ex) { Main.Entry.Logger.Log("Could not restore renderer process priority: " + ex.Message); }
            finally { processPriorityChanged = false; }
        }

        internal static string ResolveFfmpegExecutable(RendererSettings settings)
        {
            var configured = settings.ResolveFfmpegExecutable(Main.Entry.Path);
            if (!string.IsNullOrEmpty(configured)) return configured;

            var bundled = FfmpegInstaller.GetBundledExecutable(Main.Entry.Path);
            if (!string.IsNullOrEmpty(bundled) && File.Exists(bundled)) return bundled;

            var windows = Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.WindowsEditor;
            var names = windows ? new[] { "ffmpeg.exe", "ffmpeg" } : new[] { "ffmpeg", "ffmpeg.exe" };
            var candidates = new[]
            {
                Path.Combine(Main.Entry.Path, names[0]),
                Path.Combine(Main.Entry.Path, names[1])
            };
            foreach (var local in candidates)
            {
                if (File.Exists(local)) return local;
            }

            // Let the operating system resolve a system-installed FFmpeg from
            // PATH. This is the normal setup on macOS and Linux.
            return names[0];
        }

        internal static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "calculating...";
            var span = TimeSpan.FromSeconds(Math.Max(0.0, seconds));
            if (span.TotalHours >= 1) return span.ToString(@"h\:mm\:ss");
            return span.ToString(@"mm\:ss");
        }

        internal static string FormatFinishTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "--:--:--";
            return DateTime.Now.AddSeconds(seconds).ToString("HH:mm:ss");
        }

        private void ProcessRpcCommands()
        {
            while (rpcCommands.TryDequeue(out var command))
            {
                var render = command as RpcRenderRequest;
                if (render != null)
                {
                    if (activeRpcJob != null || Busy)
                    {
                        render.Job.Fail("A render is already in progress.");
                        continue;
                    }
                    activeRpcJob = render.Job;
                    cancellation = false;
                    activeRpcJob.SetState(RpcJobState.Loading);
                    rpcLoadRoutine = StartCoroutine(LoadRpcLevelAndStart(render.Job));
                    continue;
                }

                var cancel = command as RpcCancelRequest;
                if (cancel != null && activeRpcJob != null &&
                    string.Equals(activeRpcJob.Id, cancel.JobId, StringComparison.OrdinalIgnoreCase))
                {
                    Cancel();
                }
            }
        }

        private IEnumerator LoadRpcLevelAndStart(RpcRenderJob job)
        {
            var core = LoadRpcLevelAndStartCore(job);
            while (true)
            {
                object next = null;
                bool hasNext;
                Exception failure = null;
                try
                {
                    hasNext = core.MoveNext();
                    if (hasNext) next = core.Current;
                }
                catch (Exception ex)
                {
                    hasNext = false;
                    failure = ex;
                }
                if (failure != null)
                {
                    HandleRpcPreparationFailure(job, failure);
                    yield break;
                }
                if (!hasNext) yield break;
                yield return next;
            }
        }

        private IEnumerator LoadRpcLevelAndStartCore(RpcRenderJob job)
        {
            if (FfmpegInstaller.IsAwaitingConsent)
                throw new InvalidOperationException("FFmpeg installation requires user confirmation before an RPC render can start.");
            while (FfmpegInstaller.IsDownloading)
            {
                if (cancellation) throw new OperationCanceledException();
                yield return null;
            }
            if (!File.Exists(job.LevelPath))
                throw new FileNotFoundException("Level file does not exist.", job.LevelPath);

            var waitFrames = 0;
            var editorDeadline = Time.realtimeSinceStartup + 60f;
            var sceneRequested = false;
            Main.Entry.Logger.Log("RPC preparing level: " + job.LevelPath + ", loader=" + (scrLoader.instance != null));
            while (ADOBase.editor == null || (sceneRequested && !ADOBase.isLevelEditor))
            {
                if (cancellation) throw new OperationCanceledException();
                if (!sceneRequested && (waitFrames == 0 || waitFrames % 30 == 0)
                    && (scrLoader.instance != null || ADOBase.loader != null || ADOBase.controller != null || ADOBase.customLevel != null))
                {
                    try
                    {
                        OpenLevelEditorScene();
                        sceneRequested = true;
                        Main.Entry.Logger.Log("RPC requested scnEditor scene.");
                    }
                    catch (Exception ex)
                    {
                        Main.Entry.Logger.Log("Waiting for the game loader before opening the editor: " + ex.Message);
                    }
                }
                waitFrames++;
                if (waitFrames > 36000 || Time.realtimeSinceStartup > editorDeadline)
                    throw new TimeoutException("The level editor did not become available within 60 seconds.");
                yield return null;
            }

            var targetEditor = ADOBase.editor;
            if (targetEditor.playMode) targetEditor.SwitchToEditMode();
            var previousLevel = targetEditor.customLevel;
            targetEditor.OpenLevel(job.LevelPath);
            Main.Entry.Logger.Log("RPC dispatched editor.OpenLevel: customLevel=" + (previousLevel != null)
                + ", isLoading=" + targetEditor.isLoading);

            var sawLoading = false;
            var loaded = false;
            var loadDeadline = Time.realtimeSinceStartup + 120f;
            for (var frame = 0; frame < 72000 && Time.realtimeSinceStartup <= loadDeadline; frame++)
            {
                if (cancellation) throw new OperationCanceledException();
                yield return null;
                targetEditor = ADOBase.editor;
                if (targetEditor == null) continue;
                if (targetEditor.isLoading) sawLoading = true;
                var loadedLevel = targetEditor.customLevel;
                if (!targetEditor.isLoading && loadedLevel != null && loadedLevel.levelData != null
                    && targetEditor.floors != null && targetEditor.floors.Count > 1
                    && (sawLoading || loadedLevel != previousLevel
                        || (frame >= 5 && PathsEqual(loadedLevel.levelPath, job.LevelPath))))
                {
                    loaded = true;
                    break;
                }
            }
            if (!loaded)
            {
                var finalEditor = ADOBase.editor;
                var finalLevel = finalEditor != null ? finalEditor.customLevel : null;
                Main.Entry.Logger.Log("RPC level load state: editor=" + (finalEditor != null)
                    + ", isLoading=" + (finalEditor != null && finalEditor.isLoading)
                    + ", levelData=" + (finalLevel != null && finalLevel.levelData != null)
                    + ", floors=" + (finalEditor != null && finalEditor.floors != null ? finalEditor.floors.Count.ToString() : "null")
                    + ", previousSame=" + (finalLevel == previousLevel)
                    + ", path=" + (finalLevel != null ? finalLevel.levelPath : "null"));
                throw new TimeoutException("The requested level did not finish loading in the editor.");
            }
            if (cancellation) throw new OperationCanceledException();

            job.SetState(RpcJobState.Preparing);
            rpcLoadRoutine = null;
            StartRender();
        }

        private static void OpenLevelEditorScene()
        {
            if (scrLoader.instance != null) scrLoader.instance.GoToLevelEditor();
            else if (ADOBase.loader != null) ADOBase.loader.GoToLevelEditor();
            else if (ADOBase.controller != null) ADOBase.controller.GoToLevelEditor();
            else if (ADOBase.customLevel != null) ADOBase.customLevel.GoToLevelEditor();
            else throw new InvalidOperationException("The game loader is not ready.");
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        }

        private void HandleRpcPreparationFailure(RpcRenderJob job, Exception ex)
        {
            rpcLoadRoutine = null;
            if (ex is OperationCanceledException)
            {
                State = RenderState.Cancelled;
                Message = Localization.Get("render-cancelled");
                job.Cancel();
            }
            else
            {
                State = RenderState.Failed;
                Message = ex.Message;
                job.Fail(ex.Message);
                ShowToast(Localization.Format("render-failed-value", Message), 10f);
                Main.Entry.Logger.Error("RPC render preparation: " + ex);
            }
            activeRpcJob = null;
        }

        private void UpdateRpcJob()
        {
            var job = activeRpcJob;
            if (job == null) return;
            job.SetProgress(CapturedFrames, TotalFrames, OutputPath);
            if (rpcLoadRoutine != null) job.SetState(RpcJobState.Loading);
            else if (State == RenderState.AwaitingConfirmation) job.SetState(RpcJobState.AwaitingConfirmation, EncoderFallbackReason);
            else if (State == RenderState.Preparing) job.SetState(RpcJobState.Preparing);
            else if (State == RenderState.Rendering) job.SetState(RpcJobState.Rendering);
            else if (State == RenderState.Finishing) job.SetState(RpcJobState.Finishing);
            else if (State == RenderState.Completed)
            {
                job.SetState(RpcJobState.Completed);
                job.SetProgress(CapturedFrames, TotalFrames, OutputPath);
            }
            else if (State == RenderState.Cancelled) job.Cancel();
            else if (State == RenderState.Failed) job.Fail(Message);

            if (rpcLoadRoutine == null && routine == null && !Busy &&
                (State == RenderState.Completed || State == RenderState.Cancelled || State == RenderState.Failed))
                activeRpcJob = null;
        }

        private static void ApplyFramePacing()
        {
            // Game settings or other mods may restore a cap after editor.Play.
            // Do not use -1 here: Unity may resolve it to the display refresh
            // rate, making a 200 Hz monitor a hard render cap.
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate != RenderTargetFrameRate)
                Application.targetFrameRate = RenderTargetFrameRate;
            if (UnityEngine.Rendering.OnDemandRendering.renderFrameInterval != 1)
                UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
        }

        private void WaitForAudioFrame(long simulationFrameIndex)
        {
            if (!captureAudioForRun || !audioRealtimePacing || audioPacingOrigin <= 0.0) return;
            var target = audioPacingOrigin + simulationFrameIndex / (double)profile.TargetFps;
            while (!cancellation)
            {
                // Leave a small margin for the game frame itself. Sleeping
                // blocks only the main-thread simulation; Unity's audio thread
                // continues producing samples for the fallback capture.
                var remaining = target - Time.realtimeSinceStartupAsDouble - 0.002;
                if (remaining <= 0.0) break;
                var milliseconds = (int)Math.Floor(remaining * 1000.0);
                if (milliseconds > 0) System.Threading.Thread.Sleep(milliseconds);
                else System.Threading.Thread.Yield();
            }
        }
        private void Fail(Exception ex)
        {
            State = RenderState.Failed;
            Message = ex.Message;
            ShowToast(Localization.Format("render-failed-value", Message), 10f);
            Main.Entry.Logger.Error(ex.ToString());
        }
        internal void AbortWithError(Exception ex) { Fail(ex); StopAndClean(); }
        public void StopAndClean()
        {
            encoderFallbackPending = false;
            encoderFallbackReason = null;
            if (rpcLoadRoutine != null) { StopCoroutine(rpcLoadRoutine); rpcLoadRoutine = null; }
            if (routine != null) { StopCoroutine(routine); routine = null; }
            if (Busy)
            {
                State = RenderState.Cancelled;
                Message = Localization.Get("render-cancelled");
            }
            Cleanup();
        }
        private void Cleanup()
        {
            // Clear patch ownership before calling any normal game reset methods.
            var restore = saved;
            var recoveryEditor = editor;
            var recoveryLevel = level;
            saved = null;
            TryCleanup(RestoreInputOffsetAfterRender);
            renderTimer.Stop();
            TryCleanup(() => capture?.Dispose()); capture = null;
            TryCleanup(() => encoder?.Dispose()); encoder = null;
            TryCleanup(() => audio?.Dispose()); audio = null;
            TryCleanup(() => planetRings?.Dispose()); planetRings = null;
            TryCleanup(() => bga?.Dispose()); bga = null;
            TryCleanup(() => defaultText?.Dispose()); defaultText = null;
            TryCleanup(RestoreRenderPerformance);
            if (restore != null)
            {
                // Reset playback with the user's autoplay setting, otherwise Play
                // would retain renderer fast-takeoff flags in the restored session.
                TryCleanup(restore.RestoreTiming);
                // Keep the fully decoded song clip attached. The original
                // external streaming clip reports length zero after Unity's
                // audio-device reset and cannot be reused by editor playback.
                TryCleanup(restore.RestoreAudioSources);
                TryCleanup(() => {
                    var conductor = ADOBase.conductor;
                    if (conductor != null) {
                        var handle = AccessTools.Field(typeof(scrConductor), "startMusicCoroutine").GetValue(conductor) as Coroutine;
                        if (handle != null) conductor.StopCoroutine(handle);
                        conductor.Rewind();
                        conductor.song?.Stop(); conductor.song2?.Stop(); conductor.song3?.Stop();
                    }
                    if (recoveryEditor == null && recoveryLevel != null
                        && ADOBase.customLevel == recoveryLevel && ADOBase.controller != null) {
                        recoveryLevel.ResetScene();
                        recoveryLevel.Play(0); // Return to the game's normal press-to-start preparation.
                    }
                });
                TryCleanup(restore.Restore);
                if (recoveryEditor != null && recoveryLevel != null)
                    TryCleanup(() => StartEditorRecovery(recoveryEditor, recoveryLevel, restore));
            }
            TryCleanup(DisposePendingSongRequest);
            if (State != RenderState.Completed && !string.IsNullOrEmpty(partialPath))
                TryCleanup(() => { if (File.Exists(partialPath)) File.Delete(partialPath); });
            foreach (var temporary in new[] { audioPath, muxPath })
                if (!string.IsNullOrEmpty(temporary)) TryCleanup(() => { if (File.Exists(temporary)) File.Delete(temporary); });
            ClearQueuedInput();
        }

        private void StartEditorRecovery(scnEditor targetEditor, scnGame targetLevel, SavedState restore)
        {
            if (shuttingDown || targetEditor == null || targetLevel == null) return;
            if (editorRecoveryRoutine != null) StopCoroutine(editorRecoveryRoutine);
            editorStateReady = false;
            editorRecoveryRoutine = StartCoroutine(RecoverEditorAfterRender(targetEditor, targetLevel, restore));
        }

        private IEnumerator RecoverEditorAfterRender(scnEditor targetEditor, scnGame targetLevel, SavedState restore)
        {
            LogRenderChain("before editor recovery", targetLevel);
            RebuildEditorRenderState(targetEditor, targetLevel);
            yield return null;

            if (!IsEditorRenderStateValid(targetLevel, out var reason))
            {
                Main.Entry.Logger.Log("Editor render state validation failed; rebuilding once: " + reason);
                RebuildEditorRenderState(targetEditor, targetLevel);
                yield return null;
            }

            restore?.RestoreEditorState();
            editorStateReady = IsEditorRenderStateValid(targetLevel, out reason);
            LogRenderChain("after editor recovery", targetLevel);
            if (!editorStateReady)
                Main.Entry.Logger.Error("Editor render state is still invalid after recovery: " + reason);
            editorRecoveryRoutine = null;
        }

        private static void RebuildEditorRenderState(scnEditor targetEditor, scnGame targetLevel)
        {
            targetLevel.ResetScene();
            targetEditor.SwitchToEditMode();
            targetLevel.ReloadAssets(force: true, reloadDecorations: true);
            targetEditor.UpdateDecorationObjects();
            RepairInternalCameraTarget();
        }

        private static void RepairInternalCameraTarget()
        {
            var camera = scrCamera.instance;
            if (camera == null) return;
            SyncBackgroundCameras(camera);

            // SetCustomFrameRate(false) releases the texture currently shown by
            // the camera quad. After a custom-FPS event that texture is camRT,
            // so a later ResetScene can leave the same object assigned but no
            // longer created. Recreate it without replacing level/editor data.
            var field = AccessTools.Field(typeof(scrCamera), "camRT");
            var target = field?.GetValue(camera) as RenderTexture;
            if (target == null && field != null)
            {
                target = new RenderTexture(Math.Max(1, Screen.width), Math.Max(1, Screen.height), 24) {
                    name = "ADOFAI Camera RT"
                };
                field.SetValue(camera, target);
            }
            if (target != null && !target.IsCreated() && !target.Create())
                throw new InvalidOperationException("The game camera render texture could not be recreated.");

            if (target != null && camera.quad != null)
            {
                var renderer = camera.quad.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material.mainTexture != target)
                    renderer.material.mainTexture = target;
            }
            camera.SetupRTCam(false);
        }

        private static void SyncBackgroundCameras(scrCamera camera)
        {
            if (camera == null || camera.camobj == null) return;
            var position = camera.camobj.transform.position;
            var rotation = camera.camobj.transform.rotation;
            if (camera.Bgcamstatic != null)
                camera.Bgcamstatic.transform.SetPositionAndRotation(position, rotation);
            if (camera.BGcam != null)
                camera.BGcam.transform.SetPositionAndRotation(position, rotation);
        }

        private static bool IsEditorRenderStateValid(scnGame targetLevel, out string reason)
        {
            var camera = scrCamera.instance;
            if (camera == null || camera.Bgcamstatic == null || camera.BGcam == null || camera.camobj == null)
            {
                reason = "camera chain is missing";
                return false;
            }
            foreach (var item in new[] { camera.Bgcamstatic, camera.BGcam, camera.camobj })
            {
                if (!item.enabled || !item.gameObject.activeInHierarchy || item.cullingMask == 0)
                {
                    reason = item.name + " is disabled, inactive, or has an empty culling mask";
                    return false;
                }
                if (item.targetTexture != null)
                {
                    reason = item.name + " still targets a render texture in edit mode";
                    return false;
                }
            }
            if ((camera.Bgcamstatic.transform.position - camera.camobj.transform.position).sqrMagnitude > 0.0001f
                || (camera.BGcam.transform.position - camera.camobj.transform.position).sqrMagnitude > 0.0001f)
            {
                reason = "background camera transform does not match the gameplay camera";
                return false;
            }

            var target = AccessTools.Field(typeof(scrCamera), "camRT")?.GetValue(camera) as RenderTexture;
            if (target == null || !target.IsCreated())
            {
                reason = "internal camera render texture is released";
                return false;
            }
            if (camera.flashPlusRendererBg == null || camera.flashPlusRendererFg == null
                || !camera.flashPlusRendererBg.enabled || !camera.flashPlusRendererFg.enabled)
            {
                reason = "Flash Plus background/foreground renderer is unavailable";
                return false;
            }

            // The editor may intentionally keep its default background hidden.
            // Bgcamstatic clears to SolidColor in that configuration, so the
            // absence of a visible default-background renderer is not a broken
            // render state and must not prevent export.
            reason = null;
            return true;
        }

        private static GameObject FindDefaultBackground(scnGame targetLevel)
        {
            if (targetLevel == null) return null;
            var custom = AccessTools.Field(typeof(scnGame), "customEditorBG")?.GetValue(targetLevel) as GameObject;
            if (custom != null) return custom;
            return AccessTools.Field(typeof(scnGame), "editorBG")?.GetValue(targetLevel) as GameObject;
        }

        private static void LogRenderChain(string stage, scnGame targetLevel)
        {
            var camera = scrCamera.instance;
            if (camera == null)
            {
                Main.Entry.Logger.Log("Render chain [" + stage + "]: <missing>");
                return;
            }
            var target = AccessTools.Field(typeof(scrCamera), "camRT")?.GetValue(camera) as RenderTexture;
            var background = FindDefaultBackground(targetLevel);
            var backgroundRenderers = background != null
                ? background.GetComponentsInChildren<UnityEngine.Renderer>(true)
                : new UnityEngine.Renderer[0];
            Main.Entry.Logger.Log("Render chain [" + stage + "]: "
                + DescribeCamera("Bgcamstatic", camera.Bgcamstatic) + "; "
                + DescribeCamera("BGcam", camera.BGcam) + "; "
                + DescribeCamera("camobj", camera.camobj) + "; camRT="
                + (target == null ? "missing" : target.width + "x" + target.height + "/created=" + target.IsCreated())
                + "; useRTCam=" + ReadBoolField(camera, "useRTCam")
                + ", customFPS=" + camera.enableCustomFPS
                + ", forceRTCam=" + camera.forceRTCam
                + ", lockCustomFrameUpdate=" + camera.lockCustomFrameUpdate
                + ", hom=" + (ADOBase.controller != null && ADOBase.controller.homEnabled)
                + "; " + DescribeRenderer("flashBg", camera.flashPlusRendererBg)
                + "; " + DescribeRenderer("flashFg", camera.flashPlusRendererFg)
                + "; defaultBG(active=" + (background != null && background.activeInHierarchy)
                + ", renderers=" + backgroundRenderers.Length
                + ", visible=" + backgroundRenderers.Count(item => item.enabled && item.gameObject.activeInHierarchy) + ")");
        }

        private static bool ReadBoolField(object instance, string name)
        {
            var field = instance != null ? AccessTools.Field(instance.GetType(), name) : null;
            return field != null && (bool)field.GetValue(instance);
        }

        private static string DescribeCamera(string name, Camera camera)
        {
            if (camera == null) return name + "=<missing>";
            return name + "(active=" + camera.gameObject.activeInHierarchy + ", target="
                + (camera.targetTexture == null ? "screen" : camera.targetTexture.name)
                + ", enabled=" + camera.enabled + ", mask=0x" + camera.cullingMask.ToString("X8")
                + ", clear=" + camera.clearFlags + ", depth=" + camera.depth.ToString("F1")
                + ", ortho=" + camera.orthographicSize.ToString("F2")
                + ", pos=" + camera.transform.position
                + ", rect=" + camera.rect
                + ", effects=" + string.Join(",", camera.GetComponents<MonoBehaviour>()
                    .Where(item => item != null && item.enabled).Select(item => item.GetType().Name).ToArray()) + ")";
        }

        private static string DescribeRenderer(string name, UnityEngine.Renderer renderer)
        {
            if (renderer == null) return name + "=<missing>";
            return name + "(active=" + renderer.gameObject.activeInHierarchy + ", enabled=" + renderer.enabled
                + ", layer=" + renderer.gameObject.layer + ", color=" + renderer.material.color + ")";
        }
        private static void ClearQueuedInput()
        {
            try { AsyncInputManager.ClearKeys(); } catch { }
        }
        private void TryCleanup(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Main.Entry.Logger.Error("Cleanup: " + ex);
                State = RenderState.Failed;
                Message = Localization.Format("cleanup-failed-value", ex.Message);
            }
        }
        private void OnDestroy()
        {
            shuttingDown = true;
            StopAndClean();
            TryCleanup(DisposePendingSongRequest);
            TryCleanup(DisposeSongRequest);
            if (Instance == this) Instance = null;
        }
        private void OnApplicationQuit()
        {
            shuttingDown = true;
            StopAndClean();
            TryCleanup(DisposePendingSongRequest);
            TryCleanup(DisposeSongRequest);
        }
        internal static string SanitizeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var name = new string((value ?? "Level").Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
            return "Render_" + (string.IsNullOrEmpty(name) ? "Level" : name.Substring(0, Math.Min(80, name.Length)));
        }
        private sealed class SavedState
        {
            private readonly int captureRate = Time.captureFramerate, targetRate = Application.targetFrameRate, vsync = QualitySettings.vSyncCount, checkpoint = GCS.checkpointNum;
            private readonly float timeScale = Time.timeScale, volume = AudioListener.volume;
            private readonly bool auto = RDC.auto, noFail = ADOBase.controller.noFail, pauseAudio = AudioListener.pause, background = Application.runInBackground;
            private readonly bool smoothTweens = DG.Tweening.DOTween.useSmoothDeltaTime;
            private readonly bool noHud = RDC.noHud, noAutoHud = RDC.noAutoHud;
            private readonly int renderInterval = UnityEngine.Rendering.OnDemandRendering.renderFrameInterval;
            private readonly bool wasPaused = ADOBase.controller.paused, controllerEnabled = ADOBase.controller.enabled;
            private readonly SkipIntroBehavior intro = Persistence.skipIntroBehavior;
            private readonly bool strictlyEditing = ADOBase.editor != null && ADOBase.editor.inStrictlyEditingMode;
            private readonly int[] selection = ADOBase.editor != null ? ADOBase.editor.selectedFloors.Select(f => f.seqID).ToArray() : new int[0];
            private readonly AudioConfiguration audioConfiguration = AudioSettings.GetConfiguration();
            private readonly AudioSource song = ADOBase.conductor != null ? ADOBase.conductor.song : null;
            private readonly AudioSource song2 = ADOBase.conductor != null ? ADOBase.conductor.song2 : null;
            private readonly AudioSource song3 = ADOBase.conductor != null ? ADOBase.conductor.song3 : null;
            private readonly float songVolume = ADOBase.conductor != null && ADOBase.conductor.song != null
                ? ADOBase.conductor.song.volume : 0f;
            private readonly float song2Volume = ADOBase.conductor != null && ADOBase.conductor.song2 != null
                ? ADOBase.conductor.song2.volume : 0f;
            private readonly float song3Volume = ADOBase.conductor != null && ADOBase.conductor.song3 != null
                ? ADOBase.conductor.song3.volume : 0f;
            private readonly float songPitch = ADOBase.conductor != null && ADOBase.conductor.song != null
                ? ADOBase.conductor.song.pitch : 1f;
            private readonly float song2Pitch = ADOBase.conductor != null && ADOBase.conductor.song2 != null
                ? ADOBase.conductor.song2.pitch : 1f;
            private readonly float song3Pitch = ADOBase.conductor != null && ADOBase.conductor.song3 != null
                ? ADOBase.conductor.song3.pitch : 1f;
            public void Restore()
            {
                RestoreTiming();
                // Rendering always returns to editing, even if it was requested
                // during playback. Restoring the old unpaused flag here would
                // incorrectly turn editor.playMode back on with its conductor off.
                if (ADOBase.controller != null && ADOBase.editor == null)
                {
                    ADOBase.controller.paused = wasPaused;
                    ADOBase.controller.enabled = controllerEnabled;
                }
                RestoreEditorState();
            }
            public void RestoreEditorState()
            {
                var editor = ADOBase.editor;
                if (editor != null)
                {
                    // SwitchToEditMode() used during cleanup puts the editor
                    // into strict-editing mode. Restore the exact flag from
                    // before the render; otherwise the next render starts on
                    // a different editor path and level camera filters can
                    // retain a stale effect state.
                    editor.inStrictlyEditingMode = strictlyEditing;
                    if (selection.Length > 0 && editor.floors.Count > selection.Max())
                    {
                        if (selection.Length == 1) editor.SelectFloor(editor.floors[selection[0]], cameraJump: false);
                        else editor.MultiSelectFloors(editor.floors[selection.Min()], editor.floors[selection.Max()], setSelectPoint: true);
                    }
                }
            }
            public void RestoreTiming()
            {
                Time.captureFramerate = captureRate; Time.timeScale = timeScale;
                DG.Tweening.DOTween.useSmoothDeltaTime = smoothTweens;
                UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = renderInterval;
                Application.targetFrameRate = targetRate; QualitySettings.vSyncCount = vsync;
                Application.runInBackground = background;
                var currentAudio = AudioSettings.GetConfiguration();
                if (currentAudio.sampleRate != audioConfiguration.sampleRate
                    || currentAudio.dspBufferSize != audioConfiguration.dspBufferSize
                    || currentAudio.numRealVoices != audioConfiguration.numRealVoices
                    || currentAudio.numVirtualVoices != audioConfiguration.numVirtualVoices
                    || currentAudio.speakerMode != audioConfiguration.speakerMode)
                    AudioSettings.Reset(audioConfiguration);
                // AudioSettings.Reset can recreate the Unity audio device, so
                // apply the listener state after the reset rather than before
                // it. Otherwise the editor may remain paused after rendering.
                AudioListener.volume = volume; AudioListener.pause = pauseAudio;
                RDC.auto = auto; GCS.checkpointNum = checkpoint; Persistence.skipIntroBehavior = intro;
                RDC.noHud = noHud; RDC.noAutoHud = noAutoHud;
                if (ADOBase.controller != null) ADOBase.controller.noFail = noFail;
            }
            public void RestoreAudioSources()
            {
                RestoreAudioSource(song, songVolume, songPitch);
                RestoreAudioSource(song2, song2Volume, song2Pitch);
                RestoreAudioSource(song3, song3Volume, song3Pitch);
            }
            private static void RestoreAudioSource(AudioSource source, float volume, float pitch)
            {
                if (source == null) return;
                source.Stop();
                source.volume = volume;
                source.pitch = pitch;
                if (source.clip != null) source.time = 0f;
                Main.Entry.Logger.Log(string.Format(
                    "Restored audio source: clip={0}, length={1:F2}s, volume={2:F3}, pitch={3:F3}.",
                    source.clip != null ? source.clip.name : "<none>",
                    source.clip != null ? source.clip.length : 0.0f, volume, pitch));
            }
        }
    }
}
