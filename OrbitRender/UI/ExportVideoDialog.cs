using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OrbitRender.Renderer;

namespace OrbitRender.UI
{
    internal static class ExportVideoDialog
    {
        private static bool open;
        private static scnEditor editor;
        private static Draft draft;
        private static string error;
        private static int selectedTab;
        private static Text filenamePreviewText;
        private static InputField filenameInputField;
        private static Dropdown outputFormatDropdown;
        private static string filenamePreviewSignature;
        private static string filenameError;
        private static bool filenameHelpVisible;
        private static bool showHelp;
        private static GameObject filenameHelpPanel;
        private static DateTime filenamePreviewTime;
        private static string filenamePreviewId;
        private static GameObject canvasObject;
        private static RectTransform panelRect;
        private static RectTransform content;
        private static ScrollRect scrollRect;
        private static Text errorText;
        private static Text audioPreviewText;
        private static Text storageText;
        private static float nextStorageRefresh;
        private static EncoderAvailability.CombinedResult encoderAvailability;
        private static bool encoderAvailabilityComplete;
        private static UserRenderPreset selectedUserPreset;
        private static readonly System.Collections.Generic.Dictionary<string, FieldView> fieldViews = new System.Collections.Generic.Dictionary<string, FieldView>();
        private static int queueRevision;
        private static string replayManifestPath;
        private static System.Collections.Generic.List<OrbitRender.TufReplay.RecordedRun> replayRuns;
        private static string replayJobId;
        private static float nextReplayPoll;

        internal static bool IsOpen => open;
        internal static void CloseDialog() => Close();

        internal static void Open(scnEditor owner)
        {
            if (owner == null || Main.Settings == null) return;
            Close();
            editor = owner;
            draft = Draft.From(Main.Settings);
            replayManifestPath = null;
            replayRuns = null;
            selectedUserPreset = null;
            selectedTab = 0;
            showHelp = false;
            filenameHelpVisible = false;
            filenamePreviewSignature = null;
            filenameError = null;
            filenamePreviewTime = DateTime.Now;
            filenamePreviewId = Guid.NewGuid().ToString("N").Substring(0, 6);
            error = string.Empty;
            open = true;
            editor.ShowFileActionsPanel(false);
            BuildCanvas();
        }

        internal static void Refresh(RendererController renderer)
        {
            if (!open || canvasObject == null) return;
            if (replayJobId != null && Time.unscaledTime >= nextReplayPoll)
            {
                nextReplayPoll = Time.unscaledTime + 0.5f;
                try
                {
                    var status = OrbitRender.TufReplay.TufReplayBridge.Status(replayJobId);
                    var state = OrbitRender.TufReplay.TufReplayBridge.Read(status, "State");
                    if (state == "completed")
                    {
                        var path = OrbitRender.TufReplay.TufReplayBridge.Read(status, "ManifestPath");
                        var bundle = TUFReplayRenderer.Contracts.RecordingBundle.Load(path);
                        bundle.ValidateLevelHash();
                        replayManifestPath = path;
                        replayJobId = null;
                        error = string.Empty;
                        RebuildContent();
                    }
                    else if (state == "failed" || state == "cancelled")
                    {
                        error = OrbitRender.TufReplay.TufReplayBridge.Read(status, "ErrorMessage") ?? Localization.Get("replay-export-failed");
                        replayJobId = null;
                        RebuildContent();
                    }
                }
                catch (Exception ex) { replayJobId = null; error = ex.Message; RebuildContent(); }
            }
            RefreshLayout();
            if (Time.unscaledTime >= nextStorageRefresh) { nextStorageRefresh = Time.unscaledTime + 1f; RefreshStorage(); }
            if (selectedTab == 3 && queueRevision != RendererController.Queue.Revision) { RebuildContent(); return; }
            if (encoderAvailability != null && encoderAvailability.Complete != encoderAvailabilityComplete)
            { encoderAvailabilityComplete = encoderAvailability.Complete; RebuildContent(); return; }
            if (!string.IsNullOrEmpty(AudioPreview.ErrorMessage)) error = AudioPreview.ErrorMessage;
            if (errorText != null)
            {
                errorText.text = error ?? string.Empty;
                errorText.gameObject.SetActive(!string.IsNullOrEmpty(error));
            }
            if (audioPreviewText != null)
                audioPreviewText.text = SettingsUi.AudioPreviewCaption;
            RefreshFilenamePreview();
        }

        private static void RefreshStorage()
        {
            if (storageText == null || draft == null) return;
            if (!int.TryParse(draft.WidthText, out var width) || !int.TryParse(draft.HeightText, out var height)
                || !int.TryParse(draft.VideoFpsText, out var fps) || !int.TryParse(draft.BitrateText, out var bitrate)
                || width <= 0 || height <= 0 || fps <= 0 || bitrate <= 0) { storageText.text = ""; return; }
            var free = RenderResources.FreeBytes(Main.Settings.ResolveOutputDirectory());
            var available = free.HasValue ? RenderResources.Size(free.Value) : Localization.Get("storage-unknown");
            var floors = editor != null ? editor.floors : null;
            var conductor = ADOBase.conductor;
            var pitch = conductor != null && conductor.song != null ? Math.Abs(conductor.song.pitch) : 1d;
            if (pitch < .001 || double.IsNaN(pitch)) pitch = 1;
            double.TryParse(draft.EndDelayText, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay);
            delay = double.IsNaN(delay) || double.IsInfinity(delay) ? 2 : Math.Max(0, delay);
            var chart = floors != null ? floors.Where(f => f != null).ToArray() : new scrFloor[0];
            if (chart.Length < 2) { storageText.text = Localization.Format("storage-available", available); return; }
            var seconds = Math.Max(0, chart[chart.Length - 1].entryTime) / pitch + delay + 5;
            if (conductor != null && conductor.song != null && conductor.song.clip != null)
                seconds = Math.Max(seconds, conductor.song.clip.length / pitch + delay + 5);
            var selected = GetSelectedTileRange();
            var selection = selected.Length >= 2 ? chart.Where(f => f.seqID >= selected[0] && f.seqID <= selected[selected.Length - 1]).ToArray() : new scrFloor[0];
            storageText.text = StorageLine("storage-estimate", seconds, seconds, width, height, fps, bitrate, available, draft.Codec);
            if (selection.Length >= 2)
            {
                var duration = Math.Max(1d / fps, (selection[selection.Length - 1].entryTime - selection[0].entryTime) / pitch);
                storageText.text += "\n" + StorageLine("storage-selection", duration,
                    Math.Max(0, selection[selection.Length - 1].entryTime) / pitch + 5, width, height, fps, bitrate, available, draft.Codec);
            }
            storageText.text += "\n" + Localization.Get(draft.Codec == VideoCodec.ProRes ? "storage-prores-note"
                : draft.Codec == VideoCodec.FFV1 ? "storage-ffv1-note" : "storage-estimate-note");
        }

        private static string StorageLine(string key, double seconds, double audioSeconds,
            int width, int height, int fps, int bitrate, string available, VideoCodec codec)
        {
            var video = RenderResources.VideoBytes(seconds, width, height, fps, bitrate, codec);
            var required = RenderResources.RequiredBytes(video, audioSeconds, draft.CaptureAudio);
            return Localization.Format(key, RenderResources.Size(video), RenderResources.Size(required), available);
        }

