using UnityEngine;
using UnityEngine.UI;

namespace OrbitRender.UI
{
    // IMGUI events are independent from Unity's EventSystem and the game's
    // per-frame input polling. The canvas blocks regular Unity UI, while the
    // resetter consumes the same frame's polled input before game Update calls.
    internal static class ModalInputBlocker
    {
        private const int SortingOrder = 32767;
        private static GameObject blockerObject;

        internal static void Open()
        {
            if (blockerObject != null) return;

            blockerObject = new GameObject("OrbitRender.ModalInputBlocker",
                typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(GraphicRaycaster),
                typeof(ModalInputResetter));
            Object.DontDestroyOnLoad(blockerObject);

            var canvas = blockerObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            var image = blockerObject.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            var rect = blockerObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static void Close()
        {
            if (blockerObject == null) return;
            Object.Destroy(blockerObject);
            blockerObject = null;
        }
    }

    [DefaultExecutionOrder(-32000)]
    internal sealed class ModalInputResetter : MonoBehaviour
    {
        private void Update()
        {
            if (ExportVideoDialog.IsOpen) Input.ResetInputAxes();
        }
    }
}
