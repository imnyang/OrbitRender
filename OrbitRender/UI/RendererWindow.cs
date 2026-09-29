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
        private static RawImage preview;
        private static Text state, title, detail, percent, frames, speed, eta, hint, path;
        private static Image progress;
        private static int mode;
        private static RenderState builtRenderState;

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
            if (canvasObject == null || mode != nextMode
                || (nextMode == 3 && builtRenderState != renderer.State)) Build(nextMode, renderer);
            if (nextMode == 1) UpdateFfmpeg();
            else if (nextMode == 3) UpdateRender(renderer);
        }

        internal static void Dispose()
        {
            if (canvasObject != null) UnityEngine.Object.Destroy(canvasObject);
            canvasObject = null; backdropObject = null; panelObject = null; preview = null;
            state = title = detail = percent = frames = speed = eta = hint = path = null;
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
            var card = UguiFactory.Image(canvasObject.transform, "Card", UguiFactory.Surface, true);
            UguiFactory.Anchor(card.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-width / 2f, -height / 2f), new Vector2(width / 2f, height / 2f));
            panelObject = card;
            return card;
        }

        private static Text At(Transform parent, string value, int size, float left, float top,
            float width, float height, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
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
            var card = Card(live ? 500f : 700f, renderer.State == RenderState.Completed ? 250f : 290f);
            var rect = card.GetComponent<RectTransform>();
            if (live) { rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(-24f, -24f); }
            At(card.transform, "OrbitRender", 24, 24, 16, 300, 28, UguiFactory.Accent);
            state = At(card.transform, string.Empty, 17, live ? 320 : 500, 16, live ? 156 : 176, 28,
                UguiFactory.Accent, TextAnchor.MiddleRight);
            title = At(card.transform, string.Empty, 23, 24, 50, live ? 452 : 652, 32, UguiFactory.Foreground);
            detail = At(card.transform, string.Empty, 18, 24, 83, live ? 452 : 652, 34, UguiFactory.Muted);
            percent = At(card.transform, string.Empty, 42, 24, 119, 300, 50, UguiFactory.Foreground);
            var track = UguiFactory.Image(card.transform, "Progress track", UguiFactory.Backdrop);
            UguiFactory.Anchor(track.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -180f), new Vector2(-24f, -169f));
            progress = UguiFactory.Image(track.transform, "Progress", UguiFactory.Accent).GetComponent<Image>();
            progress.type = Image.Type.Filled; progress.fillMethod = Image.FillMethod.Horizontal; progress.fillOrigin = 0;
            UguiFactory.Stretch(progress.rectTransform);
            frames = At(card.transform, string.Empty, 16, 24, 190, live ? 145 : 210, 42, UguiFactory.Foreground);
            speed = At(card.transform, string.Empty, 16, live ? 178 : 246, 190, live ? 145 : 210, 42, UguiFactory.Foreground);
            eta = At(card.transform, string.Empty, 16, live ? 332 : 468, 190, live ? 145 : 208, 42, UguiFactory.Foreground);
            hint = At(card.transform, string.Empty, 15, 24, 238, live ? 316 : 450, 32, UguiFactory.Muted);
            path = At(card.transform, string.Empty, 16, 24, 190, live ? 300 : 500, 35, UguiFactory.Muted);
            AtButton(card.transform, Localization.Get("cancel-render"), renderer.Cancel, false,
                live ? 352 : 538, 238, 124, 36);
            AtButton(card.transform, Localization.Get("copy-path"), () => GUIUtility.systemCopyBuffer = renderer.OutputPath,
                false, live ? 352 : 538, 190, 124, 36);
        }

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
            var showProgress = renderer.TotalFrames > 0 && (renderer.State == RenderState.Rendering || renderer.State == RenderState.Finishing);
            var completed = renderer.State == RenderState.Completed;
            percent.gameObject.SetActive(showProgress || completed);
            progress.transform.parent.gameObject.SetActive(showProgress);
            frames.gameObject.SetActive(showProgress); speed.gameObject.SetActive(showProgress); eta.gameObject.SetActive(showProgress);
            hint.gameObject.SetActive(showProgress || (!completed && !showProgress));
            path.gameObject.SetActive(completed && !string.IsNullOrEmpty(renderer.OutputPath));
            var buttons = panelObject.GetComponentsInChildren<Button>(true);
            if (buttons.Length > 0) buttons[0].gameObject.SetActive(showProgress && renderer.State == RenderState.Rendering);
            if (buttons.Length > 1) buttons[1].gameObject.SetActive(path.gameObject.activeSelf);
            if (showProgress)
            {
                progress.fillAmount = Mathf.Clamp01((float)renderer.CapturedFrames / renderer.TotalFrames);
                percent.text = renderer.ProgressPercentText ?? string.Empty;
                frames.text = Localization.Get("frames") + "\n" + (renderer.ProgressText ?? string.Empty);
                speed.text = Localization.Get("speed") + "\n" + (renderer.SpeedText ?? string.Empty);
                eta.text = Localization.Get("eta") + "\n" + (renderer.EtaText ?? string.Empty);
                hint.text = Localization.Get("hold-esc-for-1-second-to-cancel");
            }
            else if (completed)
            {
                percent.text = Localization.Get("time-spent") + "  " + FormatElapsed(renderer.ElapsedSeconds);
                path.text = CompactPath(renderer.OutputPath);
            }
            else hint.text = Localization.Get("the-render-window-will-update-when-the-next-stage-is-re");
        }

        private static void BuildEncoderPrompt(RendererController renderer)
        {
            var card = Card(760f, 250f);
            At(card.transform, Localization.Get("encoder-confirmation"), 25, 24, 18, 712, 30, UguiFactory.Accent);
            At(card.transform, Localization.Get("the-selected-hardware-encoder-could-not-be-initialized"), 20,
                24, 55, 712, 36, UguiFactory.Foreground);
            At(card.transform, renderer.EncoderFallbackReason ?? string.Empty, 17, 24, 94, 712, 48, UguiFactory.Muted);
            At(card.transform, Localization.Get("use-software-encoder-for-this-render-your-saved-encoder"), 17,
                24, 143, 712, 32, UguiFactory.Muted);
            AtButton(card.transform, Localization.Get("use-software-and-continue"), renderer.ConfirmEncoderFallback,
                true, 24, 190, 320, 38);
            AtButton(card.transform, Localization.Get("cancel-render"), renderer.RejectEncoderFallback,
                false, 360, 190, 170, 38);
        }

        private static void BuildFfmpegPrompt()
        {
            var card = Card(760f, 270f);
            state = At(card.transform, string.Empty, 25, 24, 18, 712, 30, UguiFactory.Accent);
            title = At(card.transform, string.Empty, 20, 24, 55, 712, 44, UguiFactory.Foreground);
            detail = At(card.transform, string.Empty, 17, 24, 100, 712, 54, UguiFactory.Muted);
            var track = UguiFactory.Image(card.transform, "Progress track", UguiFactory.Backdrop);
            UguiFactory.Anchor(track.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -178f), new Vector2(-24f, -164f));
            progress = UguiFactory.Image(track.transform, "Progress", UguiFactory.Accent).GetComponent<Image>();
            progress.type = Image.Type.Filled; progress.fillMethod = Image.FillMethod.Horizontal; UguiFactory.Stretch(progress.rectTransform);
            percent = At(card.transform, string.Empty, 16, 24, 184, 350, 28, UguiFactory.Muted);
            path = At(card.transform, string.Empty, 16, 386, 184, 350, 28, UguiFactory.Muted, TextAnchor.MiddleRight);
            AtButton(card.transform, Localization.Get("install-ffmpeg"), FfmpegInstaller.ConfirmInstall, true, 24, 212, 310, 38);
            AtButton(card.transform, Localization.Get("not-now"), FfmpegInstaller.DeclineInstall, false, 350, 212, 170, 38);
        }

        private static void UpdateFfmpeg()
        {
            canvasObject.SetActive(true);
            var downloading = FfmpegInstaller.IsDownloading;
            var waiting = FfmpegInstaller.IsAwaitingConsent;
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
            progress.fillAmount = FfmpegInstaller.HasDownloadSize ? Mathf.Clamp01((float)FfmpegInstaller.Progress)
                : Mathf.PingPong(Time.unscaledTime, 1f);
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
