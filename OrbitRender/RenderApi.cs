using System;
using Newtonsoft.Json;
using OrbitRender.Renderer;

namespace OrbitRender
{
    /// <summary>
    /// Render entry point for other mods. Every member must be called from
    /// Unity's main thread. Arguments and results are JSON strings so callers
    /// can bind through reflection without a compile-time reference.
    /// </summary>
    public static class RenderApi
    {
        /// <summary>Bumped when the JSON shape below changes incompatibly.</summary>
        public const int ApiVersion = 2;

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
        /// Opens the recorded level, starts rendering, and returns a job envelope.
        /// <paramref name="optionsJson"/> may be null or empty to use OrbitRender defaults.
        /// </summary>
        public static string StartReplay(string manifestPath, string optionsJson)
        {
            return Execute(() =>
            {
                var service = Service;
                if (service == null) return Error(Unavailable());
                if (string.IsNullOrWhiteSpace(manifestPath))
                {
                    return Error(RenderErrorDetails.Create(RenderErrorCodes.ManifestMissing,
                        "The recording manifest is missing. Export the recording again before rendering.",
                        "manifestPath", file: "manifest.json"));
                }
                RpcRenderPayload payload;
                try
                {
                    payload = string.IsNullOrWhiteSpace(optionsJson)
                        ? new RpcRenderPayload()
                        : JsonConvert.DeserializeObject<RpcRenderPayload>(optionsJson);
                }
                catch (JsonException exception)
                {
                    return Error(RenderErrorDetails.Create(RenderErrorCodes.OptionsInvalid,
                        "Render options are not valid JSON: " + exception.Message));
                }
                payload = payload ?? new RpcRenderPayload();
                // The manifest is the only source of truth for the recorded level.
                payload.ReplayManifestPath = manifestPath;
                payload.LevelPath = null;
                payload.Path = null;
                if (!service.TryCreate(payload, out var job, out var error)) return Error(error);
                return Ok(job);
            });
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
