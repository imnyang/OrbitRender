using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using OrbitRender.Renderer;

namespace OrbitRender.UI
{
    // Runtime uGUI presentation for rendering, installer and confirmation UI.
    internal static class RendererWindow
    {
        private static GameObject canvasObject;
        private static GameObject backdropObject, panelObject;
        private static GameObject metrics;
        private static RawImage preview;
        private static Text state, title, detail, percent, frames, speed, eta, hint, path, elapsedLabel;
        private static Image progress;
        private static int mode;
        private static RenderState builtRenderState;
        private static bool builtShowProgress;
        private static int builtScreenWidth;

        internal static void Sync(RendererController renderer)
        {
            if (renderer == null || !Main.Enabled || ExportVideoDialog.IsOpen)
            {
                Hide();
                ExportVideoDialog.Refresh(renderer);
                return;
            }
            var nextMode = FfmpegInstaller.IsInstallPromptVisible ? 1
                : renderer.EncoderFallbackPending ? 2
                : renderer.Busy || renderer.ToastVisible ? 3 : 0;
            if (nextMode == 0) { Hide(); return; }
            if (canvasObject == null || mode != nextMode || builtScreenWidth != Screen.width
                || (nextMode == 3 && (builtRenderState != renderer.State
                    || builtShowProgress != HasProgress(renderer)))) Build(nextMode, renderer);
            if (nextMode == 1) UpdateFfmpeg();
            else if (nextMode == 3) UpdateRender(renderer);
        }

        internal static void Dispose()
        {
            if (canvasObject != null) UnityEngine.Object.Destroy(canvasObject);
            canvasObject = null; backdropObject = null; panelObject = null; metrics = null; preview = null;
            state = title = detail = percent = frames = speed = eta = hint = path = elapsedLabel = null;
            progress = null; mode = 0;
        }

        private static void Hide()
        {
            if (canvasObject != null) canvasObject.SetActive(false);
            mode = 0;
        }

        private static void Build(int nextMode, RendererController renderer)
        {
            Dispose();
            mode = nextMode;
            builtRenderState = renderer.State;
            builtShowProgress = HasProgress(renderer);
            builtScreenWidth = Screen.width;
            canvasObject = UguiFactory.Canvas("OrbitRender.RendererWindow", 32750);
            backdropObject = UguiFactory.Image(canvasObject.transform, "Backdrop", UguiFactory.Backdrop,
                nextMode != 3 || renderer.Busy);
            UguiFactory.Stretch(backdropObject.GetComponent<RectTransform>());
            if (nextMode == 3)
            {
                var previewObject = UguiFactory.New(canvasObject.transform, "RenderPreview", typeof(RawImage));
                preview = previewObject.GetComponent<RawImage>();
                preview.color = Color.white;
                preview.raycastTarget = false;
                UguiFactory.Stretch(preview.rectTransform);
                var fitter = previewObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                BuildRenderCard(renderer);
            }
            else if (nextMode == 2) BuildEncoderPrompt(renderer);
            else BuildFfmpegPrompt();
        }

        private static GameObject Card(float width, float height)
        {
            width = Mathf.Min(width, Mathf.Max(1f, Screen.width - 40f));
            var card = UguiFactory.Image(canvasObject.transform, "Card", UguiFactory.Surface, true);
            UguiFactory.Round(card.GetComponent<Image>(), true);
            UguiFactory.Anchor(card.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-width / 2f, -height / 2f), new Vector2(width / 2f, height / 2f));
            panelObject = card;
            return card;
        }

        private static Text At(Transform parent, string value, int size, float left, float top,
            float width, float height, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var text = UguiFactory.Text(parent, value, size, anchor, color);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
            return text;
        }

        private static Button AtButton(Transform parent, string value, Action action, bool primary,
            float left, float top, float width, float height)
        {
            var button = UguiFactory.Button(parent, value, action, primary);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
            return button;
        }

