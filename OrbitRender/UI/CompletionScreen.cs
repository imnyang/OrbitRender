using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.EventSystems;
using OrbitRender.Renderer;

namespace OrbitRender.UI
{
    internal static class CompletionScreen
    {
        private static VideoPlayer player;
        private static RawImage video;
        private static AspectRatioFitter aspect;
        private static Slider seek;
        private static Text playLabel, clock, status, frameLabel;
        private static Button playButton;
        private static Button fallbackButton;
        private static Image[] fullscreenStrokes;
        private static AudioSource audio;
        private static GameObject root;
        private static bool updatingSeek, firstFramePending, seeking, seekQueued;
        private static double seekTarget;
        private static long frameTarget;
        private static bool seekByFrame;
        private static int heldDirection, dismissedFrame = -1;
        private static float repeatAt;
        private static GameObject playerShell;
        private static RectTransform viewportRect, controlsRect;
        private static CanvasGroup controlsGroup;
        private static Image controlsBackground;
        private static float controlsReveal;
        private static bool holdingControls;
        private static bool expanded;
        private static float layoutWidth, layoutHeight;
        private static readonly Color Background = new Color(.055f, .05f, .068f);
        private static readonly Color Surface = new Color(.10f, .09f, .12f);
        private static readonly Color Muted = new Color(.57f, .55f, .62f);
        private static readonly Color Success = new Color(.55f, .87f, .70f);

        // Keep the closing key blocked for its entire frame, including game updates
        // that run after the player has dismissed this screen.
        internal static bool BlocksInput => (root != null && root.activeInHierarchy && Main.Enabled)
            || dismissedFrame == Time.frameCount;

        internal static void Build(GameObject canvas, RendererController renderer)
        {
            var scale = Mathf.Max(.1f, Mathf.Min(Screen.height / 900f, Screen.width / 1280f));
            canvas.GetComponent<CanvasScaler>().scaleFactor = scale;
            var width = Screen.width / scale;
            var height = Screen.height / scale;
            layoutWidth = width;
            layoutHeight = height;
            const float sidebar = UiLayout.CompletionSidebarWidth;
            root = UguiFactory.Image(canvas.transform, "Completion", Background, true);
            root.AddComponent<CompletionInputFocus>();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            UguiFactory.Stretch(root.GetComponent<RectTransform>());
            var side = UguiFactory.Image(root.transform, "Sidebar", Surface);
            UguiFactory.Anchor(side.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0, 1),
                Vector2.zero, new Vector2(sidebar, 0));
            Label(side.transform, "OrbitRender", 28, 28, sidebar - 56, 24, 18).fontStyle = FontStyle.Bold;
            Label(side.transform, "EXPORT", 28, 92, sidebar - 56, 20, 11, Muted).fontStyle = FontStyle.Bold;
            var badge = UguiFactory.Image(side.transform, "Success badge", new Color(.15f, .24f, .20f));
            UguiFactory.Round(badge.GetComponent<Image>());
            Place(badge.GetComponent<RectTransform>(), 28, 128, 34, 34);
            Label(badge.transform, "✓", 0, 0, 34, 34, 19, Success).alignment = TextAnchor.MiddleCenter;
            Label(side.transform, Localization.Get("export-complete"), 28, 178, sidebar - 56, 86, 28)
                .fontStyle = FontStyle.Bold;
            Rule(side.transform, 28, 294, sidebar - 56);
            Label(side.transform, Localization.Get("time-spent"), 28, 320, sidebar - 56, 20, 11, Muted);
            Label(side.transform, Duration(renderer.ElapsedSeconds), 28, 346, sidebar - 56, 40, 30)
                .fontStyle = FontStyle.Bold;
            Label(side.transform, Localization.Get("frames"), 28, 416, sidebar - 56, 20, 11, Muted);
            Label(side.transform, renderer.CapturedFrames.ToString("N0"), 28, 442, sidebar - 56, 28, 20);
            var shortcuts = Label(side.transform, Localization.Get("completion-shortcuts").Replace("\\n", "\n"),
                28, height - 270, sidebar - 56, 168, 12, Muted);
            shortcuts.lineSpacing = 1.35f;
            TextButton(side.transform, Localization.Get("completion-back"), () => Dismiss(renderer),
                28, height - 70, sidebar - 56, 42);

            var mainLeft = sidebar + 16;
            var mainWidth = width - mainLeft - 16;

