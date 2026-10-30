using System;
using System.IO;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using AdofaiIpc.Runtime;
using AdofaiIpc.Server;
using Newtonsoft.Json;
using OrbitRender.Renderer;

namespace OrbitRender
{
    internal static class Main
    {
        internal static EntryStub Entry = new EntryStub();
        internal static RendererSettings Settings = new RendererSettings();
    }
    internal sealed class EntryStub
    {
        public string Path = ".";
        public LoggerStub Logger = new LoggerStub();
    }
    internal sealed class LoggerStub
    {
        public void Log(string message) => Console.Error.WriteLine(message);
        public void Error(string message) => Console.Error.WriteLine(message);
    }
    public enum RendererPreset { Custom, Preview, FullHD, QHD, UHD4K }
    internal sealed class RendererSettings
    {
        public const float MinAudioGainDb = -60;
        public const float MaxAudioGainDb = 12;
        public bool CaptureAudio = true;
        public VideoContainer Container = VideoContainer.Auto;
        public VideoCodec Codec = VideoCodec.H264;
    }
}

namespace OrbitRender.Renderer
{
    internal enum RendererState { Idle }
    internal sealed class RendererController
    {
        internal RendererState State => RendererState.Idle;
        internal bool Busy => false;
        internal RpcRenderJob LastJob;
        internal void EnqueueRpcRender(RpcRenderRequest request) { LastJob = request.Job; }
        internal void EnqueueRpcCancel(RpcCancelRequest request) { }
    }
}

internal static class Program
{
    private static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "orbitrender-ipc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string modDirectory = Path.Combine(directory, "Mods", "OrbitRender");
        string expectedMods = Path.Combine(directory, "Mods");
        if (ADOFAIIpcBridge.GetModsDirectory(modDirectory) != expectedMods ||
            ADOFAIIpcBridge.GetModsDirectory(modDirectory + Path.DirectorySeparatorChar) != expectedMods)
            throw new Exception("Failed to resolve Mods directory from the mod path.");
        string levelPath = Path.Combine(directory, "test.adofai");
        string outputPath = Path.Combine(directory, "test.mp4");
        File.WriteAllText(levelPath, "{}");
        File.WriteAllText(outputPath, "video-content");
        var registry = new IpcRegistry(action => action());
        var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.1.0", 1, 3, "commands", "downloads", "typed-files"), action => action(), () => false);
        var controller = new RendererController();
        using var renderer = new RendererRpcServer(controller, 0);
        using var bridge = new ADOFAIIpcBridge(renderer);
        bridge.Register(runtime);
        var server = new IpcServer(registry);
        server.Start();
        Console.WriteLine(JsonConvert.SerializeObject(new { port = server.Port, levelPath }));
        Console.Out.Flush();
        try
        {
            string command;
            while ((command = Console.ReadLine()) != null && command != "stop")
            {
                if (command == "disable") bridge.Dispose();
                else if (command == "enable") bridge.Register(runtime);
                else if (command == "complete")
                {
                    controller.LastJob.SetProgress(10, 10, outputPath);
                    controller.LastJob.SetState(RpcJobState.Completed);
                }
                Console.WriteLine("OK");
                Console.Out.Flush();
            }
        }
        finally
        {
            bridge.Dispose();
            runtime.Shutdown();
            server.Stop();
            Directory.Delete(directory, true);
        }
    }
}
