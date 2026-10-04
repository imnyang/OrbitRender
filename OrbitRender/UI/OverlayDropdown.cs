using UnityEngine;
using UnityEngine.UI;

namespace OrbitRender.UI
{
    internal sealed class OverlayDropdown : Dropdown
    {
        private GameObject popup;
        private int itemIndex;
        internal int[] SeparatorIndices;

        protected override GameObject CreateDropdownList(GameObject template)
        {
            itemIndex = 0;
            popup = base.CreateDropdownList(template);
            return popup;
        }

        protected override DropdownItem CreateItem(DropdownItem itemTemplate)
        {
            var item = base.CreateItem(itemTemplate);
            if (SeparatorIndices != null && System.Array.IndexOf(SeparatorIndices, itemIndex) >= 0)
            {
                var line = UguiFactory.Image(item.transform, "Codec Separator",
                    new Color(.69f, .68f, .72f, .55f));
                UguiFactory.Anchor(line.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                    new Vector2(8f, -1f), new Vector2(-8f, 0f));
            }
            itemIndex++;
            return item;
        }

        protected override GameObject CreateBlocker(Canvas rootCanvas)
        {
            // Unity's template setup assigns sorting order 30000, which is
            // behind our modal. Set both popup and click blocker after Show
            // has finished configuring the list, before the first draw.
            var popupCanvas = popup.GetComponent<Canvas>();
            popupCanvas.overrideSorting = true;
            popupCanvas.sortingLayerID = rootCanvas.sortingLayerID;
            popupCanvas.sortingOrder = rootCanvas.sortingOrder + 2;
            var blocker = base.CreateBlocker(rootCanvas);
            var blockerCanvas = blocker.GetComponent<Canvas>();
            blockerCanvas.sortingLayerID = rootCanvas.sortingLayerID;
            blockerCanvas.sortingOrder = rootCanvas.sortingOrder + 1;
            // Keep the selected encoder visible even when its codec appears
            // near the end of a long list (for example AOM AV1).
            var scroll = popup.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                var overflow = scroll.content.rect.height - scroll.viewport.rect.height;
                if (overflow > 0f)
                {
                    var offset = value * 32f - (scroll.viewport.rect.height - 32f) / 2f;
                    scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(offset / overflow);
                }
            }
            return blocker;
        }

        protected override void DestroyDropdownList(GameObject dropdownList)
        {
            base.DestroyDropdownList(dropdownList);
            popup = null;
        }
    }
}
