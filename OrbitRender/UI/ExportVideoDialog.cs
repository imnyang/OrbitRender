using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using OrbitRender.Renderer;

namespace OrbitRender.UI
{
    internal static class ExportVideoDialog
    {
        private const int WindowId = 18473;
        private static bool open;
        private static scnEditor editor;
        private static Draft draft;
        private static string error;
        private static Rect windowRect;
        private static Vector2 scrollPosition;
        private static bool renderOptionsExpanded = true;
        private static bool visibleComponentsExpanded = true;
        private static bool encodingExpanded;

        internal static bool IsOpen => open;

        internal static void CloseDialog()
        {
            Close();
        }

        internal static void Open(scnEditor owner)
        {
            if (owner == null || Main.Settings == null) return;
            editor = owner;
            draft = Draft.From(Main.Settings);
            error = string.Empty;
            scrollPosition = Vector2.zero;
            open = true;
            ModalInputBlocker.Open();
            editor.ShowFileActionsPanel(false);
            windowRect = new Rect(0f, 0f, 720f, 610f);
        }

        internal static void Draw(RendererController renderer)
        {
            if (!open || draft == null) return;
            // Scale the entire IMGUI window so text and controls grow with high
            // resolution or high pixel density, while keeping a screen margin.
            var resolutionScale = Mathf.Max(1f, Screen.height / 1080f);
            var dpiScale = Screen.dpi > 0f ? Mathf.Max(1f, Screen.dpi / 96f) : 1f;
            var fitScale = Mathf.Min((Screen.width - 36f) / 760f, (Screen.height - 36f) / 680f);
            var scale = Mathf.Min(Mathf.Min(Mathf.Max(resolutionScale, dpiScale), 2f),
                Mathf.Max(1f, fitScale));
            var width = Mathf.Min(760f, (Screen.width - 36f) / scale);
            var height = Mathf.Min(680f, (Screen.height - 36f) / scale);
            if (windowRect.width != width || windowRect.height != height)
            {
                windowRect.width = width;
                windowRect.height = height;
            }
            windowRect.x = (Screen.width - windowRect.width) * 0.5f;
            windowRect.y = Mathf.Max(18f, (Screen.height - windowRect.height) * 0.5f);

            if (Event.current.type == EventType.Repaint)
            {
                var previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            var originalMatrix = GUI.matrix;
            var originalColor = GUI.color;
            var originalBackgroundColor = GUI.backgroundColor;
            var originalContentColor = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                GUIUtility.ScaleAroundPivot(new Vector2(scale, scale),
                    new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                windowRect = GUI.Window(WindowId, windowRect, id => DrawWindow(id, renderer),
                    string.Empty, UiTheme.Window);
            }
            finally
            {
                GUI.matrix = originalMatrix;
                GUI.color = originalColor;
                GUI.backgroundColor = originalBackgroundColor;
                GUI.contentColor = originalContentColor;
            }
            if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint)
                Event.current.Use();
        }

