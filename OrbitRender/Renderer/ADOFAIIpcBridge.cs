using System;
using System.IO;
using System.Threading.Tasks;
using AdofaiIpc;
using AdofaiIpc.Loader;
using AdofaiIpc.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbitRender.Ipc.Generated;

namespace OrbitRender.Renderer
{
    internal sealed class ADOFAIIpcBridge : IDisposable
    {
        private readonly RenderJobService renderer;
        private ModRegistration<OrbitRenderIpcApp> registration;
        internal ADOFAIIpcBridge(RenderJobService renderer) { this.renderer = renderer; }

        internal void Initialize()
        {
            try
            {
                var modsDirectory = GetModsDirectory(Main.Entry.Path);
                var runtime = IpcLoader.GetRuntime(modsDirectory,
                    new RuntimeRequirements("2.1.0", 1, 3, "commands", "downloads", "typed-files"));
                Register(runtime);
            }
            catch (Exception error)
            {
                Dispose();
                Main.Entry.Logger.Error("[System] ADOFAI IPC SDK unavailable: " + error);
            }
        }

        internal static string GetModsDirectory(string modDirectory)
        {
            var path = Path.GetFullPath(modDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Directory.GetParent(path).FullName;
        }

        internal void Register(IIpcRuntime runtime)
        {
            try
            {
                var mod = runtime.RegisterMod(OrbitRenderIpcContract.Instance, "OrbitRender",
                    new[]
                    {
                        "https://orbitrender.imnya.ng",
                        "https://tufreplay.impl1113.dev",
                        "https://tufreplay-dev.impl1113.dev",
                        "https://tufreplay-auto.impl1113.dev",
                        // Match TUFReplay's local development origins. The IPC
                        // loader compares origins exactly, including the port.
                        "http://localhost",
                        "http://127.0.0.1",
                        "http://localhost:5173",
                        "http://127.0.0.1:5173",
                        "http://localhost:5174",
                        "http://127.0.0.1:5174",
                        "http://localhost:4173",
                        "http://127.0.0.1:4173",
                    });
                registration = mod;
                mod.Handle(OrbitRenderIpcMethods.Health, (request, context) =>
                    Task.FromResult(Convert<Health>(renderer.Health())));
                mod.Handle(OrbitRenderIpcMethods.ListJobs, (request, context) =>
                    Task.FromResult(Convert<Job[]>(renderer.ListJobs())));
                mod.Handle(OrbitRenderIpcMethods.CreateRender, (request, context) =>
                {
                    var body = JObject.Parse(OrbitRenderIpcMethods.CreateRender.Input.Write(request))["options"].ToString();
                    var result = renderer.CreateJob(body, out var status);
                    if (status != 202) throw new ArgumentException(JObject.FromObject(result).Value<string>("error"));
                    return Task.FromResult(Convert<RenderAccepted>(result));
                });
                mod.Handle(OrbitRenderIpcMethods.RenderStatus, (request, context) =>
                    Task.FromResult(Convert<Job>(renderer.FindJob(request.Id).Snapshot())));
                mod.Handle(OrbitRenderIpcMethods.CancelRender, (request, context) =>
                    Task.FromResult(Convert<Cancelled>(renderer.CancelJob(request.Id))));
                mod.Handle(OrbitRenderIpcMethods.DownloadRender, (request, context) =>
                {
                    var job = renderer.FindJob(request.Id);
                    var path = job.CompletedOutputPath;
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidOperationException("Render is not complete.");
                    return Task.FromResult(IpcFile.FromPath(path, OutputFormat.MimeType(Path.GetExtension(path))));
                });
                mod.MarkReady();
                Main.Entry.Logger.Log("[System] ADOFAI IPC SDK registered: orbitrender");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static T Convert<T>(object value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        public void Dispose() { registration?.Dispose(); registration = null; }
    }
}
