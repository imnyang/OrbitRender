using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace OrbitRender.Renderer
{
    internal static class DiagnosticContext
    {
        private static readonly Queue<string> recentLogs = new Queue<string>();
        private static bool listening;

        internal static void Start()
        {
            if (listening) return;
            listening = true;
            Application.logMessageReceivedThreaded += RecordLog;
        }

        internal static void Stop()
        {
            if (!listening) return;
            Application.logMessageReceivedThreaded -= RecordLog;
            listening = false;
        }

        private static void RecordLog(string message, string stack, LogType kind)
        {
            if (message != null && message.Contains("[Render] Failed:")) return;
            var line = DateTimeOffset.Now.ToString("O") + " [" + kind + "] " + message;
            if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) line += "\n" + stack;
            if (line.Length > 2000) line = line.Substring(0, 2000) + " [truncated]";
            lock (recentLogs)
            {
                recentLogs.Enqueue(line);
                while (recentLogs.Count > 60) recentLogs.Dequeue();
            }
        }

        internal static string RecentLogs()
        {
            lock (recentLogs) return "Recent Unity logs (last 60 entries; 2000 characters per entry):\n"
                + (recentLogs.Count == 0 ? "none captured" : string.Join("\n", recentLogs));
        }

        internal static string Capture()
        {
            var report = new StringBuilder();
            Append(report, "Game", () => Application.version + "; build GUID " + Application.buildGUID);
            Append(report, "Game assembly", () => typeof(ADOBase).Assembly.FullName
                + "; MVID " + typeof(ADOBase).Module.ModuleVersionId);
            Append(report, "OrbitRender build", () => Main.Entry?.Info.Version
                + "; assembly version " + typeof(Main).Assembly.GetName().Version
                + "; MVID " + typeof(Main).Module.ModuleVersionId);
            Append(report, "Scene", () => SceneManager.GetActiveScene().name);
            Append(report, "CPU / RAM", () => SystemInfo.processorType + "; " + SystemInfo.processorCount
                + " logical processors; " + SystemInfo.systemMemorySize + " MB RAM");
            Append(report, "GPU capabilities", () => SystemInfo.graphicsMemorySize + " MB VRAM; driver="
                + SystemInfo.graphicsDeviceVersion + "; max texture=" + SystemInfo.maxTextureSize
                + "; async readback=" + SystemInfo.supportsAsyncGPUReadback);
            Append(report, "Process memory", () => {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    return "working set=" + process.WorkingSet64 + "; private=" + process.PrivateMemorySize64 + " bytes";
            });
            Append(report, "Unity timing", () => "timeScale=" + Time.timeScale + "; captureFramerate=" + Time.captureFramerate
                + "; targetFrameRate=" + Application.targetFrameRate + "; vSync=" + QualitySettings.vSyncCount
                + "; screen=" + Screen.width + "x" + Screen.height + "; color space=" + QualitySettings.activeColorSpace);
            Append(report, "Audio device configuration", () => {
                var config = AudioSettings.GetConfiguration();
                return "sampleRate=" + config.sampleRate + "; speakerMode=" + config.speakerMode
                    + "; DSP buffer=" + config.dspBufferSize + "; listener paused=" + AudioListener.pause;
            });
            Append(report, "Player log", () => Application.consoleLogPath);
            Append(report, "Runtime", () => Environment.Version + "; " + (Environment.Is64BitProcess ? "64-bit" : "32-bit")
                + "; Harmony " + typeof(HarmonyLib.Harmony).Assembly.FullName
                + "; UMM " + typeof(UnityModManager).Assembly.GetName().Version);
            Append(report, "Mods (active / enabled)", () => string.Join(Environment.NewLine,
                UnityModManager.modEntries.OrderBy(mod => mod.Info.Id).Select(mod =>
                    "  " + mod.Info.Id + " " + mod.Info.Version + "; active=" + mod.Active + "; enabled=" + mod.Enabled
                    + "; assembly=" + ModAssembly(mod))));
            return report.ToString();
        }

        private static string ModAssembly(UnityModManager.ModEntry mod)
        {
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var assembly = (typeof(UnityModManager.ModEntry).GetField("Assembly", flags)?.GetValue(mod)
                    ?? typeof(UnityModManager.ModEntry).GetProperty("Assembly", flags)?.GetValue(mod, null)) as Assembly;
                return assembly == null ? "not loaded / unavailable" : assembly.FullName + "; MVID " + assembly.ManifestModule.ModuleVersionId;
            }
            catch (Exception ex) { return "unavailable (" + ex.Message + ")"; }
        }

        internal static string Storage(string directory)
        {
            var report = new StringBuilder();
            Append(report, "Output storage", () => {
                if (string.IsNullOrWhiteSpace(directory)) return "directory not resolved";
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory)));
                return directory + "; available=" + drive.AvailableFreeSpace + "; total=" + drive.TotalSize + " bytes";
            });
            return report.ToString();
        }

        private static void Append(StringBuilder report, string label, Func<string> read)
        {
            try { report.AppendLine(label + ": " + read()); }
            catch (Exception ex) { report.AppendLine(label + ": unavailable (" + ex.GetType().Name + ": " + ex.Message + ")"); }
        }
    }
}