            var shell = UguiFactory.Image(root.transform, "Player shell", Surface);
            playerShell = shell;
            var viewport = UguiFactory.Image(shell.transform, "Video viewport", Color.black, true);
            viewportRect = viewport.GetComponent<RectTransform>();
            var image = UguiFactory.New(viewport.transform, "Video", typeof(RawImage));
            video = image.GetComponent<RawImage>();
            video.raycastTarget = false;
            video.color = Color.white;
            UguiFactory.Stretch(video.rectTransform);
            aspect = image.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 16f / 9f;
            var click = viewport.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            DisableNavigation(click);
            click.onClick.AddListener(TogglePlayback);
            status = UguiFactory.Text(viewport.transform, Localization.Get("completion-loading"), 16,
                TextAnchor.MiddleCenter, Muted);
            UguiFactory.SetOffsets(status.rectTransform, 32, 20, 32, 20);
            fallbackButton = TextButton(viewport.transform, Localization.Get("completion-open-video"),
                () => Application.OpenURL(new Uri(renderer.OutputPath).AbsoluteUri), 0, 0, 160, 36);
            UguiFactory.Anchor(fallbackButton.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(-80, -70), new Vector2(80, -34));
            fallbackButton.gameObject.SetActive(false);
            frameLabel = UguiFactory.Text(viewport.transform, "", 12, TextAnchor.MiddleRight, UguiFactory.Foreground);
            UguiFactory.Anchor(frameLabel.rectTransform, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-200, 12), new Vector2(-16, 36));