        private static void RefreshFilenamePreview()
        {
            if (filenamePreviewText == null || draft == null) return;
            var level = editor != null ? editor.customLevel : ADOBase.customLevel;
            var artist = level != null && level.levelData != null ? level.levelData.artist : string.Empty;
            var signature = string.Join("\n", draft.FileNameFormat, draft.WidthText, draft.HeightText,
                draft.FpsText, draft.VideoFpsText, draft.BitrateText, draft.Codec.ToString(), draft.BitDepth.ToString(),
                draft.Container.ToString(), draft.BgaMode.ToString(),
                ExportFileName.LevelName, artist);
            if (signature == filenamePreviewSignature) return;
            filenamePreviewSignature = signature;
            try
            {
                var name = OutputFormat.FileName(draft.FileNameFormat, draft.FilenameVariables());
                if (error == filenameError) error = string.Empty;
                filenameError = null;
                filenamePreviewText.color = UguiFactory.Muted;
                filenamePreviewText.text = Localization.Format("export-filename-preview",
                    name + OutputFormat.Extension(draft.Container, draft.Codec));
                var compatibility = ExportValidation.ContainerError(draft.Codec, draft.Container);
                if (compatibility != null)
                {
                    filenamePreviewText.text = Localization.Get(compatibility);
                    filenamePreviewText.color = UguiFactory.Error;
                }
                if (outputFormatDropdown != null) outputFormatDropdown.GetComponent<Image>().color = compatibility != null
                    ? new Color(.30f, .10f, .13f) : UguiFactory.Control;
                if (filenameInputField != null) filenameInputField.GetComponent<Image>().color = new Color(.10f, .09f, .12f);
            }
            catch (FormatException ex)
            {
                filenameError = Localization.Format("filename-template-error", ex.Message);
                filenamePreviewText.color = UguiFactory.Error;
                filenamePreviewText.text = filenameError;
                if (filenameInputField != null) filenameInputField.GetComponent<Image>().color = new Color(.30f, .10f, .13f);
            }
        }

        private static void AddTemplateInsertDropdown(Transform parent)
        {
            var tokens = FileNameTemplateOptions.Tokens;
            var labels = new[] { Localization.Get("filename-insert-variable") }.Concat(FileNameTemplateOptions.Labels).ToArray();
            Dropdown dropdown = null;
            dropdown = UguiFactory.Dropdown(parent, labels, 0, value => {
                if (value == 0 || filenameInputField == null) return;
                var token = tokens[value - 1];
                var text = filenameInputField.text;
                var start = Mathf.Clamp(Math.Min(filenameInputField.selectionAnchorPosition,
                    filenameInputField.selectionFocusPosition), 0, text.Length);
                var end = Mathf.Clamp(Math.Max(filenameInputField.selectionAnchorPosition,
                    filenameInputField.selectionFocusPosition), start, text.Length);
                filenameInputField.text = text.Substring(0, start) + token + text.Substring(end);
                filenameInputField.ActivateInputField();
                filenameInputField.selectionAnchorPosition = filenameInputField.selectionFocusPosition = start + token.Length;
                dropdown.value = 0;
                RefreshFilenamePreview();
            });
            UguiFactory.Preferred(dropdown, 136f, 30f);
        }

        private static void BuildCanvas()
        {
            fieldViews.Clear();
            content = null; scrollRect = null; storageText = null; audioPreviewText = null;
            filenamePreviewText = null; filenameInputField = null; filenamePreviewSignature = null;
            canvasObject = UguiFactory.Canvas("OrbitRender.ExportVideoDialog", 32765);
            var dimmer = UguiFactory.Image(canvasObject.transform, "Dimmer", new Color(0f, 0f, 0f, .72f), true);
            UguiFactory.Stretch(dimmer.GetComponent<RectTransform>());
            var panel = UguiFactory.Image(canvasObject.transform, "Panel", UguiFactory.Surface, true);
            UguiFactory.Round(panel.GetComponent<Image>(), true);
            panelRect = panel.GetComponent<RectTransform>();
            UguiFactory.Anchor(panelRect, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-UiLayout.ExportWidth / 2f, -UiLayout.ExportHeight / 2f),
                new Vector2(UiLayout.ExportWidth / 2f, UiLayout.ExportHeight / 2f));
            RefreshLayout();
            if (Time.unscaledTime >= nextStorageRefresh) { nextStorageRefresh = Time.unscaledTime + 1f; RefreshStorage(); }
            var title = UguiFactory.Text(panel.transform, Localization.Get("export-video"), UiLayout.TitleFontSize);
            title.fontStyle = FontStyle.Bold;
            UguiFactory.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(22f, -44f), new Vector2(-22f, -20f));

            var tabs = UguiFactory.Toolbar(panel.transform, new[] { Localization.Get("export-tab-basic"),
                Localization.Get("export-tab-game"), Localization.Get("export-tab-advanced"), Localization.Get("export-tab-queue") }, selectedTab,
                value => { selectedTab = value; AudioPreview.Stop(); RebuildContent(false); });
            UguiFactory.Anchor(tabs.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(26f, -92f), new Vector2(-26f, -60f));

