using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace OrbitRender
{
    /// <summary>Copies a staged package over the installed mod, with rollback.</summary>
    internal static class UpdateInstaller
    {
        private const string ApplyingMarker = "applying.lock";

        /// <summary>
        /// Overwrites installed files with the staged ones. Info.json is written last so a half-finished
        /// install never advertises the new version. On any failure every touched file is restored.
        /// </summary>
        internal static void Install(string packageRoot, string targetDirectory, string backupDirectory)
        {
            var files = new List<string>(Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories));
            files.Sort((a, b) => IsInfo(a).CompareTo(IsInfo(b)));

            var backedUp = new List<string>();
            var created = new List<string>();
            try
            {
                foreach (var source in files)
                {
                    var relative = Relative(packageRoot, source);
                    var destination = Path.Combine(targetDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));

                    if (File.Exists(destination))
                    {
                        var backup = Path.Combine(backupDirectory, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        File.Copy(destination, backup, true);
                        backedUp.Add(relative);
                    }
                    else
                    {
                        created.Add(destination);
                    }
                    CopyWithRetry(source, destination);
                }
            }
            catch
            {
                Rollback(targetDirectory, backupDirectory, backedUp, created);
                throw;
            }
        }

        /// <summary>
        /// Starts a detached helper that waits for this process to exit, then installs the staged package.
        /// Used when a loaded DLL is locked and cannot be replaced while the game runs.
        /// </summary>
        internal static void ScheduleAfterExit(string packageRoot, string targetDirectory, string workDirectory)
        {
            var pid = Process.GetCurrentProcess().Id;
            File.WriteAllText(Path.Combine(workDirectory, ApplyingMarker), DateTime.UtcNow.ToString("o"));

            if (Path.DirectorySeparatorChar == '\\')
            {
                var script = Path.Combine(workDirectory, "apply-update.ps1");
                File.WriteAllText(script, WindowsScript(pid, packageRoot, targetDirectory, workDirectory), new UTF8Encoding(true));
                Launch("powershell.exe", "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"");
            }
            else
            {
                var script = Path.Combine(workDirectory, "apply-update.sh");
                File.WriteAllText(script, UnixScript(pid, packageRoot, targetDirectory, workDirectory), new UTF8Encoding(false));
                // Interpreter passed explicitly: no shebang / FHS path needed (works on NixOS).
                Launch("sh", ShellQuote(script));
            }
        }

        /// <summary>True while a helper started within the last few minutes may still be installing.</summary>
        internal static bool IsHelperActive(string workDirectory)
        {
            var marker = Path.Combine(workDirectory, ApplyingMarker);
            return File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < TimeSpan.FromMinutes(5);
        }

        private static void Rollback(string target, string backupDirectory, List<string> backedUp, List<string> created)
        {
            foreach (var relative in backedUp)
            {
                try { File.Copy(Path.Combine(backupDirectory, relative), Path.Combine(target, relative), true); }
                catch { /* best effort: keep restoring the rest */ }
            }
            foreach (var path in created)
            {
                try { File.Delete(path); }
                catch { }
            }
        }

        private static void CopyWithRetry(string source, string destination)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try { File.Copy(source, destination, true); return; }
                catch (IOException ex) { last = ex; }
                catch (UnauthorizedAccessException ex) { last = ex; }
                Thread.Sleep(150);
            }
            throw new IOException("Could not replace " + Path.GetFileName(destination), last);
        }

        private static int IsInfo(string path)
        {
            return string.Equals(Path.GetFileName(path), "Info.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private static string Relative(string root, string path)
        {
            return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void Launch(string fileName, string arguments)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null) throw new InvalidOperationException("Could not start the update installer.");
            process.Dispose();
        }

        private static string WindowsScript(int pid, string source, string target, string work)
        {
            return
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$pidToWait = " + pid + "\r\n" +
                "$source = " + PsQuote(source) + "\r\n" +
                "$target = " + PsQuote(target) + "\r\n" +
                "$work = " + PsQuote(work) + "\r\n" +
                "for ($i = 0; $i -lt 240; $i++) { if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 500 }\r\n" +
                "$ok = $false\r\n" +
                "for ($i = 0; $i -lt 30 -and -not $ok; $i++) {\r\n" +
                "  try {\r\n" +
                "    $files = Get-ChildItem -LiteralPath $source -File -Recurse | Sort-Object { $_.Name -ieq 'Info.json' }\r\n" +
                "    foreach ($f in $files) {\r\n" +
                "      $rel = $f.FullName.Substring($source.Length).TrimStart('\\','/')\r\n" +
                "      $dest = Join-Path $target $rel\r\n" +
                "      New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null\r\n" +
                "      Copy-Item -LiteralPath $f.FullName -Destination $dest -Force\r\n" +
                "    }\r\n" +
                "    $ok = $true\r\n" +
                "  } catch { Start-Sleep -Seconds 1 }\r\n" +
                "}\r\n" +
                "if ($ok) { Remove-Item -LiteralPath $work -Recurse -Force } else { Remove-Item -LiteralPath (Join-Path $work 'applying.lock') -Force -ErrorAction SilentlyContinue; exit 1 }\r\n";
        }

        private static string UnixScript(int pid, string source, string target, string work)
        {
            return
                "set -u\n" +
                "pid_to_wait=" + pid + "\n" +
                "source=" + ShellQuote(source) + "\n" +
                "target=" + ShellQuote(target) + "\n" +
                "work=" + ShellQuote(work) + "\n" +
                "i=0\n" +
                "while kill -0 \"$pid_to_wait\" 2>/dev/null && [ $i -lt 240 ]; do sleep 0.5; i=$((i + 1)); done\n" +
                "attempt=0\n" +
                "while [ $attempt -lt 30 ]; do\n" +
                "  mkdir -p \"$target\" && cp -R \"$source\"/. \"$target\"/ && rm -rf \"$work\" && exit 0\n" +
                "  attempt=$((attempt + 1)); sleep 1\n" +
                "done\n" +
                "rm -f \"$work/applying.lock\"\n" +
                "exit 1\n";
        }

        private static string PsQuote(string value) { return "'" + value.Replace("'", "''") + "'"; }
        private static string ShellQuote(string value) { return "'" + value.Replace("'", "'\"'\"'") + "'"; }
    }
}
