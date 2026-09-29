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
        private static bool renderOptionsExpanded = true;
        private static bool visibleComponentsExpanded = true;
        private static bool encodingExpanded;
        private static GameObject canvasObject;
        private static RectTransform content;
        private static ScrollRect scrollRect;
        private static Text errorText;
        private static Text audioPreviewText;

        internal static bool IsOpen => open;
        internal static void CloseDialog() => Close();

        internal static void Open(scnEditor owner)
        {
            if (owner == null || Main.Settings == null) return;
            Close();
            editor = owner;
            draft = Draft.From(Main.Settings);
            error = string.Empty;
            open = true;
            editor.ShowFileActionsPanel(false);
            BuildCanvas();
        }

        internal static void Refresh(RendererController renderer)
        {
            if (!open || canvasObject == null) return;
            if (!string.IsNullOrEmpty(AudioPreview.ErrorMessage)) error = AudioPreview.ErrorMessage;
            if (errorText != null)
            {
                errorText.text = error ?? string.Empty;
                errorText.gameObject.SetActive(!string.IsNullOrEmpty(error));
            }
            if (audioPreviewText != null)
                audioPreviewText.text = AudioPreview.IsLoading ? Localization.Get("loading-audio-preview")
                    : AudioPreview.IsActive ? Localization.Get("stop-audio-preview")
                    : Localization.Get("preview-audio");
        }

        private static void BuildCanvas()
        {
            canvasObject = UguiFactory.Canvas("OrbitRender.ExportVideoDialog", 32760);
            var dimmer = UguiFactory.Image(canvasObject.transform, "Dimmer", new Color(0f, 0f, 0f, .72f), true);
            UguiFactory.Stretch(dimmer.GetComponent<RectTransform>());
            var panel = UguiFactory.Image(canvasObject.transform, "Panel", UguiFactory.Surface, true);
            UguiFactory.Anchor(panel.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-380f, -340f), new Vector2(380f, 340f));
            var title = UguiFactory.Text(panel.transform, Localization.Get("export-video"), 28);
            UguiFactory.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -62f), new Vector2(-24f, -16f));

            var scrollView = UguiFactory.New(panel.transform, "Scroll View", typeof(RectTransform), typeof(ScrollRect));
            UguiFactory.Anchor(scrollView.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(24f, 82f), new Vector2(-24f, -68f));
            var viewport = UguiFactory.New(scrollView.transform, "Viewport", typeof(RectTransform), typeof(RectMask2D));
            UguiFactory.Stretch(viewport.GetComponent<RectTransform>());
            var body = UguiFactory.New(viewport.transform, "Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content = body.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f); content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var layout = body.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f; layout.padding = new RectOffset(4, 14, 4, 10);
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>(); scrollRect.content = content;
            scrollRect.horizontal = false; scrollRect.scrollSensitivity = 28f;

            errorText = UguiFactory.Text(panel.transform, string.Empty, 17, TextAnchor.MiddleLeft, UguiFactory.Error);
            UguiFactory.Anchor(errorText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 51f), new Vector2(-24f, 78f));
            var footer = UguiFactory.New(panel.transform, "Footer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            UguiFactory.Anchor(footer.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 12f), new Vector2(-24f, 48f));
            var footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f; footerLayout.childForceExpandHeight = true; footerLayout.childForceExpandWidth = false;
            var cancel = UguiFactory.Button(footer.transform, Localization.Get("cancel"), Close); UguiFactory.Preferred(cancel, 120f);
            UguiFactory.New(footer.transform, "Spacer", typeof(LayoutElement)).GetComponent<LayoutElement>().flexibleWidth = 1f;
            if (GetSelectedTileRange().Length >= 2)
            {
                var selection = UguiFactory.Button(footer.transform, Localization.Get("export-video-selection"),
                    () => Confirm(RendererController.Instance, true)); UguiFactory.Preferred(selection, 210f);
            }
            var export = UguiFactory.Button(footer.transform, Localization.Get("export-video"),
                () => Confirm(RendererController.Instance, false), true); UguiFactory.Preferred(export, 160f);
            RebuildContent(false);
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
            AddLabel(Localization.Get("choose-the-settings-for-this-video-export"), 20, 40f, UguiFactory.Muted);
            AddDropdown(Localization.Get("preset"), new[] { Localization.Get("custom"), Localization.Get("preview"),
                "FullHD", "QHD", "UHD 4K" }, (int)draft.Preset, value => {
                    draft.Preset = (RendererPreset)value; if (draft.Preset != RendererPreset.Custom) draft.ApplyPreset(); RebuildContent();
                });
            if (draft.Preset == RendererPreset.Custom)
                AddInputRow(new[] { new Field(Localization.Get("width"), draft.WidthText, v => draft.WidthText = v),
                    new Field(Localization.Get("height"), draft.HeightText, v => draft.HeightText = v),
                    new Field(Localization.Get("bitrate") + " (Mbps)", draft.BitrateText, v => draft.BitrateText = v) });
            else AddLabel(Localization.Format("preset-output", draft.WidthText, draft.HeightText, draft.FpsText,
                draft.VideoFpsText, draft.BitrateText), 18, 34f, UguiFactory.Muted);
            AddInputRow(new[] { new Field(Localization.Get("ingame-fps"), draft.FpsText, v => draft.FpsText = v),
                new Field(Localization.Get("video-fps"), draft.VideoFpsText, v => draft.VideoFpsText = v) });

            AddSection(Localization.Get("render-options"), renderOptionsExpanded,
                () => { renderOptionsExpanded = !renderOptionsExpanded; RebuildContent(); });
            if (renderOptionsExpanded)
            {
                AddInputRow(new[] { new Field(Localization.Get("end-delay-seconds"), draft.EndDelayText, v => draft.EndDelayText = v) });
                AddToggle(Localization.Get("capture-audio"), draft.CaptureAudio, v => draft.CaptureAudio = v);
                AddAudioGain();
                var preview = UguiFactory.Button(content, string.Empty, () => {
                    var wasPlaying = AudioPreview.IsActive; var playing = AudioPreview.Toggle(draft.AudioGainDb);
                    error = !wasPlaying && !playing ? AudioPreview.ErrorMessage ?? Localization.Get("audio-preview-unavailable") : string.Empty;
                    Refresh(RendererController.Instance);
                });
                UguiFactory.Preferred(preview, 0f, 40f); audioPreviewText = preview.GetComponentInChildren<Text>();
                AddToggle(Localization.Get("show-render-preview"), draft.ShowRenderPreview, v => draft.ShowRenderPreview = v);
                AddToggle(Localization.Get("bga-mode-hide-tiles-planets-hit-sounds"), draft.BgaMode, v => draft.BgaMode = v);
                AddToggle(Localization.Get("open-output-folder-after-render"), draft.OpenOutputFolder, v => draft.OpenOutputFolder = v);
                AddToggle(Localization.Get("save-these-values-as-the-default-renderer-settings"), draft.SaveAsDefault, v => draft.SaveAsDefault = v);
            }
            AddSection(Localization.Get("visible-components"), visibleComponentsExpanded,
                () => { visibleComponentsExpanded = !visibleComponentsExpanded; RebuildContent(); });
            if (visibleComponentsExpanded)
            {
                AddToggle(Localization.Get("show-planet-rings"), draft.ShowPlanetRings, v => draft.ShowPlanetRings = v);
                AddToggle(Localization.Get("show-song-title"), draft.ShowSongTitle, v => draft.ShowSongTitle = v);
                AddToggle(Localization.Get("show-countdown"), draft.ShowCountdown, v => draft.ShowCountdown = v);
                AddToggle(Localization.Get("show-result-text-hit-judgments-stay-hidden"), draft.ShowResultText, v => draft.ShowResultText = v);
                AddToggle(Localization.Get("show-hit-judgments"), draft.ShowHitJudgments, v => draft.ShowHitJudgments = v);
            }
            AddSection(Localization.Get("encoding"), encodingExpanded,
                () => { encodingExpanded = !encodingExpanded; RebuildContent(); });
            if (encodingExpanded)
            {
                AddDropdown(Localization.Get("encoding-speed"), new[] { Localization.Get("maximum"),
                    Localization.Get("balanced"), Localization.Get("quality") }, (int)draft.Encoding,
                    v => draft.Encoding = (EncoderSpeed)v);
                var encoders = new[] { VideoEncoder.Auto, VideoEncoder.NvidiaNvenc, VideoEncoder.IntelQsv,
                    VideoEncoder.AmdAmf, VideoEncoder.Software };
                AddDropdown(Localization.Get("video-encoder"), new[] { Localization.Get("auto"), "NVIDIA NVENC",
                    "Intel QSV", "AMD AMF", Localization.Get("software") }, Array.IndexOf(encoders, draft.Encoder),
                    v => draft.Encoder = encoders[v]);
                AddDropdown(Localization.Get("video-codec"), new[] { "H.264", "H.265", "VP9", "AV1" },
                    (int)draft.Codec, v => draft.Codec = (VideoCodec)v);
                AddDropdown(Localization.Get("video-bit-depth"), new[] { "8-bit", "10-bit" },
                    (int)draft.BitDepth, v => draft.BitDepth = (VideoBitDepth)v);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, LayoutUtility.GetPreferredHeight(content));
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = position;
            Refresh(RendererController.Instance);
        }

        private static void AddAudioGain()
        {
            var row = UguiFactory.Row(content, 52f);
            var label = UguiFactory.Text(row.transform, Localization.Get("audio-volume-db"), 18); UguiFactory.Preferred(label, 180f);
            var valueLabel = UguiFactory.Text(row.transform, string.Empty, 18, TextAnchor.MiddleRight); UguiFactory.Preferred(valueLabel, 125f);
            Action<float> update = value => { draft.AudioGainDb = RendererSettings.ClampAudioGainDb(value);
                draft.AudioGainDbText = draft.AudioGainDb.ToString("0.#", CultureInfo.InvariantCulture);
                var percent = draft.AudioGainDb <= RendererSettings.MinAudioGainDb ? 0f : Mathf.Pow(10f, draft.AudioGainDb / 20f) * 100f;
                valueLabel.text = percent.ToString("0.#", CultureInfo.InvariantCulture) + "%  " + draft.AudioGainDbText + " dB";
                AudioPreview.SetGain(draft.AudioGainDb); };
            var slider = UguiFactory.Slider(row.transform, RendererSettings.MinAudioGainDb, RendererSettings.MaxAudioGainDb,
                draft.AudioGainDb, update); UguiFactory.Preferred(slider, 250f); update(draft.AudioGainDb);
        }

        private static void AddDropdown(string label, string[] options, int value, Action<int> changed)
        { var row = UguiFactory.Row(content, 46f); var text = UguiFactory.Text(row.transform, label, 18);
            UguiFactory.Preferred(text, 210f); UguiFactory.Dropdown(row.transform, options, Mathf.Max(0, value), changed); }
        private static void AddInputRow(Field[] fields)
        { var row = UguiFactory.Row(content, 48f); foreach (var field in fields) { var label = UguiFactory.Text(row.transform,
                field.Label, 17, TextAnchor.MiddleRight, UguiFactory.Muted); UguiFactory.Preferred(label, 100f);
                var input = UguiFactory.Input(row.transform, field.Value, field.Changed); UguiFactory.Preferred(input, 110f); } }
        private static void AddToggle(string label, bool value, Action<bool> changed) => UguiFactory.Toggle(content, label, value, changed);
        private static void AddSection(string title, bool expanded, Action action)
        { var button = UguiFactory.Button(content, (expanded ? "▼  " : "▶  ") + title, action);
            UguiFactory.Preferred(button, 0f, 42f); button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft; }
        private static void AddLabel(string value, int size, float height, Color color)
        { var label = UguiFactory.Text(content, value, size, TextAnchor.MiddleLeft, color); UguiFactory.Preferred(label, 0f, height); }

        private static void Confirm(RendererController renderer, bool selectionOnly)
        {
            if (renderer == null || renderer.Busy) { error = Localization.Get("a-render-is-already-in-progress"); Refresh(renderer); return; }
            if (!draft.TryCreateOptions(out var options, out var message)) { error = message; Refresh(renderer); return; }
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
            canvasObject = null; content = null; scrollRect = null; errorText = null; audioPreviewText = null;
            editor = null; draft = null; error = string.Empty; }

        private sealed class Field
        { internal readonly string Label, Value; internal readonly Action<string> Changed;
            internal Field(string label, string value, Action<string> changed) { Label = label; Value = value; Changed = changed; } }

        private sealed class Draft
        {
            internal RendererPreset Preset;
            internal string WidthText, HeightText, FpsText, VideoFpsText, BitrateText, EndDelayText, AudioGainDbText;
            internal float AudioGainDb;
            internal bool CaptureAudio, ShowRenderPreview, BgaMode, ShowPlanetRings, ShowSongTitle, ShowCountdown,
                ShowResultText, ShowHitJudgments, OpenOutputFolder, SaveAsDefault;
            internal EncoderSpeed Encoding; internal VideoEncoder Encoder; internal VideoCodec Codec; internal VideoBitDepth BitDepth;
            internal static Draft From(RendererSettings s) => new Draft { Preset = s.Preset,
                WidthText = s.Width.ToString(CultureInfo.InvariantCulture), HeightText = s.Height.ToString(CultureInfo.InvariantCulture),
                FpsText = s.Fps.ToString(CultureInfo.InvariantCulture), VideoFpsText = s.VideoFps.ToString(CultureInfo.InvariantCulture),
                BitrateText = s.BitrateMbps.ToString(CultureInfo.InvariantCulture), EndDelayText = s.EndDelaySeconds.ToString("0.##", CultureInfo.InvariantCulture),
                AudioGainDb = RendererSettings.ClampAudioGainDb(s.AudioGainDb), CaptureAudio = s.CaptureAudio,
                ShowRenderPreview = s.ShowRenderPreview, BgaMode = s.BgaMode, ShowPlanetRings = s.ShowPlanetRings,
                ShowSongTitle = s.ShowSongTitle, ShowCountdown = s.ShowCountdown, ShowResultText = s.ShowResultText,
                ShowHitJudgments = s.ShowHitJudgments, OpenOutputFolder = s.OpenOutputFolder, Encoding = s.Encoding,
                Encoder = s.Encoder, Codec = s.Codec, BitDepth = s.BitDepth };
            internal void ApplyPreset() { switch (Preset) { case RendererPreset.Preview: SetVideoValues(1280,720,30,30,8); break;
                case RendererPreset.QHD: SetVideoValues(2560,1440,60,60,30); break; case RendererPreset.UHD4K: SetVideoValues(3840,2160,60,60,50); break;
                case RendererPreset.FullHD: SetVideoValues(1920,1080,60,60,18); break; } }
            internal bool TryCreateOptions(out RenderRequestOptions options, out string message)
            { options = null; message = string.Empty; int width=0,height=0,targetFps,videoFps,bitrate=0; float endDelay;
                if (!int.TryParse(FpsText,out targetFps)||targetFps<15||targetFps>1024) { message=Localization.Get("ingame-fps-must-be-between-15-and-1024"); return false; }
                if (!int.TryParse(VideoFpsText,out videoFps)||videoFps<15||videoFps>240) { message=Localization.Get("video-fps-must-be-between-15-and-240"); return false; }
                if (Preset==RendererPreset.Custom) { if(!int.TryParse(WidthText,out width)||!int.TryParse(HeightText,out height)||!int.TryParse(BitrateText,out bitrate)) { message=Localization.Get("width-height-and-bitrate-must-be-valid-numbers"); return false; }
                    if(width<320||width>3840||height<180||height>2160||bitrate<1||bitrate>200) { message=Localization.Get("custom-values-are-outside-the-supported-ranges"); return false; }
                    if((width&1)!=0||(height&1)!=0) { message=Localization.Get("width-and-height-must-be-even-numbers"); return false; } }
                if(!TryParseFloat(EndDelayText,out endDelay)||endDelay<0f||endDelay>30f) { message=Localization.Get("end-delay-must-be-between-0-and-30-seconds"); return false; }
                options=new RenderRequestOptions { Preset=Preset,EndDelaySeconds=endDelay,CaptureAudio=CaptureAudio,AudioGainDb=RendererSettings.ClampAudioGainDb(AudioGainDb),
                    ShowRenderPreview=ShowRenderPreview,BgaMode=BgaMode,ShowPlanetRings=ShowPlanetRings,ShowSongTitle=ShowSongTitle,ShowCountdown=ShowCountdown,
                    ShowResultText=ShowResultText,ShowHitJudgments=ShowHitJudgments,Encoding=Encoding,Encoder=Encoder,VideoCodec=Codec,BitDepth=BitDepth,
                    OpenOutputFolder=OpenOutputFolder,TargetFps=targetFps,VideoFps=videoFps };
                if(Preset==RendererPreset.Custom){options.Width=width;options.Height=height;options.BitrateMbps=bitrate;} return true; }
            internal void ApplyTo(RendererSettings s, RenderRequestOptions o)
            { s.Preset=Preset;s.Fps=o.TargetFps.Value;s.VideoFps=o.VideoFps.Value;if(Preset==RendererPreset.Custom){s.Width=o.Width.Value;s.Height=o.Height.Value;s.BitrateMbps=o.BitrateMbps.Value;}
                s.EndDelaySeconds=o.EndDelaySeconds.Value;s.CaptureAudio=CaptureAudio;s.AudioGainDb=o.AudioGainDb.Value;s.ShowRenderPreview=ShowRenderPreview;s.BgaMode=BgaMode;
                s.ShowPlanetRings=ShowPlanetRings;s.ShowSongTitle=ShowSongTitle;s.ShowCountdown=ShowCountdown;s.ShowResultText=ShowResultText;s.ShowHitJudgments=ShowHitJudgments;
                s.Encoding=Encoding;s.Encoder=Encoder;s.Codec=Codec;s.BitDepth=BitDepth;s.OpenOutputFolder=OpenOutputFolder;s.OnChange(); }
            private void SetVideoValues(int w,int h,int f,int vf,int b){WidthText=w.ToString(CultureInfo.InvariantCulture);HeightText=h.ToString(CultureInfo.InvariantCulture);
                FpsText=f.ToString(CultureInfo.InvariantCulture);VideoFpsText=vf.ToString(CultureInfo.InvariantCulture);BitrateText=b.ToString(CultureInfo.InvariantCulture);}
            private static bool TryParseFloat(string value,out float result)=>float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)||float.TryParse(value,NumberStyles.Float,CultureInfo.CurrentCulture,out result);
        }
    }
}
