using UnityEngine;
using UnityEngine.UI;

namespace OrbitRender.UI
{
    internal static class NoticeModal
    {
        private static GameObject root;
        internal static bool IsOpen => root != null;

        internal static void Open(string message, bool isError = false, System.Action retry = null)
        {
            Close();
            root = UguiFactory.Canvas("OrbitRender.Notice", 32767);
            root.AddComponent<NoticeModalInput>();
            root.GetComponent<CanvasScaler>().scaleFactor = Mathf.Clamp(
                Mathf.Min(Screen.width / 600f, Screen.height / 420f), .1f, 1.5f);
            var dimmer = UguiFactory.Image(root.transform, "Dimmer", new Color(0, 0, 0, .72f), true);
            UguiFactory.Stretch(dimmer.GetComponent<RectTransform>());
            var panel = UguiFactory.Image(root.transform, "Panel", UguiFactory.Surface, true);
            UguiFactory.Round(panel.GetComponent<Image>(), true);
            var center = new Vector2(.5f, .5f);
            UguiFactory.Anchor(panel.GetComponent<RectTransform>(), center, center,
                new Vector2(-280, -190), new Vector2(280, 190));
            var title = UguiFactory.Text(panel.transform,
                Localization.Get(isError ? "notice-error-title" : "notice-title"), 18,
                TextAnchor.MiddleLeft, isError ? UguiFactory.Error : UguiFactory.Accent);
            title.fontStyle = FontStyle.Bold;
            UguiFactory.Anchor(title.rectTransform, Vector2.up, Vector2.one,
                new Vector2(24, -58), new Vector2(-24, -24));
            var scroll = UguiFactory.New(panel.transform, "Message", typeof(RectTransform), typeof(ScrollRect));
            UguiFactory.Anchor(scroll.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(24, isError ? 128 : 84), new Vector2(-24, -76));
            var viewport = UguiFactory.Image(scroll.transform, "Viewport", Color.clear, true);
            UguiFactory.Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();
            var body = UguiFactory.Text(viewport.transform, message, 14, TextAnchor.UpperLeft);
            body.supportRichText = false;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            UguiFactory.Anchor(body.rectTransform, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            body.rectTransform.pivot = Vector2.up;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroller = scroll.GetComponent<ScrollRect>();
            scroller.viewport = viewport.GetComponent<RectTransform>();
            scroller.content = body.rectTransform;
            scroller.horizontal = false;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            if (retry != null)
            {
                var retryButton = UguiFactory.Button(panel.transform, Localization.Get("recovery-retry"), () => { Close(); retry(); });
                UguiFactory.Anchor(retryButton.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero,
                    new Vector2(24, 24), new Vector2(304, 60));
            }
            if (isError)
            {
                var report = Renderer.RendererController.Instance != null
                    ? Renderer.RendererController.Instance.DiagnosticReport : message;
                Button copy = null;
                copy = UguiFactory.Button(panel.transform, Localization.Get("copy-error-diagnostics"), () => {
                    GUIUtility.systemCopyBuffer = report;
                    copy.GetComponentInChildren<Text>().text = Localization.Get("diagnostics-copied");
                });
                UguiFactory.Anchor(copy.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero,
                    new Vector2(24, 72), new Vector2(304, 108));
            }
            var confirm = UguiFactory.Button(panel.transform, Localization.Get("notice-confirm"), Close, true);
            UguiFactory.Anchor(confirm.GetComponent<RectTransform>(), Vector2.right, Vector2.right,
                new Vector2(-144, 24), new Vector2(-24, 60));
        }

        internal static void Close()
        {
            if (root != null) { root.SetActive(false); Object.Destroy(root); }
            root = null;
        }
    }

    [DefaultExecutionOrder(32001)]
    internal sealed class NoticeModalInput : MonoBehaviour
    {
        private int openedFrame;
        private void Awake() { openedFrame = Time.frameCount; }
        private void Update()
        {
            if (!Main.Enabled || Main.Settings == null) { NoticeModal.Close(); return; }
            if (Time.frameCount == openedFrame) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter)) NoticeModal.Close();
        }
    }
}
