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
        private static string filenamePreviewSignature;
        private static string filenameError;
        private static bool filenameHelpVisible;
        private static GameObject filenameHelpPanel;
        private static DateTime filenamePreviewTime;
        private static string filenamePreviewId;
        private static GameObject canvasObject;
        private static RectTransform panelRect;
        private static RectTransform content;
        private static ScrollRect scrollRect;
        private static Text errorText;
        private static Text audioPreviewText;
        private static EncoderAvailability.CombinedResult encoderAvailability;
        private static bool encoderAvailabilityComplete;
        private static UserRenderPreset selectedUserPreset;
        private static string userPresetName = string.Empty;
        private static bool confirmPresetDelete;

        internal static bool IsOpen => open;
        internal static void CloseDialog() => Close();

        internal static void Open(scnEditor owner)
        {
            if (owner == null || Main.Settings == null) return;
            Close();
            editor = owner;
            draft = Draft.From(Main.Settings);
            selectedUserPreset = null;
            userPresetName = string.Empty;
            confirmPresetDelete = false;
            selectedTab = 0;
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
            RefreshLayout();
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
            }
            catch (FormatException ex)
            {
                filenameError = Localization.Format("filename-template-error", ex.Message);
                filenamePreviewText.color = UguiFactory.Error;
                filenamePreviewText.text = filenameError;
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
            var title = UguiFactory.Text(panel.transform, Localization.Get("export-video"), UiLayout.TitleFontSize);
            title.fontStyle = FontStyle.Bold;
            UguiFactory.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(22f, -44f), new Vector2(-22f, -20f));

            var tabs = UguiFactory.Toolbar(panel.transform, new[] { Localization.Get("export-tab-basic"),
                Localization.Get("export-tab-game"), Localization.Get("export-tab-advanced") }, selectedTab,
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
            UguiFactory.Anchor(filenameHelpPanel.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f),
                new Vector2(26f, 244f), new Vector2(-26f, 414f));
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
                11, TextAnchor.MiddleLeft, UguiFactory.Muted);
            UguiFactory.Preferred(variableHelp, 0f, 32f);
            var templateHelp = UguiFactory.Text(filenameHelpPanel.transform, Localization.Get("filename-template-help"),
                11, TextAnchor.MiddleLeft, UguiFactory.Muted);
            UguiFactory.Preferred(templateHelp, 0f, 64f);
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
            if (content != null) UpdateContentLayout(position);
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
            encoderAvailability = null;
            if (selectedTab == 0)
            {
                AddUserPresets();
                AddRadioGroup(Localization.Get("preset"), new[] { Localization.Get("custom"), Localization.Get("preview"),
                    "FullHD", "QHD", "UHD 4K" }, (int)draft.Preset, value => {
                        draft.Preset = (RendererPreset)value; if (draft.Preset != RendererPreset.Custom) draft.ApplyPreset(); RebuildContent();
                    });
                if (draft.Preset == RendererPreset.Custom)
                    AddInputRow(new[] { new Field(Localization.Get("width"), draft.WidthText, v => draft.WidthText = v),
                        new Field(Localization.Get("height"), draft.HeightText, v => draft.HeightText = v),
                        new Field(Localization.Get("bitrate") + " (Mbps)", draft.BitrateText, v => draft.BitrateText = v) });
                else AddLabel(Localization.Format("preset-output", draft.WidthText, draft.HeightText, draft.FpsText,
                    draft.VideoFpsText, draft.BitrateText), UiLayout.LabelFontSize, 22f, UguiFactory.Muted);
                AddInputRow(new[] { new Field(Localization.Get("ingame-fps"), draft.FpsText, v => draft.FpsText = v),
                    new Field(Localization.Get("video-fps"), draft.VideoFpsText, v => draft.VideoFpsText = v) });

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
                }
            }
            else if (selectedTab == 2)
            {
                AddHeading(Localization.Get("encoding"));
                AddEncoderDropdown();
                AddRadioGroup(Localization.Get("encoding-speed"), new[] { Localization.Get("maximum"),
                    Localization.Get("balanced"), Localization.Get("quality") }, (int)draft.Encoding,
                    v => draft.Encoding = (EncoderSpeed)v);
                AddRadioGroup(Localization.Get("video-bit-depth"), new[] { "8-bit", "10-bit" },
                    (int)draft.BitDepth, v => { draft.BitDepth = (VideoBitDepth)v; RebuildContent(); });
                AddHeading(Localization.Get("render-options"));
                AddToggle(Localization.Get("show-render-preview"), draft.ShowRenderPreview, v => draft.ShowRenderPreview = v);
                AddToggle(Localization.Get("open-output-folder-after-render"), draft.OpenOutputFolder, v => draft.OpenOutputFolder = v);
            }
            else
            {
                AddInputRow(new[] { new Field(Localization.Get("end-delay-seconds"), draft.EndDelayText, v => draft.EndDelayText = v) });
                AddToggle(Localization.Get("bga-mode-hide-tiles-planets-hit-sounds"), draft.BgaMode, v => draft.BgaMode = v);
                AddHeading(Localization.Get("visible-components"));
                AddToggle(Localization.Get("show-planet-rings"), draft.ShowPlanetRings, v => draft.ShowPlanetRings = v);
                AddToggle(Localization.Get("show-song-title"), draft.ShowSongTitle, v => draft.ShowSongTitle = v);
                AddToggle(Localization.Get("show-countdown"), draft.ShowCountdown, v => draft.ShowCountdown = v);
                AddToggle(Localization.Get("show-result-text-hit-judgments-stay-hidden"), draft.ShowResultText, v => draft.ShowResultText = v);
                AddToggle(Localization.Get("show-hit-judgments"), draft.ShowHitJudgments, v => draft.ShowHitJudgments = v);
            }
            UpdateContentLayout(position);
            Refresh(RendererController.Instance);
        }

        private static void UpdateContentLayout(float position)
        {
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
            var filenameInput = UguiFactory.Input(fields.transform, draft.FileNameFormat, v => draft.FileNameFormat = v);
            filenameInputField = filenameInput;
            UguiFactory.Preferred(filenameInput, 0f, 34f);
            filenameInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var formatDropdown = UguiFactory.Dropdown(fields.transform,
                new[] { Localization.Get("auto"), ".mp4", ".ts", ".mkv", ".mov" },
                (int)draft.Container, v => { draft.Container = (VideoContainer)v; RebuildContent(); });
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
            }, separators);
            dropdown.interactable = choices.Length > 0;
            UguiFactory.Preferred(dropdown, 0f, 34f);
            var refresh = UguiFactory.Button(content, Localization.Get("refresh-encoders"),
                () => { EncoderAvailability.Refresh(); RebuildContent(); });
            UguiFactory.Preferred(refresh, 0f, 30f);
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
            var row = UguiFactory.Row(content);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            foreach (var field in fields)
            {
                var label = UguiFactory.Text(row.transform, field.Label, UiLayout.LabelFontSize,
                    TextAnchor.MiddleLeft, UguiFactory.Muted);
                UguiFactory.Preferred(label, 100f);
                var labelLayout = label.GetComponent<LayoutElement>();
                labelLayout.minWidth = 100f;
                labelLayout.flexibleWidth = 0f;
                var input = UguiFactory.Input(row.transform, field.Value, field.Changed);
                UguiFactory.Preferred(input, 110f);
                input.GetComponent<LayoutElement>().flexibleWidth = 1f;
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
            AddHeading(Localization.Get("user-presets"));
            var presets = UserRenderPresets.Items(Main.Settings).Where(p => p != null).ToArray();
            var choices = new[] { Localization.Get("user-preset-select") }.Concat(presets.Select(p => p.Name)).ToArray();
            var selection = Array.IndexOf(presets, selectedUserPreset) + 1;
            var dropdown = UguiFactory.Dropdown(content, choices, selection, index => {
                confirmPresetDelete = false;
                selectedUserPreset = index > 0 ? presets[index - 1] : null;
                if (selectedUserPreset != null)
                {
                    AudioPreview.Stop();
                    var settings = new RendererSettings();
                    selectedUserPreset.ApplyTo(settings);
                    var saveAsDefault = draft.SaveAsDefault;
                    draft = Draft.From(settings);
                    draft.SaveAsDefault = saveAsDefault;
                    userPresetName = selectedUserPreset.Name;
                    error = string.Empty;
                    filenamePreviewSignature = null;
                }
                RebuildContent();
            });
            UguiFactory.Preferred(dropdown, 0f, 34f);
            AddLabel(Localization.Get("user-preset-hint"), 11, 32f, UguiFactory.Muted);
            var nameRow = UguiFactory.Row(content, 34f);
            var nameLabel = UguiFactory.Text(nameRow.transform, Localization.Get("user-preset-name"));
            UguiFactory.Preferred(nameLabel, 100f, 34f);
            var nameInput = UguiFactory.Input(nameRow.transform, userPresetName, value => userPresetName = value);
            nameInput.characterLimit = 64;
            nameInput.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var actions = UguiFactory.Row(content, 34f);
            UguiFactory.Preferred(UguiFactory.Button(actions.transform, Localization.Get("user-preset-save-new"),
                () => SaveUserPreset(false)), 120f, 34f);
            var update = UguiFactory.Button(actions.transform, Localization.Get("user-preset-update"),
                () => SaveUserPreset(true));
            UguiFactory.Preferred(update, 120f, 34f);
            update.interactable = selectedUserPreset != null;
            var rename = UguiFactory.Button(actions.transform, Localization.Get("user-preset-rename"), () => {
                var message = UserRenderPresets.Rename(Main.Settings, selectedUserPreset, userPresetName);
                if (message != null) { error = Localization.Get(message); Refresh(RendererController.Instance); return; }
                Main.Settings.Save(Main.Entry);
                error = string.Empty;
                RebuildContent();
            });
            UguiFactory.Preferred(rename, 100f, 34f);
            rename.interactable = selectedUserPreset != null;
            var delete = UguiFactory.Button(actions.transform, Localization.Get("user-preset-delete"),
                () => { confirmPresetDelete = true; RebuildContent(); });
            UguiFactory.Preferred(delete, 100f, 34f);
            delete.interactable = selectedUserPreset != null;
            if (confirmPresetDelete && selectedUserPreset != null)
            {
                AddLabel(Localization.Format("user-preset-delete-question", selectedUserPreset.Name),
                    UiLayout.LabelFontSize, 38f, UguiFactory.Error);
                var confirmation = UguiFactory.Row(content, 34f);
                UguiFactory.Preferred(UguiFactory.Button(confirmation.transform, Localization.Get("user-preset-delete-confirm"), () => {
                    UserRenderPresets.Items(Main.Settings).Remove(selectedUserPreset);
                    Main.Settings.Save(Main.Entry);
                    selectedUserPreset = null;
                    userPresetName = string.Empty;
                    confirmPresetDelete = false;
                    error = string.Empty;
                    RebuildContent();
                }), 150f, 34f);
                UguiFactory.Preferred(UguiFactory.Button(confirmation.transform, Localization.Get("cancel"),
                    () => { confirmPresetDelete = false; RebuildContent(); }), 100f, 34f);
            }
        }

        private static void SaveUserPreset(bool update)
        {
            if (!draft.TryCreateOptions(out var options, out var message))
            { error = message; Refresh(RendererController.Instance); return; }
            RefreshFilenamePreview();
            if (!string.IsNullOrEmpty(filenameError))
            { error = filenameError; Refresh(RendererController.Instance); return; }
            var source = new RendererSettings();
            draft.ApplyTo(source, options);
            var savedName = update && selectedUserPreset != null ? selectedUserPreset.Name : userPresetName;
            message = UserRenderPresets.Save(Main.Settings, savedName, source,
                update ? selectedUserPreset : null);
            if (message != null) { error = Localization.Get(message); Refresh(RendererController.Instance); return; }
            Main.Settings.Save(Main.Entry);
            selectedUserPreset = UserRenderPresets.Items(Main.Settings).First(p => p != null
                && string.Equals(p.Name, savedName.Trim(), StringComparison.OrdinalIgnoreCase));
            userPresetName = selectedUserPreset.Name;
            confirmPresetDelete = false;
            error = string.Empty;
            RebuildContent();
        }

        private static void Confirm(RendererController renderer, bool selectionOnly)
        {
            if (renderer == null || renderer.Busy) { error = Localization.Get("a-render-is-already-in-progress"); Refresh(renderer); return; }
            if (!draft.TryCreateOptions(out var options, out var message)) { error = message; Refresh(renderer); return; }
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
        { AudioPreview.Stop(); open = false; if (canvasObject != null) UnityEngine.Object.Destroy(canvasObject);
            canvasObject = null; panelRect = null; content = null; scrollRect = null; errorText = null; audioPreviewText = null;
            filenamePreviewText = null;
            filenameInputField = null; filenamePreviewSignature = null; filenameError = null;
            filenameHelpPanel = null;
            encoderAvailability = null; editor = null; draft = null; error = string.Empty; }

        private sealed class Field
        { internal readonly string Label, Value; internal readonly Action<string> Changed;
            internal Field(string label, string value, Action<string> changed) { Label = label; Value = value; Changed = changed; } }

        private sealed class Draft
        {
            internal RendererPreset Preset;
            internal VideoContainer Container;
            internal AudioCodec AudioCodec;
            internal string FileNameFormat;
            internal string WidthText, HeightText, FpsText, VideoFpsText, BitrateText, EndDelayText, AudioGainDbText;
            internal float AudioGainDb;
            internal bool CaptureAudio, ShowRenderPreview, BgaMode, ShowPlanetRings, ShowSongTitle, ShowCountdown,
                ShowResultText, ShowHitJudgments, OpenOutputFolder, SaveAsDefault;
            internal EncoderSpeed Encoding; internal VideoEncoder Encoder; internal VideoCodec Codec; internal VideoBitDepth BitDepth;
            internal static Draft From(RendererSettings s) => new Draft { Preset = s.Preset,
                WidthText = s.Width.ToString(CultureInfo.InvariantCulture), HeightText = s.Height.ToString(CultureInfo.InvariantCulture),
                FpsText = s.Fps.ToString(CultureInfo.InvariantCulture), VideoFpsText = s.VideoFps.ToString(CultureInfo.InvariantCulture),
                BitrateText = s.BitrateMbps.ToString(CultureInfo.InvariantCulture), EndDelayText = s.EndDelaySeconds.ToString("0.##", CultureInfo.InvariantCulture),
                AudioGainDb = RendererSettings.ClampAudioGainDb(s.AudioGainDb), CaptureAudio = s.CaptureAudio, AudioCodec = s.AudioCodec,
                ShowRenderPreview = s.ShowRenderPreview, BgaMode = s.BgaMode, ShowPlanetRings = s.ShowPlanetRings,
                ShowSongTitle = s.ShowSongTitle, ShowCountdown = s.ShowCountdown, ShowResultText = s.ShowResultText,
                ShowHitJudgments = s.ShowHitJudgments, OpenOutputFolder = s.OpenOutputFolder, Encoding = s.Encoding,
                Encoder = s.Encoder, Codec = s.Codec, BitDepth = s.BitDepth, Container = s.Container, FileNameFormat = s.FileNameFormat };
            internal void ApplyPreset() { switch (Preset) { case RendererPreset.Preview: SetVideoValues(1280,720,30,30,8); break;
                case RendererPreset.QHD: SetVideoValues(2560,1440,60,60,30); break; case RendererPreset.UHD4K: SetVideoValues(3840,2160,60,60,50); break;
                case RendererPreset.FullHD: SetVideoValues(1920,1080,60,60,18); break; } }
            internal System.Collections.Generic.Dictionary<string, object> FilenameVariables()
            {
                int.TryParse(WidthText, out var width); int.TryParse(HeightText, out var height);
                int.TryParse(FpsText, out var fps); int.TryParse(VideoFpsText, out var videoFps);
                int.TryParse(BitrateText, out var bitrate);
                return ExportFileName.Variables(new RenderProfile(width, height, fps, videoFps, bitrate,
                    "", videoCodec: Codec, bitDepth: BitDepth, container: Container), BgaMode, filenamePreviewTime, filenamePreviewId);
            }
            internal bool TryCreateOptions(out RenderRequestOptions options, out string message)
            { options = null; message = string.Empty; int width=0,height=0,targetFps,videoFps,bitrate=0; float endDelay;
                if (!int.TryParse(FpsText,out targetFps)||targetFps<15||targetFps>1024) { message=Localization.Get("ingame-fps-must-be-between-15-and-1024"); return false; }
                if (!int.TryParse(VideoFpsText,out videoFps)||videoFps<15||videoFps>240) { message=Localization.Get("video-fps-must-be-between-15-and-240"); return false; }
                if (Preset==RendererPreset.Custom) { if(!int.TryParse(WidthText,out width)||!int.TryParse(HeightText,out height)||!int.TryParse(BitrateText,out bitrate)) { message=Localization.Get("width-height-and-bitrate-must-be-valid-numbers"); return false; }
                    if(width<320||width>3840||height<180||height>2160||bitrate<1||bitrate>200) { message=Localization.Get("custom-values-are-outside-the-supported-ranges"); return false; }
                    if((width&1)!=0||(height&1)!=0) { message=Localization.Get("width-and-height-must-be-even-numbers"); return false; } }
                if(!TryParseFloat(EndDelayText,out endDelay)||endDelay<0f||endDelay>30f) { message=Localization.Get("end-delay-must-be-between-0-and-30-seconds"); return false; }
                options=new RenderRequestOptions { Preset=Preset,EndDelaySeconds=endDelay,CaptureAudio=CaptureAudio,AudioGainDb=RendererSettings.ClampAudioGainDb(AudioGainDb),
                    AudioCodec=AudioCodecCatalog.CompatibleSelection(AudioCodec,OutputFormat.Extension(Container,Codec)),
                    ShowRenderPreview=ShowRenderPreview,BgaMode=BgaMode,ShowPlanetRings=ShowPlanetRings,ShowSongTitle=ShowSongTitle,ShowCountdown=ShowCountdown,
                    ShowResultText=ShowResultText,ShowHitJudgments=ShowHitJudgments,Encoding=Encoding,Encoder=Encoder,VideoCodec=Codec,BitDepth=BitDepth,
                    Container=Container,FileNameFormat=FileNameFormat,OpenOutputFolder=OpenOutputFolder,TargetFps=targetFps,VideoFps=videoFps };
                if(Preset==RendererPreset.Custom){options.Width=width;options.Height=height;options.BitrateMbps=bitrate;} return true; }
            internal void ApplyTo(RendererSettings s, RenderRequestOptions o)
            { s.Preset=Preset;s.Fps=o.TargetFps.Value;s.VideoFps=o.VideoFps.Value;if(Preset==RendererPreset.Custom){s.Width=o.Width.Value;s.Height=o.Height.Value;s.BitrateMbps=o.BitrateMbps.Value;}
                s.EndDelaySeconds=o.EndDelaySeconds.Value;s.CaptureAudio=CaptureAudio;s.AudioGainDb=o.AudioGainDb.Value;s.AudioCodec=o.AudioCodec.Value;s.ShowRenderPreview=ShowRenderPreview;s.BgaMode=BgaMode;
                s.ShowPlanetRings=ShowPlanetRings;s.ShowSongTitle=ShowSongTitle;s.ShowCountdown=ShowCountdown;s.ShowResultText=ShowResultText;s.ShowHitJudgments=ShowHitJudgments;
                s.Container=Container;s.FileNameFormat=FileNameFormat;s.Encoding=Encoding;s.Encoder=Encoder;s.Codec=Codec;s.BitDepth=BitDepth;s.OpenOutputFolder=OpenOutputFolder;s.OnChange(); }
            private void SetVideoValues(int w,int h,int f,int vf,int b){WidthText=w.ToString(CultureInfo.InvariantCulture);HeightText=h.ToString(CultureInfo.InvariantCulture);
                FpsText=f.ToString(CultureInfo.InvariantCulture);VideoFpsText=vf.ToString(CultureInfo.InvariantCulture);BitrateText=b.ToString(CultureInfo.InvariantCulture);}
            private static bool TryParseFloat(string value,out float result)=>float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)||float.TryParse(value,NumberStyles.Float,CultureInfo.CurrentCulture,out result);
        }
    }
}
