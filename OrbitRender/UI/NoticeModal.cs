using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Linq;
using OrbitRender.Renderer;

namespace OrbitRender.UI
{
    internal static class NoticeModal
    {
        private static GameObject root;
        internal static bool IsOpen => root != null;

        internal static void Open(string message, bool isError = false, System.Action retry = null)
        {
            Close();
            var controller = RendererController.Instance;
            var compatibility = isError && controller != null && controller.HasTimingCompatibilityFailure;
            var candidates = compatibility ? ConflictRecovery.Candidates(controller.ConflictModIds) : null;
            root = UguiFactory.Canvas("OrbitRender.Notice", 32767);
            root.AddComponent<NoticeModalInput>();
            root.GetComponent<CanvasScaler>().scaleFactor = Mathf.Clamp(
                Mathf.Min(Screen.width / (compatibility ? 660f : 600f), Screen.height / (compatibility ? 560f : 420f)), .1f, 1.5f);
            var dimmer = UguiFactory.Image(root.transform, "Dimmer", new Color(0, 0, 0, .72f), true);
            UguiFactory.Stretch(dimmer.GetComponent<RectTransform>());
            var panel = UguiFactory.Image(root.transform, "Panel", UguiFactory.Surface, true);
            UguiFactory.Round(panel.GetComponent<Image>(), true);
            var center = new Vector2(.5f, .5f);
            UguiFactory.Anchor(panel.GetComponent<RectTransform>(), center, center,
                new Vector2(compatibility ? -310 : -280, compatibility ? -260 : -190),
                new Vector2(compatibility ? 310 : 280, compatibility ? 260 : 190));
            var title = UguiFactory.Text(panel.transform,
                Localization.Get(isError ? "notice-error-title" : "notice-title"), 18,
                TextAnchor.MiddleLeft, isError ? UguiFactory.Error : UguiFactory.Accent);
            title.fontStyle = FontStyle.Bold;
            UguiFactory.Anchor(title.rectTransform, Vector2.up, Vector2.one,
                new Vector2(24, -58), new Vector2(-24, -24));
            var scroll = UguiFactory.New(panel.transform, "Message", typeof(RectTransform), typeof(ScrollRect));
            UguiFactory.Anchor(scroll.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(24, compatibility ? 184 : isError ? 128 : 84), new Vector2(-24, -76));
            var viewport = UguiFactory.Image(scroll.transform, "Viewport", Color.clear, true);
            UguiFactory.Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();
            var content = UguiFactory.New(viewport.transform, "Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = content.GetComponent<RectTransform>();
            UguiFactory.Anchor(contentRect, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            contentRect.pivot = Vector2.up;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12; layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var body = UguiFactory.Text(content.transform, message, 14, TextAnchor.UpperLeft);
            body.supportRichText = false;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            var scroller = scroll.GetComponent<ScrollRect>();
            scroller.viewport = viewport.GetComponent<RectTransform>();
            scroller.content = contentRect;
            scroller.horizontal = false;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            if (compatibility)
            {
                var explanation = UguiFactory.Text(content.transform, Localization.Get(
                    candidates.Length == 0 ? "conflict-no-candidate" : "conflict-candidate-explanation"), 14, TextAnchor.UpperLeft);
                explanation.supportRichText = false;
                foreach (var mod in candidates)
                {
                    var label = UguiFactory.Text(content.transform,
                        mod.Info.DisplayName + " " + mod.Info.Version + "\n" + mod.Info.Id, 14, TextAnchor.UpperLeft);
                    label.supportRichText = false;
                    Button disable = null;
                    disable = UguiFactory.Button(content.transform, Localization.Get("conflict-disable-next-launch"), () => {
                        try
                        {
                            ConflictRecovery.DisableForNextLaunch(mod);
                            disable.GetComponentInChildren<Text>().text = Localization.Get("conflict-disabled-next-launch");
                            disable.interactable = false;
                        }
                        catch (System.Exception error)
                        {
                            body.text += "\n\n" + Localization.Format("conflict-action-failed", error.Message);
                            Main.Entry.Logger.Error("[Recovery] Disable failed: " + error);
                        }
                    });
                    disable.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
                    if (!mod.Enabled)
                    {
                        disable.GetComponentInChildren<Text>().text = Localization.Get("conflict-disabled-next-launch");
                        disable.interactable = false;
                    }
                }
                var warning = UguiFactory.Text(panel.transform, Localization.Get("conflict-restart-warning"), 12, TextAnchor.UpperLeft);
                UguiFactory.Anchor(warning.rectTransform, Vector2.zero, Vector2.right,
                    new Vector2(24, 120), new Vector2(-24, 176));
                Button restart = null;
                var restartConfirmed = false;
                restart = UguiFactory.Button(panel.transform, Localization.Get("conflict-restart-game"), () => {
                    if (!restartConfirmed)
                    {
                        restartConfirmed = true;
                        restart.GetComponentInChildren<Text>().text = Localization.Get("conflict-restart-confirm");
                        return;
                    }
                    try { ConflictRecovery.Restart(); }
                    catch (System.Exception error)
                    {
                        restartConfirmed = false;
                        restart.GetComponentInChildren<Text>().text = Localization.Get("conflict-restart-game");
                        body.text += "\n\n" + Localization.Format("conflict-action-failed", error.Message);
                        Main.Entry.Logger.Error("[Recovery] Restart failed: " + error);
                    }
                });
                restart.interactable = ConflictRecovery.CanRestart;
                if (!restart.interactable) restart.GetComponentInChildren<Text>().text = Localization.Get("conflict-restart-manually");
                UguiFactory.Anchor(restart.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero,
                    new Vector2(24, 24), new Vector2(304, 60));
            }
            if (retry != null && !compatibility)
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
            foreach (var button in root.GetComponentsInChildren<Button>()) button.gameObject.AddComponent<NoticeButtonFocus>();
            confirm.Select();
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
            if (Input.GetKeyDown(KeyCode.Escape)) { NoticeModal.Close(); return; }
            var buttons = GetComponentsInChildren<Button>().Where(button => button.IsInteractable()).ToArray();
            if (buttons.Length == 0 || EventSystem.current == null) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            var index = System.Array.FindIndex(buttons, button => button.gameObject == selected);
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                var direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                buttons[(index + direction + buttons.Length) % buttons.Length].Select();
            }
            else if (index < 0) buttons[buttons.Length - 1].Select();
        }
    }

    internal sealed class NoticeButtonFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Outline outline;
        private void Awake()
        {
            outline = gameObject.AddComponent<Outline>();
            outline.effectColor = UguiFactory.Accent;
            outline.effectDistance = new Vector2(2, -2);
            outline.enabled = false;
        }
        public void OnSelect(BaseEventData data)
        {
            outline.enabled = true;
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll == null) return;
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, transform);
            var viewport = scroll.viewport.rect;
            if (bounds.max.y > viewport.yMax)
                scroll.content.anchoredPosition -= new Vector2(0, bounds.max.y - viewport.yMax);
            else if (bounds.min.y < viewport.yMin)
                scroll.content.anchoredPosition += new Vector2(0, viewport.yMin - bounds.min.y);
        }
        public void OnDeselect(BaseEventData data) { outline.enabled = false; }
    }
}
