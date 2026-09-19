using System.IO;
using UnityEngine;
using OrbitRender.Renderer;

namespace OrbitRender.UI
{
    internal static class RendererWindow
    {
        private static bool initialized;
        private static GUIStyle panel, panelBorder;
        private static GUIStyle title, state, message, detail, percent, metricLabel, metricValue, hint, path;
        private static Texture2D backdrop, border, background, progressTrack, progressFill, divider;

        // Renderer UI palette supplied by the user.
        private static readonly Color DarkBackground = Hsl(315f, 21f, 8f);
        private static readonly Color Foreground = Hsl(0f, 0f, 98f);
        private static readonly Color DarkMuted = Hsl(296f, 18f, 15f);
        private static readonly Color MutedForeground = Hsl(240f, 5f, 68f);
        private static readonly Color DarkBorder = Hsl(296f, 18f, 15f);
        private static readonly Color Ring = Hsl(240f, 4.9f, 83.9f);
        private static readonly Color Success = Hsl(156f, 54f, 70f);
        private static readonly Color Warning = Hsl(40f, 82f, 72f);

        internal static void DrawBackdrop()
        {
            EnsureStyles();
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                backdrop, ScaleMode.StretchToFill, false);
        }

        internal static void DrawRenderPreview(Texture preview)
        {
            EnsureStyles();
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                backdrop, ScaleMode.StretchToFill, false);
            if (preview != null)
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                    preview, ScaleMode.ScaleToFit, false);
        }

        internal static void DrawToast(RendererController renderer)
        {
            EnsureStyles();
            bool liveRender = renderer.State == RenderState.Rendering;
            float width = Mathf.Min(liveRender ? 480f : 680f, Screen.width - 40f);
            bool showProgress = renderer.TotalFrames > 0
                && (renderer.State == RenderState.Rendering
                    || renderer.State == RenderState.Finishing);
            bool showCompleted = renderer.State == RenderState.Completed;
            bool showPath = !string.IsNullOrEmpty(renderer.OutputPath)
                && showCompleted;
            float height = showCompleted ? 236f : (showProgress ? 274f : 176f);
            float left = (Screen.width - width) * 0.5f;
            float top = Mathf.Max(20f, (Screen.height - height) * 0.5f);
            if (liveRender)
            {
                left = Screen.width - width - 24f;
                top = Screen.height - height - 24f;
            }
            var rect = new Rect(left, top, width, height);

            if (Event.current.type == EventType.Repaint)
            {
                panelBorder.Draw(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f),
                    GUIContent.none, 0);
                panel.Draw(rect, GUIContent.none, 0);
            }

            const float padding = 24f;
            var content = new Rect(rect.x + padding, rect.y + 18f, rect.width - padding * 2f, rect.height - 36f);
            state.normal.textColor = StateColor(renderer.State);
            GUI.Label(new Rect(content.x, content.y, content.width * 0.55f, 18f), "OrbitRender", title);
            GUI.Label(new Rect(content.x + content.width * 0.55f, content.y,
                content.width * 0.45f, 18f), StateLabel(renderer.State), state);
            GUI.Label(new Rect(content.x, content.y + 25f, content.width, 24f),
                StateTitle(renderer.State), message);
            GUI.Label(new Rect(content.x, content.y + 50f, content.width, 22f),
                renderer.Message ?? renderer.ToastText ?? string.Empty, detail);

            if (showProgress)
            {
                float progress = Mathf.Clamp01((float)renderer.CapturedFrames / renderer.TotalFrames);
                GUI.Label(new Rect(content.x, content.y + 78f, content.width, 38f),
                    renderer.ProgressPercentText ?? string.Empty, percent);
                var progressRect = new Rect(content.x, content.y + 119f, content.width, 9f);
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.DrawTexture(progressRect, progressTrack, ScaleMode.StretchToFill, false);
                    if (progress > 0f)
                        GUI.DrawTexture(new Rect(progressRect.x, progressRect.y,
                            progressRect.width * progress, progressRect.height), progressFill,
                            ScaleMode.StretchToFill, false);
                }

                var metrics = new Rect(content.x, content.y + 141f, content.width, 43f);
                DrawMetric(metrics, 0, Localization.Text("FRAMES", "프레임"),
                    renderer.ProgressText ?? string.Empty);
                DrawMetric(metrics, 1, Localization.Text("SPEED", "속도"),
                    renderer.State == RenderState.Completed
                        ? Localization.Text("Complete", "완료")
                        : renderer.SpeedText ?? string.Empty);
                DrawMetric(metrics, 2, Localization.Text("ETA", "예상 시간"),
                    renderer.State == RenderState.Completed
                        ? Localization.Text("Done", "완료")
                        : renderer.EtaText ?? string.Empty);

            }
            else if (showCompleted)
            {
                GUI.Label(new Rect(content.x, content.y + 83f, content.width, 16f),
                    Localization.Text("TIME SPENT", "걸린 시간"), metricLabel);
                GUI.Label(new Rect(content.x, content.y + 99f, content.width, 36f),
                    FormatElapsed(renderer.ElapsedSeconds), percent);
                if (showPath)
                {
                    if (Event.current.type == EventType.Repaint)
                        GUI.DrawTexture(new Rect(content.x, content.y + 145f, content.width, 1f), divider,
                            ScaleMode.StretchToFill, false);
                    GUI.Label(new Rect(content.x, content.y + 155f, content.width - 118f, 22f),
                        CompactPath(renderer.OutputPath), path);
                    if (GUI.Button(new Rect(content.x + content.width - 110f, content.y + 152f, 110f, 28f),
                        Localization.Text("Copy path", "경로 복사")))
                        GUIUtility.systemCopyBuffer = renderer.OutputPath;
                }
            }
            else
            {
                GUI.Label(new Rect(content.x, content.y + 83f, content.width, 42f),
                    Localization.Text("The render window will update when the next stage is ready.",
                        "다음 단계가 준비되면 렌더 상태가 업데이트됩니다."), hint);
            }

            if (showProgress)
            {
                GUI.Label(new Rect(content.x, content.y + 201f, content.width - 132f, 22f),
                    Localization.Text("Hold Esc for 1 second to cancel", "취소하려면 Esc를 1초간 누르세요"), hint);
                if (renderer.State == RenderState.Rendering
                    && GUI.Button(new Rect(content.x + content.width - 118f, content.y + 198f, 118f, 28f),
                        Localization.Text("Cancel render", "렌더 취소")))
                    renderer.Cancel();
            }
        }

        private static void DrawMetric(Rect area, int index, string label, string value)
        {
            float width = area.width / 3f;
            var metric = new Rect(area.x + width * index, area.y, width - 8f, area.height);
            GUI.Label(new Rect(metric.x, metric.y, metric.width, 16f), label, metricLabel);
            GUI.Label(new Rect(metric.x, metric.y + 17f, metric.width, 24f), value, metricValue);
        }

        private static string StateLabel(RenderState value)
        {
            switch (value)
            {
                case RenderState.Preparing: return Localization.Text("PREPARING", "준비 중");
                case RenderState.Rendering: return Localization.Text("RENDERING", "렌더링 중");
                case RenderState.Finishing: return Localization.Text("FINALIZING", "마무리 중");
                case RenderState.Completed: return Localization.Text("COMPLETED", "완료");
                case RenderState.Cancelled: return Localization.Text("CANCELLED", "취소됨");
                case RenderState.Failed: return Localization.Text("FAILED", "실패");
                default: return Localization.Text("STATUS", "상태");
            }
        }

        private static string StateTitle(RenderState value)
        {
            switch (value)
            {
                case RenderState.Preparing: return Localization.Text("Preparing your export", "영상 내보내기 준비 중");
                case RenderState.Rendering: return Localization.Text("Rendering video", "영상 렌더링 중");
                case RenderState.Finishing: return Localization.Text("Finishing video", "영상 마무리 중");
                case RenderState.Completed: return Localization.Text("Export complete", "영상 내보내기 완료");
                case RenderState.Cancelled: return Localization.Text("Render cancelled", "렌더 취소됨");
                case RenderState.Failed: return Localization.Text("Render failed", "렌더 실패");
                default: return Localization.Text("Render status", "렌더 상태");
            }
        }

        private static Color StateColor(RenderState value)
        {
            if (value == RenderState.Completed) return Success;
            if (value == RenderState.Failed || value == RenderState.Cancelled) return Warning;
            return Ring;
        }

        private static string CompactPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var fileName = Path.GetFileName(value);
            return Localization.Format("Output: {0}", "출력: {0}", fileName);
        }

        private static string FormatElapsed(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            var duration = System.TimeSpan.FromSeconds(System.Math.Max(0d, seconds));
            if (duration.TotalHours >= 1d)
                return Localization.Format("{0}:{1:D2}:{2:D2}", "{0}:{1:D2}:{2:D2}",
                    (int)duration.TotalHours, duration.Minutes, duration.Seconds);
            return Localization.Format("{0}:{1:D2}", "{0}:{1:D2}", duration.Minutes, duration.Seconds);
        }

        internal static void DrawEncoderFallbackPrompt(RendererController renderer)
        {
            EnsureStyles();
            float width = Mathf.Min(760f, Screen.width - 40f);
            float height = 230f;
            float left = (Screen.width - width) * 0.5f;
            float top = Mathf.Max(24f, (Screen.height - height) * 0.5f);
            var rect = new Rect(left, top, width, height);
            GUI.Box(rect, GUIContent.none, panel);
            var content = new Rect(rect.x + 24f, rect.y + 18f, rect.width - 48f, rect.height - 36f);
            GUI.Label(new Rect(content.x, content.y, content.width, 24f),
                Localization.Text("ENCODER CONFIRMATION", "인코더 확인"), title);
            GUI.Label(new Rect(content.x, content.y + 34f, content.width, 38f),
                Localization.Text("The selected hardware encoder could not be initialized.",
                    "선택한 하드웨어 인코더를 초기화할 수 없습니다."), message);
            GUI.Label(new Rect(content.x, content.y + 76f, content.width, 54f),
                renderer.EncoderFallbackReason ?? string.Empty, detail);
            GUI.Label(new Rect(content.x, content.y + 132f, content.width, 28f),
                Localization.Text(
                    "Use Software encoder for this render? Your saved encoder setting will not be changed.",
                    "이번 렌더에 소프트웨어 인코더를 사용할까요? 저장된 인코더 설정은 변경되지 않습니다."), detail);
            if (GUI.Button(new Rect(content.x, content.y + 170f, 310f, 34f),
                Localization.Text("Use Software and continue", "소프트웨어로 계속")))
                renderer.ConfirmEncoderFallback();
            if (GUI.Button(new Rect(content.x + 326f, content.y + 170f, 160f, 34f),
                Localization.Text("Cancel render", "렌더 취소")))
                renderer.RejectEncoderFallback();
        }

        internal static void DrawFfmpegInstallPrompt()
        {
            EnsureStyles();
            float width = Mathf.Min(760f, Screen.width - 40f);
            float height = 238f;
            float left = (Screen.width - width) * 0.5f;
            float top = Mathf.Max(24f, (Screen.height - height) * 0.5f);
            var rect = new Rect(left, top, width, height);
            GUI.Box(rect, GUIContent.none, panel);
            var content = new Rect(rect.x + 24f, rect.y + 18f, rect.width - 48f, rect.height - 36f);

            var waiting = FfmpegInstaller.IsAwaitingConsent;
            GUI.Label(new Rect(content.x, content.y, content.width, 24f),
                waiting
                    ? Localization.Text("FFMPEG INSTALLATION", "FFMPEG 설치")
                    : Localization.Text("INSTALLING FFMPEG", "FFMPEG 설치 중"), title);
            GUI.Label(new Rect(content.x, content.y + 34f, content.width, 40f),
                waiting
                    ? Localization.Text(
                        "OrbitRender needs FFmpeg to export videos. Download the platform-compatible binary now?",
                        "OrbitRender가 동영상을 내보내려면 FFmpeg가 필요합니다. 현재 플랫폼용 파일을 지금 다운로드할까요?")
                    : Localization.Text(
                        "Downloading FFmpeg for this platform. The renderer will be ready when the download finishes.",
                        "현재 플랫폼용 FFmpeg를 다운로드하고 있습니다. 다운로드가 끝나면 렌더러를 사용할 수 있습니다."),
                message);
            GUI.Label(new Rect(content.x, content.y + 82f, content.width, 52f),
                waiting
                    ? Localization.Text(
                        "The download comes from the FFmpeg build provider and is saved inside the mod folder. An internet connection is required.",
                        "다운로드는 FFmpeg 빌드 제공처에서 진행되며 모드 폴더에 저장됩니다. 인터넷 연결이 필요합니다.")
                    : FfmpegInstaller.StatusMessage,
                detail);

            if (waiting)
            {
                if (GUI.Button(new Rect(content.x, content.y + 158f, 310f, 34f),
                    Localization.Text("Install FFmpeg", "FFmpeg 설치")))
                    FfmpegInstaller.ConfirmInstall();
                if (GUI.Button(new Rect(content.x + 326f, content.y + 158f, 160f, 34f),
                    Localization.Text("Not now", "나중에")))
                    FfmpegInstaller.DeclineInstall();
            }
        }

        private static void EnsureStyles()
        {
            if (initialized) return;
            initialized = true;
            backdrop = Solid(DarkBackground);
            border = Rounded(DarkBorder, 48, 12);
            background = Rounded(DarkMuted, 48, 11);
            progressTrack = Solid(DarkBackground);
            progressFill = Solid(Ring);
            divider = Solid(DarkBorder);
            panelBorder = new GUIStyle
            {
                border = new RectOffset(12, 12, 12, 12),
                normal = { background = border }
            };
            panel = new GUIStyle
            {
                border = new RectOffset(11, 11, 11, 11),
                normal = { background = background }
            };
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Ring }
            };
            message = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Foreground }
            };
            state = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Ring }
            };
            detail = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = MutedForeground }
            };
            percent = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Foreground }
            };
            metricLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                normal = { textColor = MutedForeground }
            };
            metricValue = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = Foreground }
            };
            hint = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = MutedForeground }
            };
            path = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                clipping = TextClipping.Clip,
                normal = { textColor = MutedForeground }
            };
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "OrbitRender UI" };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static Texture2D Rounded(Color color, int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = "OrbitRender Rounded UI",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;
                    var dx = Mathf.Max(Mathf.Max(radius - px, 0f), px - (size - radius));
                    var dy = Mathf.Max(Mathf.Max(radius - py, 0f), py - (size - radius));
                    texture.SetPixel(x, y, dx * dx + dy * dy <= radius * radius ? color : Color.clear);
                }
            }
            texture.Apply();
            return texture;
        }

        private static Color Hsl(float hue, float saturationPercent, float lightnessPercent)
        {
            float h = Mathf.Repeat(hue, 360f) / 360f;
            float s = saturationPercent / 100f;
            float l = lightnessPercent / 100f;
            float chroma = (1f - Mathf.Abs(2f * l - 1f)) * s;
            float x = chroma * (1f - Mathf.Abs((h * 6f) % 2f - 1f));
            float r = 0f, g = 0f, b = 0f;
            if (h < 1f / 6f) { r = chroma; g = x; }
            else if (h < 2f / 6f) { r = x; g = chroma; }
            else if (h < 3f / 6f) { g = chroma; b = x; }
            else if (h < 4f / 6f) { g = x; b = chroma; }
            else if (h < 5f / 6f) { r = x; b = chroma; }
            else { r = chroma; b = x; }
            float match = l - chroma / 2f;
            return new Color(r + match, g + match, b + match, 1f);
        }
    }
}
