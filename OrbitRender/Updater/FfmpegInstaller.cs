using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using UnityModManagerNet;

namespace OrbitRender
{
    // FFmpeg is an optional runtime dependency shared with TUFReplay. Both mods
    // install into <game>/ffmpeg/<platform>/ and coordinate through the same
    // receipt, lock file and executable layout.
    internal static class FfmpegInstaller
    {
        private const long MaximumDownloadBytes = 256L * 1024L * 1024L;
        private const int NotStarted = 0;
        private const int Downloading = 1;
        private const int Ready = 2;
        private const int Failed = 3;
        private const int Skipped = 4;
        private const int AwaitingConsent = 5;
        private const int Declined = 6;
        private const int DownloadStage = 0;
        private const int ExtractStage = 1;
        private const int InstallStage = 2;
        private const int VerifyStage = 3;
        private const int WaitStage = 4;
        private static readonly TimeSpan InstallLockTimeout = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan InstallLockPoll = TimeSpan.FromMilliseconds(250);
        private static int state = NotStarted;
        private static int installStage = DownloadStage;
        private static long downloadedBytes;
        private static long downloadTotalBytes = -1;
        private static string error;
        private static UnityModManager.ModEntry installEntry;
        private static RendererSettings installSettings;
        private static PlatformSpec installSpec;
        private static string installGameDirectory;
        private static string installRoot;
        private static string installPreviousMacDirectory;
        private static string installDestinationDirectory;
        private static string installDestination;

        private sealed class PlatformSpec
        {
            internal readonly string Name;
            internal readonly string ExecutableName;
            internal readonly string Url;
            internal readonly bool TarXz;
            internal readonly string ChecksumUrl;

            internal PlatformSpec(string name, string executableName, string url, bool tarXz,
                string checksumUrl = null)
            {
                Name = name;
                ExecutableName = executableName;
                Url = url;
                TarXz = tarXz;
                ChecksumUrl = checksumUrl;
            }
        }

        private sealed class LegacyCandidate
        {
            internal readonly string Directory;
            internal readonly string Executable;

            internal LegacyCandidate(string directory, string executable)
            {
                Directory = directory;
                Executable = executable;
            }
        }

        private sealed class InstallGate : IDisposable
        {
            internal readonly FileStream File;
            internal readonly bool Ready;
            internal readonly string ReadyPath;
            private readonly Mutex mutex;
            private readonly bool mutexHeld;

            internal InstallGate(FileStream file, Mutex mutex, bool mutexHeld, bool ready, string readyPath)
            {
                File = file;
                this.mutex = mutex;
                this.mutexHeld = mutexHeld;
                Ready = ready;
                ReadyPath = readyPath;
            }

            public void Dispose()
            {
                if (File != null)
                {
                    try { File.Dispose(); } catch { }
                }
                if (mutexHeld && mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
                if (mutex != null)
                {
                    try { mutex.Dispose(); } catch { }
                }
            }
        }

        internal static bool IsDownloading
        {
            get { return Interlocked.CompareExchange(ref state, NotStarted, NotStarted) == Downloading; }
        }

        internal static bool IsAwaitingConsent
        {
            get { return Interlocked.CompareExchange(ref state, NotStarted, NotStarted) == AwaitingConsent; }
        }

        internal static bool IsInstallPromptVisible
        {
            get
            {
                var current = Interlocked.CompareExchange(ref state, NotStarted, NotStarted);
                return current == AwaitingConsent || current == Downloading;
            }
        }

        internal static long DownloadedBytes
        {
            get { return Interlocked.Read(ref downloadedBytes); }
        }

        internal static long DownloadTotalBytes
        {
            get { return Interlocked.Read(ref downloadTotalBytes); }
        }

        internal static bool HasDownloadSize
        {
            get { return DownloadTotalBytes > 0; }
        }

        // The download accounts for most of the bar. Extraction, verification
        // and the final publish are intentionally visible too, so a finished
        // download does not look like the installer has frozen.
        internal static double Progress
        {
            get
            {
                var current = Interlocked.CompareExchange(ref state, NotStarted, NotStarted);
                if (current == Ready) return 1d;
                if (current != Downloading) return 0d;

                var stage = Interlocked.CompareExchange(ref installStage, DownloadStage, DownloadStage);
                if (stage == ExtractStage) return 0.82d;
                if (stage == VerifyStage) return 0.93d;
                if (stage == InstallStage) return 0.97d;
                if (stage == WaitStage) return 0.05d;

                var total = DownloadTotalBytes;
                if (total <= 0) return 0d;
                return Math.Min(0.80d, DownloadedBytes / (double)total * 0.80d);
            }
        }

