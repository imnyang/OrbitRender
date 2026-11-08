using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace OrbitRender
{
    // The on-disk contract shared with TUFReplay. Both mods install FFmpeg into
    // <game>/ffmpeg/<platform>/ and accept the same receipt, lock file and
    // executable layout so either mod can reuse the other's installation.
    internal static class FfmpegInstallContract
    {
        internal const int SchemaVersion = 1;
        internal const string SharedFolderName = "ffmpeg";
        internal const string ReceiptFileName = "installation.json";
        internal const string LicenseFileName = "LICENSE.txt";
        internal const string LockFileName = ".install.lock";
        internal const string MutexName = "Adofai.SharedFFmpeg.Install";
        internal const int MaximumReceiptBytes = 64 * 1024;

        // Unity's data folder is <game>/<name>_Data, or <game>/<name>.app/Contents on
        // macOS. The game folder is the one that holds Mods, outside the app bundle.
        internal static string GetGameDirectory(string dataPath, bool macOS)
        {
            var dataDirectory = new DirectoryInfo(Path.GetFullPath(dataPath));
            return IsAppContents(dataDirectory, macOS)
                ? dataDirectory.Parent.Parent.FullName
                : dataDirectory.Parent.FullName;
        }

        // OrbitRender 2.8.0 to 2.8.2 put the shared folder inside the app bundle on
        // macOS, so TUFReplay never found it there.
        internal static string GetPreviousMacInstallDirectory(string dataPath, bool macOS, string platformName)
        {
            var dataDirectory = new DirectoryInfo(Path.GetFullPath(dataPath));
            return IsAppContents(dataDirectory, macOS)
                ? GetInstallDirectory(dataDirectory.FullName, platformName)
                : null;
        }

        private static bool IsAppContents(DirectoryInfo dataDirectory, bool macOS)
        {
            return macOS && string.Equals(dataDirectory.Name, "Contents", StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetSharedRoot(string gameDirectory)
        {
            return Path.Combine(gameDirectory, SharedFolderName);
        }

        internal static string GetInstallDirectory(string gameDirectory, string platformName)
        {
            return Path.Combine(GetSharedRoot(gameDirectory), platformName);
        }

        internal static FfmpegInstallReceipt ReadReceipt(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumReceiptBytes) return null;
                return JsonConvert.DeserializeObject<FfmpegInstallReceipt>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        internal static void WriteReceipt(string directory, FfmpegInstallReceipt receipt)
        {
            var path = Path.Combine(directory, ReceiptFileName);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(receipt, Formatting.Indented),
                    new UTF8Encoding(false));
                if (!File.Exists(path))
                {
                    File.Move(temporary, path);
                    return;
                }

                try
                {
                    File.Replace(temporary, path, null);
                }
                catch
                {
                    File.Copy(temporary, path, true);
                    TryDeleteFile(temporary);
                }
            }
            finally
            {
                TryDeleteFile(temporary);
            }
        }

        internal static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        internal static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        internal static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }

    internal sealed class FfmpegInstallReceipt
    {
        public int SchemaVersion;
        public string Platform;
        public string Executable;
        public string Source;
        public string Sha256;
        public string Version;
        public DateTime InstalledAtUtc;
    }
}
