using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;
using UnityModManagerNet;

namespace OrbitRender.Renderer
{
    internal static class ConflictRecovery
    {
        internal static UnityModManager.ModEntry[] Candidates(string[] ids) => UnityModManager.modEntries
            .Where(mod => mod.Info.Id != Main.Entry?.Info.Id && (ids ?? new string[0]).Contains(mod.Info.Id))
            .OrderBy(mod => mod.Info.Id, StringComparer.Ordinal).ToArray();

        internal static void DisableForNextLaunch(UnityModManager.ModEntry mod)
        {
            if (mod == null || mod == Main.Entry) throw new InvalidOperationException("Invalid recovery target.");
            var previous = mod.Enabled;
            try
            {
                // Avoid rebuilding live Harmony chains while the failed render is unwinding.
                mod.Enabled = false;
                UnityModManager.SaveSettingsAndParams();
                var path = Path.Combine(Path.GetDirectoryName(typeof(UnityModManager).Assembly.Location), "Params.xml");
                var document = new XmlDocument { XmlResolver = null };
                document.Load(path);
                var saved = document.SelectNodes("/Param/ModParams/Mod").Cast<XmlNode>()
                    .Any(node => node.Attributes?["Id"]?.Value == mod.Info.Id
                        && string.Equals(node.Attributes?["Enabled"]?.Value, "false", StringComparison.OrdinalIgnoreCase));
                if (!saved) throw new IOException("The disabled mod state could not be verified in UMM Params.xml.");
                Main.Entry?.Logger.Log("[Recovery] Disabled for next launch: " + mod.Info.Id + " " + mod.Info.Version);
            }
            catch { mod.Enabled = previous; throw; }
        }

        internal static bool CanRestart => Application.platform == RuntimePlatform.WindowsPlayer;

        internal static void Restart()
        {
            if (!CanRestart) throw new PlatformNotSupportedException("Use your launcher to restart the game on this platform.");
            UnityModManager.SaveSettingsAndParams();
            using (var protocol = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"steam\shell\open\command"))
                if (protocol == null) throw new InvalidOperationException("Steam launch protocol is not registered. Restart the game through Steam manually.");
            using (var current = Process.GetCurrentProcess())
            {
                var script = BuildSteamRestartScript(current.Id);
                var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe");
                using (var helper = Process.Start(new ProcessStartInfo {
                    FileName = powershell,
                    Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                }))
                {
                    if (helper == null) throw new IOException("Could not start the game restart helper.");
                    if (helper.WaitForExit(250)) throw new IOException("The game restart helper exited before the game closed.");
                }
            }
            Application.Quit();
        }

        internal static string BuildSteamRestartScript(int processId)
        {
            if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
            // Steam needs a moment to release its running-game state after the process exits.
            return "$ErrorActionPreference='Stop'; $game=Get-Process -Id " + processId
                + " -ErrorAction SilentlyContinue; if($game){$game | Wait-Process -Timeout 120}; "
                + "Start-Sleep -Seconds 2; Start-Process -FilePath 'steam://rungameid/977950'";
        }
    }
}
