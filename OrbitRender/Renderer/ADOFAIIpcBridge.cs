using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace OrbitRender.Renderer
{
    // Only inspect assemblies loaded by UMM. Never load or install the dependency ourselves.
    internal sealed class ADOFAIIpcBridge : IDisposable
    {
        private const string Namespace = "orbitrender";
        private readonly RendererRpcServer renderer;
        private object attemptedServer;
        private Type facade;
        private object registration;
        private bool disposed;

        internal ADOFAIIpcBridge(RendererRpcServer renderer) { this.renderer = renderer; }

        internal void Initialize()
        {
            if (disposed) return;
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(
                    candidate => candidate.GetName().Name == "AdofaiIpc");
                if (assembly == null) return;
                var main = assembly.GetType("AdofaiIpc.Main", true);
                var server = main.GetProperty("Server").GetValue(null, null);
                if (server == null || (int)server.GetType().GetProperty("Port").GetValue(server, null) <= 0)
                {
                    Unregister();
                    attemptedServer = null;
                    return;
                }
                if (ReferenceEquals(server, attemptedServer)) return;
                Unregister();
                attemptedServer = server;
                facade = assembly.GetType("AdofaiIpc.AdofaiIpc", true);
                var metadataType = assembly.GetType("AdofaiIpc.IpcNamespaceInfo", true);
                var metadata = Activator.CreateInstance(metadataType);
                SetMetadata(metadataType, metadata, "DisplayName", "OrbitRender");
                SetMetadata(metadataType, metadata, "Version", Main.Entry.Info.Version);
                // Match the existing renderer RPC's unrestricted browser-origin policy.
                SetMetadata(metadataType, metadata, "AllowedOrigins", new string[0]);
                registration = facade.GetMethod("RegisterNamespace").Invoke(null, new[] { (object)Namespace, metadata });
                Register("health", _ => renderer.Health());
                Register("jobs.list", _ => renderer.ListJobs());
                Register("render.create", request =>
                {
                    var result = renderer.CreateJob(Parameters(request).ToString(), out var status);
                    if (status != 202) throw new ArgumentException(JObject.FromObject(result).Value<string>("error"));
                    return result;
                });
                Register("render.status", request => renderer.FindJob(JobId(request)).Snapshot());
                Register("render.cancel", request => renderer.CancelJob(JobId(request)));
                Register("render.download", request =>
                {
                    var job = renderer.FindJob(JobId(request));
                    if (job.State != RpcJobState.Completed || string.IsNullOrEmpty(job.OutputPath))
                        throw new InvalidOperationException("Render is not complete.");
                    var path = job.OutputPath;
                    var file = File.OpenRead(path);
                    try
                    {
                        var contentType = string.Equals(Path.GetExtension(path), ".webm", StringComparison.OrdinalIgnoreCase)
                            ? "video/webm" : "video/mp4";
                        return Activator.CreateInstance(assembly.GetType("AdofaiIpc.IpcDownloadResponse", true),
                            new object[] { file, file.Length, Path.GetFileName(path), contentType });
                    }
                    catch { file.Dispose(); throw; }
                }, download: true);
                registration.GetType().GetMethod("MarkReady").Invoke(registration, null);
                Main.Entry.Logger.Log("Optional AdofaiIpc namespace registered: " + Namespace);
            }
            catch (Exception error)
            {
                Unregister();
                Main.Entry.Logger.Error("Optional AdofaiIpc integration unavailable: " + error.GetBaseException().Message);
            }
        }

        private static void SetMetadata(Type type, object target, string name, object value)
        {
            var property = type.GetProperty(name);
            if (property != null) property.SetValue(target, value, null);
            else type.GetField(name).SetValue(target, value);
        }

        private void Register(string name, Func<object, object> handler, bool download = false)
        {
            var method = registration.GetType().GetMethod(download ? "RegisterDownload" : "Register");
            var requestType = method.GetParameters()[1].ParameterType.GetGenericArguments()[0];
            var callback = typeof(ADOFAIIpcBridge).GetMethod(nameof(CreateHandler), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(requestType).Invoke(null, new object[] { handler });
            method.Invoke(registration, new[] { (object)name, callback });
        }

        private static Func<T, object> CreateHandler<T>(Func<object, object> handler) => request => handler(request);
        private static JToken Parameters(object request) =>
            (JToken)request.GetType().GetField("Params").GetValue(request) ?? new JObject();
        private static string JobId(object request) => Parameters(request).Value<string>("id");

        private void Unregister()
        {
            if (registration == null) return;
            registration = null;
            try { facade.GetMethod("UnregisterNamespace").Invoke(null, new object[] { Namespace }); }
            catch (Exception error) { Main.Entry.Logger.Error("Optional AdofaiIpc unregister: " + error.GetBaseException().Message); }
        }

        public void Dispose() { disposed = true; Unregister(); }
    }
}
