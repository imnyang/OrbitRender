using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using OrbitRender.Renderer;
using UnityEngine;

namespace OrbitRender
{
    /// <summary>
    /// Render entry point for other mods. Every member must be called from
    /// Unity's main thread. Arguments and results are JSON strings, plus the
    /// replay host object, so callers can bind through reflection without a
    /// compile-time reference.
    /// </summary>
    public static class RenderApi
    {
        /// <summary>
        /// Bumped when the JSON shape or host contract below changes incompatibly.
        /// v3: StartHostedReplay replaces StartReplay(manifestPath, optionsJson).
        /// </summary>
        public const int ApiVersion = 3;

        /// <summary>Indicates support for the outputDirectory replay option.</summary>
        public const int OutputDirectoryOptionVersion = 1;

        /// <summary>
        /// Indicates that a hosted replay's host may add videos over the finished video with an
        /// optional <c>string GetOverlayVideos()</c> member. See docs/CSharp-API.md.
        /// </summary>
        public const int OverlayVideoVersion = 1;

        /// <summary>Indicates support for the exact bitrateKbps render option.</summary>
        public const int BitrateKbpsVersion = 1;

        /// <summary>True when OrbitRender is loaded and can accept render jobs.</summary>
        public static bool IsAvailable
        {
            get
            {
                var service = Service;
                return service != null && service.Controller != null;
            }
        }

        /// <summary>Human readable OrbitRender version, for diagnostics.</summary>
        public static string Version
        {
            get
            {
                try { return Main.Entry?.Info?.Version ?? "0.0.0"; }
                catch (Exception) { return "0.0.0"; }
            }
        }

        /// <summary>True while a render job is queued or running.</summary>
        public static bool IsBusy => Service?.IsBusy ?? false;

        /// <summary>
        /// Opens the request's level and renders a replay that <paramref name="host"/>
        /// drives, then returns a job envelope. OrbitRender binds the host's
        /// members once, here, and refuses a host that lacks one with
        /// <c>render_host_invalid</c>. See docs/CSharp-API.md for the request
        /// JSON and the host contract.
        /// </summary>
        public static string StartHostedReplay(string requestJson, object host)
        {
            return Execute(() =>
            {
                var service = Service;
                if (service == null) return Error(Unavailable());
                HostedReplayDriver replay;
                RpcRenderPayload payload;
                string outputDirectory;
                try
                {
                    var request = HostedReplayRequest.Parse(requestJson);
                    if (!TryReadOptions(request.Options, out payload, out outputDirectory, out var optionsError))
                        return Error(optionsError);
                    replay = new HostedReplayDriver(request, HostedReplayHost<Canvas>.Bind(host));
                }
                catch (RenderRequestException exception)
                {
                    return Error(RenderErrorDetails.From(exception));
                }
                // The request is the only source of truth for the level.
                payload.LevelPath = replay.Request.LevelPath;
                payload.Path = null;
                payload.ReplayManifestPath = null;
                if (!service.TryCreate(payload, out var job, out var error, outputDirectory, replay)) return Error(error);
                return Ok(job);
            });
        }

        private static bool TryReadOptions(JObject options, out RpcRenderPayload payload, out string outputDirectory,
            out RenderErrorDetails error)
        {
            payload = null;
            outputDirectory = null;
            error = null;
            options = options ?? new JObject();
            var output = options.GetValue("outputDirectory", StringComparison.OrdinalIgnoreCase);
            if (output != null && output.Type != JTokenType.Null)
            {
                if (output.Type != JTokenType.String)
                {
                    error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                        "outputDirectory must be an absolute folder path.", "outputDirectory");
                    return false;
                }
                var path = ((string)output).Trim();
                if (path.Length > 0)
                {
                    try
                    {
                        if (path == "~")
                            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        else if (path.StartsWith("~/", StringComparison.Ordinal)
                            || path.StartsWith("~\\", StringComparison.Ordinal))
                            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
                        var root = Path.GetPathRoot(path);
                        if (!Path.IsPathRooted(path) || (Path.DirectorySeparatorChar == '\\'
                            && (string.IsNullOrEmpty(root) || root.Length < 3)))
                            throw new ArgumentException("The path must be absolute.");
                        outputDirectory = Path.GetFullPath(path);
                    }
                    catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
                    {
                        error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                            "Invalid outputDirectory: " + exception.Message, "outputDirectory");
                        return false;
                    }
                }
            }
            try { payload = options.ToObject<RpcRenderPayload>() ?? new RpcRenderPayload(); }
            catch (JsonException exception)
            {
                error = RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                    "Render options are not valid: " + exception.Message, "options");
                return false;
            }
            return true;
        }

        /// <summary>Returns the current snapshot of one job.</summary>
        public static string ReadStatus(string jobId)
        {
            return Execute(() =>
            {
                var service = Service;
                if (service == null) return Error(Unavailable());
                return Ok(service.Read(jobId));
            });
        }

        /// <summary>Requests cancellation. The job reports cancelled on a later poll.</summary>
        public static string Cancel(string jobId)
        {
            return Execute(() =>
            {
                var service = Service;
                if (service == null) return Error(Unavailable());
                return Ok(service.Cancel(jobId));
            });
        }

        private static RenderJobService Service => Main.Enabled ? Main.RenderJobs : null;

        private static RenderErrorDetails Unavailable()
        {
            return RenderErrorDetails.Create(RenderErrorCodes.RendererUnavailable,
                "OrbitRender is not available. Enable it in UnityModManager and try again.");
        }

        private static string Execute(Func<string> action)
        {
            if (!Main.IsMainThread)
            {
                return Error(RenderErrorDetails.Create(RenderErrorCodes.WrongThread,
                    "OrbitRender render calls must run on Unity's main thread."));
            }
            try { return action(); }
            catch (ArgumentException exception)
            {
                return Error(RenderErrorDetails.Create(RenderErrorCodes.JobUnknown, exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Error(RenderErrorDetails.Create(RenderErrorCodes.JobFinished, exception.Message));
            }
            catch (Exception exception)
            {
                try { Main.Entry.Logger.Error("[RenderApi] " + exception); } catch (Exception) { }
                return Error(RenderErrorDetails.Create(RenderErrorCodes.Internal, exception.Message));
            }
        }

        private static string Ok(RenderJobSnapshot job) => JsonConvert.SerializeObject(new { ok = true, job });

        private static string Error(RenderErrorDetails error) =>
            JsonConvert.SerializeObject(new { ok = false, error });
    }
}