        private static void BuildRenderCard(RendererController renderer)
        {
            var live = renderer.State == RenderState.Rendering;
            var completed = renderer.State == RenderState.Completed;
            var card = Card(live ? 480f : 680f, completed ? 236f : builtShowProgress ? 274f : 176f);
            var rect = card.GetComponent<RectTransform>();
            if (live)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-24f, 24f);
            }
            var width = rect.rect.width - 48f;
            At(card.transform, "OrbitRender", 15, 24, 18, width * .55f, 18, UguiFactory.Accent).fontStyle = FontStyle.Bold;
            state = At(card.transform, string.Empty, 10, 24 + width * .55f, 18, width * .45f, 18,
                UguiFactory.Accent, TextAnchor.MiddleRight);
            state.fontStyle = FontStyle.Bold;
            title = At(card.transform, string.Empty, 13, 24, 43, width, 24, UguiFactory.Foreground);
            title.fontStyle = FontStyle.Bold;
            detail = At(card.transform, string.Empty, 11, 24, 68, width, 22, UguiFactory.Muted);
            elapsedLabel = At(card.transform, Localization.Get("time-spent"), 9, 24, 101, width, 16, UguiFactory.Muted);
            elapsedLabel.fontStyle = FontStyle.Bold;
            percent = At(card.transform, string.Empty, 30, 24, completed ? 117 : 96, width, 38,
                UguiFactory.Foreground, TextAnchor.MiddleLeft);
            percent.fontStyle = FontStyle.Bold;
            var track = UguiFactory.Image(card.transform, "Progress track", UguiFactory.Backdrop);
            UguiFactory.Anchor(track.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -146f), new Vector2(-24f, -137f));
            progress = UguiFactory.ProgressFill(track.transform);
            metrics = UguiFactory.New(card.transform, "Metrics", typeof(RectTransform));
            UguiFactory.Stretch(metrics.GetComponent<RectTransform>());
            var metricWidth = width / 3f;
            frames = Metric(0, Localization.Get("frames"), metricWidth);
            speed = Metric(1, Localization.Get("speed"), metricWidth);
            eta = Metric(2, Localization.Get("eta"), metricWidth);
            hint = At(card.transform, string.Empty, 10, 24, builtShowProgress ? 219 : 101,
                builtShowProgress ? width - 132 : width, builtShowProgress ? 22 : 42, UguiFactory.Muted);
            path = At(card.transform, string.Empty, 10, 24, 173, width - 118, 22, UguiFactory.Muted);
            AtButton(card.transform, Localization.Get("cancel-render"), renderer.Cancel, false,
                24 + width - 118, 216, 118, 28);
            AtButton(card.transform, Localization.Get("copy-path"), () => GUIUtility.systemCopyBuffer = renderer.OutputPath,
                false, 24 + width - 110, 170, 110, 28);
        }

        private static Text Metric(int index, string label, float width)
        {
            At(metrics.transform, label, 9, 24 + width * index, 159, width - 8, 16,
                UguiFactory.Muted).fontStyle = FontStyle.Bold;
            return At(metrics.transform, string.Empty, 11, 24 + width * index, 176, width - 8, 24,
                UguiFactory.Foreground);
        }

        private static bool HasProgress(RendererController renderer) => renderer.TotalFrames > 0
            && (renderer.State == RenderState.Rendering || renderer.State == RenderState.Finishing);

        private static void UpdateRender(RendererController renderer)
        {
            canvasObject.SetActive(true);
            var busyBackdrop = renderer.Busy;
            backdropObject.SetActive(busyBackdrop);
            preview.gameObject.SetActive(renderer.State == RenderState.Rendering && renderer.ShowPreviewForRun
                && renderer.RenderPreviewTexture != null);
            preview.texture = renderer.RenderPreviewTexture;
            if (renderer.RenderPreviewTexture != null)
                preview.GetComponent<AspectRatioFitter>().aspectRatio =
                    renderer.RenderPreviewTexture.width / (float)Mathf.Max(1, renderer.RenderPreviewTexture.height);
            panelObject.SetActive(renderer.ToastVisible || renderer.EncoderFallbackPending);
            if (!panelObject.activeSelf) return;
            state.text = StateLabel(renderer.State); state.color = StateColor(renderer.State);
            title.text = StateTitle(renderer.State);
            detail.text = renderer.Message ?? renderer.ToastText ?? string.Empty;
            var showProgress = HasProgress(renderer);
            var completed = renderer.State == RenderState.Completed;
            percent.gameObject.SetActive(showProgress || completed);
            progress.transform.parent.gameObject.SetActive(showProgress);
            metrics.SetActive(showProgress);
            elapsedLabel.gameObject.SetActive(completed);
            hint.gameObject.SetActive(showProgress || (!completed && !showProgress));
            path.gameObject.SetActive(completed && !string.IsNullOrEmpty(renderer.OutputPath));
            var buttons = panelObject.GetComponentsInChildren<Button>(true);
            if (buttons.Length > 0) buttons[0].gameObject.SetActive(showProgress && renderer.State == RenderState.Rendering);
            if (buttons.Length > 1) buttons[1].gameObject.SetActive(path.gameObject.activeSelf);
            if (showProgress)
            {
                UguiFactory.SetProgress(progress, (float)renderer.CapturedFrames / renderer.TotalFrames);
                percent.text = renderer.ProgressPercentText ?? string.Empty;
                frames.text = renderer.ProgressText ?? string.Empty;
                speed.text = renderer.SpeedText ?? string.Empty;
                eta.text = renderer.EtaText ?? string.Empty;
                hint.text = Localization.Get("hold-esc-for-1-second-to-cancel");
            }
            else if (completed)
            {
                percent.text = FormatElapsed(renderer.ElapsedSeconds);
                path.text = CompactPath(renderer.OutputPath);
            }
            else hint.text = Localization.Get("the-render-window-will-update-when-the-next-stage-is-re");
        }

        private static void BuildEncoderPrompt(RendererController renderer)
        {
            var card = Card(760f, 230f);
            var width = card.GetComponent<RectTransform>().rect.width - 48f;
            At(card.transform, Localization.Get("encoder-confirmation"), 15, 24, 18, width, 24,
                UguiFactory.Accent).fontStyle = FontStyle.Bold;
            At(card.transform, Localization.Get("the-selected-hardware-encoder-could-not-be-initialized"), 13,
                24, 52, width, 38, UguiFactory.Foreground).fontStyle = FontStyle.Bold;
            At(card.transform, renderer.EncoderFallbackReason ?? string.Empty, 11, 24, 94, width, 54, UguiFactory.Muted);
            At(card.transform, Localization.Get("use-software-encoder-for-this-render-your-saved-encoder"), 11,
                24, 150, width, 28, UguiFactory.Muted);
            AtButton(card.transform, Localization.Get("use-software-and-continue"), renderer.ConfirmEncoderFallback,
                true, 24, 188, 310, 34);
            AtButton(card.transform, Localization.Get("cancel-render"), renderer.RejectEncoderFallback,
                false, 350, 188, 160, 34);
        }

        private static void BuildFfmpegPrompt()
        {
            var card = Card(760f, FfmpegInstaller.IsDownloading ? 270f : 238f);
            var width = card.GetComponent<RectTransform>().rect.width - 48f;
            state = At(card.transform, string.Empty, 15, 24, 18, width, 24, UguiFactory.Accent);
            state.fontStyle = FontStyle.Bold;
            title = At(card.transform, string.Empty, 13, 24, 52, width, 40, UguiFactory.Foreground);
            title.fontStyle = FontStyle.Bold;
            detail = At(card.transform, string.Empty, 11, 24, 100, width, 52, UguiFactory.Muted);
            var track = UguiFactory.Image(card.transform, "Progress track", UguiFactory.Backdrop);
            UguiFactory.Anchor(track.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -174f), new Vector2(-24f, -162f));
            progress = UguiFactory.ProgressFill(track.transform);
            percent = At(card.transform, string.Empty, 11, 24, 182, width * .52f, 20, UguiFactory.Muted);
            path = At(card.transform, string.Empty, 11, 24 + width * .52f, 182, width * .48f, 20, UguiFactory.Muted);
            AtButton(card.transform, Localization.Get("install-ffmpeg"), FfmpegInstaller.ConfirmInstall, true, 24, 176, 310, 34);
            AtButton(card.transform, Localization.Get("not-now"), FfmpegInstaller.DeclineInstall, false, 350, 176, 160, 34);
        }

        private static void UpdateFfmpeg()
        {
            canvasObject.SetActive(true);
            var downloading = FfmpegInstaller.IsDownloading;
            var waiting = FfmpegInstaller.IsAwaitingConsent;
            panelObject.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                downloading ? 270f : 238f);
            state.text = waiting ? Localization.Get("ffmpeg-installation") : Localization.Get("installing-ffmpeg");
            title.text = waiting ? Localization.Get("orbitrender-needs-ffmpeg-to-export-videos-download-the")
                : Localization.Get("downloading-ffmpeg-for-this-platform-the-renderer-will");
            detail.text = waiting ? Localization.Get("the-download-comes-from-the-ffmpeg-build-provider-and-i")
                : FfmpegInstaller.StatusMessage;
            progress.transform.parent.gameObject.SetActive(downloading);
            percent.gameObject.SetActive(downloading); path.gameObject.SetActive(downloading);
            var buttons = panelObject.GetComponentsInChildren<Button>(true);
            foreach (var button in buttons) button.gameObject.SetActive(waiting);
            if (!downloading) return;
            UguiFactory.SetProgress(progress, FfmpegInstaller.HasDownloadSize ? (float)FfmpegInstaller.Progress
                : Mathf.PingPong(Time.unscaledTime, 1f));
            percent.text = FfmpegInstaller.HasDownloadSize
                ? Localization.FormatWithCurrentCulture("ffmpeg-progress-percent", FfmpegInstaller.Progress * 100d)
                : Localization.Get("ffmpeg-progress-waiting-for-size");
            path.text = FormatFfmpegDownloadSize();
        }

        private static string StateLabel(RenderState value)
        { switch (value) { case RenderState.Preparing:return Localization.Get("preparing"); case RenderState.Rendering:return Localization.Get("rendering");
            case RenderState.Finishing:return Localization.Get("finalizing"); case RenderState.Completed:return Localization.Get("completed");
            case RenderState.Cancelled:return Localization.Get("cancelled"); case RenderState.Failed:return Localization.Get("failed"); default:return Localization.Get("status"); } }
        private static string StateTitle(RenderState value)
        { switch (value) { case RenderState.Preparing:return Localization.Get("preparing-your-export"); case RenderState.Rendering:return Localization.Get("rendering-video");
            case RenderState.Finishing:return Localization.Get("finishing-video"); case RenderState.Completed:return Localization.Get("export-complete");
            case RenderState.Cancelled:return Localization.Get("render-cancelled-status"); case RenderState.Failed:return Localization.Get("render-failed-status"); default:return Localization.Get("render-status"); } }
        private static Color StateColor(RenderState value) => value == RenderState.Completed ? new Color(.55f,.9f,.73f)
            : value == RenderState.Failed || value == RenderState.Cancelled ? new Color(.94f,.76f,.48f) : UguiFactory.Accent;
        private static string CompactPath(string value) => string.IsNullOrEmpty(value) ? string.Empty : Localization.Format("output-file", Path.GetFileName(value));
        private static string FormatElapsed(double seconds)
        { if(double.IsNaN(seconds)||double.IsInfinity(seconds))return "—"; var d=TimeSpan.FromSeconds(Math.Max(0d,seconds));
            return d.TotalHours>=1d?Localization.Format("duration-hours",(int)d.TotalHours,d.Minutes,d.Seconds):Localization.Format("duration-minutes",d.Minutes,d.Seconds); }
        private static string FormatFfmpegDownloadSize() => !FfmpegInstaller.HasDownloadSize
            ? Localization.Get("ffmpeg-download-size-unknown") : Localization.FormatWithCurrentCulture("ffmpeg-download-size",
                FfmpegInstaller.DownloadedBytes/(1024d*1024d),FfmpegInstaller.DownloadTotalBytes/(1024d*1024d));
    }
}
