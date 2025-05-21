using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.UI
{
    /// <summary>
    /// A generic scroll view that dynamically creates and destroys child items
    /// (slots) based on visibility. TGameObject must implement <see cref="IListingItem"/>
    /// to receive data initialization and UI updates.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public abstract class DynamicScrollView<TGameObject, TData> : MonoBehaviour, IDisposable
        where TGameObject : Component, DynamicScrollView<TGameObject, TData>.IListingItem
        where TData : class
    {
        #region Nested Types

        public interface IListingItem
        {
            /// <summary>
            /// Called once when the item is created.
            /// </summary>
            void Init(TData item);

            /// <summary>
            /// Called to update the item's UI when its data changes.
            /// </summary>
            void UpdateUI(TData item);
        }

        #endregion

        #region Events

        /// <summary>
        /// Invoked each time a new item is created (instantiated).
        /// </summary>
        public event Action<TGameObject, TData> OnCreateItem;

        /// <summary>
        /// Invoked each time an existing item is destroyed/removed.
        /// </summary>
        public event Action<TGameObject, TData> OnRemoveItem;

        /// <summary>
        /// Invoked once the listing generation is complete (slots match data).
        /// </summary>
        public event Action<List<TGameObject>> OnCompleteListingGeneration;

        #endregion

        #region Fields and Properties

        /// <summary>
        /// The current data set for this scroll view.
        /// </summary>
        public List<TData> DataSource { get; } = new List<TData>();

        /// <summary>
        /// Returns the list of currently active items (i.e., those that are instantiated and visible).
        /// </summary>
        public List<TGameObject> Listing
        {
            get
            {
                // Snapshot of the currently spawned items
                return new List<TGameObject>(itemsAtSlot.Values);
            }
        }

        [Header("References")]
        [Tooltip("Reference to the parent Canvas. Used to compute viewport rect in canvas space.")]
        public Canvas MainCanvas;

        [Tooltip("Reference to the ScrollRect controlling this dynamic list.")]
        public ScrollRect ScrollRect;

        [Tooltip("Prefab for each list item. Must implement IListingItem.")]
        public TGameObject Prefab;

        [Header("Settings")]
        [Tooltip("If true, the item RectTransform is auto-configured (stretch anchors, zero offsets) to fill its slot.")]
        public bool CanOverrideItemRectTransform = false;

        [Tooltip("Enable gizmo drawing for debugging (viewport box, slot boxes, etc.).")]
        public bool DebugMode = false;

        [Header("Slots")]
        [Tooltip("Number of new slots to create per frame when expanding the slot list.")]
        public int SlotNumInstantiateCallsPerFrame = 10;

        [Tooltip("Number of extra slots beyond viewport bounds to keep alive (for 'buffered' visibility).")]
        public Vector2 ItemBufferCount = Vector2.zero;

        // Internal slot prefab container
        private RectTransform slotPrefab;
        private const string SLOT_PREFAB = "SlotPrefab";

        // Internal Lists
        private readonly List<RectTransform> slots = new List<RectTransform>();
        private readonly Dictionary<int, TGameObject> itemsAtSlot = new Dictionary<int, TGameObject>();

        // Coroutines
        private Coroutine generateRoutine;
        private Coroutine scrollToRoutine;

        // Keep track if generation is in progress
        private bool isGenerating;

        // If user requests a scroll-to while generating, we store index here
        private int pendingScrollToIndex = -1;

        // Initialization guard
        private bool isInit;

        // Cached references
        private RectTransform rectTransform;
        private RectTransform prefabRectTransform;

        // For viewport intersection checks
        private readonly Vector3[] bufferCorners = new Vector3[4];
        private Rect bufferRect;
        private Rect viewportRect;
        // Instead of storing a Rect for the viewport,
        // we'll store a Bounds (3D) in the ScrollRect.content space.
        private Bounds extendedViewportBounds;

        #endregion

        #region Unity Callbacks

        protected virtual void Reset()
        {
            // Attempt to auto-assign
            if (!ScrollRect) ScrollRect = GetComponent<ScrollRect>();
        }

        protected virtual void Start()
        {
            Initialize();
        }

        protected virtual void OnEnable()
        {
            // If slots and data mismatch, regenerate
            if (ScrollRect != null && slots.Count != DataSource.Count)
            {
                if (generateRoutine != null) StopCoroutine(generateRoutine);
                generateRoutine = StartCoroutine(GenerateView(DataSource.ToArray()));
            }

            // If there's a pending scroll request
            if (pendingScrollToIndex >= 0 && ScrollRect != null)
            {
                if (scrollToRoutine != null) StopCoroutine(scrollToRoutine);
                scrollToRoutine = StartCoroutine(WaitGenerateAndScrollTo(pendingScrollToIndex));
            }
        }

        protected virtual void OnDisable()
        {
            // Stop ongoing coroutines to avoid errors when the object is inactive
            if (generateRoutine != null)
            {
                StopCoroutine(generateRoutine);
                generateRoutine = null;
            }
            if (scrollToRoutine != null)
            {
                StopCoroutine(scrollToRoutine);
                scrollToRoutine = null;
            }
        }

        protected virtual void OnDestroy()
        {
            // Clean up coroutines
            if (generateRoutine != null)
            {
                StopCoroutine(generateRoutine);
                generateRoutine = null;
            }
            if (scrollToRoutine != null)
            {
                StopCoroutine(scrollToRoutine);
                scrollToRoutine = null;
            }

            // Destroy slot containers
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                if (slots[i] != null)
                {
                    Pool.Destroy(slots[i]);
                }
            }
            slots.Clear();

            // Destroy all spawned items
            DestroyAllItems();

            // Remove listener
            if (ScrollRect != null)
            {
                ScrollRect.onValueChanged.RemoveListener(OnScrollRectChange);
            }

            // Destroy the internal slot prefab used for pooling
            if (slotPrefab != null)
            {
                Destroy(slotPrefab.gameObject);
                slotPrefab = null;
            }
        }

        /// <summary>
        /// Called right after an item is instantiated to allow derived classes to perform extra setup.
        /// </summary>
        protected virtual void OnItemCreated(TGameObject item, TData data) { }

        /// <summary>
        /// Called right before an item is removed/destroyed to allow derived classes to perform cleanup.
        /// </summary>
        protected virtual void OnItemRemoved(TGameObject item, TData data) { }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!DebugMode) return;
            if (!MainCanvas) MainCanvas = GetComponentInParent<Canvas>();
            if (!MainCanvas) return;
            if (!ScrollRect) return;

            // Draw the viewport in green
            Gizmos.color = Color.green;
            var viewport = ScrollRect.viewport != null ? ScrollRect.viewport : GetComponent<RectTransform>();
            viewport.GetWorldCorners(bufferCorners);

            for (int j = 0; j < bufferCorners.Length; j++)
            {
                bufferCorners[j] = MainCanvas.transform.InverseTransformPoint(bufferCorners[j]);
            }

            float minX = Mathf.Min(bufferCorners[0].x, bufferCorners[1].x, bufferCorners[2].x, bufferCorners[3].x);
            float maxX = Mathf.Max(bufferCorners[0].x, bufferCorners[1].x, bufferCorners[2].x, bufferCorners[3].x);
            float minY = Mathf.Min(bufferCorners[0].y, bufferCorners[1].y, bufferCorners[2].y, bufferCorners[3].y);
            float maxY = Mathf.Max(bufferCorners[0].y, bufferCorners[1].y, bufferCorners[2].y, bufferCorners[3].y);
            float width = maxX - minX;
            float height = maxY - minY;

            DrawRectGizmo(new Rect(minX, minY, width, height));

            // Draw each slot in blue
            Gizmos.color = Color.blue;
            foreach (var slot in slots)
            {
                if (!slot) continue;
                slot.GetWorldCorners(bufferCorners);
                for (int j = 0; j < bufferCorners.Length; j++)
                {
                    bufferCorners[j] = MainCanvas.transform.InverseTransformPoint(bufferCorners[j]);
                }

                float sMinX = Mathf.Min(bufferCorners[0].x, bufferCorners[1].x, bufferCorners[2].x, bufferCorners[3].x);
                float sMaxX = Mathf.Max(bufferCorners[0].x, bufferCorners[1].x, bufferCorners[2].x, bufferCorners[3].x);
                float sMinY = Mathf.Min(bufferCorners[0].y, bufferCorners[1].y, bufferCorners[2].y, bufferCorners[3].y);
                float sMaxY = Mathf.Max(bufferCorners[0].y, bufferCorners[1].y, bufferCorners[2].y, bufferCorners[3].y);
                DrawRectGizmo(new Rect(sMinX, sMinY, sMaxX - sMinX, sMaxY - sMinY));
            }
        }

        private void DrawRectGizmo(Rect rect)
        {
            Gizmos.DrawWireCube(
                new Vector3(rect.center.x, rect.center.y, 0.01f),
                new Vector3(rect.size.x, rect.size.y, 0.01f)
            );
        }