        internal static bool NeedsInstallation
        {
            get
            {
                var current = Interlocked.CompareExchange(ref state, NotStarted, NotStarted);
                return current == AwaitingConsent || current == Downloading
                    || current == Failed || current == Declined;
            }
        }

        internal static void CheckForExport()
        {
            if (!NeedsInstallation || IsInstallPromptVisible || IsDownloading) return;
            var current = Interlocked.CompareExchange(ref state, NotStarted, NotStarted);
            if (current == Failed || current == Declined)
            {
                if (installSettings != null) installSettings.FfmpegInstallPrompted = false;
                if (Interlocked.CompareExchange(ref state, NotStarted, current) != current) return;
            }
            if (Interlocked.CompareExchange(ref state, NotStarted, NotStarted) == NotStarted)
                Start(installEntry, installSettings);
        }

        internal static string StatusMessage
        {
            get
            {
                var current = Interlocked.CompareExchange(ref state, NotStarted, NotStarted);
                if (current == AwaitingConsent)
                    return Localization.Get("ffmpeg-is-not-installed-waiting-for-your-confirmation-t");
                if (current == Downloading)
                {
                    var stage = Interlocked.CompareExchange(ref installStage, DownloadStage, DownloadStage);
                    if (stage == ExtractStage) return Localization.Get("extracting-ffmpeg");
                    if (stage == VerifyStage) return Localization.Get("verifying-ffmpeg");
                    if (stage == InstallStage) return Localization.Get("installing-ffmpeg-binary");
                    if (stage == WaitStage) return Localization.Get("waiting-for-shared-ffmpeg-installation");
                    return Localization.Get("downloading-ffmpeg-for-this-platform");
                }
                if (current == Failed)
                    return Localization.Format("ffmpeg-could-not-be-installed-automatically-value-check", error);
                if (current == Declined)
                    return Localization.Get("ffmpeg-installation-was-skipped-set-ffmpeg-executable-m");
                return string.Empty;
            }
        }

        internal static void Start(UnityModManager.ModEntry entry, RendererSettings settings)
        {
            if (entry == null || Interlocked.CompareExchange(ref state, Downloading, NotStarted) != NotStarted) return;

            try
            {
                if (settings != null && !string.IsNullOrWhiteSpace(settings.FfmpegExecutable))
                {
                    Interlocked.Exchange(ref state, Skipped);
                    entry.Logger.Log("[FFmpeg] FFmpeg auto-install skipped because a custom executable is configured.");
                    return;
                }

                var spec = GetPlatformSpec();
                var gameDirectory = GetGameDirectory();
                var root = FfmpegInstallContract.GetSharedRoot(gameDirectory);
                var destinationDirectory = FfmpegInstallContract.GetInstallDirectory(gameDirectory, spec.Name);
                var destination = Path.Combine(destinationDirectory, spec.ExecutableName);

                installEntry = entry;
                installSettings = settings;
                installSpec = spec;
                installGameDirectory = gameDirectory;
                installRoot = root;
                installPreviousMacDirectory = FfmpegInstallContract.GetPreviousMacInstallDirectory(
                    UnityEngine.Application.dataPath, IsMacOS(), spec.Name);
                installDestinationDirectory = destinationDirectory;
                installDestination = destination;

                var existing = InspectInstallation(destinationDirectory, spec, true, out var inspectError);
                if (existing != null)
                {
                    Interlocked.Exchange(ref state, Ready);
                    entry.Logger.Log("[FFmpeg] Using shared FFmpeg installation: " + existing);
                    return;
                }

                // An older per-mod installation can be promoted without any
                // network access. This also covers the flat <game>/ffmpeg
                // layout used by some existing setups.
                if (HasLegacyCandidate(spec))
                {
                    Interlocked.Exchange(ref installStage, InstallStage);
                    Interlocked.Exchange(ref state, Downloading);
                    entry.Logger.Log("[FFmpeg] Existing FFmpeg files were found; preparing the shared installation.");
                    ThreadPool.QueueUserWorkItem(_ => RunInstall(allowDownload: false));
                    return;
                }

                // Another mod may already be downloading into the shared
                // location. Wait for it instead of showing a second prompt.
                if (IsInstallLockHeld(root))
                {
                    Interlocked.Exchange(ref installStage, WaitStage);
                    Interlocked.Exchange(ref state, Downloading);
                    entry.Logger.Log("[FFmpeg] Waiting for another mod to finish the shared FFmpeg installation.");
                    ThreadPool.QueueUserWorkItem(_ => WaitForSharedInstall());
                    return;
                }

                if (settings != null && settings.FfmpegInstallPrompted)
                {
                    Interlocked.Exchange(ref state, Declined);
                    entry.Logger.Log("[FFmpeg] FFmpeg is not installed; the first-run installation prompt was already answered.");
                    return;
                }

                Interlocked.Exchange(ref state, AwaitingConsent);
                entry.Logger.Log("[FFmpeg] Shared FFmpeg is not installed. Waiting for user confirmation before downloading the "
                    + spec.Name + " binary.");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Interlocked.Exchange(ref state, Failed);
                entry.Logger.Log("[FFmpeg] FFmpeg auto-install is unavailable: " + ex.Message);
            }
        }

