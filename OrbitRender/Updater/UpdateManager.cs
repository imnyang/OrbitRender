using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using UnityModManagerNet;
using OrbitRender.Renderer;

namespace OrbitRender
{
    /// <summary>
    /// Automatic updates from GitHub releases.
    ///
    /// Flow: check (background) -> download + verify + extract into &lt;mod&gt;/.update (background)
    /// -> install on the main thread while no render is running -> hot reload through Unity Mod Manager.
    /// If the loaded files are locked, the verified package is installed by a detached helper after
    /// the game exits. A failed install is rolled back and never leaves a half-updated mod.
    /// </summary>
    internal static class UpdateManager
    {
        private const string WorkFolder = ".update";
        private static int started;
        private static readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
        private static string baseDisplayName;

        internal static void Start(UnityModManager.ModEntry entry)
        {
            if (entry == null || Interlocked.Exchange(ref started, 1) != 0) return;
            if (Environment.GetEnvironmentVariable("ORBITRENDER_DISABLE_UPDATE") == "1") return;

            baseDisplayName = DisplayNameOf(entry);
            ThreadPool.QueueUserWorkItem(_ => Run(entry));
        }

        /// <summary>Runs queued install steps on Unity's main thread, never while a render is active.</summary>
        internal static void PumpMainThread()
        {
            Action action;
            while (!IsRendering() && mainThreadActions.TryDequeue(out action))
            {
                try { action(); }
                catch (Exception ex) { Log("Update step failed: " + ex); }
            }
        }

        private static void Run(UnityModManager.ModEntry entry)
        {
            var work = Path.Combine(entry.Path, WorkFolder);
            try
            {
                // A helper from a previous session may still be copying; leave its files alone.
                if (UpdateInstaller.IsHelperActive(work)) return;
                DeleteDirectory(work);

                SetStatus(entry, "Checking update...");
                var release = UpdateTransport.GetLatestRelease();
                Version current;
                if (release == null
                    || !UpdateVersion.TryParse(entry.Version == null ? null : entry.Version.ToString(), out current)
                    || release.Version <= current)
                {
                    ClearStatus(entry);
                    return;
                }

                Log("Update " + UpdateVersion.Format(release.Version) + " found (installed " + UpdateVersion.Format(current) + ").");
                Directory.CreateDirectory(work);
                var archive = Path.Combine(work, "release.zip");
                SetStatus(entry, "Updating... 0%");
                UpdateTransport.Download(release, archive, p => SetStatus(entry, "Updating... " + p + "%"));

                SetStatus(entry, "Updating... unpacking");
                var packageRoot = UpdatePackage.ExtractAndValidate(archive, Path.Combine(work, "package"), release.Version);
                UpdatePackage.EnsureUnixExecutables(packageRoot);
                File.Delete(archive);

                SetStatus(entry, "Updating... waiting");
                mainThreadActions.Enqueue(() => Apply(entry, release.Version, packageRoot, work));
            }
            catch (Exception ex)
            {
                ClearStatus(entry);
                DeleteDirectory(work);
                Log("Automatic update failed: " + ex.Message);
            }
        }

        private static void Apply(UnityModManager.ModEntry entry, Version version, string packageRoot, string work)
        {
            SetStatus(entry, "Updating... installing");
            try
            {
                UpdateInstaller.Install(packageRoot, entry.Path, Path.Combine(work, "backup"));
            }
            catch (Exception ex)
            {
                // Files in use (e.g. the loaded DLL): install after ADOFAI exits instead.
                Log("Could not replace loaded files (" + ex.Message + "); the update will install after ADOFAI exits.");
                try
                {
                    UpdateInstaller.ScheduleAfterExit(packageRoot, entry.Path, work);
                    SetStatus(entry, "Update pending restart");
                }
                catch (Exception scheduleError)
                {
                    ClearStatus(entry);
                    DeleteDirectory(work);
                    Log("Could not schedule the update installer: " + scheduleError.Message);
                }
                return;
            }

            // Files are now up to date on disk. From here on a failure only means "restart needed".
            try
            {
                ReloadMod(entry);
                DeleteDirectory(work);
                Log("Update " + UpdateVersion.Format(version) + " applied without restarting ADOFAI.");
            }
            catch (Exception ex)
            {
                DeleteDirectory(work);
                SetStatus(entry, "Restart to finish update");
                Log("Update " + UpdateVersion.Format(version) + " installed; hot reload unavailable (" + ex.Message + "). Restart ADOFAI to finish.");
            }
        }

        private static void ReloadMod(UnityModManager.ModEntry entry)
        {
            if (!entry.CanReload) throw new NotSupportedException("hot reload is not enabled for this mod");
            var reload = typeof(UnityModManager.ModEntry).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (reload == null) throw new MissingMethodException("ModEntry.Reload not found");

            RefreshMetadata(entry);
            var oldAssembly = entry.Assembly;
            reload.Invoke(entry, null);
            if (entry.ErrorOnLoading || !entry.Loaded || entry.Assembly == oldAssembly)
                throw new InvalidOperationException("Unity Mod Manager did not load the updated assembly");
        }

        /// <summary>
        /// Points the live ModEntry at the freshly installed Info.json (same reflection trick JALib uses)
        /// so UMM's UI and dependency checks show the new version immediately.
        /// </summary>
        private static void RefreshMetadata(UnityModManager.ModEntry entry)
        {
            var infoPath = Path.Combine(entry.Path, "Info.json");
            if (!File.Exists(infoPath)) infoPath = Path.Combine(entry.Path, "info.json");
            var info = JsonConvert.DeserializeObject<UnityModManager.ModInfo>(File.ReadAllText(infoPath));
            Version version;
            if (info == null || !string.Equals(info.Id, entry.Info.Id, StringComparison.OrdinalIgnoreCase)
                || !UpdateVersion.TryParse(info.Version, out version))
                throw new InvalidDataException("installed Info.json is invalid");

            var infoField = typeof(UnityModManager.ModEntry).GetField("Info", BindingFlags.Instance | BindingFlags.Public);
            var versionField = typeof(UnityModManager.ModEntry).GetField("Version", BindingFlags.Instance | BindingFlags.Public);
            if (infoField == null || versionField == null) throw new MissingFieldException("ModEntry.Info/Version not found");
            infoField.SetValue(entry, info);
            versionField.SetValue(entry, version);
        }

        private static bool IsRendering()
        {
            return RendererController.Instance != null && RendererController.Instance.Busy;
        }

        private static string DisplayNameOf(UnityModManager.ModEntry entry)
        {
            if (entry.Info == null) return "OrbitRender";
            return string.IsNullOrEmpty(entry.Info.DisplayName) ? entry.Info.Id : entry.Info.DisplayName;
        }

        private static void SetStatus(UnityModManager.ModEntry entry, string status)
        {
            if (entry.Info != null)
                entry.Info.DisplayName = baseDisplayName + " <color=grey>[" + status + "]</color>";
        }

        private static void ClearStatus(UnityModManager.ModEntry entry)
        {
            if (entry.Info != null) entry.Info.DisplayName = baseDisplayName;
        }

        private static void DeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { /* leftovers are wiped on the next launch */ }
        }

        private static void Log(string message)
        {
            if (Main.Entry != null) Main.Entry.Logger.Log("[Update] " + message);
        }
    }
}
