using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace OrbitRender.Renderer
{
    /// <summary>
    /// Optional loopback HTTP front end over <see cref="RenderJobService"/>.
    /// Jobs and state live in the service so the HTTP listener, ADOFAI IPC, and
    /// the C# <see cref="RenderApi"/> facade always agree.
    /// </summary>
    internal sealed class RendererRpcServer : IDisposable
    {
        private readonly RenderJobService jobs;
        private readonly int port;
        private readonly HttpListener listener = new HttpListener();
        private Thread thread;
        private volatile bool stopping;

        public RendererRpcServer(RenderJobService jobs, int port)
        {
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            this.port = port;
        }

        public void Start()
        {
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.IgnoreWriteExceptions = true;
            listener.Start();
            thread = new Thread(ListenLoop) { IsBackground = true, Name = "OrbitRender RPC" };
            thread.Start();
            Main.Entry.Logger.Log("[RPC] Renderer RPC listening on http://127.0.0.1:" + port + "/");
        }

        private void ListenLoop()
        {
            while (!stopping)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!stopping) Main.Entry.Logger.Error("[RPC] Renderer RPC listener: " + ex);
                    break;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(context));
            }
        }

        private void Handle(HttpListenerContext context)
        {
            try
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,DELETE,OPTIONS";
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                var path = context.Request.Url.AbsolutePath.Trim('/');
                var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1 && parts[0].Equals("health", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context, 200, jobs.Health());
                    return;
                }
                if (parts.Length == 1 && parts[0].Equals("jobs", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "GET")
                {
                    WriteJson(context, 200, jobs.ListJobs());
                    return;
                }
                if (parts.Length == 1 && parts[0].Equals("render", StringComparison.OrdinalIgnoreCase)
                    && context.Request.HttpMethod == "POST")
                {
                    CreateJob(context);
                    return;
                }
                if (parts.Length >= 2 && parts[0].Equals("render", StringComparison.OrdinalIgnoreCase))
                {
                    RpcRenderJob job;
                    try { job = jobs.FindJob(parts[1]); }
                    catch (ArgumentException) { WriteJson(context, 404, new { error = "Unknown render job." }); return; }
                    if (parts.Length == 2 && context.Request.HttpMethod == "GET")
                    {
                        WriteJson(context, 200, job.Snapshot());
                        return;
                    }
                    if (parts.Length == 3 && parts[2].Equals("download", StringComparison.OrdinalIgnoreCase)
                        && context.Request.HttpMethod == "GET")
                    {
                        Download(context, job);
                        return;
                    }
                    if (parts.Length == 3 && parts[2].Equals("cancel", StringComparison.OrdinalIgnoreCase)
                        && (context.Request.HttpMethod == "POST" || context.Request.HttpMethod == "DELETE"))
                    {
                        try { WriteJson(context, 202, jobs.CancelJob(job.Id)); }
                        catch (InvalidOperationException ex) { WriteJson(context, 409, new { error = ex.Message }); }
                        return;
                    }
                }
                WriteJson(context, 404, new { error = "Unknown renderer RPC endpoint." });
            }
            catch (Exception ex)
            {
                Main.Entry.Logger.Error("[RPC] Renderer RPC request: " + ex);
                try { WriteJson(context, 500, new { error = ex.Message }); } catch { }
            }
        }

        private void CreateJob(HttpListenerContext context)
        {
            if (context.Request.ContentLength64 < 0 || context.Request.ContentLength64 > 1024 * 1024)
            {
                WriteJson(context, 413, new { error = "Request body is too large." });
                return;
            }
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
            var result = jobs.CreateJob(body, out var status);
            WriteJson(context, status, result);
        }

        private static void Download(HttpListenerContext context, RpcRenderJob job)
        {
            var path = job.CompletedOutputPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                WriteJson(context, job.IsTerminal ? 409 : 404,
                    new { error = job.IsTerminal ? "Render output is unavailable." : "Render is not complete." });
                return;
            }
            var info = new FileInfo(path);
            context.Response.StatusCode = 200;
            context.Response.ContentType = OutputFormat.MimeType(Path.GetExtension(path));
            context.Response.ContentLength64 = info.Length;
            context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + Uri.EscapeDataString(info.Name) + "\"");
            using (var file = File.OpenRead(path)) file.CopyTo(context.Response.OutputStream);
            context.Response.Close();
        }

        private static void WriteJson(HttpListenerContext context, int status, object value)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = bytes.Length;
            using (var stream = context.Response.OutputStream) stream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        public void Dispose()
        {
            stopping = true;
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
            if (thread != null && thread.IsAlive && !thread.Join(500))
                Main.Entry.Logger.Log("[RPC] Renderer RPC listener thread did not stop immediately.");
            thread = null;
        }
    }
}