#endif

        #endregion

        #region Public API

        /// <summary>
        /// Initializes the scroll view (slots, references) if not already initialized.
        /// </summary>
        public void Initialize()
        {
            if (isInit) return;

            rectTransform = GetComponent<RectTransform>();
            if (!MainCanvas)
            {
                MainCanvas = rectTransform.GetComponentInParent<Canvas>();
                if (!MainCanvas)
                {
                    Debug.LogWarning(
                        $"[{nameof(DynamicScrollView<TGameObject, TData>)}] No parent Canvas found. Some features may not work."
                    );
                }
            }

            // Create an internal slot prefab only once
            if (!slotPrefab)
            {
                var slotPrefabGO = new GameObject(SLOT_PREFAB, typeof(RectTransform), typeof(GameObjectPoolReference));
                slotPrefab = slotPrefabGO.GetComponent<RectTransform>();
            }

            prefabRectTransform = Prefab != null ? Prefab.GetComponent<RectTransform>() : null;

            if (ScrollRect != null)
            {
                ScrollRect.onValueChanged.AddListener(OnScrollRectChange);
            }

            isInit = true;
        }

        /// <summary>
        /// Sets/updates the data of this scroll view and begins (re)generating the slots/items.
        /// </summary>
        public void Generate(TData[] data)
        {
            if (data == null) data = Array.Empty<TData>();

            Initialize();
            isGenerating = true;

            // Destroy existing items (not slots)
            DestroyAllItems();

            // Update data source
            DataSource.Clear();
            DataSource.AddRange(data);

            // If the object is active, start generation routine
            if (gameObject.activeInHierarchy && ScrollRect != null)
            {
                if (generateRoutine != null) StopCoroutine(generateRoutine);
                generateRoutine = StartCoroutine(GenerateView(DataSource.ToArray()));
            }
            else
            {
                // If it's not active, we skip generation. (Optionally, you could store a flag and generate later.)
                isGenerating = false;
            }
        }

        /// <summary>
        /// Refreshes the entire list using the existing DataSource. Does not create/remove slots,
        /// only updates the UI for items currently visible.
        /// </summary>
        public void RefreshAll()
        {
            foreach (var kvp in itemsAtSlot)
            {
                int index = kvp.Key;
                TGameObject viewItem = kvp.Value;

                if (index >= 0 && index < DataSource.Count)
                {
                    viewItem.UpdateUI(DataSource[index]);
                }
            }
        }

        /// <summary>
        /// Refreshes a single item if it exists in the listing.
        /// </summary>
        public void Refresh(TData itemData)
        {
            if (itemData == null) return;

            int index = DataSource.FindIndex(item => item == itemData);
            if (index >= 0 && itemsAtSlot.TryGetValue(index, out TGameObject viewItem))
            {
                viewItem.UpdateUI(itemData);
            }
        }

        /// <summary>
        /// Refreshes all matching items from the subset array.
        /// </summary>
        public void RefreshAll(TData[] subset)
        {
            if (subset == null) return;
            for (int i = 0; i < subset.Length; i++)
            {
                Refresh(subset[i]);
            }
        }

        /// <summary>
        /// Scrolls to the given data item (if found in the DataSource), centering it if possible.
        /// </summary>
        public void ScrollTo(TData itemToFocus)
        {
            if (itemToFocus == null) return;
            int index = DataSource.FindIndex(d => d == itemToFocus);
            if (index >= 0)
            {
                ScrollToIndex(index);
            }
        }

        /// <summary>
        /// Scroll to the first item (if any).
        /// </summary>
        public void ScrollToBeginning()
        {
            if (DataSource.Count > 0) ScrollToIndex(0);
        }

        /// <summary>
        /// Scroll to the last item (if any).
        /// </summary>
        public void ScrollToEnd()
        {
            if (DataSource.Count > 0) ScrollToIndex(int.MaxValue);
        }

        /// <summary>
        /// Destroys all instantiated items (the visual objects), but not the underlying slot containers.
        /// Useful before repopulating data or clearing the list.
        /// </summary>
        public void DestroyAllItems()
        {
            foreach (var kvp in itemsAtSlot)
            {
                int index = kvp.Key;
                TGameObject item = kvp.Value;

                // Invoke removal callbacks
                if (index >= 0 && index < DataSource.Count)
                {
                    OnItemRemoved(item, DataSource[index]);
                    OnRemoveItem?.Invoke(item, DataSource[index]);
                }

                Pool.Destroy(item);
            }
            itemsAtSlot.Clear();
        }

        /// <summary>
        /// Clears the prefab pool if one is being used.
        /// </summary>
        public void Dispose()
        {
            Pool.ClearPool(Prefab);
        }

        #endregion

        #region Slot / Generation Routines

        private IEnumerator GenerateView(TData[] data)
        {
            if (!gameObject.activeInHierarchy || ScrollRect == null)
            {
                // Cannot proceed if inactive or missing references
                isGenerating = false;
                generateRoutine = null;
                yield break;
            }

            int dataCount = data.Length;
            int slotCount = slots.Count;

            // Expand or shrink slots to match data size
            if (dataCount > slotCount)
            {
                // We need to add more slots
                int needed = dataCount - slotCount;
                while (needed > 0)
                {
                    int createCount = Mathf.Clamp(needed, 0, SlotNumInstantiateCallsPerFrame);
                    for (int i = 0; i < createCount; i++)
                    {
                        RectTransform newSlot = Pool.Instantiate(slotPrefab);
                        newSlot.SetParent(ScrollRect.content, false);
                        newSlot.localPosition = Vector3.zero;
                        newSlot.localRotation = Quaternion.identity;
                        newSlot.localScale = Vector3.one;

                        // Match size of prefab if available
                        if (prefabRectTransform != null)
                        {
                            newSlot.sizeDelta = prefabRectTransform.sizeDelta;
                        }

                        slots.Add(newSlot);
                    }

                    // Rebuild layout so positions are up-to-date
                    LayoutRebuilder.ForceRebuildLayoutImmediate(ScrollRect.content);

                    yield return null; // Wait a frame
                    yield return null; // Another yield in case layout needs a second pass

                    UpdateVisibility();

                    needed = dataCount - slots.Count;
                }
            }
            else if (dataCount < slotCount)
            {
                // Remove extra slots
                int removeCount = slotCount - dataCount;
                for (int i = 0; i < removeCount; i++)
                {
                    int lastIdx = slots.Count - 1;
                    RectTransform slotToRemove = slots[lastIdx];
                    slots.RemoveAt(lastIdx);

                    Pool.Destroy(slotToRemove);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(ScrollRect.content);
                yield return null;

                UpdateVisibility();
            }
            else
            {
                // Same count, just rebuild layout
                LayoutRebuilder.ForceRebuildLayoutImmediate(ScrollRect.content);
                yield return null;

                UpdateVisibility();
            }

            isGenerating = false;
            generateRoutine = null;
            OnCompleteListingGeneration?.Invoke(Listing);
        }

        #endregion

        #region Scroll-To Logic

        private void ScrollToIndex(int index)
        {
            if (isGenerating)
            {
                // If we're still generating, queue the scroll until after
                if (gameObject.activeInHierarchy)
                {
                    if (scrollToRoutine != null) StopCoroutine(scrollToRoutine);
                    scrollToRoutine = StartCoroutine(WaitGenerateAndScrollTo(index));
                }
                else
                {
                    pendingScrollToIndex = index;
                }
            }
            else
            {
                ScrollToImmediate(index);
            }
        }

        private IEnumerator WaitGenerateAndScrollTo(int index)
        {
            yield return new WaitUntil(() => !isGenerating);
            ScrollToImmediate(index);
            pendingScrollToIndex = -1;
            scrollToRoutine = null;
        }

        /// <summary>
        /// Immediately scrolls to the given slot index, centering it in the viewport if possible.
        /// </summary>
        private void ScrollToImmediate(int index)
        {
            if (ScrollRect == null || slots.Count == 0) return;

            // Force layout to ensure up-to-date positions
            LayoutRebuilder.ForceRebuildLayoutImmediate(ScrollRect.content);

            index = Mathf.Clamp(index, 0, slots.Count - 1);
            var target = slots[index];
            if (!target) return;

            Vector2 contentSize = ScrollRect.content.rect.size;
            Vector2 viewportHalfSize = ScrollRect.viewport.rect.size * 0.5f;

            // Convert target's world pos to content space
            Vector3 targetPositionInContent = ScrollRect.content.InverseTransformPoint(target.position);

            // Offset to center the item
            Vector3 targetSizeOffset = new Vector3(
                target.rect.size.x * 0.5f,
                target.rect.size.y * 0.5f,
                0f
            );
            targetPositionInContent += targetSizeOffset;

            // Calculate normalized scroll position
            // - For vertical scrolling, only 'y' matters; for horizontal, only 'x' matters.
            //   Adjust if your scroll rect can scroll both ways simultaneously.
            Vector2 normalized = new Vector2(
                Mathf.Clamp01(targetPositionInContent.x / (contentSize.x - viewportHalfSize.x)),
                // Flip the y-axis, typical for vertical scroll rects
                1f - Mathf.Clamp01(targetPositionInContent.y / -(contentSize.y - viewportHalfSize.y))
            );

            // Additional offset to ensure the target is centered
            Vector2 normalizedOffset = new Vector2(
                viewportHalfSize.x / contentSize.x,
                viewportHalfSize.y / contentSize.y
            );

            // Shift so the item is at center
            normalized.x = Mathf.Clamp01(normalized.x - (1f - normalized.x) * normalizedOffset.x);
            normalized.y = Mathf.Clamp01(normalized.y + normalized.y * normalizedOffset.y);

            // Set final scroll position
            ScrollRect.normalizedPosition = normalized;

            // Trigger an immediate update to spawn/unspawn items
            UpdateVisibility();
        }

        #endregion

        #region Visibility / Item Lifecycle

        private void OnScrollRectChange(Vector2 scrollDelta)
        {
            UpdateVisibility();
        }

        /// <summary>
        /// Called to update item visibility after scrolling or slot creation.
        /// </summary>
        private void UpdateVisibility()
        {
            if (!ScrollRect || !ScrollRect.content) return;

            // 1) Recompute the (expanded) viewport bounds in content-space.
            UpdateViewportBoundsWithBuffer();

            // 2) For each slot, check if it intersects those bounds
            for (int i = 0; i < slots.Count; i++)
            {
                if (IsVisibleInExtendedViewport(slots[i], i))
                {
                    CreateItemAt(i);
                }
                else
                {
                    DestroyItemAt(i);
                }
            }
        }
        
        /// <summary>
        /// Computes the viewport's bounding box in content-space, then
        /// expands it by <see cref="ItemBufferCount"/> * slot size.
        /// </summary>
        private void UpdateViewportBoundsWithBuffer()
        {
            // A "Bounds" that covers the viewport in the coordinate system of the ScrollRect.content.
            // This accounts for potential movement of the content transform.
            Bounds viewportBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                ScrollRect.content,
                ScrollRect.viewport
            );

            // Expand the bounds by the buffer. We use the size of the first slot
            // (or a default guess if no slots exist) as a reference for how big "1 buffer unit" is.
            var extents = viewportBounds.extents; // half-size

            if (slots.Count > 0)
            {
                // Estimate slot size from the first slot
                Bounds slotBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                    ScrollRect.content,
                    slots[0]
                );
                Vector3 slotSize = slotBounds.size;

                // Expand extents by buffer factor
                extents.x += slotSize.x * ItemBufferCount.x;
                extents.y += slotSize.y * ItemBufferCount.y;
            }

            viewportBounds.extents = extents;
            extendedViewportBounds = viewportBounds;
        }

        /// <summary>
        /// Creates an item at the specified slot index if one does not already exist.
        /// </summary>
        private void CreateItemAt(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            if (slotIndex >= DataSource.Count) return;
            if (itemsAtSlot.ContainsKey(slotIndex)) return; // Already created

            var newItem = Pool.Instantiate(Prefab);
            var newItemRT = newItem.GetComponent<RectTransform>();
            var slotTransform = slots[slotIndex];

            newItemRT.SetParent(slotTransform, false);
            if (prefabRectTransform != null)
            {
                newItemRT.localPosition = prefabRectTransform.localPosition;
                newItemRT.localRotation = prefabRectTransform.localRotation;
                newItemRT.localScale = prefabRectTransform.localScale;
            }

            if (CanOverrideItemRectTransform)
            {
                // Make it fully stretch in the slot
                newItemRT.pivot = new Vector2(0.5f, 0.5f);
                newItemRT.anchorMin = Vector2.zero;
                newItemRT.anchorMax = Vector2.one;
                newItemRT.offsetMin = Vector2.zero;
                newItemRT.offsetMax = Vector2.zero;
            }

            itemsAtSlot[slotIndex] = newItem;

            // Initialize the item
            var data = DataSource[slotIndex];
            newItem.Init(data);
            OnItemCreated(newItem, data);
            OnCreateItem?.Invoke(newItem, data);
        }

        /// <summary>
        /// Destroys an item at the specified slot index if one exists.
        /// </summary>
        private void DestroyItemAt(int slotIndex)
        {
            if (!itemsAtSlot.TryGetValue(slotIndex, out TGameObject existingItem)) return;

            itemsAtSlot.Remove(slotIndex);
            var data = (slotIndex >= 0 && slotIndex < DataSource.Count) ? DataSource[slotIndex] : null;

            OnItemRemoved(existingItem, data);
            OnRemoveItem?.Invoke(existingItem, data);

            Pool.Destroy(existingItem);
        }

        /// <summary>
        /// Computes the viewport rectangle in canvas space, extended by <see cref="ItemBufferCount"/> slots.
        /// </summary>
        private void UpdateViewportRect()
        {
            if (!MainCanvas)
            {
                // Try a fallback
                MainCanvas = GetComponentInParent<Canvas>();
                if (!MainCanvas) return;
            }

            if (!ScrollRect || !ScrollRect.viewport) return;

            RectTransform viewport = ScrollRect.viewport;
            viewport.GetWorldCorners(bufferCorners);
            for (int j = 0; j < 4; j++)
            {
                // Convert each corner to canvas space
                bufferCorners[j] = MainCanvas.transform.InverseTransformPoint(bufferCorners[j]);
            }

            float minX = Mathf.Min(bufferCorners[0].x, bufferCorners[1].x, bufferCorners[2].x, bufferCorners[3].x);
            float maxX = Mathf.Max(bufferCorners[0].x, bufferCorners[1].x, bufferCorners[2].x, bufferCorners[3].x);
            float minY = Mathf.Min(bufferCorners[0].y, bufferCorners[1].y, bufferCorners[2].y, bufferCorners[3].y);
            float maxY = Mathf.Max(bufferCorners[0].y, bufferCorners[1].y, bufferCorners[2].y, bufferCorners[3].y);

            float width = maxX - minX;
            float height = maxY - minY;

            if (slots.Count > 0)
            {
                // Estimate slot size from the first slot
                slots[0].GetWorldCorners(bufferCorners);
                for (int j = 0; j < 4; j++)
                {
                    bufferCorners[j] = MainCanvas.transform.InverseTransformPoint(bufferCorners[j]);
                }

                float slotWidth = Mathf.Abs(bufferCorners[2].x - bufferCorners[1].x);
                float slotHeight = Mathf.Abs(bufferCorners[1].y - bufferCorners[0].y);

                // Expand by buffer
                minX -= (ItemBufferCount.x * slotWidth);
                minY -= (ItemBufferCount.y * slotHeight);
                width += (ItemBufferCount.x * slotWidth * 2);
                height += (ItemBufferCount.y * slotHeight * 2);
            }

            viewportRect = new Rect(minX, minY, width, height);
        }

        /// <summary>
        /// Checks if the given slot is within the expanded viewport bounds.
        /// </summary>
        private bool IsVisibleInExtendedViewport(RectTransform slotRect, int slotIndex)
        {
            if (!slotRect) return false;

            // Calculate the bounding box of the slot (including its children) in the same (content) space.
            Bounds slotBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                ScrollRect.content,
                slotRect
            );

            // Use 3D bounds intersection (z will usually be 0 for UI), which effectively becomes a 2D overlap.
            return extendedViewportBounds.Intersects(slotBounds);
        }

        #endregion
    }
}