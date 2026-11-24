using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OrbitRender
{
    /// <summary>Safe extraction and validation of a downloaded update ZIP.</summary>
    internal static class UpdatePackage
    {
        private const long MaximumExtractedBytes = 1024L * 1024 * 1024;
        private const int MaximumEntries = 4096;

        /// <summary>
        /// Extracts <paramref name="archive"/> into <paramref name="destination"/> and returns the directory
        /// containing the OrbitRender Info.json whose version equals <paramref name="expected"/>.
        /// </summary>
        internal static string ExtractAndValidate(string archive, string destination, Version expected)
        {
            Extract(archive, destination);

            foreach (var infoPath in Directory.GetFiles(destination, "Info.json", SearchOption.AllDirectories))
            {
                JObject info;
                try { info = JObject.Parse(File.ReadAllText(infoPath)); }
                catch (JsonException) { continue; } // unrelated Info.json inside the archive

                if (!string.Equals((string)info["Id"], "OrbitRender", StringComparison.OrdinalIgnoreCase)) continue;
                Version packageVersion;
                if (!UpdateVersion.TryParse((string)info["Version"], out packageVersion) || packageVersion != Normalize(expected))
                    throw new InvalidDataException("The update package version does not match the release tag.");

                var root = Path.GetDirectoryName(infoPath);
                var assembly = (string)info["AssemblyName"] ?? "OrbitRender.dll";
                if (!File.Exists(Path.Combine(root, assembly)))
                    throw new InvalidDataException("The update package is missing " + assembly + ".");
                return root;
            }
            throw new InvalidDataException("The update archive does not contain a valid OrbitRender package.");
        }

        /// <summary>Marks bundled Unix executables runnable (ZIP extraction drops the mode bits).</summary>
        internal static void EnsureUnixExecutables(string packageRoot)
        {
            if (Path.DirectorySeparatorChar == '\\') return;
            foreach (var path in Directory.GetFiles(packageRoot, "ffmpeg", SearchOption.AllDirectories))
            {
                using (var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = "+x \"" + path.Replace("\"", "\\\"") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (chmod == null || !chmod.WaitForExit(5000) || chmod.ExitCode != 0)
                        throw new InvalidOperationException("Could not mark the bundled FFmpeg executable.");
                }
            }
        }

        private static Version Normalize(Version version)
        {
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
        }

        private static void Extract(string archivePath, string destination)
        {
            Directory.CreateDirectory(destination);
            var rootPrefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            using (var zip = ZipFile.OpenRead(archivePath))
            {
                if (zip.Entries.Count > MaximumEntries) throw new InvalidDataException("The update archive has too many entries.");

                long declared = 0;
                foreach (var entry in zip.Entries)
                {
                    declared += entry.Length;
                    if (declared > MaximumExtractedBytes) throw new InvalidDataException("The update archive expands too large.");
                    var target = ResolveEntry(destination, entry.FullName);
                    if (!target.StartsWith(rootPrefix, comparison))
                        throw new InvalidDataException("The update archive contains an unsafe path.");
                }

                // Count real bytes too: the declared size in a ZIP header is attacker-controlled.
                long written = 0;
                foreach (var entry in zip.Entries)
                {
                    var target = ResolveEntry(destination, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[64 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            written += read;
                            if (written > MaximumExtractedBytes) throw new InvalidDataException("The update archive expands too large.");
                            output.Write(buffer, 0, read);
                        }
                    }
                }
            }
        }

        private static string ResolveEntry(string destination, string entryName)
        {
            return Path.GetFullPath(Path.Combine(destination, entryName.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
