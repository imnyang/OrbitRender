using System;
using System.Globalization;
using System.Threading;
using HarmonyLib;
using UnityModManagerNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using OrbitRender.Renderer;
using OrbitRender.UI;
namespace OrbitRender
{
    [EnableReloading]
    public static class Main
    {
        internal static UnityModManager.ModEntry Entry;
        internal static bool Enabled;
        internal static bool RpcEnabled { get; private set; }
        internal static int RpcPort { get; private set; } = 1108;
        internal static RendererRpcServer RpcServer { get; private set; }
        internal static RenderJobService RenderJobs { get; private set; }
        private static ADOFAIIpcBridge ipcBridge;
        internal static RendererSettings Settings;
        internal static bool IsBatchMode { get; private set; }
        private static Harmony harmony;
        private static GameObject host;

        internal static int MainThreadId { get; private set; } = -1;
        internal static bool IsMainThread => MainThreadId == Thread.CurrentThread.ManagedThreadId;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            Entry = entry;
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            IsBatchMode = Application.isBatchMode;
            Localization.Initialize(entry.Path);
            try
            {
                ReadCommandLineOptions();
                Settings = RendererSettings.Load(entry);
                Settings.OnChange();
                harmony = new Harmony(entry.Info.Id);
                harmony.PatchAll(typeof(Main).Assembly);
                OrbitRender.Patches.RendererMainKeyListPatches.Apply(harmony);
                host = new GameObject("OrbitRender");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<RendererController>();
                SceneManager.sceneLoaded += OnSceneLoaded;
                Enabled = true;
                if (IsBatchMode) RpcEnabled = true;
                StartRpcServer();
                // Batchmode still performs the normal FFmpeg availability check;
                // installation remains gated by explicit user consent.
                FfmpegInstaller.Start(entry, Settings);
                UpdateManager.Start(entry);
                entry.OnToggle = (mod, enabled) =>
                {
                    if (!enabled)
                    {
                        AudioPreview.Stop();
                        ExportVideoDialog.CloseDialog();
                        NoticeModal.Close();
                        RendererController.Instance?.StopAndClean();
                    }
                    Enabled = enabled;
                    if (!enabled) StopRpcServer();
                    else StartRpcServer();
                    return true;
                };
                if (!IsBatchMode)
                {
                    entry.OnGUI = mod => ModSettingsWindow.Draw();
                    entry.OnSaveGUI = mod => Settings?.Save(mod);
                }
                entry.OnUpdate = (mod, deltaTime) => UpdateManager.PumpMainThread();
                entry.OnUnload = mod =>
                {
                    ExportVideoDialog.CloseDialog();
                    NoticeModal.Close();
                    AudioPreview.Stop();
                    RendererController.Instance?.StopAndClean();
                    OrbitRender.Patches.RenderTimingPatches.Remove();
                    StopRpcServer();
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    UnityEngine.Object.Destroy(host);
                    harmony.UnpatchAll(mod.Info.Id);
                    Settings = null;
                    return true;
                };
                entry.Logger.Log("[System] Renderer loaded. Unity " + Application.unityVersion);
                return true;
            }
            catch (Exception ex)
            {
                if (host != null) UnityEngine.Object.Destroy(host);
                harmony?.UnpatchAll(entry.Info.Id);
                entry.Logger.Error("[System] Load failed: " + ex);
                return false;
            }
        }

        private static void ReadCommandLineOptions()
        {
            RpcEnabled = IsBatchMode;
            RpcPort = 1108;
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, "--renderer-rpc", StringComparison.OrdinalIgnoreCase))
                {
                    RpcEnabled = true;
                    continue;
                }
                const string prefix = "--renderer-rpc-port=";
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(argument.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                    && port >= 1 && port <= 65535)
                {
                    RpcPort = port;
                    RpcEnabled = true;
                }
            }
        }

        private static void StartRpcServer()
        {
            if (RenderJobs != null || RendererController.Instance == null) return;
            try
            {
                // The shared job service is always available to optional AdofaiIpc
                // and to other mods through RenderApi. Only the primary HTTP
                // listener requires the launch option.
                RenderJobs = new RenderJobService(RendererController.Instance);
                if (RpcEnabled)
                {
                    RpcServer = new RendererRpcServer(RenderJobs, RpcPort);
                    RpcServer.Start();
                }
                ipcBridge = new ADOFAIIpcBridge(RenderJobs);
                ipcBridge.Initialize();
            }
            catch (Exception ex)
            {
                RenderJobs = null;
                RpcServer = null;
                Entry.Logger.Error("[System] Renderer RPC could not start on port " + RpcPort + ": " + ex);
            }
        }

        private static void StopRpcServer()
        {
            ipcBridge?.Dispose();
            ipcBridge = null;
            var server = RpcServer;
            RpcServer = null;
            RenderJobs = null;
            try { server?.Dispose(); } catch (Exception ex) { Entry.Logger.Error("[System] Renderer RPC shutdown: " + ex); }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Enabled) return;
            // ADOFAI's KillAll cleanup can remove DontDestroyOnLoad objects
            // while switching between menu, editor, and gameplay scenes.
            // Recreate our tiny host so RPC jobs survive that transition.
            if (RendererController.Instance == null)
            {
                host = new GameObject("OrbitRender");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<RendererController>();
                RenderJobs?.Rebind(RendererController.Instance);
                Entry.Logger.Log("[System] Renderer host recreated after scene load: " + scene.name);
            }
        }

    }
}