        internal static void ConfirmInstall()
        {
            var current = Interlocked.CompareExchange(ref state, NotStarted, NotStarted);
            if (current != AwaitingConsent && current != Failed && current != Declined) return;
            if (Interlocked.CompareExchange(ref state, Downloading, current) != current) return;

            var entry = installEntry;
            var settings = installSettings;
            var spec = installSpec;
            if (entry == null || spec == null || string.IsNullOrEmpty(installDestinationDirectory)
                || string.IsNullOrEmpty(installDestination))
            {
                error = "The FFmpeg installer was not initialized.";
                Interlocked.Exchange(ref state, Failed);
                return;
            }

            if (settings != null)
            {
                settings.FfmpegInstallPrompted = true;
                try { settings.Save(entry); }
                catch (Exception ex) { entry.Logger.Log("[FFmpeg] Could not save the FFmpeg installation choice: " + ex.Message); }
            }

            Interlocked.Exchange(ref installStage, DownloadStage);
            Interlocked.Exchange(ref downloadedBytes, 0L);
            Interlocked.Exchange(ref downloadTotalBytes, -1L);
            entry.Logger.Log("[FFmpeg] User approved the shared FFmpeg installation. Downloading the " + spec.Name + " binary.");
            ThreadPool.QueueUserWorkItem(_ => RunInstall(allowDownload: true));
        }

        internal static void DeclineInstall()
        {
            if (!IsAwaitingConsent) return;
            var entry = installEntry;
            var settings = installSettings;
            if (settings != null)
            {
                settings.FfmpegInstallPrompted = true;
                try { settings.Save(entry); }
                catch (Exception ex) { entry?.Logger.Log("[FFmpeg] Could not save the FFmpeg installation choice: " + ex.Message); }
            }
            Interlocked.Exchange(ref state, Declined);
            entry?.Logger.Log("[FFmpeg] User declined the first-run FFmpeg installation.");
        }

        private static void WaitForSharedInstall()
        {
            try
            {
                using (var gate = AcquireInstallGate())
                {
                    if (gate.Ready)
                    {
                        error = null;
                        Interlocked.Exchange(ref state, Ready);
                        installEntry.Logger.Log("[FFmpeg] Another mod completed the shared FFmpeg installation: " + gate.ReadyPath);
                        return;
                    }
                }

                var existing = InspectInstallation(installDestinationDirectory, installSpec, true, out var inspectError);
                if (existing != null)
                {
                    error = null;
                    Interlocked.Exchange(ref state, Ready);
                    installEntry.Logger.Log("[FFmpeg] Using shared FFmpeg installation: " + existing);
                    return;
                }

                if (HasLegacyCandidate(installSpec))
                {
                    RunInstall(allowDownload: false);
                    return;
                }

                if (installSettings != null && installSettings.FfmpegInstallPrompted)
                {
                    Interlocked.Exchange(ref state, Declined);
                    installEntry.Logger.Log("[FFmpeg] Shared FFmpeg installation is still missing after waiting.");
                    return;
                }

                Interlocked.Exchange(ref state, AwaitingConsent);
                installEntry.Logger.Log("[FFmpeg] Shared FFmpeg installation is still missing; waiting for user confirmation.");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Interlocked.Exchange(ref state, Failed);
                installEntry.Logger.Log("[FFmpeg] Waiting for the shared FFmpeg installation failed: " + ex.Message);
            }
        }