            var controls = UguiFactory.Image(shell.transform, "Playback controls", Surface, true);
            controlsRect = controls.GetComponent<RectTransform>();
            controlsRect.pivot = new Vector2(.5f, 0f);
            controlsBackground = controls.GetComponent<Image>();
            controlsGroup = controls.AddComponent<CanvasGroup>();
            UguiFactory.Anchor(controlsRect, Vector2.zero, new Vector2(1, 0),
                Vector2.zero, new Vector2(0, UiLayout.CompletionControlsHeight));
            playButton = TextButton(controls.transform, "▶", TogglePlayback, 12, 10, 36, 32);
            playLabel = playButton.GetComponent<Text>();
            playLabel.fontSize = 18;
            playLabel.alignment = TextAnchor.MiddleCenter;
            playButton.interactable = false;
            clock = Label(controls.transform, "0:00/0:00", 0, 0, 124, 32, 13, Muted);
            UguiFactory.Anchor(clock.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-180, -16), new Vector2(-56, 16));
            clock.alignment = TextAnchor.MiddleRight;
            seek = UguiFactory.Slider(controls.transform, 0f, 1f, 0f, value => {
                if (!updatingSeek && player != null) SeekTo(value * player.length);
            });
            UguiFactory.Anchor(seek.GetComponent<RectTransform>(), new Vector2(0, .5f), new Vector2(1, .5f),
                new Vector2(60, -16), new Vector2(-196, 16));
            DisableNavigation(seek);
            seek.interactable = false;
            var fullscreen = UguiFactory.Button(controls.transform, string.Empty, ToggleExpanded);
            fullscreen.gameObject.name = "Fullscreen (F)";
            fullscreen.GetComponent<Image>().color = Surface;
            DisableNavigation(fullscreen);
            UguiFactory.Anchor(fullscreen.GetComponent<RectTransform>(), new Vector2(1, .5f), new Vector2(1, .5f),
                new Vector2(-48, -16), new Vector2(-12, 16));
            fullscreenStrokes = new Image[8];
            for (var i = 0; i < fullscreenStrokes.Length; i++)
                fullscreenStrokes[i] = UguiFactory.Image(fullscreen.transform, "Fullscreen icon stroke", UguiFactory.Foreground)
                    .GetComponent<Image>();
            UpdateFullscreenIcon();
            LayoutPlayer();

            var filename = Label(root.transform, System.IO.Path.GetFileName(renderer.OutputPath),
                mainLeft, height - 44, mainWidth - 192, 32, 14, Muted);
            filename.alignment = TextAnchor.MiddleLeft;
            filename.resizeTextForBestFit = true;
            filename.resizeTextMinSize = 11;
            filename.resizeTextMaxSize = 14;
            TextButton(root.transform, Localization.Get("completion-open-folder"), renderer.OpenOutputFolder,
                width - 184, height - 44, 168, 32).GetComponent<Text>().alignment = TextAnchor.MiddleRight;

            player = root.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = false;
            player.renderMode = VideoRenderMode.APIOnly;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.ignoreListenerPause = true;
            audio.mute = true;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.source = VideoSource.Url;
            player.url = new Uri(renderer.OutputPath).AbsoluteUri;
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
            player.SetTargetAudioSource(0, audio);
            player.prepareCompleted += Prepared;
            player.errorReceived += PlaybackError;
            player.seekCompleted += SeekCompleted;
            try { player.Prepare(); }
            catch (Exception ex) { PlaybackError(player, ex.Message); }
        }

        private static void Prepared(VideoPlayer source)
        {
            if (source != player) return;
            aspect.aspectRatio = source.width / (float)Math.Max(1u, source.height);
            status.gameObject.SetActive(false);
            playButton.interactable = true;
            seek.interactable = source.canSetTime && source.length > 0;
            // Decode the poster frame without playing sound or starting playback.
            firstFramePending = true;
            source.sendFrameReadyEvents = true;
            source.frameReady += FirstFrame;
            source.Play();
        }

        private static void FirstFrame(VideoPlayer source, long frame)
        {
            if (source != player) return;
            source.Pause();
            FinishFirstFrame(source);
        }

        private static void FinishFirstFrame(VideoPlayer source)
        {
            if (!firstFramePending) return;
            firstFramePending = false;
            source.frameReady -= FirstFrame;
            source.sendFrameReadyEvents = false;
            audio.mute = false;
        }

        private static void PlaybackError(VideoPlayer source, string message)
        {
            if (source != player) return;
            FinishFirstFrame(source);
            seeking = seekQueued = false;
            source.Stop();
            video.texture = null;
            status.gameObject.SetActive(true);
            status.text = Localization.Get("completion-preview-unavailable");
            fallbackButton.gameObject.SetActive(true);
            playButton.interactable = seek.interactable = false;
            Main.Entry.Logger.Log("Completed video preview unavailable: " + message);
        }

        private static void TogglePlayback()
        {
            if (player == null || !player.isPrepared) return;
            seekByFrame = false;
            var wasPriming = firstFramePending;
            FinishFirstFrame(player);
            if (player.isPlaying && !wasPriming) player.Pause();
            else
            {
                if (!seeking && player.length > 0 && player.time >= EndTime()) SeekTo(0);
                player.Play();
            }
        }

        private static double EndTime() => player.frameCount > 0 && player.frameRate > 0
            ? (player.frameCount - 1d) / player.frameRate
            : Math.Max(0, player.length - 1d / Math.Max(1d, player.frameRate));

        private static void SeekTo(double seconds)
        {
            if (player == null || !player.isPrepared || !player.canSetTime || player.length <= 0) return;
            if (firstFramePending) { player.Pause(); FinishFirstFrame(player); }
            seekTarget = Math.Max(0, Math.Min(EndTime(), seconds));
            seekByFrame = false;
            // VideoPlayer seeks asynchronously. Coalesce fast key repeats and drags
            // instead of computing every new position from the stale decoder clock.
            if (seeking) { seekQueued = true; return; }
            seeking = true;
            player.time = seekTarget;
        }

        private static void StepFrame(int direction)
        {
            if (player == null || !player.isPrepared || player.isPlaying || !player.canSetTime
                || player.frameRate <= 0 || player.frameCount == 0) return;
            var current = seeking ? (long)Math.Round(seekTarget * player.frameRate) : Math.Max(0, player.frame);
            var last = (long)Math.Min((ulong)long.MaxValue, player.frameCount - 1);
            frameTarget = Math.Max(0, Math.Min(last, current + direction));
            if (!seeking && frameTarget == player.frame) return;
            seekTarget = frameTarget / (double)player.frameRate;
            seekByFrame = true;
            if (seeking) { seekQueued = true; return; }
            seeking = true;
            // Seek by decoded frame index, not an approximate time offset.
            player.frame = frameTarget;
        }

        private static void SeekCompleted(VideoPlayer source)
        {
            if (source != player) return;
            if (seekQueued)
            {
                seekQueued = false;
                if (seekByFrame)
                {
                    if (source.frame == frameTarget) seeking = false;
                    else source.frame = frameTarget;
                }
                else source.time = seekTarget;
            }
            else seeking = false;
        }

        private static void Dismiss(RendererController renderer)
        {
            dismissedFrame = Time.frameCount;
            renderer.DismissCompletion();
            if (root != null) root.SetActive(false);
            if (player != null) player.Pause();
        }

        private static void ToggleExpanded()
        {
            if (root == null || playerShell == null) return;
            expanded = !expanded;
            foreach (Transform child in root.transform)
                if (child.gameObject != playerShell) child.gameObject.SetActive(!expanded);
            LayoutPlayer();
            UpdateFullscreenIcon();
        }

        private static void UpdateFullscreenIcon()
        {
            if (fullscreenStrokes == null) return;
            // Four outward corners for enter, four inward corners for exit.
            // Build the icon from quads so it does not depend on font glyphs.
            var corner = expanded ? 4f : 9f;
            for (var i = 0; i < 4; i++)
            {
                var x = (i & 1) == 0 ? -1f : 1f;
                var y = (i & 2) == 0 ? -1f : 1f;
                UguiFactory.Anchor(fullscreenStrokes[i * 2].rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(Mathf.Min(x * 4, x * 9) - 1, y * corner - 1),
                    new Vector2(Mathf.Max(x * 4, x * 9) + 1, y * corner + 1));
                UguiFactory.Anchor(fullscreenStrokes[i * 2 + 1].rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new Vector2(x * corner - 1, Mathf.Min(y * 4, y * 9) - 1),
                    new Vector2(x * corner + 1, Mathf.Max(y * 4, y * 9) + 1));
            }
        }

        private static void LayoutPlayer()
        {
            var layout = UiLayout.CompletionPlayer(layoutWidth, layoutHeight, expanded);
            if (expanded)
            {
                // The controls overlay the full-size video; showing them must
                // never shrink or move the picture.
                UguiFactory.Stretch(playerShell.GetComponent<RectTransform>());
                playerShell.GetComponent<Image>().color = Color.black;
                Place(viewportRect, layout.Left, layout.Top, layout.Width, layout.VideoHeight);
                controlsBackground.color = new Color(0f, 0f, 0f, .82f);
            }
            else
            {
                Place(playerShell.GetComponent<RectTransform>(), layout.Left, layout.Top,
                    layout.Width, layout.VideoHeight + UiLayout.CompletionControlsHeight);
                playerShell.GetComponent<Image>().color = Surface;
                UguiFactory.SetOffsets(viewportRect, 0, UiLayout.CompletionControlsHeight, 0, 0);
                controlsBackground.color = Surface;
            }
            holdingControls = false;
            controlsReveal = expanded ? 0 : 1;
            ApplyControlsPresentation();
        }

        // Run before uGUI's input module so hidden controls cannot intercept a
        // click, and a held slider drag keeps the overlay visible off the bar.
        internal static void UpdateControlsPresentation()
        {
            if (controlsRect == null || root == null || !root.activeInHierarchy) return;
            if (!expanded) return;
            var pointer = (Vector2)Input.mousePosition;
            var overBottom = RectTransformUtility.RectangleContainsScreenPoint(root.GetComponent<RectTransform>(), pointer, null)
                && pointer.y <= (UiLayout.CompletionControlsHeight + 24f) * Screen.height / layoutHeight;
            if (Input.GetMouseButtonDown(0) && overBottom) holdingControls = true;
            if (!Input.GetMouseButton(0)) holdingControls = false;
            controlsReveal = Mathf.MoveTowards(controlsReveal, overBottom || holdingControls ? 1 : 0,
                Time.unscaledDeltaTime / .18f);
            ApplyControlsPresentation();
        }

        private static void ApplyControlsPresentation()
        {
            var eased = controlsReveal * controlsReveal * (3f - 2f * controlsReveal);
            controlsRect.anchoredPosition = new Vector2(0, -(1 - eased) * UiLayout.CompletionControlsHeight);
            controlsGroup.alpha = eased;
            controlsGroup.blocksRaycasts = controlsReveal > .05f;
            controlsGroup.interactable = controlsReveal > .05f;
        }

        private static void HandleKeyboard(RendererController renderer)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (expanded) ToggleExpanded();
                else Dismiss(renderer);
                return;
            }
            if (Input.GetKeyDown(KeyCode.F)) ToggleExpanded();
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.K)) TogglePlayback();
            if (player == null || !player.isPrepared) return;
            if (Input.GetKeyDown(KeyCode.Home)) SeekTo(0);
            if (Input.GetKeyDown(KeyCode.End)) SeekTo(EndTime());
            for (var digit = 0; digit <= 9; digit++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + digit))
                    || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + digit)))
                    SeekTo(player.length * digit / 10d);

            var direction = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            var step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 10d : 5d;
            var action = 1;
            var jump = (Input.GetKey(KeyCode.L) ? 1 : 0) - (Input.GetKey(KeyCode.J) ? 1 : 0);
            if (jump != 0) { direction = jump; step = 10d; action = 2; }
            var frame = (Input.GetKey(KeyCode.Period) ? 1 : 0) - (Input.GetKey(KeyCode.Comma) ? 1 : 0);
            if (frame != 0 && !player.isPlaying && !firstFramePending) { direction = frame; action = 3; }
            if (direction == 0) { heldDirection = 0; return; }
            var heldAction = direction * action;
            var now = Time.unscaledTime;
            if (heldAction != heldDirection || now >= repeatAt)
            {
                if (action == 3) StepFrame(direction);
                else SeekTo((seeking ? seekTarget : player.time) + direction * step);
                repeatAt = now + (heldAction != heldDirection ? .4f : action == 3 ? .08f : .15f);
                heldDirection = heldAction;
            }
        }

        internal static void Sync(RendererController renderer)
        {
            HandleKeyboard(renderer);
            if (!renderer.CompletionVisible) return;
            if (player == null || !player.isPrepared) return;
            video.texture = player.texture;
            playLabel.text = player.isPlaying && !firstFramePending ? "Ⅱ" : "▶";
            var position = seeking ? seekTarget : player.time;
            clock.text = Duration(position) + "/" + Duration(player.length);
            frameLabel.gameObject.SetActive(seekByFrame && !player.isPlaying && !firstFramePending && player.frame >= 0);
            if (frameLabel.gameObject.activeSelf)
                frameLabel.text = Localization.Format("completion-frame", seeking && seekByFrame ? frameTarget + 1 : player.frame + 1);
            updatingSeek = true;
            try { seek.value = player.length > 0 ? (float)(position / player.length) : 0f; }
            finally { updatingSeek = false; }
        }

        internal static void Dispose()
        {
            if (root != null) root.SetActive(false);
            if (player != null)
            {
                player.prepareCompleted -= Prepared;
                player.errorReceived -= PlaybackError;
                player.frameReady -= FirstFrame;
                player.seekCompleted -= SeekCompleted;
                player.Stop();
            }
            player = null; audio = null; root = playerShell = null;
            video = null; aspect = null; seek = null;
            playLabel = clock = status = frameLabel = null; playButton = fallbackButton = null;
            fullscreenStrokes = null;
            viewportRect = controlsRect = null;
            controlsGroup = null; controlsBackground = null;
            controlsReveal = 0; holdingControls = false;
            firstFramePending = seeking = seekQueued = seekByFrame = updatingSeek = false;
            heldDirection = 0;
            expanded = false;
        }

        private static string Duration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return time.TotalHours >= 1 ? string.Format("{0}:{1:D2}:{2:D2}", (int)time.TotalHours, time.Minutes, time.Seconds)
                : string.Format("{0}:{1:D2}", (int)time.TotalMinutes, time.Seconds);
        }

        private static Text Label(Transform parent, string value, float left, float top, float width, float height,
            int size = 16, Color? color = null)
        {
            var label = UguiFactory.Text(parent, value.Replace("\\n", "\n"), size, TextAnchor.UpperLeft, color ?? UguiFactory.Foreground);
            Place(label.rectTransform, left, top, width, height);
            return label;
        }

        private static Button TextButton(Transform parent, string caption, Action action,
            float left, float top, float width, float height)
        {
            var button = UguiFactory.TextButton(parent, caption, action);
            button.GetComponent<Text>().fontSize = 14;
            Place(button.GetComponent<RectTransform>(), left, top, width, height);
            var colors = button.colors;
            colors.normalColor = UguiFactory.Foreground;
            button.colors = colors;
            DisableNavigation(button);
            return button;
        }

        private static void DisableNavigation(Selectable selectable)
        {
            // Global player shortcuts own Space and arrows, even after a mouse click.
            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        private static void Rule(Transform parent, float left, float top, float width)
        {
            var rule = UguiFactory.Image(parent, "Divider", new Color(.20f, .18f, .23f));
            Place(rule.GetComponent<RectTransform>(), left, top, width, 1);
        }

        private static void Place(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }
    }

    // Clear selection before uGUI processes Submit/Move. Otherwise Space can
    // both activate the last clicked button and toggle playback, and arrows can
    // change the focused slider in addition to seeking by the requested amount.
    [DefaultExecutionOrder(-32000)]
    internal sealed class CompletionInputFocus : MonoBehaviour
    {
        private void Update()
        {
            CompletionScreen.UpdateControlsPresentation();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
