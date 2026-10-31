using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OrbitRender;

namespace OrbitRender.Renderer
{
    internal static class RenderErrorCodes
    {
        internal const string RendererUnavailable = "renderer_unavailable";
        internal const string Busy = "render_busy";
        internal const string LevelPathMissing = "render_level_path_missing";
        internal const string LevelPathInvalid = "render_level_path_invalid";
        internal const string LevelMissing = "render_level_missing";
        internal const string LevelMismatch = "render_level_mismatch";
        internal const string LevelFileMissing = "level_file_missing";
        internal const string OptionsInvalid = "render_options_invalid";
        internal const string ManifestMissing = "render_manifest_missing";
        internal const string ManifestInvalid = "render_manifest_invalid";
        internal const string JobUnknown = "render_job_unknown";
        internal const string JobFinished = "render_job_finished";
        internal const string Failed = "render_failed";
        internal const string Cancelled = "render_cancelled";
        internal const string Internal = "render_internal_error";
        internal const string WrongThread = "render_wrong_thread";
    }

    /// <summary>A structured failure that can cross the reflection/JSON boundary.</summary>
    internal sealed class RenderErrorDetails
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("field")] public string Field { get; set; }
        [JsonProperty("line")] public int? Line { get; set; }
        [JsonProperty("file")] public string File { get; set; }

        internal static RenderErrorDetails Create(string code, string message, string field = null,
            int? line = null, string file = null)
        {
            return new RenderErrorDetails
            {
                Code = string.IsNullOrWhiteSpace(code) ? RenderErrorCodes.Failed : code,
                Message = message,
                Field = field,
                Line = line,
                File = file
            };
        }

        internal static RenderErrorDetails From(TUFReplayRenderer.Contracts.RecordingFormatException exception)
        {
            return Create(exception.Code, exception.Message, exception.Field, exception.Line, exception.File);
        }
    }

    internal sealed class RpcRenderRequest
    {
        public RpcRenderJob Job;
    }

    internal sealed class RpcCancelRequest
    {
        public string JobId;
    }

    internal sealed class RpcRenderOptions
    {
        public RendererPreset? Preset;
        public int? Width;
        public int? Height;
        public int? TargetFps;
        public int? VideoFps;
        public int? BitrateMbps;
        public float? EndDelaySeconds;
        public float? AudioGainDb;
        public AudioCodec? AudioCodec;
        public bool? BgaMode;
        public bool? ShowPlanetRings;
        public bool? ShowSongTitle;
        public bool? ShowCountdown;
        public bool? ShowResultText;
        public bool? ShowHitJudgments;
        public bool? ShowModOverlay;
        public VideoCodec? VideoCodec;
        public VideoBitDepth? BitDepth;

        public bool HasValues
        {
            get
            {
                return Preset.HasValue || Width.HasValue || Height.HasValue || TargetFps.HasValue || VideoFps.HasValue
                    || BitrateMbps.HasValue || EndDelaySeconds.HasValue || AudioGainDb.HasValue || AudioCodec.HasValue || BgaMode.HasValue
                    || ShowPlanetRings.HasValue
                    || ShowSongTitle.HasValue || ShowCountdown.HasValue || ShowResultText.HasValue
                    || ShowHitJudgments.HasValue || ShowModOverlay.HasValue
                    || VideoCodec.HasValue || BitDepth.HasValue;
            }
        }

        public object Snapshot()
        {
            return new
            {
                preset = Preset.HasValue ? Preset.Value.ToString() : null,
                width = Width,
                height = Height,
                targetFps = TargetFps,
                videoFps = VideoFps,
                fps = VideoFps,
                bitrateMbps = BitrateMbps,
                endDelaySeconds = EndDelaySeconds,
                audioGainDb = AudioGainDb,
                audioCodec = AudioCodec.HasValue ? AudioCodec.Value.ToString() : null,
                bgaMode = BgaMode,
                showPlanetRings = ShowPlanetRings,
                showSongTitle = ShowSongTitle,
                showCountdown = ShowCountdown,
                showResultText = ShowResultText,
                showHitJudgments = ShowHitJudgments,
                showModOverlay = ShowModOverlay,
                videoCodec = VideoCodec.HasValue ? VideoCodec.Value.ToString() : null,
                bitDepth = BitDepth.HasValue ? (int)(BitDepth.Value == VideoBitDepth.Ten ? 10 : 8) : (int?)null
            };
        }
    }

    internal enum RpcJobState
    {
        Queued,
        Loading,
        Preparing,
        Rendering,
        Finishing,
        AwaitingConfirmation,
        Completed,
        Failed,
        Cancelled
    }

    internal sealed class RenderJobSnapshot
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("levelPath")] public string LevelPath { get; set; }
        [JsonProperty("settings")] public object Settings { get; set; }
        [JsonProperty("outputPath")] public string OutputPath { get; set; }
        [JsonProperty("totalFrames")] public long TotalFrames { get; set; }
        [JsonProperty("capturedFrames")] public long CapturedFrames { get; set; }
        [JsonProperty("progress")] public double Progress { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("warnings")] public string[] Warnings { get; set; }
        [JsonProperty("waitingForGameFocus")] public bool WaitingForGameFocus { get; set; }
        // Kept alongside errorMessage so older HTTP clients keep working.
        [JsonProperty("error")] public string Error { get; set; }
        [JsonProperty("errorMessage")] public string ErrorMessage { get; set; }
        [JsonProperty("errorCode")] public string ErrorCode { get; set; }
        [JsonProperty("errorDetails")] public RenderErrorDetails ErrorDetails { get; set; }
        [JsonProperty("updatedUtc")] public string UpdatedUtc { get; set; }
    }

    internal sealed class RpcRenderPayload
    {
        [JsonProperty("levelPath")] public string LevelPath { get; set; }
        [JsonProperty("replayManifestPath")] public string ReplayManifestPath { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("preset")] public string Preset { get; set; }
        [JsonProperty("width")] public int? Width { get; set; }
        [JsonProperty("height")] public int? Height { get; set; }
        [JsonProperty("fps")] public int? Fps { get; set; }
        [JsonProperty("targetFps")] public int? TargetFps { get; set; }
        [JsonProperty("videoFps")] public int? VideoFps { get; set; }
        [JsonProperty("bitrateMbps")] public int? BitrateMbps { get; set; }
        [JsonProperty("bitrate")] public int? Bitrate { get; set; }
        [JsonProperty("endDelaySeconds")] public float? EndDelaySeconds { get; set; }
        [JsonProperty("audioGainDb")] public float? AudioGainDb { get; set; }
        [JsonProperty("audioCodec")] public string AudioCodec { get; set; }
        [JsonProperty("bgaMode")] public bool? BgaMode { get; set; }
        [JsonProperty("showPlanetRings")] public bool? ShowPlanetRings { get; set; }
        [JsonProperty("showSongTitle")] public bool? ShowSongTitle { get; set; }
        [JsonProperty("showCountdown")] public bool? ShowCountdown { get; set; }
        [JsonProperty("showResultText")] public bool? ShowResultText { get; set; }
        [JsonProperty("showHitJudgments")] public bool? ShowHitJudgments { get; set; }
        [JsonProperty("showModOverlay")] public bool? ShowModOverlay { get; set; }
        [JsonProperty("videoCodec")] public string VideoCodec { get; set; }
        [JsonProperty("codec")] public string Codec { get; set; }
        [JsonProperty("bitDepth")] public int? BitDepth { get; set; }
        [JsonProperty("captureAudio")] public bool? CaptureAudio { get; set; }
        [JsonProperty("audio")] public bool? Audio { get; set; }
    }

    internal sealed class RpcRenderJob
    {
        private readonly object sync = new object();
        private RpcJobState state = RpcJobState.Queued;
        private string message;
        private string errorMessage;
        private RenderErrorDetails errorDetails;
        private string[] warnings = Array.Empty<string>();
        private string outputPath;
        private long totalFrames;
        private long capturedFrames;
        private DateTime updatedUtc = DateTime.UtcNow;

        public RpcRenderJob(string id, string levelPath, bool captureAudio, RpcRenderOptions options)
            : this(id, levelPath, captureAudio, options, null)
        {
        }

        public RpcRenderJob(string id, string levelPath, bool captureAudio, RpcRenderOptions options,
            string replayManifestPath)
        {
            Id = id;
            LevelPath = levelPath;
            CaptureAudio = captureAudio;
            Options = options;
            ReplayManifestPath = replayManifestPath;
        }

        public string Id { get; }
        public string LevelPath { get; }
        public string ReplayManifestPath { get; }
        public bool CaptureAudio { get; }
        public RpcRenderOptions Options { get; }

        public void SetState(RpcJobState value, string info = null)
        {
            lock (sync)
            {
                // Job state only moves forward. A late callback from a finished
                // render must not resurrect a terminal job for the caller.
                if (IsTerminalState(state) && !IsTerminalState(value)) return;
                state = value;
                message = info;
                updatedUtc = DateTime.UtcNow;
            }
        }

        public void SetProgress(long captured, long total, string output)
        {
            lock (sync)
            {
                capturedFrames = captured;
                totalFrames = total;
                if (!string.IsNullOrEmpty(output)) outputPath = output;
                updatedUtc = DateTime.UtcNow;
            }
        }

        public void SetWarnings(IEnumerable<string> values)
        {
            lock (sync) warnings = values == null ? Array.Empty<string>() : values.Where(value => !string.IsNullOrEmpty(value)).ToArray();
        }

        public void Fail(string error, string code = null, RenderErrorDetails details = null)
        {
            lock (sync)
            {
                if (IsTerminalState(state)) return;
                state = RpcJobState.Failed;
                message = error;
                errorMessage = error;
                errorDetails = details ?? RenderErrorDetails.Create(code, error);
                updatedUtc = DateTime.UtcNow;
            }
        }

        public void Fail(RenderErrorDetails error)
        {
            Fail(error?.Message, error?.Code ?? RenderErrorCodes.Failed, error);
        }

        public void Cancel()
        {
            lock (sync)
            {
                if (IsTerminalState(state)) return;
                state = RpcJobState.Cancelled;
                errorMessage = null;
                errorDetails = null;
                updatedUtc = DateTime.UtcNow;
            }
        }

        public RpcJobState State
        {
            get { lock (sync) return state; }
        }

        public bool IsTerminal
        {
            get { lock (sync) return IsTerminalState(state); }
        }

        internal DateTime UpdatedUtc
        {
            get { lock (sync) return updatedUtc; }
        }

        public string OutputPath
        {
            get { lock (sync) return outputPath; }
        }

        internal string CompletedOutputPath
        {
            get { lock (sync) return state == RpcJobState.Completed ? outputPath : null; }
        }

        public RenderJobSnapshot Snapshot()
        {
            lock (sync)
            {
                return new RenderJobSnapshot
                {
                    Id = Id,
                    State = state.ToString().ToLowerInvariant(),
                    LevelPath = LevelPath,
                    Settings = Options?.Snapshot(),
                    OutputPath = outputPath,
                    TotalFrames = totalFrames,
                    CapturedFrames = capturedFrames,
                    Progress = totalFrames > 0 ? Math.Min(1.0, capturedFrames / (double)totalFrames) : 0.0,
                    Message = message,
                    Warnings = warnings,
                    WaitingForGameFocus = state == RpcJobState.AwaitingConfirmation,
                    Error = errorMessage ?? message,
                    ErrorMessage = errorMessage,
                    ErrorCode = errorDetails?.Code,
                    ErrorDetails = errorDetails,
                    UpdatedUtc = updatedUtc.ToString("o")
                };
            }
        }

        private static bool IsTerminalState(RpcJobState value)
        {
            return value == RpcJobState.Completed || value == RpcJobState.Failed || value == RpcJobState.Cancelled;
        }
    }

    /// <summary>
    /// Owns render job lifecycle for every front end: the HTTP RPC listener, the
    /// optional ADOFAI IPC mod, and the C# <see cref="RenderApi"/> facade used by
    /// other mods. Opening the recorded level, rendering, and cleanup stay in
    /// <see cref="RendererController"/>; this type only owns job identity, state
    /// snapshots, and duplicate-start protection.
    /// </summary>
    internal sealed class RenderJobService
    {
        private static readonly TimeSpan TerminalRetention = TimeSpan.FromHours(1);
        private const int MaxJobs = 64;

        private readonly object createGate = new object();
        private readonly ConcurrentDictionary<string, RpcRenderJob> jobs =
            new ConcurrentDictionary<string, RpcRenderJob>(StringComparer.OrdinalIgnoreCase);
        private volatile RendererController controller;

        public RenderJobService(RendererController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        internal RendererController Controller => controller;

        internal bool IsBusy
        {
            get
            {
                var current = controller;
                if (current != null && current.Busy) return true;
                return jobs.Values.Any(job => !job.IsTerminal);
            }
        }

        public void Rebind(RendererController replacement)
        {
            if (replacement == null) return;
            controller = replacement;
            foreach (var job in jobs.Values)
            {
                if (job.State != RpcJobState.Queued && job.State != RpcJobState.Loading) continue;
                job.SetState(RpcJobState.Queued);
                replacement.EnqueueRpcRender(new RpcRenderRequest { Job = job });
            }
        }

        internal object Health()
        {
            var current = controller;
            if (current == null) return new { ok = false, renderer = "unavailable" };
            return new { ok = true, renderer = current.State.ToString().ToLowerInvariant() };
        }

        internal object ListJobs()
        {
            Prune();
            return jobs.Values.Select(job => job.Snapshot()).ToArray();
        }

        internal RpcRenderJob FindJob(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !jobs.TryGetValue(id, out var job))
                throw new ArgumentException("Unknown render job.");
            return job;
        }

        internal RenderJobSnapshot Read(string id)
        {
            return FindJob(id).Snapshot();
        }

        internal object CancelJob(string id)
        {
            var job = FindJob(id);
            if (job.IsTerminal) throw new InvalidOperationException("Render job has already finished.");
            var current = controller;
            if (current != null) current.EnqueueRpcCancel(new RpcCancelRequest { JobId = job.Id });
            return new { id = job.Id, state = "cancelling" };
        }

        internal RenderJobSnapshot Cancel(string id)
        {
            CancelJob(id);
            return FindJob(id).Snapshot();
        }

        /// <summary>Legacy response shape used by the HTTP listener and the ADOFAI IPC mod.</summary>
        internal object CreateJob(string body, out int status)
        {
            RpcRenderPayload payload;
            try { payload = JsonConvert.DeserializeObject<RpcRenderPayload>(body ?? "{}"); }
            catch (JsonException exception)
            {
                status = 400;
                return new { error = "Invalid JSON: " + exception.Message };
            }
            if (!TryCreate(payload, out var job, out var error))
            {
                status = error.Code == RenderErrorCodes.Busy ? 409 : 400;
                return new { error = error.Message };
            }
            status = 202;
            return new
            {
                id = job.Id,
                state = "queued",
                statusUrl = "/render/" + job.Id,
                downloadUrl = "/render/" + job.Id + "/download"
            };
        }

        /// <summary>
        /// Validates the request, resolves the recorded level, and queues a job.
        /// Returns false with a structured error instead of throwing so the
        /// reflection boundary stays exception-free.
        /// </summary>
        internal bool TryCreate(RpcRenderPayload payload, out RenderJobSnapshot job, out RenderErrorDetails error)
        {
            job = null;
            error = null;
            string levelPath = payload?.LevelPath ?? payload?.Path;
            string manifestPath = payload?.ReplayManifestPath;
            string[] warnings = Array.Empty<string>();

            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                TUFReplayRenderer.Contracts.RecordingBundle bundle;
                try { bundle = TUFReplayRenderer.Contracts.RecordingBundle.Load(manifestPath); }
                catch (TUFReplayRenderer.Contracts.RecordingFormatException exception)
                {
                    error = RenderErrorDetails.From(exception);
                    return false;
                }
                catch (Exception exception)
                {
                    error = RenderErrorDetails.Create(RenderErrorCodes.ManifestInvalid,
                        "Invalid replay bundle: " + exception.Message, "replayManifestPath", file: "manifest.json");
                    return false;
                }
                string replayLevel;
                try { replayLevel = bundle.ResolveLevelPath(); }
                catch (TUFReplayRenderer.Contracts.RecordingFormatException exception)
                {
                    error = RenderErrorDetails.From(exception);
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(levelPath) && !PathEquals(levelPath, replayLevel))
                {
                    error = RenderErrorDetails.Create(RenderErrorCodes.LevelMismatch,
                        "The replay does not match the requested level.", "levelPath", file: "manifest.json");
                    return false;
                }
                levelPath = replayLevel;
                warnings = bundle.Manifest.Warnings ?? Array.Empty<string>();
            }

            if (string.IsNullOrWhiteSpace(levelPath))
            {
                error = RenderErrorDetails.Create(RenderErrorCodes.LevelPathMissing,
                    "The level file path is missing.", "levelPath");
                return false;
            }
            try { levelPath = Path.GetFullPath(levelPath); }
            catch (Exception exception)
            {
                error = RenderErrorDetails.Create(RenderErrorCodes.LevelPathInvalid,
                    "Invalid level path: " + exception.Message, "levelPath");
                return false;
            }
            if (!File.Exists(levelPath))
            {
                error = RenderErrorDetails.Create(RenderErrorCodes.LevelMissing,
                    "Level file does not exist: " + levelPath, "levelPath", file: Path.GetFileName(levelPath));
                return false;
            }
            var options = ParseOptions(payload, out error);
            if (error != null) return false;

            lock (createGate)
            {
                Prune();
                if (IsBusy)
                {
                    error = RenderErrorDetails.Create(RenderErrorCodes.Busy, "A render is already in progress.");
                    return false;
                }
                var current = controller;
                if (current == null)
                {
                    error = RenderErrorDetails.Create(RenderErrorCodes.RendererUnavailable,
                        "OrbitRender is not available. Enable it in UnityModManager and try again.");
                    return false;
                }
                var id = Guid.NewGuid().ToString("N");
                var renderJob = new RpcRenderJob(id, levelPath,
                    payload.CaptureAudio ?? payload.Audio ?? (Main.Settings == null || Main.Settings.CaptureAudio),
                    options, manifestPath);
                if (warnings.Length > 0) renderJob.SetWarnings(warnings);
                jobs[id] = renderJob;
                current.EnqueueRpcRender(new RpcRenderRequest { Job = renderJob });
                job = renderJob.Snapshot();
                return true;
            }
        }

        private void Prune()
        {
            var cutoff = DateTime.UtcNow - TerminalRetention;
            foreach (var pair in jobs)
            {
                if (!pair.Value.IsTerminal || pair.Value.UpdatedUtc >= cutoff) continue;
                jobs.TryRemove(pair.Key, out _);
            }
            if (jobs.Count <= MaxJobs) return;
            foreach (var job in jobs.Values.Where(candidate => candidate.IsTerminal)
                         .OrderBy(candidate => candidate.UpdatedUtc).ToArray())
            {
                if (jobs.Count <= MaxJobs) break;
                jobs.TryRemove(job.Id, out _);
            }
        }

        private static bool PathEquals(string left, string right)
        {
            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
                    Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException
                || exception is PathTooLongException)
            {
                return false;
            }
        }

        private static RpcRenderOptions ParseOptions(RpcRenderPayload payload, out RenderErrorDetails error)
        {
            error = null;
            if (payload == null) return null;
            RendererPreset? preset = null;
            if (!string.IsNullOrWhiteSpace(payload.Preset))
            {
                if (!TryParsePreset(payload.Preset, out var parsed))
                {
                    error = InvalidOption("preset", "Custom, Preview, FullHD, QHD, or UHD4K");
                    return null;
                }
                preset = parsed;
            }
            var targetFps = payload.TargetFps;
            var videoFps = payload.VideoFps ?? payload.Fps;
            var bitrate = payload.BitrateMbps ?? payload.Bitrate;
            if (payload.Width.HasValue && (payload.Width.Value < 320 || payload.Width.Value > 3840))
            {
                error = InvalidOption("width", "320..3840");
                return null;
            }
            if (payload.Height.HasValue && (payload.Height.Value < 180 || payload.Height.Value > 2160))
            {
                error = InvalidOption("height", "180..2160");
                return null;
            }
            if (payload.VideoFps.HasValue && payload.Fps.HasValue && payload.VideoFps.Value != payload.Fps.Value)
            {
                error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                    "Use either 'videoFps' or 'fps'; both values must match.");
                return null;
            }
            if (targetFps.HasValue && (targetFps.Value < 15 || targetFps.Value > 1024))
            {
                error = InvalidOption("targetFps", "15..1024");
                return null;
            }
            if (videoFps.HasValue && (videoFps.Value < 15 || videoFps.Value > 240))
            {
                error = InvalidOption("videoFps", "15..240");
                return null;
            }
            if (payload.BitrateMbps.HasValue && payload.Bitrate.HasValue && payload.BitrateMbps.Value != payload.Bitrate.Value)
            {
                error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                    "Use either 'bitrateMbps' or 'bitrate'; both values must match.");
                return null;
            }
            if (bitrate.HasValue && (bitrate.Value < 1 || bitrate.Value > 200))
            {
                error = InvalidOption("bitrateMbps", "1..200");
                return null;
            }
            if (payload.EndDelaySeconds.HasValue && (float.IsNaN(payload.EndDelaySeconds.Value)
                || float.IsInfinity(payload.EndDelaySeconds.Value) || payload.EndDelaySeconds.Value < 0f))
            {
                error = InvalidOption("endDelaySeconds", "finite number >= 0");
                return null;
            }
            if (payload.AudioGainDb.HasValue && (float.IsNaN(payload.AudioGainDb.Value)
                || float.IsInfinity(payload.AudioGainDb.Value)
                || payload.AudioGainDb.Value < RendererSettings.MinAudioGainDb
                || payload.AudioGainDb.Value > RendererSettings.MaxAudioGainDb))
            {
                error = InvalidOption("audioGainDb", "-60..12");
                return null;
            }
            VideoCodec? primaryCodec = null;
            VideoCodec? aliasCodec = null;
            if (!string.IsNullOrWhiteSpace(payload.VideoCodec))
            {
                if (!VideoCodecCatalog.TryParse(payload.VideoCodec, out var parsedCodec))
                {
                    error = InvalidOption("videoCodec", "H264, H265, VP9, AV1, ProRes, or FFV1");
                    return null;
                }
                primaryCodec = parsedCodec;
            }
            if (!string.IsNullOrWhiteSpace(payload.Codec))
            {
                if (!VideoCodecCatalog.TryParse(payload.Codec, out var parsedAliasCodec))
                {
                    error = InvalidOption("codec", "H264, H265, VP9, AV1, ProRes, or FFV1");
                    return null;
                }
                aliasCodec = parsedAliasCodec;
            }
            if (primaryCodec.HasValue && aliasCodec.HasValue && primaryCodec.Value != aliasCodec.Value)
            {
                error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                    "Use either 'videoCodec' or 'codec'; both values must match.");
                return null;
            }
            var videoCodec = primaryCodec ?? aliasCodec;
            VideoBitDepth? bitDepth = null;
            if (payload.BitDepth.HasValue)
            {
                if (payload.BitDepth.Value != 8 && payload.BitDepth.Value != 10)
                {
                    error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                        "Option 'bitDepth' must be 8 or 10.", "bitDepth");
                    return null;
                }
                bitDepth = payload.BitDepth.Value == 10 ? VideoBitDepth.Ten : VideoBitDepth.Eight;
            }
            AudioCodec? audioCodec = null;
            if (!string.IsNullOrWhiteSpace(payload.AudioCodec))
            {
                if (!Enum.TryParse<AudioCodec>(payload.AudioCodec, true, out var parsedAudio)
                    || !Enum.IsDefined(typeof(AudioCodec), parsedAudio))
                {
                    error = InvalidOption("audioCodec", "Auto, AAC, Opus, or Flac");
                    return null;
                }
                var extension = OutputFormat.Extension(Main.Settings?.Container ?? VideoContainer.Auto,
                    videoCodec ?? Main.Settings?.Codec ?? VideoCodec.H264);
                if (!AudioCodecCatalog.IsSupported(parsedAudio, extension))
                {
                    error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                        parsedAudio + " audio is not supported in " + extension + ".", "audioCodec");
                    return null;
                }
                audioCodec = parsedAudio;
            }
            var options = new RpcRenderOptions
            {
                Preset = preset,
                Width = payload.Width,
                Height = payload.Height,
                TargetFps = targetFps,
                VideoFps = videoFps,
                BitrateMbps = bitrate,
                EndDelaySeconds = payload.EndDelaySeconds,
                AudioGainDb = payload.AudioGainDb,
                AudioCodec = audioCodec,
                BgaMode = payload.BgaMode,
                ShowPlanetRings = payload.ShowPlanetRings,
                ShowSongTitle = payload.ShowSongTitle,
                ShowCountdown = payload.ShowCountdown,
                ShowResultText = payload.ShowResultText,
                ShowHitJudgments = payload.ShowHitJudgments,
                ShowModOverlay = payload.ShowModOverlay,
                VideoCodec = videoCodec,
                BitDepth = bitDepth
            };
            return options.HasValues ? options : null;
        }

        private static RenderErrorDetails InvalidOption(string name, string allowed)
        {
            return RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                "Option '" + name + "' must be " + allowed + ".", name);
        }

        private static bool TryParsePreset(string value, out RendererPreset preset)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "custom": preset = RendererPreset.Custom; return true;
                case "preview": preset = RendererPreset.Preview; return true;
                case "fullhd": case "1080p": preset = RendererPreset.FullHD; return true;
                case "qhd": case "1440p": preset = RendererPreset.QHD; return true;
                case "uhd4k": case "4k": case "2160p": preset = RendererPreset.UHD4K; return true;
                default: preset = RendererPreset.Custom; return false;
            }
        }
    }
}
