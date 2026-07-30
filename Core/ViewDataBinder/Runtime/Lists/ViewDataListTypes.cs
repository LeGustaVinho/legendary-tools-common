using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LegendaryTools.ViewBinding
{
    public enum ViewDataListSurplusPolicy
    {
        Destroy = 0,
        Deactivate = 1
    }

    public enum ViewDataListCreationMode
    {
        Immediate = 0,
        Batched = 1
    }

    public enum ViewDataListSortDirection
    {
        Ascending = 0,
        Descending = 1
    }

    public enum ViewDataListNullOrder
    {
        First = 0,
        Last = 1
    }

    public enum ViewDataListLayoutMode
    {
        Vertical = 0,
        Horizontal = 1,
        Grid = 2
    }

    public enum ViewDataListBindStatus
    {
        Success = 0,
        Scheduled = 1,
        Cancelled = 2,
        InvalidConfiguration = 3,
        SourceError = 4
    }

    [Serializable]
    public sealed class ViewDataListBindResult
    {
        public ViewDataListBindResult(
            ViewDataListBindStatus status,
            string message,
            int sourceCount,
            int projectedCount,
            int activeCount)
        {
            Status = status;
            Message = message ?? string.Empty;
            SourceCount = sourceCount;
            ProjectedCount = projectedCount;
            ActiveCount = activeCount;
        }

        public ViewDataListBindStatus Status { get; }
        public string Message { get; }
        public int SourceCount { get; }
        public int ProjectedCount { get; }
        public int ActiveCount { get; }
        public bool IsSuccess =>
            Status == ViewDataListBindStatus.Success ||
            Status == ViewDataListBindStatus.Scheduled;
    }

    [Serializable]
    public sealed class ViewDataListItemContext
    {
        internal ViewDataListItemContext()
        {
        }

        public ViewDataListBinder Binder { get; internal set; }
        public object Item { get; internal set; }
        public object ParentItem { get; internal set; }
        public object Key { get; internal set; }
        public int Index { get; internal set; }
        public int SourceIndex { get; internal set; }
        public int Count { get; internal set; }
        public bool IsFirst => Index == 0 && Count > 0;
        public bool IsLast => Index == Count - 1 && Count > 0;
        public GameObject GameObject { get; internal set; }
    }

    public interface IViewDataListItem
    {
        void OnListItemCreated(ViewDataListItemContext context);
        void OnListItemBound(ViewDataListItemContext context);
        void OnListItemUnbound(ViewDataListItemContext context);
        void OnListItemVisibilityChanged(ViewDataListItemContext context, bool visible);
        void OnListItemDestroyed(ViewDataListItemContext context);
    }

    [Serializable]
    public sealed class ViewDataListItemUnityEvent : UnityEvent<ViewDataListItemContext>
    {
    }

    [Serializable]
    public sealed class ViewDataListItemVisibilityUnityEvent :
        UnityEvent<ViewDataListItemContext, bool>
    {
    }

    public sealed class ViewDataListDuplicateKey : IEquatable<ViewDataListDuplicateKey>
    {
        public ViewDataListDuplicateKey(object baseKey, int occurrence)
        {
            BaseKey = baseKey;
            Occurrence = occurrence;
        }

        public object BaseKey { get; }
        public int Occurrence { get; }

        public bool Equals(ViewDataListDuplicateKey other) =>
            other != null && Equals(BaseKey, other.BaseKey) && Occurrence == other.Occurrence;

        public override bool Equals(object obj) => Equals(obj as ViewDataListDuplicateKey);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((BaseKey?.GetHashCode() ?? 0) * 397) ^ Occurrence;
            }
        }

        public override string ToString() => $"{BaseKey}#{Occurrence}";
    }

    public sealed class ViewDataListReferenceKey : IEquatable<ViewDataListReferenceKey>
    {
        public ViewDataListReferenceKey(object value) { Value = value; }
        public object Value { get; }
        public bool Equals(ViewDataListReferenceKey other) =>
            other != null && ReferenceEquals(Value, other.Value);
        public override bool Equals(object obj) => Equals(obj as ViewDataListReferenceKey);
        public override int GetHashCode() =>
            Value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Value);
        public override string ToString() => Value?.ToString() ?? "null";
    }

    public readonly struct ViewDataListIndexKey : IEquatable<ViewDataListIndexKey>
    {
        public ViewDataListIndexKey(int index) { Index = index; }
        public int Index { get; }
        public bool Equals(ViewDataListIndexKey other) => Index == other.Index;
        public override bool Equals(object obj) =>
            obj is ViewDataListIndexKey other && Equals(other);
        public override int GetHashCode() => Index;
        public override string ToString() => $"Index:{Index}";
    }

    [Serializable]
    public sealed class ViewDataListPredicate
    {
        [Tooltip("Enables this Inspector predicate. A disabled predicate accepts every item.")]
        [SerializeField] private bool enabled;
        [Tooltip("Item endpoints referenced by the predicate clauses.")]
        [SerializeField] private List<BindingSource> sources =
            new List<BindingSource> { new BindingSource() };
        [Tooltip("Boolean condition evaluated from the configured Sources.")]
        [SerializeField] private EventBindingCondition condition = new EventBindingCondition();

        public bool Enabled { get => enabled; set => enabled = value; }
        public IReadOnlyList<BindingSource> Sources => sources;
        public EventBindingCondition Condition => condition;

        public int AddSource(BindingSource source)
        {
            sources.Add(source ?? throw new ArgumentNullException(nameof(source)));
            return sources.Count - 1;
        }

        public void ClearSources()
        {
            sources.Clear();
        }

        public bool RemoveSourceAt(int index)
        {
            if (index < 0 || index >= sources.Count) return false;
            sources.RemoveAt(index);
            return true;
        }
    }

    [Serializable]
    public sealed class ViewDataListSortDescriptor
    {
        [Tooltip("Item member whose value is used by this sort level.")]
        [SerializeField] private BindingEndpoint endpoint = new BindingEndpoint();
        [Tooltip("Sort direction for non-null values.")]
        [SerializeField] private ViewDataListSortDirection direction;
        [Tooltip("Controls whether null values appear before or after non-null values, independently of direction.")]
        [SerializeField] private ViewDataListNullOrder nullOrder = ViewDataListNullOrder.Last;

        public BindingEndpoint Endpoint
        {
            get => endpoint;
            set => endpoint = value ?? new BindingEndpoint();
        }

        public ViewDataListSortDirection Direction
        {
            get => direction;
            set => direction = value;
        }

        public ViewDataListNullOrder NullOrder
        {
            get => nullOrder;
            set => nullOrder = value;
        }
    }

    [Serializable]
    public sealed class ViewDataListPrefabRule
    {
        [Tooltip("Display name used by diagnostics and the Inspector.")]
        [SerializeField] private string name = "Prefab Rule";
        [Tooltip("Disabled rules are skipped.")]
        [SerializeField] private bool enabled = true;
        [Tooltip("Predicate evaluated for this rule. A disabled predicate makes the rule unconditional.")]
        [SerializeField] private ViewDataListPredicate predicate = new ViewDataListPredicate();
        [Tooltip("Prefab or child scene template selected when this rule is the first match.")]
        [SerializeField] private GameObject prefab;

        public string Name { get => name; set => name = value ?? string.Empty; }
        public bool Enabled { get => enabled; set => enabled = value; }
        public ViewDataListPredicate Predicate => predicate;
        public GameObject Prefab { get => prefab; set => prefab = value; }
    }

    [Serializable]
    public sealed class ViewDataListVirtualizationSettings
    {
        [Tooltip("Creates only the visible uniform-size item window plus the configured buffer.")]
        [SerializeField] private bool enabled;
        [Tooltip("ScrollRect whose content is the binder Target Parent.")]
        [SerializeField] private ScrollRect scrollRect;
        [Tooltip("Vertical, horizontal or vertically scrolling uniform grid layout.")]
        [SerializeField] private ViewDataListLayoutMode layoutMode;
        [Tooltip("Uniform width and height of every virtualized item.")]
        [SerializeField] private Vector2 itemSize = new Vector2(100f, 30f);
        [Tooltip("Horizontal and vertical spacing between virtualized items.")]
        [SerializeField] private Vector2 spacing;
        [Tooltip("Content padding used for size, positioning and visible-range calculations.")]
        [SerializeField] private RectOffset padding = new RectOffset();
        [Tooltip("Number of columns used by Grid mode.")]
        [SerializeField, Min(1)] private int gridConstraint = 1;
        [Tooltip("Extra rows or items retained before and after the visible window.")]
        [SerializeField, Min(0)] private int bufferItems = 2;

        public bool Enabled { get => enabled; set => enabled = value; }
        public ScrollRect ScrollRect { get => scrollRect; set => scrollRect = value; }
        public ViewDataListLayoutMode LayoutMode { get => layoutMode; set => layoutMode = value; }
        public Vector2 ItemSize { get => itemSize; set => itemSize = value; }
        public Vector2 Spacing { get => spacing; set => spacing = value; }
        public RectOffset Padding => padding;
        public int GridConstraint { get => Mathf.Max(1, gridConstraint); set => gridConstraint = Mathf.Max(1, value); }
        public int BufferItems { get => Mathf.Max(0, bufferItems); set => bufferItems = Mathf.Max(0, value); }
    }

    public sealed class ViewDataListStatistics
    {
        public int Refreshes { get; internal set; }
        public int Created { get; internal set; }
        public int Reused { get; internal set; }
        public int Bound { get; internal set; }
        public int Unbound { get; internal set; }
        public int Destroyed { get; internal set; }
        public int Filtered { get; internal set; }
        public int NullItemsSkipped { get; internal set; }

        public void Reset()
        {
            Refreshes = Created = Reused = Bound = Unbound = Destroyed = Filtered = NullItemsSkipped = 0;
        }
    }
}