        private static void DrawWindow(int id, RendererController renderer)
        {
            UiTheme.DrawWindowBackground(new Rect(0f, 0f, windowRect.width, windowRect.height));
            GUILayout.BeginVertical();
            GUILayout.Label(Localization.Get("export-video"), UiTheme.Title);
            GUILayout.Space(8f);
            var originalSkin = GUI.skin;
            GUI.skin = UiTheme.ScrollSkin(originalSkin);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            try
            {
            GUILayout.Label(Localization.Get("choose-the-settings-for-this-video-export"), UiTheme.Label);
            GUILayout.Space(12f);

            GUILayout.Label(Localization.Get("preset"), UiTheme.Label);
            var preset = SettingsUi.DrawPreset(draft.Preset, true);
            if (preset != draft.Preset)
            {
                draft.Preset = preset;
                if (preset != RendererPreset.Custom) draft.ApplyPreset();
            }

            if (draft.Preset == RendererPreset.Custom)
            {
                GUILayout.BeginHorizontal();
                draft.WidthText = SettingsUi.LabeledField(Localization.Get("width"), draft.WidthText, 90f, true);
                draft.HeightText = SettingsUi.LabeledField(Localization.Get("height"), draft.HeightText, 90f, true);
                draft.BitrateText = SettingsUi.LabeledField(Localization.Get("bitrate"), draft.BitrateText, 80f, true);
                GUILayout.Label("Mbps", UiTheme.Label, GUILayout.Width(44f));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(Localization.Format("preset-output", draft.WidthText, draft.HeightText, draft.FpsText, draft.VideoFpsText, draft.BitrateText), UiTheme.Label);
            }

            GUILayout.BeginHorizontal();
            draft.FpsText = SettingsUi.LabeledField(Localization.Get("ingame-fps"), draft.FpsText, 80f, true);
            draft.VideoFpsText = SettingsUi.LabeledField(Localization.Get("video-fps"), draft.VideoFpsText, 80f, true);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            if (SettingsUi.DrawSectionHeader(Localization.Get("render-options"),
                ref renderOptionsExpanded, true))
            {
                draft.EndDelayText = SettingsUi.LabeledField(Localization.Get("end-delay-seconds"),
                    draft.EndDelayText, 90f, true);
                draft.CaptureAudio = UiTheme.DrawToggle(draft.CaptureAudio,
                    Localization.Get("capture-audio"));
                draft.BgaMode = UiTheme.DrawToggle(draft.BgaMode,
                    Localization.Get("bga-mode-hide-tiles-planets-hit-sounds"));
                draft.OpenOutputFolder = UiTheme.DrawToggle(draft.OpenOutputFolder,
                    Localization.Get("open-output-folder-after-render"));
                draft.SaveAsDefault = UiTheme.DrawToggle(draft.SaveAsDefault,
                    Localization.Get("save-these-values-as-the-default-renderer-settings"));
            }

            if (SettingsUi.DrawSectionHeader(Localization.Get("visible-components"),
                ref visibleComponentsExpanded, true))
            {
                draft.ShowPlanetRings = UiTheme.DrawToggle(draft.ShowPlanetRings,
                    Localization.Get("show-planet-rings"));
                draft.ShowSongTitle = UiTheme.DrawToggle(draft.ShowSongTitle,
                    Localization.Get("show-song-title"));
                draft.ShowCountdown = UiTheme.DrawToggle(draft.ShowCountdown,
                    Localization.Get("show-countdown"));
                draft.ShowResultText = UiTheme.DrawToggle(draft.ShowResultText,
                    Localization.Get("show-result-text-hit-judgments-stay-hidden"));
                draft.ShowHitJudgments = UiTheme.DrawToggle(draft.ShowHitJudgments,
                    Localization.Get("show-hit-judgments"));
            }

            GUILayout.Space(6f);
            if (SettingsUi.DrawSectionHeader(Localization.Get("encoding"), ref encodingExpanded, true))
            {
                draft.Encoding = SettingsUi.DrawEncoding(draft.Encoding, true);
                draft.Encoder = SettingsUi.DrawEncoder(draft.Encoder, true);
                draft.Codec = SettingsUi.DrawCodec(draft.Codec, true);
                draft.BitDepth = SettingsUi.DrawBitDepth(draft.BitDepth, true);
            }

            }
            finally
            {
                GUILayout.EndScrollView();
                GUI.skin = originalSkin;
            }
            if (!string.IsNullOrEmpty(error))
            {
                var previous = GUI.color;
                GUI.color = new Color(1f, 0.55f, 0.55f, 1f);
                GUILayout.Label(error, UiTheme.Label);
                GUI.color = previous;
            }

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Localization.Get("cancel"), UiTheme.Button, GUILayout.Width(120f))) Close();
            GUILayout.FlexibleSpace();
            var selectedTiles = GetSelectedTileRange();
            if (selectedTiles.Length >= 2
                && GUILayout.Button(Localization.Get("export-video-selection"), UiTheme.Button, GUILayout.Width(190f)))
                Confirm(renderer, true);
            if (GUILayout.Button(Localization.Get("export-video"), UiTheme.PrimaryButton, GUILayout.Width(150f)))
                Confirm(renderer, false);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 26f));
        }

        private static void Confirm(RendererController renderer, bool selectionOnly)
        {
            if (renderer == null || renderer.Busy)
            {
                error = Localization.Get("a-render-is-already-in-progress");
                return;
            }

            if (!draft.TryCreateOptions(out var options, out var message))
            {
                error = message;
                return;
            }

            if (selectionOnly)
            {
                var selected = GetSelectedTileRange();
                if (selected.Length < 2)
                {
                    error = Localization.Get("select-at-least-two-tiles-to-export-a-selection");
                    return;
                }
                options.SelectionStartTile = selected[0];
                options.SelectionEndTile = selected[selected.Length - 1];
            }

            if (draft.SaveAsDefault)
            {
                draft.ApplyTo(Main.Settings, options);
                Main.Settings.Save(Main.Entry);
            }

            var targetEditor = editor;
            Close();
            if (targetEditor != null) targetEditor.ShowFileActionsPanel(false);
            renderer.StartRender(options);
        }

        private static int[] GetSelectedTileRange()
        {
            return editor != null && editor.selectedFloors != null
                ? editor.selectedFloors.Where(floor => floor != null)
                    .Select(floor => floor.seqID).Distinct().OrderBy(id => id).ToArray()
                : new int[0];
        }

        private static void Close()
        {
            open = false;
            ModalInputBlocker.Close();
            editor = null;
            draft = null;
            error = string.Empty;
        }

        private sealed class Draft
        {
            internal RendererPreset Preset;
            internal string WidthText;
            internal string HeightText;
            internal string FpsText;
            internal string VideoFpsText;
            internal string BitrateText;
            internal string EndDelayText;
            internal bool CaptureAudio;
            internal bool BgaMode;
            internal bool ShowPlanetRings;
            internal bool ShowSongTitle;
            internal bool ShowCountdown;
            internal bool ShowResultText;
            internal bool ShowHitJudgments;
            internal bool OpenOutputFolder;
            internal bool SaveAsDefault;
            internal EncoderSpeed Encoding;
            internal VideoEncoder Encoder;
            internal VideoCodec Codec;
            internal VideoBitDepth BitDepth;

            internal static Draft From(RendererSettings settings)
            {
                return new Draft {
                    Preset = settings.Preset,
                    WidthText = settings.Width.ToString(CultureInfo.InvariantCulture),
                    HeightText = settings.Height.ToString(CultureInfo.InvariantCulture),
                    FpsText = settings.Fps.ToString(CultureInfo.InvariantCulture),
                    VideoFpsText = settings.VideoFps.ToString(CultureInfo.InvariantCulture),
                    BitrateText = settings.BitrateMbps.ToString(CultureInfo.InvariantCulture),
                    EndDelayText = settings.EndDelaySeconds.ToString("0.##", CultureInfo.InvariantCulture),
                    CaptureAudio = settings.CaptureAudio,
                    BgaMode = settings.BgaMode,
                    ShowPlanetRings = settings.ShowPlanetRings,
                    ShowSongTitle = settings.ShowSongTitle,
                    ShowCountdown = settings.ShowCountdown,
                    ShowResultText = settings.ShowResultText,
                    ShowHitJudgments = settings.ShowHitJudgments,
                    OpenOutputFolder = settings.OpenOutputFolder,
                    Encoding = settings.Encoding,
                    Encoder = settings.Encoder,
                    Codec = settings.Codec,
                    BitDepth = settings.BitDepth
                };
            }

            internal void ApplyPreset()
            {
                switch (Preset)
                {
                    case RendererPreset.Preview: SetVideoValues(1280, 720, 30, 30, 8); break;
                    case RendererPreset.QHD: SetVideoValues(2560, 1440, 60, 60, 30); break;
                    case RendererPreset.UHD4K: SetVideoValues(3840, 2160, 60, 60, 50); break;
                    case RendererPreset.FullHD: SetVideoValues(1920, 1080, 60, 60, 18); break;
                }
            }

            internal bool TryCreateOptions(out RenderRequestOptions options, out string message)
            {
                options = null;
                message = string.Empty;
                int width = 0, height = 0, targetFps = 0, videoFps = 0, bitrate = 0;
                float endDelay;
                if (!int.TryParse(FpsText, out targetFps) || targetFps < 15 || targetFps > 1024)
                {
                    message = Localization.Get("ingame-fps-must-be-between-15-and-1024");
                    return false;
                }
                if (!int.TryParse(VideoFpsText, out videoFps) || videoFps < 15 || videoFps > 240)
                {
                    message = Localization.Get("video-fps-must-be-between-15-and-240");
                    return false;
                }
                if (Preset == RendererPreset.Custom)
                {
                    if (!int.TryParse(WidthText, out width) || !int.TryParse(HeightText, out height)
                        || !int.TryParse(BitrateText, out bitrate))
                    {
                        message = Localization.Get("width-height-and-bitrate-must-be-valid-numbers");
                        return false;
                    }
                    if (width < 320 || width > 3840 || height < 180 || height > 2160
                        || bitrate < 1 || bitrate > 200)
                    {
                        message = Localization.Get("custom-values-are-outside-the-supported-ranges");
                        return false;
                    }
                    if ((width & 1) != 0 || (height & 1) != 0)
                    {
                        message = Localization.Get("width-and-height-must-be-even-numbers");
                        return false;
                    }
                }
                if (!TryParseFloat(EndDelayText, out endDelay) || endDelay < 0f || endDelay > 30f)
                {
                    message = Localization.Get("end-delay-must-be-between-0-and-30-seconds");
                    return false;
                }

                options = new RenderRequestOptions {
                    Preset = Preset,
                    EndDelaySeconds = endDelay,
                    CaptureAudio = CaptureAudio,
                    BgaMode = BgaMode,
                    ShowPlanetRings = ShowPlanetRings,
                    ShowSongTitle = ShowSongTitle,
                    ShowCountdown = ShowCountdown,
                    ShowResultText = ShowResultText,
                    ShowHitJudgments = ShowHitJudgments,
                    Encoding = Encoding,
                    Encoder = Encoder,
                    VideoCodec = Codec,
                    BitDepth = BitDepth,
                    OpenOutputFolder = OpenOutputFolder
                };
                options.TargetFps = targetFps;
                options.VideoFps = videoFps;
                if (Preset == RendererPreset.Custom)
                {
                    options.Width = width;
                    options.Height = height;
                    options.BitrateMbps = bitrate;
                }
                return true;
            }

            internal void ApplyTo(RendererSettings settings, RenderRequestOptions options)
            {
                settings.Preset = Preset;
                settings.Fps = options.TargetFps.Value;
                settings.VideoFps = options.VideoFps.Value;
                if (Preset == RendererPreset.Custom)
                {
                    settings.Width = options.Width.Value;
                    settings.Height = options.Height.Value;
                    settings.BitrateMbps = options.BitrateMbps.Value;
                }
                settings.EndDelaySeconds = options.EndDelaySeconds.Value;
                settings.CaptureAudio = CaptureAudio;
                settings.BgaMode = BgaMode;
                settings.ShowPlanetRings = ShowPlanetRings;
                settings.ShowSongTitle = ShowSongTitle;
                settings.ShowCountdown = ShowCountdown;
                settings.ShowResultText = ShowResultText;
                settings.ShowHitJudgments = ShowHitJudgments;
                settings.Encoding = Encoding;
                settings.Encoder = Encoder;
                settings.Codec = Codec;
                settings.BitDepth = BitDepth;
                settings.OpenOutputFolder = OpenOutputFolder;
                settings.OnChange();
            }

            private void SetVideoValues(int width, int height, int targetFps, int videoFps, int bitrate)
            {
                WidthText = width.ToString(CultureInfo.InvariantCulture);
                HeightText = height.ToString(CultureInfo.InvariantCulture);
                FpsText = targetFps.ToString(CultureInfo.InvariantCulture);
                VideoFpsText = videoFps.ToString(CultureInfo.InvariantCulture);
                BitrateText = bitrate.ToString(CultureInfo.InvariantCulture);
            }

            private static bool TryParseFloat(string value, out float result)
            {
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                    || float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
            }
        }
    }
}
