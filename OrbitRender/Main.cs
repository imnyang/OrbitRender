using System;
using System.Globalization;
using System.IO;
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
        internal static RendererSettings Settings;
        private static Harmony harmony;
        private static GameObject host;
        private static string diagnosticsSummary = Localization.Get("diagnostics-have-not-been-run");
        private static string diagnosticsReport = Localization.Get("click-run-diagnostics-to-check-ffmpeg-the-output-folder");
        private static string localizedWidthText;
        private static string localizedHeightText;
        private static string localizedFpsText;
        private static string localizedVideoFpsText;
        private static string localizedBitrateText;
        private static string localizedEndDelayText;
        private static string localizedAudioGainText;
        private static int localizedWidthValue = int.MinValue;
        private static int localizedHeightValue = int.MinValue;
        private static int localizedFpsValue = int.MinValue;
        private static int localizedVideoFpsValue = int.MinValue;
        private static int localizedBitrateValue = int.MinValue;
        private static float localizedEndDelayValue = float.NaN;
        private static float localizedAudioGainValue = float.NaN;
        private static bool diagnosticsHaveRun;
        private static bool renderOptionsExpanded = true;
        private static bool visibleComponentsExpanded = true;
        private static bool encodingExpanded;
        private static bool filesExpanded;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            Entry = entry;
            Localization.Initialize(entry.Path);
            try
            {
                ReadCommandLineOptions();
                Settings = RendererSettings.Load(entry);
                Settings.OnChange();
                harmony = new Harmony(entry.Info.Id);
                harmony.PatchAll(typeof(Main).Assembly);
                host = new GameObject("OrbitRender");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<RendererController>();
                SceneManager.sceneLoaded += OnSceneLoaded;
                Enabled = true;
                StartRpcServer();
                FfmpegInstaller.Start(entry, Settings);
                UpdateManager.Start(entry);
                entry.OnToggle = (mod, enabled) =>
                {
                    if (!enabled)
                    {
                        AudioPreview.Stop();
                        ExportVideoDialog.CloseDialog();
                        RendererController.Instance?.StopAndClean();
                    }
                    Enabled = enabled;
                    if (!enabled) StopRpcServer();
                    else StartRpcServer();
                    return true;
                };
                entry.OnGUI = mod =>
                {
                    if (Settings == null) return;
                    if (!diagnosticsHaveRun)
                    {
                        diagnosticsSummary = Localization.Get("diagnostics-have-not-been-run");
                        diagnosticsReport = Localization.Get("click-run-diagnostics-to-check-ffmpeg-the-output-folder");
                    }
                    DrawSettings();
                };
                entry.OnUpdate = (mod, deltaTime) => UpdateManager.PumpMainThread();
                entry.OnSaveGUI = mod => Settings?.Save(mod);
                entry.OnUnload = mod =>
                {
                    ExportVideoDialog.CloseDialog();
                    AudioPreview.Stop();
                    RendererController.Instance?.StopAndClean();
                    StopRpcServer();
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    UnityEngine.Object.Destroy(host);
                    harmony.UnpatchAll(mod.Info.Id);
                    Settings = null;
                    return true;
                };
                entry.Logger.Log("Renderer loaded. Unity " + Application.unityVersion);
                return true;
            }
            catch (Exception ex)
            {
                if (host != null) UnityEngine.Object.Destroy(host);
                harmony?.UnpatchAll(entry.Info.Id);
                entry.Logger.Error(ex.ToString());
                return false;
            }
        }

        private static void ReadCommandLineOptions()
        {
            RpcEnabled = false;
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
            if (!RpcEnabled || RpcServer != null || RendererController.Instance == null) return;
            try
            {
                RpcServer = new RendererRpcServer(RendererController.Instance, RpcPort);
                RpcServer.Start();
            }
            catch (Exception ex)
            {
                RpcServer = null;
                Entry.Logger.Error("Renderer RPC could not start on port " + RpcPort + ": " + ex);
            }
        }

        private static void StopRpcServer()
        {
            var server = RpcServer;
            RpcServer = null;
            try { server?.Dispose(); } catch (Exception ex) { Entry.Logger.Error("Renderer RPC shutdown: " + ex); }
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
                RpcServer?.Rebind(RendererController.Instance);
                Entry.Logger.Log("Renderer host recreated after scene load: " + scene.name);
            }
        }

        private static void DrawSettings()
        {
            GUILayout.Label(Localization.Get("configure-the-defaults-used-when-exporting-a-video-per"));
            GUILayout.Space(4f);

            DrawRenderSettings();

            if (SettingsUi.DrawSectionHeader(Localization.Get("render-options"),
                ref renderOptionsExpanded))
            {
                DrawRenderOptions();
            }

            if (SettingsUi.DrawSectionHeader(Localization.Get("visible-components"),
                ref visibleComponentsExpanded))
            {
                DrawVisibleComponents();
            }

            GUILayout.Space(6f);
            if (SettingsUi.DrawSectionHeader(Localization.Get("encoding"),
                ref encodingExpanded))
            {
                DrawEncodingSettings();
            }

            GUILayout.Space(6f);
            if (SettingsUi.DrawSectionHeader(Localization.Get("files-troubleshooting"),
                ref filesExpanded))
            {
                DrawPathSettings();
                DrawFfmpegInstallControls();
                DrawDiagnostics();
                GUILayout.Space(4f);
                if (GUILayout.Button(Localization.Get("reset-render-settings-to-defaults")))
                {
                    Settings.ResetToDefaults();
                    Settings.OnChange();
                    ResetLocalizedFieldState();
                }
            }
        }

        private static void DrawRenderSettings()
        {
            GUILayout.Label(Localization.Get("preset"));
            var previousPreset = Settings.Preset;
            Settings.Preset = SettingsUi.DrawPreset(Settings.Preset);
            if (Settings.Preset != previousPreset)
            {
                Settings.OnChange();
                ResetLocalizedFieldState();
            }

            if (Settings.Preset == RendererPreset.Custom)
            {
                GUILayout.BeginHorizontal();
                Settings.Width = DrawLocalizedIntField(
                    Localization.Get("width"), Settings.Width,
                    ref localizedWidthText, ref localizedWidthValue, 90f);
                Settings.Height = DrawLocalizedIntField(
                    Localization.Get("height"), Settings.Height,
                    ref localizedHeightText, ref localizedHeightValue, 90f);
                Settings.BitrateMbps = DrawLocalizedIntField(
                    Localization.Get("bitrate"), Settings.BitrateMbps,
                    ref localizedBitrateText, ref localizedBitrateValue, 80f);
                GUILayout.Label("Mbps", GUILayout.Width(44f));
                GUILayout.EndHorizontal();
            }
            else
            {
                var profile = Settings.ResolveProfile();
                GUILayout.Label(Localization.Format("preset-output", profile.Width, profile.Height, profile.TargetFps, profile.VideoFps, profile.BitrateMbps));
            }

            GUILayout.BeginHorizontal();
            Settings.Fps = DrawLocalizedIntField(
                Localization.Get("ingame-fps"), Settings.Fps,
                ref localizedFpsText, ref localizedFpsValue, 80f);
            Settings.VideoFps = DrawLocalizedIntField(
                Localization.Get("video-fps"), Settings.VideoFps,
                ref localizedVideoFpsText, ref localizedVideoFpsValue, 80f);
            GUILayout.EndHorizontal();
        }

        private static void DrawRenderOptions()
        {
            Settings.EndDelaySeconds = DrawLocalizedFloatField(
                Localization.Get("end-delay-seconds"), Settings.EndDelaySeconds,
                ref localizedEndDelayText, ref localizedEndDelayValue, 90f);
            Settings.CaptureAudio = DrawLocalizedToggle(
                Localization.Get("capture-audio"), Settings.CaptureAudio);
            Settings.AudioGainDb = DrawLocalizedAudioGainField(
                Localization.Get("audio-volume-db"), Settings.AudioGainDb,
                ref localizedAudioGainText, ref localizedAudioGainValue, 90f);
            if (GUILayout.Button(AudioPreview.IsPlaying
                ? Localization.Get("stop-audio-preview")
                : Localization.Get("preview-audio"), GUILayout.ExpandWidth(false)))
            {
                AudioPreview.Toggle(RendererSettings.ClampAudioGainDb(Settings.AudioGainDb));
            }
            Settings.ShowRenderPreview = DrawLocalizedToggle(
                Localization.Get("show-render-preview"), Settings.ShowRenderPreview);
            Settings.BgaMode = DrawLocalizedToggle(
                Localization.Get("bga-mode-hide-tiles-planets-hit-sounds"), Settings.BgaMode);
            Settings.OpenOutputFolder = DrawLocalizedToggle(
                Localization.Get("open-output-folder-after-render"),
                Settings.OpenOutputFolder);
        }

        private static void DrawVisibleComponents()
        {
            Settings.ShowPlanetRings = DrawLocalizedToggle(
                Localization.Get("show-planet-rings"), Settings.ShowPlanetRings);
            Settings.ShowSongTitle = DrawLocalizedToggle(
                Localization.Get("show-song-title"), Settings.ShowSongTitle);
            Settings.ShowCountdown = DrawLocalizedToggle(
                Localization.Get("show-countdown"), Settings.ShowCountdown);
            Settings.ShowResultText = DrawLocalizedToggle(
                Localization.Get("show-result-text-hit-judgments-stay-hidden"), Settings.ShowResultText);
            Settings.ShowHitJudgments = DrawLocalizedToggle(
                Localization.Get("show-hit-judgments"), Settings.ShowHitJudgments);
        }

        private static void DrawEncodingSettings()
        {
            Settings.Encoding = SettingsUi.DrawEncoding(Settings.Encoding);
            Settings.Encoder = SettingsUi.DrawEncoder(Settings.Encoder);
            Settings.Codec = SettingsUi.DrawCodec(Settings.Codec);
            Settings.BitDepth = SettingsUi.DrawBitDepth(Settings.BitDepth);
        }

        private static void DrawPathSettings()
        {
            DrawPathField(
                Localization.Get("output-folder"),
                ref Settings.OutputDirectory,
                () => FileDialogService.PickFolder(GetOutputDialogDirectory()));

            DrawPathField(
                Localization.Get("ffmpeg-executable"),
                ref Settings.FfmpegExecutable,
                () => FileDialogService.PickFile(GetFfmpegDialogDirectory()));
        }

        private static void ResetLocalizedFieldState()
        {
            localizedWidthText = null;
            localizedHeightText = null;
            localizedFpsText = null;
            localizedVideoFpsText = null;
            localizedBitrateText = null;
            localizedEndDelayText = null;
            localizedAudioGainText = null;
            localizedWidthValue = int.MinValue;
            localizedHeightValue = int.MinValue;
            localizedFpsValue = int.MinValue;
            localizedVideoFpsValue = int.MinValue;
            localizedBitrateValue = int.MinValue;
            localizedEndDelayValue = float.NaN;
            localizedAudioGainValue = float.NaN;
        }

        private static bool DrawLocalizedToggle(string label, bool value)
        {
            return GUILayout.Toggle(value, label);
        }

        private static int DrawLocalizedIntField(string label, int value, ref string text,
            ref int syncedValue, float width)
        {
            if (text == null || syncedValue != value)
            {
                text = value.ToString(CultureInfo.InvariantCulture);
                syncedValue = value;
            }
            var edited = SettingsUi.LabeledField(label, text, width);
            if (!string.Equals(edited, text, StringComparison.Ordinal)) text = edited;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                syncedValue = parsed;
                return parsed;
            }
            return value;
        }

        private static float DrawLocalizedFloatField(string label, float value, ref string text,
            ref float syncedValue, float width)
        {
            if (text == null || float.IsNaN(syncedValue) || Math.Abs(syncedValue - value) > 0.0001f)
            {
                text = value.ToString("0.##", CultureInfo.InvariantCulture);
                syncedValue = value;
            }
            var edited = SettingsUi.LabeledField(label, text, width);
            if (!string.Equals(edited, text, StringComparison.Ordinal)) text = edited;
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                syncedValue = parsed;
                return parsed;
            }
            return value;
        }

        private static float DrawLocalizedAudioGainField(string label, float value, ref string text,
            ref float syncedValue, float width)
        {
            if (text == null || float.IsNaN(syncedValue) || Math.Abs(syncedValue - value) > 0.0001f)
            {
                text = value.ToString("0.##", CultureInfo.InvariantCulture);
                syncedValue = value;
            }
            var edited = SettingsUi.LabeledField(label, text, width);
            if (!string.Equals(edited, text, StringComparison.Ordinal)) text = edited;
            if (RendererSettings.TryParseAudioGainDb(text, out var parsed))
            {
                syncedValue = parsed;
                return parsed;
            }
            return value;
        }

        private static void DrawFfmpegInstallControls()
        {
            if (!FfmpegInstaller.NeedsInstallation) return;

            GUILayout.Space(8f);
            GUILayout.Label(Localization.Get("ffmpeg-setup"));
            GUILayout.Label(FfmpegInstaller.StatusMessage);
            if (FfmpegInstaller.IsDownloading) return;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Localization.Get("install-ffmpeg"),
                GUILayout.ExpandWidth(false)))
                FfmpegInstaller.ConfirmInstall();
            if (FfmpegInstaller.IsAwaitingConsent
                && GUILayout.Button(Localization.Get("not-now"), GUILayout.ExpandWidth(false)))
                FfmpegInstaller.DeclineInstall();
            GUILayout.EndHorizontal();
        }

        private static void DrawPathField(string label, ref string value, Func<string> pick)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(false));
            var current = value ?? string.Empty;
            var edited = GUILayout.TextField(current, GUILayout.ExpandWidth(true));
            if (!string.Equals(edited, current, StringComparison.Ordinal))
            {
                value = edited;
            }

            if (GUILayout.Button(Localization.Get("browse"), GUILayout.ExpandWidth(false)))
            {
                var selected = pick();
                if (!string.IsNullOrEmpty(selected))
                {
                    value = selected;
                }
            }
            GUILayout.EndHorizontal();
        }

        private static string GetOutputDialogDirectory()
        {
            try { return FileDialogService.FindExistingDirectory(Settings.ResolveOutputDirectory()); }
            catch (Exception ex)
            {
                Entry.Logger.Log("Could not resolve output folder for file picker: " + ex.Message);
                return FileDialogService.GetGameDirectory();
            }
        }

        private static string GetFfmpegDialogDirectory()
        {
            try
            {
                var configured = Environment.ExpandEnvironmentVariables((Settings.FfmpegExecutable ?? string.Empty).Trim());
                if (!string.IsNullOrEmpty(configured))
                {
                    if (!Path.IsPathRooted(configured))
                        configured = Path.Combine(Entry.Path, configured);
                    return FileDialogService.FindExistingDirectory(configured);
                }
            }
            catch (Exception ex)
            {
                Entry.Logger.Log("Could not resolve FFmpeg path for file picker: " + ex.Message);
            }
            return FileDialogService.GetGameDirectory();
        }

        private static void DrawDiagnostics()
        {
            GUILayout.Space(8f);
            GUILayout.Label(Localization.Get("diagnostics"));
            if (!diagnosticsHaveRun)
            {
                GUILayout.Label(Localization.Get("run-this-before-reporting-an-export-problem-it-checks-f"));
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Localization.Get("run-diagnostics"),
                GUILayout.ExpandWidth(false))) RunDiagnostics();
            if (diagnosticsHaveRun
                && GUILayout.Button(Localization.Get("copy-report"),
                    GUILayout.ExpandWidth(false)))
                GUIUtility.systemCopyBuffer = diagnosticsSummary + Environment.NewLine + diagnosticsReport;
            GUILayout.EndHorizontal();
            if (diagnosticsHaveRun)
            {
                GUILayout.Label(diagnosticsSummary);
                GUILayout.TextArea(diagnosticsReport, GUILayout.MinHeight(92f));
            }
        }

        private static void RunDiagnostics()
        {
            try
            {
                var result = RendererDiagnostics.Run(Settings);
                diagnosticsHaveRun = true;
                diagnosticsSummary = result.Summary;
                diagnosticsReport = result.Report;
                if (result.HasErrors) Entry.Logger.Error(diagnosticsSummary + Environment.NewLine + diagnosticsReport);
                else Entry.Logger.Log(diagnosticsSummary + Environment.NewLine + diagnosticsReport);
            }
            catch (Exception ex)
            {
                diagnosticsHaveRun = true;
                diagnosticsSummary = Localization.Get("diagnostics-failed-unexpectedly");
                diagnosticsReport = ex.ToString();
                Entry.Logger.Error("Renderer diagnostics failed: " + ex);
            }
        }
    }
}
