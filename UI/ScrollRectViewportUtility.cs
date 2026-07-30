using UnityEngine;

namespace LegendaryTools.UI
{
    /// <summary>
    /// Shared viewport calculations for scroll controls that recycle or virtualize items.
    /// </summary>
    public static class ScrollRectViewportUtility
    {
        public static Bounds CalculateExpandedBounds(
            RectTransform content,
            RectTransform viewport,
            RectTransform sampleItem,
            Vector2 bufferCount)
        {
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                content,
                viewport);
            if (sampleItem == null)
            {
                return bounds;
            }

            Bounds itemBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                content,
                sampleItem);
            Vector3 extents = bounds.extents;
            extents.x += itemBounds.size.x * Mathf.Max(0f, bufferCount.x);
            extents.y += itemBounds.size.y * Mathf.Max(0f, bufferCount.y);
            bounds.extents = extents;
            return bounds;
        }

        public static void CalculateUniformRange(
            RectTransform content,
            RectTransform viewport,
            bool horizontal,
            float leadingPadding,
            float itemStep,
            int constraint,
            int buffer,
            int itemCount,
            out int first,
            out int last)
        {
            if (content == null || viewport == null || itemCount <= 0)
            {
                first = 0;
                last = -1;
                return;
            }

            float position = horizontal
                ? Mathf.Max(0f, -content.anchoredPosition.x - leadingPadding)
                : Mathf.Max(0f, content.anchoredPosition.y - leadingPadding);
            float extent = horizontal ? viewport.rect.width : viewport.rect.height;
            float step = Mathf.Max(1f, itemStep);
            int safeConstraint = Mathf.Max(1, constraint);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(position / step) - Mathf.Max(0, buffer));
            int lastRow = Mathf.CeilToInt((position + extent) / step) + Mathf.Max(0, buffer);
            first = firstRow * safeConstraint;
            last = Mathf.Min(itemCount - 1, ((lastRow + 1) * safeConstraint) - 1);
        }
    }
}