            var scrollView = UguiFactory.New(panel.transform, "Scroll View", typeof(RectTransform), typeof(ScrollRect));
            UguiFactory.Anchor(scrollView.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(22f, 244f), new Vector2(-22f, -104f));
            var viewport = UguiFactory.Image(scrollView.transform, "Viewport", new Color(1f, 1f, 1f, .001f), true);
            viewport.AddComponent<RectMask2D>();
            UguiFactory.Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<RectTransform>().offsetMax = new Vector2(-18f, 0f);
            var body = UguiFactory.New(viewport.transform, "Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content = body.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var layout = body.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f; layout.padding = new RectOffset(4, 14, 4, 10);
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>(); scrollRect.content = content;
            scrollRect.horizontal = false; scrollRect.vertical = true; scrollRect.scrollSensitivity = 38f;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var scrollbarObject = UguiFactory.Image(scrollView.transform, "Scrollbar", UguiFactory.Backdrop, true);
            UguiFactory.Round(scrollbarObject.GetComponent<Image>());
            UguiFactory.Anchor(scrollbarObject.GetComponent<RectTransform>(), new Vector2(1f, 0f), Vector2.one,
                new Vector2(-10f, 2f), new Vector2(0f, -2f));
            var scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handle = UguiFactory.Image(scrollbarObject.transform, "Handle", UguiFactory.Accent, true).GetComponent<Image>();
            UguiFactory.Round(handle);
            UguiFactory.SetOffsets(handle.rectTransform, 2f, 2f, 2f, 2f);
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var divider = UguiFactory.Image(panel.transform, "Output Divider", UguiFactory.Control);
            UguiFactory.Anchor(divider.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f),
                new Vector2(26f, 236f), new Vector2(-26f, 237f));
            var output = UguiFactory.New(panel.transform, "Output Settings", typeof(VerticalLayoutGroup));
            UguiFactory.Anchor(output.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f),
                new Vector2(26f, 88f), new Vector2(-26f, 228f));
            var outputLayout = output.GetComponent<VerticalLayoutGroup>();
            outputLayout.spacing = 4f;
            outputLayout.childControlHeight = true; outputLayout.childControlWidth = true;
            outputLayout.childForceExpandHeight = false; outputLayout.childForceExpandWidth = true;
            filenamePreviewText = UguiFactory.Text(output.transform, string.Empty, 11,
                TextAnchor.MiddleLeft, UguiFactory.Muted);
            UguiFactory.Preferred(filenamePreviewText, 0f, 38f);
            AddOutputFields(output.transform);
            UguiFactory.Toggle(output.transform, Localization.Get("save-these-values-as-the-default-renderer-settings"),
                draft.SaveAsDefault, v => draft.SaveAsDefault = v);

            filenameHelpPanel = UguiFactory.Image(panel.transform, "Filename Help", UguiFactory.Backdrop, true);
            UguiFactory.Round(filenameHelpPanel.GetComponent<Image>(), true);
            var helpRect = filenameHelpPanel.GetComponent<RectTransform>();
            UguiFactory.Anchor(helpRect, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(26f, 244f), new Vector2(-26f, 244f));
            helpRect.pivot = new Vector2(.5f, 0f);
            filenameHelpPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var helpLayout = filenameHelpPanel.AddComponent<VerticalLayoutGroup>();
            helpLayout.padding = new RectOffset(12, 12, 12, 12);
            helpLayout.spacing = 4f;
            helpLayout.childControlHeight = true; helpLayout.childControlWidth = true;
            helpLayout.childForceExpandHeight = false; helpLayout.childForceExpandWidth = true;
            var helpActions = UguiFactory.Row(filenameHelpPanel.transform, 30f);
            helpActions.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            AddTemplateInsertDropdown(helpActions.transform);
            UguiFactory.New(helpActions.transform, "Spacer", typeof(LayoutElement)).GetComponent<LayoutElement>().flexibleWidth = 1f;
            var closeHelp = UguiFactory.TextButton(helpActions.transform, Localization.Get("filename-help-close"), ToggleFilenameHelp);
            UguiFactory.Preferred(closeHelp, 60f, 30f);
            var variableHelp = UguiFactory.Text(filenameHelpPanel.transform, Localization.Get("filename-format-help"),
                11, TextAnchor.UpperLeft, UguiFactory.Muted);
            UguiFactory.Preferred(variableHelp, 0f);
            var templateHelp = UguiFactory.Text(filenameHelpPanel.transform, Localization.Get("filename-template-help"),
                11, TextAnchor.UpperLeft, UguiFactory.Muted);
            UguiFactory.Preferred(templateHelp, 0f);
            filenameHelpPanel.SetActive(false);

            errorText = UguiFactory.Text(panel.transform, string.Empty, UiLayout.LabelFontSize, TextAnchor.MiddleLeft, UguiFactory.Error);
            UguiFactory.Anchor(errorText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(22f, 56f), new Vector2(-22f, 80f));
            var footer = UguiFactory.New(panel.transform, "Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            UguiFactory.Anchor(footer.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(22f, 20f), new Vector2(-22f, 54f));
            var footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f; footerLayout.childForceExpandHeight = false; footerLayout.childForceExpandWidth = false;
            footerLayout.childControlHeight = true; footerLayout.childControlWidth = true;
            var cancel = UguiFactory.Button(footer.transform, Localization.Get("cancel"), Close); UguiFactory.Preferred(cancel, 120f, 34f);
            var enqueue = UguiFactory.Button(footer.transform, Localization.Get("queue-add"), () => AddQueueItem(false));
            UguiFactory.Preferred(enqueue, 140f, 34f);
            UguiFactory.New(footer.transform, "Spacer", typeof(LayoutElement)).GetComponent<LayoutElement>().flexibleWidth = 1f;
            if (GetSelectedTileRange().Length >= 2)
            {
                var selection = UguiFactory.Button(footer.transform, Localization.Get("export-video-selection"),
                    () => Confirm(RendererController.Instance, true)); UguiFactory.Preferred(selection, 190f, 34f);
            }
            var export = UguiFactory.Button(footer.transform, Localization.Get("export-video"),
                () => Confirm(RendererController.Instance, false), true); UguiFactory.Preferred(export, 150f, 34f);
            RebuildContent(false);
        }

        private static void RefreshLayout()
        {
            if (panelRect == null) return;
            var layout = UiLayout.ExportDialog(Screen.width, Screen.height, Screen.dpi);
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            var size = new Vector2(layout.Width, layout.Height);
            if (scaler.scaleFactor == layout.Scale && panelRect.sizeDelta == size) return;
            var position = scrollRect != null ? scrollRect.verticalNormalizedPosition : 1f;
            scaler.scaleFactor = layout.Scale;
            canvasObject.GetComponent<Canvas>().scaleFactor = layout.Scale;
            panelRect.sizeDelta = size;
            if (content != null) RefreshFieldErrors();
            UpdateContentLayout(position);
        }

        private static void RebuildContent(bool preserveScroll = true)
        {
            if (content == null || draft == null) return;
            var position = preserveScroll && scrollRect != null ? scrollRect.verticalNormalizedPosition : 1f;
            for (var i = content.childCount - 1; i >= 0; i--)
            {
                content.GetChild(i).gameObject.SetActive(false);
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            }
            audioPreviewText = null;
            storageText = null;
            nextStorageRefresh = 0;
            encoderAvailability = null;
            fieldViews.Clear();
            queueRevision = RendererController.Queue.Revision;
            if (selectedTab != 1) AddHelpToggle();
            if (selectedTab == 3) BuildQueue();
            else if (selectedTab == 0)
            {
                AddUserPresets();
                storageText = UguiFactory.Text(content, "", UiLayout.LabelFontSize, TextAnchor.UpperLeft, UguiFactory.Muted);
                UguiFactory.Preferred(storageText, 0f, 96f);
                RefreshStorage();
                if (draft.Preset == RendererPreset.Custom)
                    AddInputRow(new[] { new Field("width", draft.WidthText, v => draft.WidthText = v),
                        new Field("height", draft.HeightText, v => draft.HeightText = v),
                        new Field("bitrate", draft.BitrateText, v => draft.BitrateText = v) });
                else AddLabel(Localization.Format("preset-output", draft.WidthText, draft.HeightText, draft.FpsText,
                    draft.VideoFpsText, draft.BitrateText), UiLayout.LabelFontSize, 22f, UguiFactory.Muted);
                AddInputRow(new[] { new Field("ingame-fps", draft.FpsText, v => draft.FpsText = v),
                    new Field("video-fps", draft.VideoFpsText, v => draft.VideoFpsText = v) });

                if (showHelp) AddHelp("help-fps");
                AddHeading(Localization.Get("export-audio"));
                AddToggle(Localization.Get("capture-audio"), draft.CaptureAudio,
                    v => { draft.CaptureAudio = v; if (!v) AudioPreview.Stop(); RebuildContent(); });
                if (draft.CaptureAudio)
                {
                    var choices = SettingsUi.AudioChoices(draft.Container, draft.Codec);
                    draft.AudioCodec = AudioCodecCatalog.CompatibleSelection(draft.AudioCodec,
                        OutputFormat.Extension(draft.Container, draft.Codec));
                    AddRadioGroup(Localization.Get("audio-codec"), choices.Select(SettingsUi.AudioCodecLabel).ToArray(),
                        Math.Max(0, Array.IndexOf(choices, draft.AudioCodec)), v => draft.AudioCodec = choices[v]);
                    AddAudioGain();
                    if (showHelp) AddHelp("help-audio");
                }
            }
            else if (selectedTab == 2)
            {
                var list = UguiFactory.Button(content, Localization.Get("replay-list"), () => {
                    try { replayRuns = OrbitRender.TufReplay.TufReplayBridge.ListRuns(editor?.customLevel?.levelPath); error = string.Empty; }
                    catch (Exception ex) { replayRuns = null; error = ex.Message; }
                    RebuildContent();
                });
                UguiFactory.Preferred(list, 0f, 34f);
                if (replayJobId != null)
                {
                    var preparing = UguiFactory.Text(content, Localization.Get("replay-preparing"), UiLayout.LabelFontSize, TextAnchor.UpperLeft, UguiFactory.Muted);
                    UguiFactory.Preferred(preparing, 0f, 30f);
                }
                else if (replayRuns != null)
                {
                    if (replayRuns.Count == 0)
                    {
                        var empty = UguiFactory.Text(content, Localization.Get("replay-empty"), UiLayout.LabelFontSize, TextAnchor.UpperLeft, UguiFactory.Muted);
                        UguiFactory.Preferred(empty, 0f, 30f);
                    }
                    foreach (var run in replayRuns)
                    {
                        var chosen = run;
                        DateTime timestamp;
                        var time = DateTime.TryParse(chosen.StartedUtc, out timestamp) ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : chosen.StartedUtc;
                        var button = UguiFactory.Button(content, time + " · " + chosen.Result + " · #" + chosen.StartTile, () => {
                            try {
                                var status = OrbitRender.TufReplay.TufReplayBridge.Start(chosen.Id, editor.customLevel.levelPath);
                                replayJobId = OrbitRender.TufReplay.TufReplayBridge.Read(status, "JobId");
                                if (string.IsNullOrEmpty(replayJobId)) throw new InvalidOperationException(Localization.Get("replay-export-failed"));
                                replayManifestPath = null; nextReplayPoll = 0; error = string.Empty;
                            } catch (Exception ex) { error = ex.Message; }
                            RebuildContent();
                        });
                        UguiFactory.Preferred(button, 0f, 34f);
                    }
                }
                var replay = UguiFactory.Button(content, Localization.Get("replay-select"), () => {
                    if (replayJobId != null) { error = Localization.Get("replay-preparing"); return; }
                    var file = FileDialogService.PickFile(Main.Settings.ResolveOutputDirectory(), "replay-select");
                    if (string.IsNullOrEmpty(file)) return;
                    try {
                        var bundle = TUFReplayRenderer.Contracts.RecordingBundle.Load(file);
                        bundle.ValidateLevelHash();
                        replayManifestPath = file;
                        error = string.Empty;
                    } catch (Exception ex) { error = ex.Message; }
                    RebuildContent();
                });
                UguiFactory.Preferred(replay, 0f, 34f);
                if (!string.IsNullOrEmpty(replayManifestPath))
                {
                    var name = UguiFactory.Text(content, replayManifestPath, UiLayout.LabelFontSize, TextAnchor.UpperLeft, UguiFactory.Muted);
                    UguiFactory.Preferred(name, 0f, 40f);
                    var clear = UguiFactory.Button(content, Localization.Get("replay-clear"), () => { replayManifestPath = null; RebuildContent(); });
                    UguiFactory.Preferred(clear, 0f, 34f);
                }
                var recovery = UguiFactory.Button(content, Localization.Get("recovery-retry"), () => {
                    var file = FileDialogService.PickFile(Main.Settings.ResolveOutputDirectory(), "recovery-select");
                    if (string.IsNullOrEmpty(file)) return;
                    Close(); RendererController.Instance.RetryAudioMux(file);
                });
                UguiFactory.Preferred(recovery, 0f, 34f);
                AddHeading(Localization.Get("encoding"));
                AddEncoderDropdown();
                if (showHelp)
                {
                    AddHelp(draft.Codec == VideoCodec.ProRes ? "help-prores"
                        : draft.Codec == VideoCodec.FFV1 ? "help-ffv1" : "help-bit-depth");
                    AddHelp("help-encoder");
                }
                if (draft.Codec != VideoCodec.ProRes && draft.Codec != VideoCodec.FFV1)
                {
                    AddRadioGroup(Localization.Get("encoding-speed"), new[] { Localization.Get("maximum"),
                        Localization.Get("balanced"), Localization.Get("quality") }, (int)draft.Encoding,
                        v => draft.Encoding = (EncoderSpeed)v);
                    AddRadioGroup(Localization.Get("video-bit-depth"), new[] { "8-bit", "10-bit" },
                        (int)draft.BitDepth, v => { draft.BitDepth = (VideoBitDepth)v; RebuildContent(); });
                }
                AddHeading(Localization.Get("render-options"));
                AddRadioGroup(Localization.Get("frame-buffer-policy"), new[] { Localization.Get("buffer-low"),
                    Localization.Get("buffer-balanced"), Localization.Get("buffer-throughput") }, (int)draft.BufferPolicy,
                    v => { draft.BufferPolicy = (FrameBufferPolicy)v; RebuildContent(); });
                int.TryParse(draft.WidthText, out var bufferWidth); int.TryParse(draft.HeightText, out var bufferHeight);
                if (bufferWidth > 0 && bufferWidth <= 3840 && bufferHeight > 0 && bufferHeight <= 2160)
                {
                    var bytes = bufferWidth * bufferHeight * 4;
                    AddLabel(Localization.Format("frame-buffer-estimate", RenderResources.BufferCount(bytes, draft.BufferPolicy),
                        RenderResources.Size((long)bytes * RenderResources.BufferCount(bytes, draft.BufferPolicy))),
                        UiLayout.LabelFontSize, 44f, UguiFactory.Muted);
                }
                AddToggle(Localization.Get("use-rust-renderer"), draft.UseRustRenderer, v => draft.UseRustRenderer = v);
                AddToggle(Localization.Get("show-render-preview"), draft.ShowRenderPreview, v => draft.ShowRenderPreview = v);
                AddToggle(Localization.Get("open-output-folder-after-render"), draft.OpenOutputFolder, v => draft.OpenOutputFolder = v);
            }
            else
            {
                AddInputRow(new[] { new Field("end-delay-seconds", draft.EndDelayText, v => draft.EndDelayText = v) });
                AddToggle(Localization.Get("bga-mode-hide-tiles-planets-hit-sounds"), draft.BgaMode, v => draft.BgaMode = v);
                AddHeading(Localization.Get("visible-components"));
                AddToggle(Localization.Get("show-planet-rings"), draft.ShowPlanetRings, v => draft.ShowPlanetRings = v);
                AddToggle(Localization.Get("show-song-title"), draft.ShowSongTitle, v => draft.ShowSongTitle = v);
                AddToggle(Localization.Get("show-countdown"), draft.ShowCountdown, v => draft.ShowCountdown = v);
                AddToggle(Localization.Get("show-result-text-hit-judgments-stay-hidden"), draft.ShowResultText, v => draft.ShowResultText = v);
                AddToggle(Localization.Get("show-hit-judgments"), draft.ShowHitJudgments, v => draft.ShowHitJudgments = v);
                AddToggle(Localization.Get("show-mod-overlay"), draft.ShowModOverlay, v => draft.ShowModOverlay = v);
            }
            RefreshFieldErrors();
            UpdateContentLayout(position);
            Refresh(RendererController.Instance);
        }

        private static void UpdateContentLayout(float position)
        {
            if (content == null || scrollRect == null || scrollRect.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var preferredHeight = Mathf.Max(LayoutUtility.GetPreferredHeight(content),
                scrollRect.viewport.rect.height);
            content.sizeDelta = new Vector2(0f, preferredHeight);
            content.anchoredPosition = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = position;
        }

        private static void AddOutputFields(Transform parent)
        {
            const float formatWidth = 136f;
            var labels = UguiFactory.Row(parent, 20f);
            labels.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var filenameLabel = UguiFactory.Text(labels.transform, Localization.Get("filename-format"));
            UguiFactory.Preferred(filenameLabel, 0f);
            filenameLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var help = UguiFactory.TextButton(labels.transform, Localization.Get("export-filename-help"), ToggleFilenameHelp);
            UguiFactory.Preferred(help, 90f, 20f);
            var formatLabel = UguiFactory.Text(labels.transform, Localization.Get("output-format"));
            UguiFactory.Preferred(formatLabel, formatWidth);
            formatLabel.GetComponent<LayoutElement>().minWidth = formatWidth;

            var fields = UguiFactory.Row(parent, 34f);
            fields.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var filenameInput = UguiFactory.Input(fields.transform, draft.FileNameFormat, v => { draft.FileNameFormat = v; RefreshFilenamePreview(); });
            filenameInputField = filenameInput;
            UguiFactory.Preferred(filenameInput, 0f, 34f);
            filenameInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var formatDropdown = UguiFactory.Dropdown(fields.transform,
                new[] { Localization.Get("auto"), ".mp4", ".ts", ".mkv", ".mov" },
                (int)draft.Container, v => { draft.Container = (VideoContainer)v; RebuildContent(); });
            outputFormatDropdown = formatDropdown;
            UguiFactory.Preferred(formatDropdown, formatWidth, 34f);
            formatDropdown.GetComponent<LayoutElement>().minWidth = formatWidth;
        }

        private static void ToggleFilenameHelp()
        {
            filenameHelpVisible = !filenameHelpVisible;
            if (filenameHelpPanel != null) filenameHelpPanel.SetActive(filenameHelpVisible);
        }

        private static void AddEncoderDropdown()
        {
            encoderAvailability = SettingsUi.AvailableEncoders(draft.BitDepth, draft.Container);
            encoderAvailabilityComplete = encoderAvailability.Complete;
            AddLabel(Localization.Get("video-encoder"), UiLayout.LabelFontSize, 20f, UguiFactory.Foreground);
            var choices = encoderAvailability.Complete ? encoderAvailability.Choices : new EncoderAvailability.Choice[0];
            var labels = choices.Length > 0 ? choices.Select(SettingsUi.EncoderLabel).ToArray()
                : new[] { Localization.Get(encoderAvailability.Complete ? "no-compatible-encoders" : "checking-available-encoders") };
            var selected = choices.Length > 0 ? EncoderAvailability.SelectionIndex(choices, draft.Codec, draft.Encoder) : 0;
            var separators = Enumerable.Range(1, Math.Max(0, choices.Length - 1))
                .Where(i => choices[i].Codec != choices[i - 1].Codec).ToArray();
            if (choices.Length > 0) { draft.Encoder = choices[selected].Encoder; draft.Codec = choices[selected].Codec; }
            var dropdown = UguiFactory.Dropdown(content, labels, selected, v => {
                if (v >= choices.Length) return;
                draft.Encoder = choices[v].Encoder; draft.Codec = choices[v].Codec;
                RebuildContent();
            }, separators);
            dropdown.interactable = choices.Length > 0;
            UguiFactory.Preferred(dropdown, 0f, 34f);
            var refresh = UguiFactory.Button(content, Localization.Get("refresh-encoders"),
                () => { EncoderAvailability.Refresh(); RebuildContent(); });
            UguiFactory.Preferred(refresh, 0f, 30f);
            if (choices.Length > 0 && showHelp)
                AddNote(Localization.Format("help-compatibility", SettingsUi.EncoderLabel(choices[selected]),
                    VideoCodecCatalog.PixelFormat(draft.Codec, draft.BitDepth), OutputFormat.Extension(draft.Container, draft.Codec)));
        }

        private static void AddAudioGain()
        {
            var row = UguiFactory.Row(content);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var label = UguiFactory.Text(row.transform, Localization.Get("audio-volume-db")); UguiFactory.Preferred(label, 175f);
            var valueLabel = UguiFactory.Text(row.transform, string.Empty, UiLayout.LabelFontSize, TextAnchor.MiddleRight); UguiFactory.Preferred(valueLabel, 125f);
            Action<float> update = value => { draft.AudioGainDb = RendererSettings.ClampAudioGainDb(value);
                draft.AudioGainDbText = draft.AudioGainDb.ToString("0.#", CultureInfo.InvariantCulture);
                var percent = draft.AudioGainDb <= RendererSettings.MinAudioGainDb ? 0f : Mathf.Pow(10f, draft.AudioGainDb / 20f) * 100f;
                valueLabel.text = percent.ToString("0.#", CultureInfo.InvariantCulture) + "%  " + draft.AudioGainDbText + " dB";
                AudioPreview.SetGain(draft.AudioGainDb); };
            var slider = UguiFactory.Slider(row.transform, RendererSettings.MinAudioGainDb, RendererSettings.MaxAudioGainDb,
                draft.AudioGainDb, update); UguiFactory.Preferred(slider, 250f);
            slider.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var preview = UguiFactory.TextButton(row.transform, SettingsUi.AudioPreviewCaption, () => {
                var wasPlaying = AudioPreview.IsActive;
                var playing = AudioPreview.Toggle(draft.AudioGainDb);
                error = !wasPlaying && !playing ? AudioPreview.ErrorMessage ?? Localization.Get("audio-preview-unavailable") : string.Empty;
                Refresh(RendererController.Instance);
            });
            UguiFactory.Preferred(preview, 60f);
            audioPreviewText = preview.GetComponent<Text>();
            update(draft.AudioGainDb);
        }

        private static void AddRadioGroup(string label, string[] options, int value, Action<int> changed)
        { AddLabel(label, UiLayout.LabelFontSize, 20f, UguiFactory.Foreground);
            UguiFactory.RadioGroup(content, options, Mathf.Max(0, value), changed); }
        private static void AddInputRow(Field[] fields)
        {
            var row = UguiFactory.Row(content, 64f);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childForceExpandWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = false;
            var rowHeight = row.GetComponent<LayoutElement>();
            foreach (var field in fields)
            {
                var column = UguiFactory.New(row.transform, field.Key, typeof(VerticalLayoutGroup), typeof(LayoutElement));
                column.GetComponent<LayoutElement>().flexibleWidth = 1f;
                var layout = column.GetComponent<VerticalLayoutGroup>();
                layout.spacing = 3f; layout.childControlWidth = true; layout.childControlHeight = true;
                layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
                var label = UguiFactory.Text(column.transform, field.Label, UiLayout.LabelFontSize,
                    TextAnchor.MiddleLeft, UguiFactory.Muted);
                UguiFactory.Preferred(label, 0f, 20f);
                var input = UguiFactory.Input(column.transform, field.Value, value => {
                    field.Changed(value); error = string.Empty; RefreshFieldErrors();
                    UpdateContentLayout(scrollRect.verticalNormalizedPosition);
                });
                UguiFactory.Preferred(input, 0f, 34f);
                var message = UguiFactory.Text(column.transform, "", 11, TextAnchor.UpperLeft, UguiFactory.Error);
                message.supportRichText = false;
                UguiFactory.Preferred(message, 0f, 36f);
                fieldViews[field.Key] = new FieldView { Input = input, Error = message, Row = rowHeight };
            }
        }
        private sealed class FieldView
        { internal InputField Input; internal Text Error; internal LayoutElement Row; }

        private static void RefreshFieldErrors()
        {
            if (draft == null) return;
            var errors = draft.ValidationErrors();
            foreach (var view in fieldViews.Values) if (view.Row != null) view.Row.preferredHeight = 64f;
            foreach (var field in fieldViews)
            {
                if (field.Value.Input == null) continue;
                var failure = errors.FirstOrDefault(e => e.Field == field.Key);
                field.Value.Error.text = failure != null ? Localization.Get(failure.Message) : "";
                field.Value.Error.gameObject.SetActive(failure != null);
                field.Value.Input.GetComponent<Image>().color = failure != null
                    ? new Color(.30f, .10f, .13f) : new Color(.10f, .09f, .12f);
                if (failure != null) field.Value.Row.preferredHeight = 104f;
            }
        }

        private static bool TryValidatedOptions(out RenderRequestOptions options, out string message)
        {
            var failures = draft.ValidationErrors();
            if (failures.Count > 0)
            {
                options = null; var first = failures[0]; message = Localization.Get(first.Message);
                if (first.Tab >= 0 && selectedTab != first.Tab)
                {
                    selectedTab = first.Tab;
                    if (canvasObject != null) UnityEngine.Object.Destroy(canvasObject);
                    BuildCanvas();
                }
                RefreshFieldErrors();
                if (fieldViews.TryGetValue(first.Field, out var view))
                {
                    Canvas.ForceUpdateCanvases();
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content, view.Input.transform);
                    var scrollable = Mathf.Max(1, content.rect.height - scrollRect.viewport.rect.height);
                    scrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01((-bounds.center.y - 48) / scrollable);
                    view.Input.Select(); view.Input.ActivateInputField();
                }
                else if (first.Field == "output-format" && outputFormatDropdown != null) outputFormatDropdown.Select();
                return false;
            }
            RefreshFilenamePreview();
            if (!string.IsNullOrEmpty(filenameError))
            {
                options = null; message = filenameError;
                filenameInputField.Select(); filenameInputField.ActivateInputField(); return false;
            }
            return draft.TryCreateOptions(out options, out message);
        }

        private static void AddHelp(string key) => AddNote(Localization.Get(key));

        private static void AddHelpToggle()
        {
            var row = UguiFactory.Row(content, 22f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var spacer = UguiFactory.New(row.transform, "Spacer", typeof(LayoutElement));
            spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var toggle = UguiFactory.TextButton(row.transform, Localization.Get(showHelp ? "export-help-hide" : "export-help-show"),
                () => { showHelp = !showHelp; RebuildContent(false); });
            UguiFactory.Preferred(toggle, 84f, 22f);
        }

        // Help copy wraps to a different number of lines per language, so let
        // the layout measure it instead of reserving a fixed height. Fixed
        // heights left large gaps around short help text.
        private static void AddNote(string value)
        {
            var note = UguiFactory.Text(content, value, 11, TextAnchor.UpperLeft, UguiFactory.Muted);
            UguiFactory.Preferred(note, 0f);
        }

        private static void AddQueueItem(bool selectionOnly)
        {
            if (replayJobId != null) { error = Localization.Get("replay-preparing"); Refresh(RendererController.Instance); return; }
            if (!string.IsNullOrEmpty(replayManifestPath)) { error = Localization.Get("replay-no-queue"); Refresh(RendererController.Instance); return; }
            if (editor == null || editor.isLoading || editor.customLevel == null || editor.customLevel.levelData == null)
            { error = Localization.Get("open-a-custom-level-then-render"); Refresh(RendererController.Instance); return; }
            if (!TryValidatedOptions(out var options, out var message)) { error = message; Refresh(RendererController.Instance); return; }
            if (selectionOnly)
            {
                var selected = GetSelectedTileRange();
                if (selected.Length < 2) { error = Localization.Get("select-at-least-two-tiles-to-export-a-selection"); return; }
                options.SelectionStartTile = selected[0]; options.SelectionEndTile = selected[selected.Length - 1];
            }
            options.OutputDirectory = Main.Settings.ResolveOutputDirectory();
            var preset = RendererSettings.GetPresetProfile(draft.Preset);
            options.Width = draft.Preset == RendererPreset.Custom ? int.Parse(draft.WidthText) : preset.Width;
            options.Height = draft.Preset == RendererPreset.Custom ? int.Parse(draft.HeightText) : preset.Height;
            options.BitrateMbps = draft.Preset == RendererPreset.Custom ? int.Parse(draft.BitrateText) : preset.BitrateMbps;
            if (draft.SaveAsDefault) { draft.ApplyTo(Main.Settings, options); Main.Settings.Save(Main.Entry); }
            var label = string.Format("{0} · {1}x{2} / {3} FPS · {4} · {5}{6}",
                ExportFileName.LevelName, options.Width, options.Height, options.VideoFps, draft.Codec,
                Localization.Get(draft.BgaMode ? "queue-bga" : "queue-game"),
                selectionOnly ? " · " + options.SelectionStartTile + "–" + options.SelectionEndTile : "");
            RendererController.Queue.Add(editor.customLevel.levelData, label, options);
            selectedTab = 3;
            UnityEngine.Object.Destroy(canvasObject); BuildCanvas();
        }

        private static void BuildQueue()
        {
            var queue = RendererController.Queue;
            AddHeading(Localization.Format("queue-heading", queue.PendingCount));
            if (showHelp) AddHelp("queue-help");
            var add = UguiFactory.Row(content, 34f);
            UguiFactory.Preferred(UguiFactory.Button(add.transform, Localization.Get("queue-add-full"), () => AddQueueItem(false)), 0f, 34f);
            var selection = UguiFactory.Button(add.transform, Localization.Get("queue-add-selection"), () => AddQueueItem(true));
            selection.interactable = GetSelectedTileRange().Length >= 2; UguiFactory.Preferred(selection, 0f, 34f);
            var controls = UguiFactory.Row(content, 34f);
            var start = UguiFactory.Button(controls.transform, Localization.Get("queue-start"), () => {
                var renderer = RendererController.Instance;
                if (renderer == null || renderer.Busy || !queue.HasPending) return;
                Close(); renderer.StartRenderQueue();
            }, true);
            start.interactable = queue.HasPending; UguiFactory.Preferred(start, 0f, 34f);
            var clear = UguiFactory.Button(controls.transform, Localization.Get("queue-clear-finished"), () => { queue.ClearFinished(); RebuildContent(); });
            UguiFactory.Preferred(clear, 0f, 34f);
            foreach (var item in queue.Items)
            {
                var caption = UguiFactory.Text(content, Localization.Get("queue-state-" + item.State.ToString().ToLowerInvariant()) + " · " + item.Label,
                    12, TextAnchor.MiddleLeft, UguiFactory.Foreground);
                caption.supportRichText = false; UguiFactory.Preferred(caption, 0f, 48f);
                if (!string.IsNullOrEmpty(item.Error)) AddLabel(item.Error, 11, 60f, UguiFactory.Error);
                var row = UguiFactory.Row(content, 30f);
                var up = UguiFactory.Button(row.transform, Localization.Get("queue-up"), () => { queue.Move(item, -1); RebuildContent(); });
                var down = UguiFactory.Button(row.transform, Localization.Get("queue-down"), () => { queue.Move(item, 1); RebuildContent(); });
                up.interactable = down.interactable = item.State == QueueItemState.Pending;
                var remove = UguiFactory.Button(row.transform, Localization.Get("queue-remove"), () => { queue.Remove(item); RebuildContent(); });
                remove.interactable = item.State != QueueItemState.Running;
                UguiFactory.Preferred(up, 0f, 30f); UguiFactory.Preferred(down, 0f, 30f); UguiFactory.Preferred(remove, 0f, 30f);
                if (!string.IsNullOrEmpty(item.Output) && item.State == QueueItemState.Completed)
                {
                    var file = UguiFactory.Button(row.transform, Localization.Get("completion-open-video"), () => Application.OpenURL(new Uri(item.Output).AbsoluteUri));
                    UguiFactory.Preferred(file, 0f, 30f);
                }
            }
        }
        private static void AddToggle(string label, bool value, Action<bool> changed) => UguiFactory.Toggle(content, label, value, changed);
        private static void AddHeading(string title)
        { var label = UguiFactory.Text(content, title, UiLayout.LabelFontSize);
            label.fontStyle = FontStyle.Bold; UguiFactory.Preferred(label, 0f, 30f); }
        private static void AddLabel(string value, int size, float height, Color color)
        { var label = UguiFactory.Text(content, value, size, TextAnchor.MiddleLeft, color); UguiFactory.Preferred(label, 0f, height); }

        private static void AddUserPresets()
        {
            var row = UguiFactory.Row(content, 34f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var label = UguiFactory.Text(row.transform, Localization.Get("preset"));
            UguiFactory.Preferred(label, 80f, 34f);
            var presets = UserRenderPresets.Items(Main.Settings).Where(p => p != null).ToArray();
            var builtins = new[] { Localization.Get("custom"), Localization.Get("preview"), "FullHD", "QHD", "UHD 4K" };
            var choices = builtins.Concat(presets.Select(p => p.Name)).ToArray();
            var savedIndex = Array.IndexOf(presets, selectedUserPreset);
            var selected = savedIndex >= 0 ? builtins.Length + savedIndex : (int)draft.Preset;
            var dropdown = UguiFactory.Dropdown(row.transform, choices, selected, index => {
                AudioPreview.Stop();
                if (index < builtins.Length)
                {
                    selectedUserPreset = null;
                    draft.Preset = (RendererPreset)index;
                    if (draft.Preset != RendererPreset.Custom) draft.ApplyPreset();
                }
                else
                {
                    selectedUserPreset = presets[index - builtins.Length];
                    var settings = new RendererSettings();
                    selectedUserPreset.ApplyTo(settings);
                    var saveAsDefault = draft.SaveAsDefault;
                    draft = Draft.From(settings);
                    draft.SaveAsDefault = saveAsDefault;
                }
                error = string.Empty;
                filenamePreviewSignature = null;
                UnityEngine.Object.Destroy(canvasObject); BuildCanvas();
            }, presets.Length > 0 ? new[] { builtins.Length } : null);
            UguiFactory.Preferred(dropdown, 0f, 34f);
            dropdown.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var save = UguiFactory.Button(row.transform, "Preset Save", OpenPresetSave);
            UguiFactory.Preferred(save, 120f, 34f);
        }

        private static void OpenPresetSave()
        {
            if (!TryValidatedOptions(out var options, out var message))
            { error = message; Refresh(RendererController.Instance); return; }
            RefreshFilenamePreview();
            if (!string.IsNullOrEmpty(filenameError))
            { error = filenameError; Refresh(RendererController.Instance); return; }
            var source = new RendererSettings();
            draft.ApplyTo(source, options);
            PresetSaveModal.Open(name => {
                var failure = UserRenderPresets.Save(Main.Settings, name, source);
                if (failure != null) return Localization.Get(failure);
                Main.Settings.Save(Main.Entry);
                selectedUserPreset = UserRenderPresets.Items(Main.Settings).First(p => p != null
                    && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                error = string.Empty;
                RebuildContent();
                return null;
            });
        }
        private static void Confirm(RendererController renderer, bool selectionOnly)
        {
            if (replayJobId != null) { error = Localization.Get("replay-preparing"); Refresh(renderer); return; }
            if (selectionOnly && !string.IsNullOrEmpty(replayManifestPath)) { error = Localization.Get("replay-no-selection"); Refresh(renderer); return; }
            if (renderer == null || renderer.Busy || RendererController.Queue.IsRunning) { error = Localization.Get("a-render-is-already-in-progress"); Refresh(renderer); return; }
            if (!TryValidatedOptions(out var options, out var message)) { error = message; Refresh(renderer); return; }
            options.ReplayManifestPath = replayManifestPath;
            RefreshFilenamePreview();
            if (!string.IsNullOrEmpty(filenameError)) { error = filenameError; Refresh(renderer); return; }
            if (selectionOnly) { var selected = GetSelectedTileRange(); if (selected.Length < 2) {
                    error = Localization.Get("select-at-least-two-tiles-to-export-a-selection"); Refresh(renderer); return; }
                options.SelectionStartTile = selected[0]; options.SelectionEndTile = selected[selected.Length - 1]; }
            if (draft.SaveAsDefault) { draft.ApplyTo(Main.Settings, options); Main.Settings.Save(Main.Entry); }
            var targetEditor = editor; Close(); if (targetEditor != null) targetEditor.ShowFileActionsPanel(false); renderer.StartRender(options);
        }

        private static int[] GetSelectedTileRange() => editor != null && editor.selectedFloors != null
            ? editor.selectedFloors.Where(f => f != null).Select(f => f.seqID).Distinct().OrderBy(id => id).ToArray() : new int[0];
        private static void Close()
        { if (replayJobId != null) { try { OrbitRender.TufReplay.TufReplayBridge.Cancel(replayJobId); } catch (Exception ex) { Main.Entry.Logger.Error("[Replay] " + ex); } replayJobId = null; }
            PresetSaveModal.Close(); AudioPreview.Stop(); open = false; if (canvasObject != null) UnityEngine.Object.Destroy(canvasObject);
            canvasObject = null; panelRect = null; content = null; scrollRect = null; errorText = null; audioPreviewText = null;
            storageText = null;
            nextStorageRefresh = 0;
            filenamePreviewText = null;
            filenameInputField = null; filenamePreviewSignature = null; filenameError = null;
            outputFormatDropdown = null; fieldViews.Clear();
            filenameHelpPanel = null;
            encoderAvailability = null; editor = null; draft = null; error = string.Empty; }

        private sealed class Field
        { internal readonly string Key, Label, Value; internal readonly Action<string> Changed;
            internal Field(string label, string value, Action<string> changed) { Key = label; Label = Localization.Get(label) + (label == "bitrate" ? " (Mbps)" : ""); Value = value; Changed = changed; } }

        private sealed class Draft
        {
            internal RendererPreset Preset;
            internal FrameBufferPolicy BufferPolicy;
            internal VideoContainer Container;
            internal AudioCodec AudioCodec;
            internal string FileNameFormat;
            internal string WidthText, HeightText, FpsText, VideoFpsText, BitrateText, EndDelayText, AudioGainDbText;
            internal float AudioGainDb;
            internal bool UseRustRenderer, CaptureAudio, ShowRenderPreview, BgaMode, ShowPlanetRings, ShowSongTitle, ShowCountdown,
                ShowResultText, ShowHitJudgments, ShowModOverlay, OpenOutputFolder, SaveAsDefault;
            internal EncoderSpeed Encoding; internal VideoEncoder Encoder; internal VideoCodec Codec; internal VideoBitDepth BitDepth;
            internal static Draft From(RendererSettings s) => new Draft { Preset = s.Preset, BufferPolicy = s.BufferPolicy,
                WidthText = s.Width.ToString(CultureInfo.InvariantCulture), HeightText = s.Height.ToString(CultureInfo.InvariantCulture),
                FpsText = s.Fps.ToString(CultureInfo.InvariantCulture), VideoFpsText = s.VideoFps.ToString(CultureInfo.InvariantCulture),
                BitrateText = s.BitrateMbps.ToString(CultureInfo.InvariantCulture), EndDelayText = s.EndDelaySeconds.ToString("0.##", CultureInfo.InvariantCulture),
                AudioGainDb = RendererSettings.ClampAudioGainDb(s.AudioGainDb), CaptureAudio = s.CaptureAudio, AudioCodec = s.AudioCodec,
                UseRustRenderer = s.UseRustRenderer, ShowRenderPreview = s.ShowRenderPreview, BgaMode = s.BgaMode, ShowPlanetRings = s.ShowPlanetRings,
                ShowSongTitle = s.ShowSongTitle, ShowCountdown = s.ShowCountdown, ShowResultText = s.ShowResultText,
                ShowHitJudgments = s.ShowHitJudgments, ShowModOverlay = s.ShowModOverlay, OpenOutputFolder = s.OpenOutputFolder, Encoding = s.Encoding,
                Encoder = s.Encoder, Codec = s.Codec, BitDepth = s.BitDepth, Container = s.Container, FileNameFormat = s.FileNameFormat };
            internal void ApplyPreset() { switch (Preset) { case RendererPreset.Preview: SetVideoValues(1280,720,30,30,8); break;
                case RendererPreset.QHD: SetVideoValues(2560,1440,60,60,30); break; case RendererPreset.UHD4K: SetVideoValues(3840,2160,60,60,50); break;
                case RendererPreset.FullHD: SetVideoValues(1920,1080,60,60,36); break; } }
            internal System.Collections.Generic.Dictionary<string, object> FilenameVariables()
            {
                int.TryParse(WidthText, out var width); int.TryParse(HeightText, out var height);
                int.TryParse(FpsText, out var fps); int.TryParse(VideoFpsText, out var videoFps);
                int.TryParse(BitrateText, out var bitrate);
                return ExportFileName.Variables(new RenderProfile(width, height, fps, videoFps, bitrate,
                    "", videoCodec: Codec, bitDepth: BitDepth, container: Container), BgaMode, filenamePreviewTime, filenamePreviewId);
            }
            internal System.Collections.Generic.List<ExportFieldError> ValidationErrors()
            {
                var errors = ExportValidation.Check(Preset == RendererPreset.Custom, WidthText, HeightText,
                    BitrateText, FpsText, VideoFpsText, EndDelayText);
                var compatibility = ExportValidation.ContainerError(Codec, Container);
                if (compatibility != null) errors.Add(new ExportFieldError("output-format", compatibility, -1));
                return errors;
            }

            internal bool TryCreateOptions(out RenderRequestOptions options, out string message)
            {
                options = null; message = string.Empty;
                var failures = ValidationErrors();
                if (failures.Count > 0) { message = Localization.Get(failures[0].Message); return false; }
                int.TryParse(WidthText, out var width); int.TryParse(HeightText, out var height);
                int.TryParse(BitrateText, out var bitrate); int.TryParse(FpsText, out var targetFps);
                int.TryParse(VideoFpsText, out var videoFps); TryParseFloat(EndDelayText, out var endDelay);
                options=new RenderRequestOptions { Preset=Preset,BufferPolicy=BufferPolicy,EndDelaySeconds=endDelay,CaptureAudio=CaptureAudio,AudioGainDb=RendererSettings.ClampAudioGainDb(AudioGainDb),
                    AudioCodec=AudioCodecCatalog.CompatibleSelection(AudioCodec,OutputFormat.Extension(Container,Codec)),
                    UseRustRenderer=UseRustRenderer,ShowRenderPreview=ShowRenderPreview,BgaMode=BgaMode,ShowPlanetRings=ShowPlanetRings,ShowSongTitle=ShowSongTitle,ShowCountdown=ShowCountdown,
                    ShowResultText=ShowResultText,ShowHitJudgments=ShowHitJudgments,ShowModOverlay=ShowModOverlay,Encoding=Encoding,Encoder=Encoder,VideoCodec=Codec,BitDepth=BitDepth,
                    Container=Container,FileNameFormat=FileNameFormat,OpenOutputFolder=OpenOutputFolder,TargetFps=targetFps,VideoFps=videoFps };
                if(Preset==RendererPreset.Custom){options.Width=width;options.Height=height;options.BitrateMbps=bitrate;} return true; }
            internal void ApplyTo(RendererSettings s, RenderRequestOptions o)
            { s.BufferPolicy=BufferPolicy;s.UseRustRenderer=UseRustRenderer;s.Preset=Preset;s.Fps=o.TargetFps.Value;s.VideoFps=o.VideoFps.Value;if(Preset==RendererPreset.Custom){s.Width=o.Width.Value;s.Height=o.Height.Value;s.BitrateMbps=o.BitrateMbps.Value;}
                s.EndDelaySeconds=o.EndDelaySeconds.Value;s.CaptureAudio=CaptureAudio;s.AudioGainDb=o.AudioGainDb.Value;s.AudioCodec=o.AudioCodec.Value;s.ShowRenderPreview=ShowRenderPreview;s.BgaMode=BgaMode;
                s.ShowPlanetRings=ShowPlanetRings;s.ShowSongTitle=ShowSongTitle;s.ShowCountdown=ShowCountdown;s.ShowResultText=ShowResultText;s.ShowHitJudgments=ShowHitJudgments;s.ShowModOverlay=ShowModOverlay;
                s.Container=Container;s.FileNameFormat=FileNameFormat;s.Encoding=Encoding;s.Encoder=Encoder;s.Codec=Codec;s.BitDepth=BitDepth;s.OpenOutputFolder=OpenOutputFolder;s.OnChange(); }
            private void SetVideoValues(int w,int h,int f,int vf,int b){WidthText=w.ToString(CultureInfo.InvariantCulture);HeightText=h.ToString(CultureInfo.InvariantCulture);
                FpsText=f.ToString(CultureInfo.InvariantCulture);VideoFpsText=vf.ToString(CultureInfo.InvariantCulture);BitrateText=b.ToString(CultureInfo.InvariantCulture);}
            private static bool TryParseFloat(string value,out float result)=>float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)||float.TryParse(value,NumberStyles.Float,CultureInfo.CurrentCulture,out result);
        }
    }
}