        private static void RunInstall(bool allowDownload)
        {
            try
            {
                using (var gate = AcquireInstallGate())
                {
                    if (gate.Ready)
                    {
                        error = null;
                        Interlocked.Exchange(ref state, Ready);
                        installEntry.Logger.Log("[FFmpeg] Reusing the shared FFmpeg installation: " + gate.ReadyPath);
                        return;
                    }

                    var existing = InspectInstallation(installDestinationDirectory, installSpec, true, out var inspectError);
                    if (existing != null)
                    {
                        error = null;
                        Interlocked.Exchange(ref state, Ready);
                        installEntry.Logger.Log("[FFmpeg] Using shared FFmpeg installation: " + existing);
                        return;
                    }

                    var adopted = AdoptLegacyInstallation(installSpec, out var adoptionError);
                    if (adopted != null)
                    {
                        error = null;
                        Interlocked.Exchange(ref state, Ready);
                        installEntry.Logger.Log("[FFmpeg] Promoted an existing FFmpeg installation to the shared location: " + adopted);
                        return;
                    }

                    if (!allowDownload)
                    {
                        if (installSettings != null && installSettings.FfmpegInstallPrompted)
                        {
                            error = adoptionError;
                            Interlocked.Exchange(ref state, Failed);
                            installEntry.Logger.Log("[FFmpeg] Existing FFmpeg files could not be promoted: " + adoptionError);
                        }
                        else
                        {
                            error = null;
                            Interlocked.Exchange(ref state, AwaitingConsent);
                            installEntry.Logger.Log("[FFmpeg] Existing FFmpeg files could not be promoted; waiting for user confirmation. "
                                + adoptionError);
                        }
                        return;
                    }

                    InstallDownload(installEntry, installSpec, installDestinationDirectory, installDestination);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Interlocked.Exchange(ref state, Failed);
                installEntry?.Logger.Log("[FFmpeg] FFmpeg auto-install failed: " + ex.Message);
            }
        }

        private static void InstallDownload(UnityModManager.ModEntry entry, PlatformSpec spec,
            string destinationDirectory, string destination)
        {
            string staging = null;
            try
            {
                staging = CreateStagingDirectory();
                var archivePath = Path.Combine(staging, spec.TarXz ? "ffmpeg.tar.xz" : "ffmpeg.zip");
                var extractionPath = Path.Combine(staging, "extracted");
                Directory.CreateDirectory(extractionPath);
                Download(spec, archivePath);
                Interlocked.Exchange(ref installStage, ExtractStage);
                if (spec.TarXz) ExtractTarXz(archivePath, extractionPath);
                else ExtractZip(archivePath, extractionPath);

                var source = FindFile(extractionPath, spec.ExecutableName);
                if (source == null)
                    throw new InvalidDataException("The FFmpeg archive did not contain " + spec.ExecutableName + ".");

                Interlocked.Exchange(ref installStage, VerifyStage);
                var stagedExecutable = Path.Combine(staging, spec.ExecutableName);
                File.Copy(source, stagedExecutable, false);
                EnsureUnixExecutable(stagedExecutable, spec);
                var version = VerifyExecutable(stagedExecutable);
                var receipt = new FfmpegInstallReceipt
                {
                    SchemaVersion = FfmpegInstallContract.SchemaVersion,
                    Platform = spec.Name,
                    Executable = spec.ExecutableName,
                    Source = spec.Url,
                    Sha256 = FfmpegInstallContract.HashFile(stagedExecutable),
                    Version = version,
                    InstalledAtUtc = DateTime.UtcNow
                };
                FfmpegInstallContract.WriteReceipt(staging, receipt);
                InstallMetadata(extractionPath, staging, spec);
                WriteDownloadSource(staging, spec, null);
                TryDeleteFile(archivePath);
                FfmpegInstallContract.TryDeleteDirectory(extractionPath);

                Interlocked.Exchange(ref installStage, InstallStage);
                var published = PublishStaging(staging, destinationDirectory, spec);
                staging = null;
                error = null;
                Interlocked.Exchange(ref state, Ready);
                entry.Logger.Log("[FFmpeg] Shared FFmpeg installed for " + spec.Name + ": " + published);
            }
            finally
            {
                if (staging != null) FfmpegInstallContract.TryDeleteDirectory(staging);
            }
        }

        internal static string GetInstalledExecutable()
        {
            try
            {
                var spec = GetPlatformSpec();
                var gameDirectory = GetGameDirectory();
                var path = Path.Combine(
                    FfmpegInstallContract.GetInstallDirectory(gameDirectory, spec.Name), spec.ExecutableName);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        // Kept for callers built against the previous installer API.
        internal static string GetBundledExecutable(string modPath)
        {
            return GetInstalledExecutable();
        }

        private static string GetGameDirectory()
        {
            return FfmpegInstallContract.GetGameDirectory(UnityEngine.Application.dataPath, IsMacOS());
        }

        private static bool IsMacOS()
        {
            var platform = UnityEngine.Application.platform;
            return platform == UnityEngine.RuntimePlatform.OSXPlayer || platform == UnityEngine.RuntimePlatform.OSXEditor;
        }

        private static PlatformSpec GetPlatformSpec()
        {
            var platform = UnityEngine.Application.platform;
            if (platform == UnityEngine.RuntimePlatform.WindowsPlayer || platform == UnityEngine.RuntimePlatform.WindowsEditor)
                return new PlatformSpec("windows-x64", "ffmpeg.exe",
                    "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip", false,
                    "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256");
            if (platform == UnityEngine.RuntimePlatform.OSXPlayer || platform == UnityEngine.RuntimePlatform.OSXEditor)
            {
                if (IsAppleSilicon())
                    return new PlatformSpec("macos-arm64", "ffmpeg",
                        "https://ffmpeg.martin-riedl.de/redirect/latest/macos/arm64/release/ffmpeg.zip", false);
                return new PlatformSpec("macos-x64", "ffmpeg", "https://evermeet.cx/ffmpeg/getrelease/zip", false);
            }
            if (platform == UnityEngine.RuntimePlatform.LinuxPlayer || platform == UnityEngine.RuntimePlatform.LinuxEditor)
                return new PlatformSpec("linux-x64", "ffmpeg",
                    "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz", true);
            throw new PlatformNotSupportedException("This platform is not supported by the automatic FFmpeg installer.");
        }

        private static bool IsAppleSilicon()
        {
            try
            {
                var output = RunProcess("/usr/bin/uname", "-m", 15000);
                return string.Equals(output.Trim(), "arm64", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(output.Trim(), "aarch64", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string InspectInstallation(string directory, PlatformSpec spec, bool repairMetadata,
            out string inspectionError)
        {
            inspectionError = null;
            try
            {
                var executable = Path.Combine(directory, spec.ExecutableName);
                if (!File.Exists(executable) || new FileInfo(executable).Length <= 0)
                {
                    inspectionError = "The FFmpeg executable is missing.";
                    return null;
                }

                var receipt = FfmpegInstallContract.ReadReceipt(
                    Path.Combine(directory, FfmpegInstallContract.ReceiptFileName));
                var hasValidReceipt = receipt != null
                    && receipt.SchemaVersion == FfmpegInstallContract.SchemaVersion
                    && string.Equals(receipt.Platform, spec.Name, StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(receipt.Executable)
                        || string.Equals(receipt.Executable, spec.ExecutableName, StringComparison.Ordinal));
                if (hasValidReceipt && !string.IsNullOrEmpty(receipt.Sha256))
                {
                    var actual = FfmpegInstallContract.HashFile(executable);
                    if (!string.Equals(actual, receipt.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        inspectionError = "The FFmpeg executable does not match its installation record.";
                        return null;
                    }
                }
                else
                {
                    hasValidReceipt = false;
                }

                string version;
                try
                {
                    version = VerifyExecutable(executable);
                }
                catch (Exception ex)
                {
                    inspectionError = ex.Message;
                    return null;
                }

                if (!hasValidReceipt && repairMetadata)
                {
                    FfmpegInstallContract.WriteReceipt(directory, new FfmpegInstallReceipt
                    {
                        SchemaVersion = FfmpegInstallContract.SchemaVersion,
                        Platform = spec.Name,
                        Executable = spec.ExecutableName,
                        Source = spec.Url,
                        Sha256 = FfmpegInstallContract.HashFile(executable),
                        Version = version,
                        InstalledAtUtc = DateTime.UtcNow
                    });
                }

                if (repairMetadata) EnsureLicense(directory);
                return executable;
            }
            catch (Exception ex)
            {
                inspectionError = ex.Message;
                return null;
            }
        }

        private static bool HasLegacyCandidate(PlatformSpec spec)
        {
            foreach (var candidate in GetLegacyCandidates(spec))
                if (File.Exists(candidate.Executable)) return true;
            return false;
        }

        private static string AdoptLegacyInstallation(PlatformSpec spec, out string adoptionError)
        {
            adoptionError = null;
            foreach (var candidate in GetLegacyCandidates(spec))
            {
                if (!File.Exists(candidate.Executable)) continue;
                string version;
                try
                {
                    var receipt = FfmpegInstallContract.ReadReceipt(
                        Path.Combine(candidate.Directory, FfmpegInstallContract.ReceiptFileName));
                    if (receipt != null && receipt.SchemaVersion == FfmpegInstallContract.SchemaVersion
                        && string.Equals(receipt.Platform, spec.Name, StringComparison.Ordinal)
                        && !string.IsNullOrEmpty(receipt.Sha256)
                        && FfmpegInstallContract.IsSha256(receipt.Sha256)
                        && !string.Equals(FfmpegInstallContract.HashFile(candidate.Executable), receipt.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        adoptionError = "An existing installation has a changed executable.";
                        continue;
                    }

                    version = VerifyExecutable(candidate.Executable);
                }
                catch (Exception ex)
                {
                    adoptionError = ex.Message;
                    continue;
                }

                string staging = null;
                try
                {
                    staging = CreateStagingDirectory();
                    var stagedExecutable = Path.Combine(staging, spec.ExecutableName);
                    File.Copy(candidate.Executable, stagedExecutable, false);
                    EnsureUnixExecutable(stagedExecutable, spec);
                    version = VerifyExecutable(stagedExecutable);
                    FfmpegInstallContract.WriteReceipt(staging, new FfmpegInstallReceipt
                    {
                        SchemaVersion = FfmpegInstallContract.SchemaVersion,
                        Platform = spec.Name,
                        Executable = spec.ExecutableName,
                        Source = spec.Url,
                        Sha256 = FfmpegInstallContract.HashFile(stagedExecutable),
                        Version = version,
                        InstalledAtUtc = DateTime.UtcNow
                    });
                    CopyLicense(candidate.Directory, staging);
                    EnsureLicense(staging);
                    WriteDownloadSource(staging, spec, candidate.Executable);
                    var published = PublishStaging(staging, installDestinationDirectory, spec);
                    staging = null;
                    return published;
                }
                catch (Exception ex)
                {
                    adoptionError = ex.Message;
                    installEntry?.Logger.Log("[FFmpeg] Could not promote " + candidate.Executable + ": " + ex);
                }
                finally
                {
                    if (staging != null) FfmpegInstallContract.TryDeleteDirectory(staging);
                }
            }
            if (string.IsNullOrEmpty(adoptionError))
                adoptionError = "No existing FFmpeg installation could be verified.";
            return null;
        }

        private static IEnumerable<LegacyCandidate> GetLegacyCandidates(PlatformSpec spec)
        {
            if (installPreviousMacDirectory != null)
                yield return new LegacyCandidate(installPreviousMacDirectory,
                    Path.Combine(installPreviousMacDirectory, spec.ExecutableName));
            var mods = Path.Combine(installGameDirectory, "Mods");
            yield return new LegacyCandidate(Path.Combine(mods, "OrbitRender", "FFmpeg", spec.Name),
                Path.Combine(mods, "OrbitRender", "FFmpeg", spec.Name, spec.ExecutableName));
            yield return new LegacyCandidate(Path.Combine(mods, "TUFReplay", "FFmpeg", spec.Name),
                Path.Combine(mods, "TUFReplay", "FFmpeg", spec.Name, spec.ExecutableName));
            if (installEntry != null)
            {
                var own = Path.Combine(installEntry.Path, "FFmpeg", spec.Name);
                yield return new LegacyCandidate(own, Path.Combine(own, spec.ExecutableName));
            }
            // Some game distributions already ship ffmpeg next to the game
            // executable. Reuse it, but never move or delete it.
            yield return new LegacyCandidate(installRoot, Path.Combine(installRoot, spec.ExecutableName));
        }

        private static string PublishStaging(string staging, string destinationDirectory, PlatformSpec spec)
        {
            var existing = InspectInstallation(destinationDirectory, spec, true, out var inspectError);
            if (existing != null)
            {
                FfmpegInstallContract.TryDeleteDirectory(staging);
                return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationDirectory));
            string backup = null;
            if (Directory.Exists(destinationDirectory))
            {
                backup = destinationDirectory + ".previous-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                try
                {
                    MoveDirectoryWithRetry(destinationDirectory, backup);
                }
                catch (Exception ex)
                {
                    throw new IOException(
                        "The existing FFmpeg installation is in use and was not overwritten. "
                        + "Close any running FFmpeg processes and try again.", ex);
                }
            }

            try
            {
                MoveDirectoryWithRetry(staging, destinationDirectory);
            }
            catch
            {
                if (!Directory.Exists(destinationDirectory) && backup != null && Directory.Exists(backup))
                {
                    try { Directory.Move(backup, destinationDirectory); } catch { }
                }
                throw;
            }

            if (backup != null)
                installEntry?.Logger.Log("[FFmpeg] Preserved the previous FFmpeg installation at " + backup);
            return Path.Combine(destinationDirectory, spec.ExecutableName);
        }

        private static string CreateStagingDirectory()
        {
            CreateDirectoryWithRetry(installRoot);
            // Use a staging directory directly under ffmpeg. A stale or locked
            // ".staging" directory from an interrupted install must not prevent
            // the next attempt from starting.
            var staging = Path.Combine(installRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            CreateDirectoryWithRetry(staging);
            return staging;
        }

        private static void CreateDirectoryWithRetry(string path)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(path);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    last = ex;
                    if (attempt < 5) Thread.Sleep(200);
                }
            }
            throw new IOException("Could not create the FFmpeg staging directory: " + path, last);
        }

        private static void MoveDirectoryWithRetry(string source, string destination)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    last = ex;
                    if (attempt < 5) Thread.Sleep(200);
                }
            }
            throw new IOException("Could not move the FFmpeg directory from " + source + " to " + destination, last);
        }

        private static InstallGate AcquireInstallGate()
        {
            var mutex = new Mutex(false, FfmpegInstallContract.MutexName);
            var mutexHeld = false;
            FileStream file = null;
            try
            {
                var deadline = DateTime.UtcNow + InstallLockTimeout;
                while (true)
                {
                    try { mutexHeld = mutex.WaitOne(InstallLockPoll); }
                    catch (AbandonedMutexException) { mutexHeld = true; }
                    if (mutexHeld) break;

                    var published = TryReadPublishedInstallation();
                    if (published != null)
                        return new InstallGate(null, mutex, false, true, published);
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException("Timed out waiting for another FFmpeg installation to finish.");
                }

                Directory.CreateDirectory(installRoot);
                var lockPath = Path.Combine(installRoot, FfmpegInstallContract.LockFileName);
                while (true)
                {
                    try
                    {
                        file = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        break;
                    }
                    catch (IOException)
                    {
                        var published = TryReadPublishedInstallation();
                        if (published != null)
                            return new InstallGate(null, mutex, mutexHeld, true, published);
                        if (DateTime.UtcNow >= deadline)
                            throw new TimeoutException("Timed out waiting for another FFmpeg installation to finish.");
                        Thread.Sleep(InstallLockPoll);
                    }
                }

                var ready = TryReadPublishedInstallation();
                return new InstallGate(file, mutex, mutexHeld, ready != null, ready);
            }
            catch
            {
                if (file != null) { try { file.Dispose(); } catch { } }
                if (mutexHeld) { try { mutex.ReleaseMutex(); } catch { } }
                try { mutex.Dispose(); } catch { }
                throw;
            }
        }

        private static string TryReadPublishedInstallation()
        {
            try
            {
                if (!File.Exists(installDestination)) return null;
                if (!File.Exists(Path.Combine(installDestinationDirectory, FfmpegInstallContract.ReceiptFileName)))
                    return null;
                return InspectInstallation(installDestinationDirectory, installSpec, true, out var inspectionError);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsInstallLockHeld(string root)
        {
            try
            {
                var path = Path.Combine(root, FfmpegInstallContract.LockFileName);
                if (!File.Exists(path)) return false;
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        private static void Download(PlatformSpec spec, string destination)
        {
            string expected = null;
            if (!string.IsNullOrEmpty(spec.ChecksumUrl))
            {
                var checksum = DownloadText(spec.ChecksumUrl);
                var fields = checksum.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                expected = fields.Length > 0 ? fields[0] : string.Empty;
                if (!FfmpegInstallContract.IsSha256(expected))
                    throw new InvalidDataException("The FFmpeg provider returned an invalid checksum.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(spec.Url);
            request.Method = "GET";
            request.UserAgent = "OrbitRender-FFmpegInstaller/2.0 (shared with TUFReplay)";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (response.ResponseUri != null && response.ResponseUri.Scheme != "https")
                    throw new IOException("The FFmpeg provider redirected to an insecure address.");
                if (response.ContentLength > MaximumDownloadBytes)
                    throw new InvalidDataException("The FFmpeg download is larger than the safety limit.");
                var buffer = new byte[64 * 1024];
                long total = 0;
                Interlocked.Exchange(ref downloadedBytes, 0L);
                Interlocked.Exchange(ref downloadTotalBytes, response.ContentLength > 0 ? response.ContentLength : -1L);
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaximumDownloadBytes)
                        throw new InvalidDataException("The FFmpeg download is larger than the safety limit.");
                    output.Write(buffer, 0, read);
                    Interlocked.Exchange(ref downloadedBytes, total);
                }
            }

            if (expected != null && !string.Equals(FfmpegInstallContract.HashFile(destination), expected,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The FFmpeg download did not match the provider's checksum.");
        }

        private static string DownloadText(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "OrbitRender-FFmpegInstaller/2.0 (shared with TUFReplay)";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = new MemoryStream())
            {
                if (response.ResponseUri != null && response.ResponseUri.Scheme != "https")
                    throw new IOException("The FFmpeg checksum provider redirected to an insecure address.");
                var buffer = new byte[4096];
                int total = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > 4096) throw new InvalidDataException("The FFmpeg checksum response is too large.");
                    output.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private static void ExtractZip(string archivePath, string destination)
        {
            var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    var fullPath = Path.GetFullPath(Path.Combine(destination, entryPath));
                    if (!fullPath.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException("The FFmpeg archive contains an unsafe path.");
                }
            }
            ZipFile.ExtractToDirectory(archivePath, destination);
        }

        private static void ExtractTarXz(string archivePath, string destination)
        {
            var listing = RunProcess("tar", "-tJf " + QuoteArgument(archivePath), 60000);
            using (var reader = new StringReader(listing))
            {
                string line;
                while ((line = reader.ReadLine()) != null) ValidateArchivePath(line);
            }
            RunProcess("tar", "-xJf " + QuoteArgument(archivePath) + " -C " + QuoteArgument(destination), 60000);
        }

        private static void ValidateArchivePath(string path)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(":"))
                throw new InvalidDataException("The FFmpeg archive contains an unsafe path.");
            foreach (var part in normalized.Split('/'))
            {
                if (part == "..") throw new InvalidDataException("The FFmpeg archive contains an unsafe path.");
            }
        }

        private static string FindFile(string root, string name)
        {
            foreach (var path in Directory.GetFiles(root, name, SearchOption.AllDirectories)) return path;
            return null;
        }

        private static void InstallMetadata(string extractionPath, string destinationDirectory, PlatformSpec spec)
        {
            var license = FindNamedFile(extractionPath, new[] { "FFmpeg-LICENSE.txt", "LICENSE.txt", "LICENSE", "GPLv3.txt", "COPYING" });
            if (license != null)
                File.Copy(license, Path.Combine(destinationDirectory, FfmpegInstallContract.LicenseFileName), false);
            else
                WriteDefaultLicense(destinationDirectory, spec);

            var readme = FindNamedFile(extractionPath, new[] { "FFmpeg-README.txt", "README.txt", "readme.txt" });
            if (readme != null) File.Copy(readme, Path.Combine(destinationDirectory, "FFmpeg-README.txt"), false);
        }

        private static void EnsureLicense(string directory)
        {
            var target = Path.Combine(directory, FfmpegInstallContract.LicenseFileName);
            if (File.Exists(target)) return;
            CopyLicense(directory, directory);
            if (!File.Exists(target)) WriteDefaultLicense(directory, installSpec);
        }

        private static void CopyLicense(string sourceDirectory, string destinationDirectory)
        {
            var target = Path.Combine(destinationDirectory, FfmpegInstallContract.LicenseFileName);
            if (File.Exists(target)) return;
            foreach (var name in new[] { "FFmpeg-LICENSE.txt", "LICENSE.txt", "LICENSE", "GPLv3.txt", "COPYING", "vendor-notice-1.txt" })
            {
                var source = Path.Combine(sourceDirectory, name);
                if (!File.Exists(source) || string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Copy(source, target, false); } catch { }
                if (File.Exists(target)) return;
            }
        }

        private static void WriteDefaultLicense(string directory, PlatformSpec spec)
        {
            var path = Path.Combine(directory, FfmpegInstallContract.LicenseFileName);
            if (File.Exists(path)) return;
            var text = "FFmpeg is distributed under the GNU GPLv3 license." + Environment.NewLine
                + "Full license: https://www.gnu.org/licenses/gpl-3.0.txt" + Environment.NewLine
                + "Source archive: " + spec.Url + Environment.NewLine
                + "Installed in the A Dance of Fire and Ice/ffmpeg folder." + Environment.NewLine;
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static void WriteDownloadSource(string directory, PlatformSpec spec, string migratedFrom)
        {
            var path = Path.Combine(directory, "DOWNLOAD-SOURCE.txt");
            var text = "Downloaded directly from: " + spec.Url + Environment.NewLine
                + "Upstream: https://ffmpeg.org/" + Environment.NewLine
                + "Installed in the A Dance of Fire and Ice/ffmpeg folder." + Environment.NewLine;
            if (!string.IsNullOrEmpty(migratedFrom))
                text += "Migrated from: " + migratedFrom + Environment.NewLine;
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string FindNamedFile(string root, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                foreach (var path in Directory.GetFiles(root, name, SearchOption.AllDirectories)) return path;
            }
            return null;
        }

        private static void EnsureUnixExecutable(string path, PlatformSpec spec)
        {
            if (spec.Name == "windows-x64") return;
            RunProcess("/bin/chmod", "+x " + QuoteArgument(path), 15000);
        }

        private static string VerifyExecutable(string path)
        {
            var version = RunProcess(path, "-version", 20000);
            if (string.IsNullOrEmpty(version)
                || !version.TrimStart().StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The FFmpeg executable could not be verified.");
            return version;
        }

        private static string RunProcess(string fileName, string arguments, int timeoutMilliseconds)
        {
            using (var process = Process.Start(new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                if (process == null) throw new InvalidOperationException("Could not start " + fileName + ".");
                var output = process.StandardOutput.ReadToEndAsync();
                var errorOutput = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException(fileName + " timed out.");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(fileName + " failed: " + errorOutput.GetAwaiter().GetResult().Trim());
                return output.GetAwaiter().GetResult();
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeleteFile(string path)
        {
            FfmpegInstallContract.TryDeleteFile(path);
        }

        private static void TryDeleteDirectory(string path)
        {
            FfmpegInstallContract.TryDeleteDirectory(path);
        }
    }
}
