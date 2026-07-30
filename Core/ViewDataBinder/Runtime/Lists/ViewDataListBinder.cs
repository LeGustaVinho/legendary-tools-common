using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using LegendaryTools.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace LegendaryTools.ViewBinding
{
    [AddComponentMenu("Legendary Tools/View Data List Binder")]
    [DefaultExecutionOrder(-1000)]
    public sealed class ViewDataListBinder : BindingPollingBehaviour
    {
        [Header("Source")]
        [Tooltip("Endpoint that resolves the collection. The value must implement IEnumerable and cannot be a string.")]
        [SerializeField] private BindingEndpoint source = new BindingEndpoint();
        [Tooltip("Controls when the configured Source is polled. Manual disables automatic polling.")]
        [SerializeField] private BindingUpdateTiming updateTiming = BindingUpdateTiming.Manual;
        [Tooltip("Controls how collection, projection and binding errors are reported.")]
        [SerializeField] private BindingErrorPolicy errorPolicy = BindingErrorPolicy.ReportOnly;

        [Header("Items")]
        [Tooltip("Required parent for generated items. With virtualization, this must be the ScrollRect content.")]
        [SerializeField] private Transform targetParent;
        [Tooltip("Fallback prefab. It may be a prefab asset or a scene template under Target Parent.")]
        [SerializeField] private GameObject defaultPrefab;
        [Tooltip("Determines whether surplus GameObjects are destroyed or deactivated and pooled.")]
        [SerializeField] private ViewDataListSurplusPolicy surplusPolicy =
            ViewDataListSurplusPolicy.Deactivate;
        [Tooltip("Recently pooled GameObjects reserved for their stable keys. Older entries may be reused by another key. Zero disables reservations.")]
        [SerializeField, Min(0)] private int reservedKeyCapacity = 64;

        [Header("Projection")]
        [Tooltip("Number of items to skip. Zero starts at the first projected item.")]
        [SerializeField, Min(0)] private int offset;
        [Tooltip("Number of items selected after Offset. Zero means all remaining items.")]
        [SerializeField, Min(0)] private int rangeCount;
        [Tooltip("Final item limit after Offset and Range Count. Zero means unlimited, not an empty list.")]
        [SerializeField, Min(0)] private int maxItems;
        [Tooltip("Optional stable key member. Without one, reference identity or source index is used.")]
        [SerializeField] private BindingEndpoint keyEndpoint = new BindingEndpoint();
        [Tooltip("Optional Inspector-configured item predicate.")]
        [SerializeField] private ViewDataListPredicate filter = new ViewDataListPredicate();
        [Tooltip("Stable sort descriptors in priority order.")]
        [SerializeField] private List<ViewDataListSortDescriptor> sorting =
            new List<ViewDataListSortDescriptor>();
        [Tooltip("Conditional prefab rules in evaluation order. The first matching rule wins.")]
        [SerializeField] private List<ViewDataListPrefabRule> prefabRules =
            new List<ViewDataListPrefabRule>();

        [Header("Creation")]
        [Tooltip("Immediate creates synchronously. Batched spreads creation across frames.")]
        [SerializeField] private ViewDataListCreationMode creationMode =
            ViewDataListCreationMode.Immediate;
        [Tooltip("Maximum items created before a batched operation yields to the next frame.")]
        [SerializeField, Min(1)] private int itemsPerFrame = 10;
        [Tooltip("Maximum creation time per frame in milliseconds. Zero disables the time budget.")]
        [SerializeField, Min(0f)] private float frameBudgetMilliseconds = 2f;
        [Tooltip("Uniform-size ScrollRect virtualization settings.")]
        [SerializeField] private ViewDataListVirtualizationSettings virtualization =
            new ViewDataListVirtualizationSettings();

        [Header("Lifecycle")]
        [Tooltip("Invoked when a bind or refresh begins.")]
        [SerializeField] private UnityEvent refreshStarted = new UnityEvent();
        [Tooltip("Invoked when a bind or refresh succeeds, fails or is cancelled.")]
        [SerializeField] private UnityEvent refreshCompleted = new UnityEvent();
        [Tooltip("Invoked once for each newly created GameObject instance.")]
        [SerializeField] private ViewDataListItemUnityEvent itemCreated =
            new ViewDataListItemUnityEvent();
        [Tooltip("Invoked whenever an instance is bound or rebound to an item.")]
        [SerializeField] private ViewDataListItemUnityEvent itemBound =
            new ViewDataListItemUnityEvent();
        [Tooltip("Invoked before an instance leaves the active projection.")]
        [SerializeField] private ViewDataListItemUnityEvent itemUnbound =
            new ViewDataListItemUnityEvent();
        [Tooltip("Invoked on actual visibility transitions. The second argument is the new visibility.")]
        [SerializeField] private ViewDataListItemVisibilityUnityEvent itemVisibilityChanged =
            new ViewDataListItemVisibilityUnityEvent();
        [Tooltip("Invoked after a surplus instance is deactivated and returned to the pool.")]
        [SerializeField] private ViewDataListItemUnityEvent itemDeactivated =
            new ViewDataListItemUnityEvent();
        [Tooltip("Invoked before an item GameObject is destroyed.")]
        [SerializeField] private ViewDataListItemUnityEvent itemDestroyed =
            new ViewDataListItemUnityEvent();

        private readonly BindingContextResolver contextResolver = new BindingContextResolver();
        private readonly Dictionary<EffectiveKey, ItemInstance> active =
            new Dictionary<EffectiveKey, ItemInstance>();
        private readonly Dictionary<GameObject, Stack<GameObject>> inactiveByPrefab =
            new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Dictionary<GameObject, ViewDataListItemContext> inactiveContexts =
            new Dictionary<GameObject, ViewDataListItemContext>();
        private readonly Dictionary<EffectiveKey, ItemInstance> inactiveByKey =
            new Dictionary<EffectiveKey, ItemInstance>();
        private readonly Dictionary<GameObject, EffectiveKey> inactiveKeys =
            new Dictionary<GameObject, EffectiveKey>();
        private readonly Queue<GameObject> inactiveReservationOrder =
            new Queue<GameObject>();
        private readonly HashSet<GameObject> queuedReservations =
            new HashSet<GameObject>();
        private readonly Dictionary<GameObject, TemplateState> templates =
            new Dictionary<GameObject, TemplateState>();
        private readonly Dictionary<GameObject, ItemHierarchyCache> itemHierarchyCaches =
            new Dictionary<GameObject, ItemHierarchyCache>();
        private readonly List<ProjectedItem> projection = new List<ProjectedItem>();
        private readonly List<ProjectedItem> projectionBuffer = new List<ProjectedItem>();
        private readonly List<ProjectedItem> nextProjection = new List<ProjectedItem>();
        private readonly List<object> sortValuesBuffer = new List<object>();
        private readonly List<object> projectedItems = new List<object>();
        private readonly List<ViewDataListItemContext> activeItems =
            new List<ViewDataListItemContext>();
        private readonly Dictionary<object, int> keyOccurrences =
            new Dictionary<object, int>();
        private readonly HashSet<EffectiveKey> desiredKeys = new HashSet<EffectiveKey>();
        private readonly List<EffectiveKey> removeKeys = new List<EffectiveKey>();
        private readonly List<GameObject> reservedPoolBuffer = new List<GameObject>();
        private readonly Dictionary<int, PredicateBuffers> predicateBuffersByCount =
            new Dictionary<int, PredicateBuffers>();
        private readonly ViewDataListStatistics statistics = new ViewDataListStatistics();
        private IEnumerable itemsOverride;
        private bool hasItemsOverride;
        private Coroutine batchRoutine;
        private int generation;
        private GameObject stagingObject;
        private Transform staging;
        private ViewDataListBindResult lastResult;
        private string lastLoggedError;
        private bool projectionChanged;
        private int lastSourceCount;
        private object projectionParentItem;

        public event Action RefreshStarted;
        public event Action<ViewDataListBindResult> RefreshCompleted;
        public event Action<ViewDataListItemContext> ItemCreated;
        public event Action<ViewDataListItemContext> ItemBound;
        public event Action<ViewDataListItemContext> ItemUnbound;
        public event Action<ViewDataListItemContext, bool> ItemVisibilityChanged;
        public event Action<ViewDataListItemContext> ItemDeactivated;
        public event Action<ViewDataListItemContext> ItemDestroyed;

        public Predicate<object> Filter { get; set; }
        public IComparer<object> Comparer { get; set; }
        public Func<object, object> KeySelector { get; set; }
        public Func<ViewDataListItemContext, GameObject> PrefabResolver { get; set; }

        public BindingEndpoint Source { get => source; set => source = value ?? new BindingEndpoint(); }
        public BindingUpdateTiming UpdateTiming { get => updateTiming; set { updateTiming = value; RebuildExecutionPlan(); } }
        public BindingErrorPolicy ErrorPolicy { get => errorPolicy; set => errorPolicy = value; }
        public Transform TargetParent { get => targetParent; set { targetParent = value; RebuildExecutionPlan(); } }
        public GameObject DefaultPrefab { get => defaultPrefab; set { defaultPrefab = value; RebuildExecutionPlan(); } }
        public ViewDataListSurplusPolicy SurplusPolicy { get => surplusPolicy; set => surplusPolicy = value; }
        public int ReservedKeyCapacity
        {
            get => Mathf.Max(0, reservedKeyCapacity);
            set
            {
                reservedKeyCapacity = Mathf.Max(0, value);
                TrimInactiveReservations();
            }
        }
        public int Offset { get => Mathf.Max(0, offset); set => offset = Mathf.Max(0, value); }
        public int RangeCount { get => Mathf.Max(0, rangeCount); set => rangeCount = Mathf.Max(0, value); }
        public int MaxItems { get => Mathf.Max(0, maxItems); set => maxItems = Mathf.Max(0, value); }
        public BindingEndpoint KeyEndpoint { get => keyEndpoint; set => keyEndpoint = value ?? new BindingEndpoint(); }
        public ViewDataListPredicate InspectorFilter => filter;
        public IReadOnlyList<ViewDataListSortDescriptor> Sorting => sorting;
        public IReadOnlyList<ViewDataListPrefabRule> PrefabRules => prefabRules;
        public ViewDataListCreationMode CreationMode { get => creationMode; set => creationMode = value; }
        public int ItemsPerFrame { get => Mathf.Max(1, itemsPerFrame); set => itemsPerFrame = Mathf.Max(1, value); }
        public float FrameBudgetMilliseconds { get => Mathf.Max(0f, frameBudgetMilliseconds); set => frameBudgetMilliseconds = Mathf.Max(0f, value); }
        public ViewDataListVirtualizationSettings Virtualization => virtualization;
        public IReadOnlyList<object> ProjectedItems => projectedItems;
        public IReadOnlyList<ViewDataListItemContext> ActiveItems => activeItems;
        public int ProjectedCount => projection.Count;
        public ViewDataListBindResult LastResult => lastResult;
        public string LastError => lastResult != null && !lastResult.IsSuccess
            ? lastResult.Message
            : string.Empty;
        public ViewDataListStatistics Statistics => statistics;

        public int AddSort(ViewDataListSortDescriptor descriptor)
        {
            sorting.Add(descriptor ?? throw new ArgumentNullException(nameof(descriptor)));
            return sorting.Count - 1;
        }

        public int AddPrefabRule(ViewDataListPrefabRule rule)
        {
            prefabRules.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
            return prefabRules.Count - 1;
        }

        public void ClearSorting() => sorting.Clear();
        public void ClearPrefabRules() => prefabRules.Clear();
        public bool RemoveSortAt(int index)
        {
            if (index < 0 || index >= sorting.Count) return false;
            sorting.RemoveAt(index);
            return true;
        }
        public bool RemovePrefabRuleAt(int index)
        {
            if (index < 0 || index >= prefabRules.Count) return false;
            prefabRules.RemoveAt(index);
            return true;
        }

        public void SetItemsOverride(IEnumerable items)
        {
            itemsOverride = items;
            hasItemsOverride = true;
        }

        public void ClearItemsOverride()
        {
            itemsOverride = null;
            hasItemsOverride = false;
        }

        public ViewDataListBindResult Bind(IEnumerable items)
        {
            return BeginBind(
                items,
                creationMode == ViewDataListCreationMode.Immediate,
                true,
                true);
        }

        public ViewDataListBindResult BindImmediate(IEnumerable items)
        {
            return BeginBind(items, true, true, true);
        }

        public ViewDataListBindResult Refresh()
        {
            return RefreshConfigured(
                creationMode == ViewDataListCreationMode.Immediate);
        }

        public ViewDataListBindResult RefreshImmediate()
        {
            return RefreshConfigured(true);
        }

        public void CancelPendingRefresh()
        {
            generation++;
            if (batchRoutine != null)
            {
                StopCoroutine(batchRoutine);
                batchRoutine = null;
                int sourceCount = lastResult?.SourceCount ?? 0;
                lastResult = new ViewDataListBindResult(
                    ViewDataListBindStatus.Cancelled,
                    "The pending list refresh was cancelled.",
                    sourceCount,
                    projection.Count,
                    active.Count);
                refreshCompleted.Invoke();
                RefreshCompleted?.Invoke(lastResult);
            }
        }

        public void RebuildExecutionPlan()
        {
            CancelPendingRefresh();
            contextResolver.Invalidate();
            EnsureInfrastructure();
            ConfigureScrollListener();
            RefreshScheduledRegistration();
        }

        protected override void PrepareRuntime()
        {
            RebuildExecutionPlan();
        }

        protected override bool HasBindingsForTiming(BindingUpdateTiming timing)
        {
            return updateTiming == timing;
        }

        protected override void ProcessBindingTiming(BindingUpdateTiming timing)
        {
            if (timing == updateTiming && batchRoutine == null)
            {
                CancelPendingRefresh();
                BeginRefreshLifecycle();
                if (TryResolveItems(out IEnumerable items, out string error))
                {
                    BeginBind(
                        items,
                        creationMode == ViewDataListCreationMode.Immediate,
                        false,
                        false);
                }
                else
                {
                    Fail(ViewDataListBindStatus.SourceError, error, 0, 0);
                }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CancelPendingRefresh();
            RemoveScrollListener();
        }

        private void OnDestroy()
        {
            CancelPendingRefresh();
            RemoveScrollListener();
            DestroyAllInstances();
            if (stagingObject != null)
            {
                DestroySafely(stagingObject);
            }
        }

        private ViewDataListBindResult BeginBind(
            IEnumerable items,
            bool immediate,
            bool forceRebind,
            bool beginLifecycle)
        {
            CancelPendingRefresh();
            if (beginLifecycle)
            {
                BeginRefreshLifecycle();
            }

            if (items is string)
            {
                return Fail(
                    ViewDataListBindStatus.SourceError,
                    "String is not a supported list collection.",
                    0,
                    0);
            }

            if (!TryValidateConfiguration(out string configurationError))
            {
                return Fail(
                    ViewDataListBindStatus.InvalidConfiguration,
                    configurationError,
                    0,
                    0);
            }

            EnsureInfrastructure();
            if (!TryBuildProjection(items, out int sourceCount, out string error))
            {
                return Fail(ViewDataListBindStatus.SourceError, error, sourceCount, 0);
            }
            lastSourceCount = sourceCount;

            if (!projectionChanged && !forceRebind)
            {
                return Complete(ViewDataListBindStatus.Success, string.Empty, sourceCount);
            }

            int token = ++generation;
            if (immediate || !isActiveAndEnabled)
            {
                Reconcile(token, false, forceRebind);
                return Complete(ViewDataListBindStatus.Success, string.Empty, sourceCount);
            }

            lastResult = new ViewDataListBindResult(
                ViewDataListBindStatus.Scheduled,
                string.Empty,
                sourceCount,
                projection.Count,
                active.Count);
            batchRoutine = StartCoroutine(
                ReconcileBatched(token, sourceCount, true, forceRebind));
            return lastResult;
        }

        private ViewDataListBindResult RefreshConfigured(bool immediate)
        {
            CancelPendingRefresh();
            BeginRefreshLifecycle();
            return TryResolveItems(out IEnumerable items, out string error)
                ? BeginBind(items, immediate, true, false)
                : Fail(ViewDataListBindStatus.SourceError, error, 0, 0);
        }

        private void BeginRefreshLifecycle()
        {
            refreshStarted.Invoke();
            RefreshStarted?.Invoke();
            statistics.Refreshes++;
        }

        private bool TryResolveItems(out IEnumerable items, out string error)
        {
            error = string.Empty;
            if (hasItemsOverride)
            {
                items = itemsOverride;
                return true;
            }

            using (BindingResolutionScope.Push(this, contextResolver, null))
            {
                if (source == null ||
                    !BindingBackendRegistry.MemberBackend.TryRead(source, out object value, out error))
                {
                    items = null;
                    if (source == null)
                    {
                        error = "The List Source endpoint is null.";
                    }
                    return false;
                }

                if (value is string || !(value is IEnumerable enumerable))
                {
                    items = null;
                    error = "The List Source must resolve to IEnumerable and cannot be a string.";
                    return false;
                }

                items = enumerable;
                return true;
            }
        }

        private bool TryBuildProjection(IEnumerable items, out int sourceCount, out string error)
        {
            projectionBuffer.Clear();
            sortValuesBuffer.Clear();
            projectionParentItem = FindItemContext(targetParent);
            sourceCount = 0;
            error = string.Empty;
            if (items == null)
            {
                projectionChanged = projection.Count != 0;
                projection.Clear();
                projectedItems.Clear();
                return true;
            }

            keyOccurrences.Clear();
            try
            {
                foreach (object item in items)
                {
                    int sourceIndex = sourceCount++;
                    if (IsNull(item))
                    {
                        statistics.NullItemsSkipped++;
                        ReportWarning("A null list item was ignored.");
                        continue;
                    }

                    if (!TryResolveKey(item, sourceIndex, out object baseKey, out error))
                    {
                        return false;
                    }
                    if (baseKey == null)
                    {
                        baseKey = new ViewDataListIndexKey(sourceIndex);
                        ReportWarning($"Item at index {sourceIndex} produced a null key; index fallback was used.");
                    }

                    keyOccurrences.TryGetValue(baseKey, out int occurrence);
                    keyOccurrences[baseKey] = occurrence + 1;
                    if (occurrence > 0)
                    {
                        ReportWarning($"Duplicate list key '{baseKey}' was disambiguated by occurrence.");
                    }

                    if (!TryFilter(item, out bool accepted, out error))
                    {
                        return false;
                    }
                    if (!accepted)
                    {
                        statistics.Filtered++;
                        continue;
                    }
                    if (!TryResolveSortValues(item, out int sortValuesOffset, out error))
                    {
                        return false;
                    }

                    var projected = new ProjectedItem
                    {
                        Item = item,
                        SourceIndex = sourceIndex,
                        Key = new EffectiveKey(baseKey, occurrence),
                        StableOrder = sourceIndex,
                        SortValuesOffset = sortValuesOffset
                    };
                    projectionBuffer.Add(projected);
                }
            }
            catch (Exception exception)
            {
                error = $"Collection enumeration failed: {exception.Message}";
                return false;
            }

            try
            {
                if (Comparer != null)
                {
                    projectionBuffer.Sort((a, b) =>
                    {
                        int comparison = Comparer.Compare(a.Item, b.Item);
                        return comparison != 0 ? comparison : a.StableOrder.CompareTo(b.StableOrder);
                    });
                }
                else if (sorting.Count > 0)
                {
                    projectionBuffer.Sort(CompareProjected);
                }

                int start = Mathf.Min(Offset, projectionBuffer.Count);
                int count = rangeCount == 0
                    ? projectionBuffer.Count - start
                    : Mathf.Min(RangeCount, projectionBuffer.Count - start);
                if (maxItems > 0)
                {
                    count = Mathf.Min(count, MaxItems);
                }

                nextProjection.Clear();
                if (nextProjection.Capacity < count)
                {
                    nextProjection.Capacity = count;
                }
                for (int i = 0; i < count; i++)
                {
                    ProjectedItem projected = projectionBuffer[start + i];
                    projected.Index = i;
                    if (!TryResolvePrefab(projected, count, out GameObject prefab, out error))
                    {
                        return false;
                    }
                    projected.Prefab = prefab;
                    nextProjection.Add(projected);
                }

                projectionChanged = !ProjectionMatches(nextProjection);
                projection.Clear();
                projection.AddRange(nextProjection);
                projectedItems.Clear();
                for (int i = 0; i < projection.Count; i++)
                {
                    projectedItems.Add(projection[i].Item);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = $"List projection failed: {exception.Message}";
                return false;
            }
        }

        private bool ProjectionMatches(List<ProjectedItem> next)
        {
            if (projection.Count != next.Count)
            {
                return false;
            }

            for (int i = 0; i < next.Count; i++)
            {
                ProjectedItem current = projection[i];
                ProjectedItem candidate = next[i];
                bool sameItem = current.Item.GetType().IsValueType
                    ? Equals(current.Item, candidate.Item)
                    : ReferenceEquals(current.Item, candidate.Item);
                if (!current.Key.Equals(candidate.Key) ||
                    !sameItem ||
                    current.Prefab != candidate.Prefab ||
                    current.SourceIndex != candidate.SourceIndex)
                {
                    return false;
                }
            }
            return true;
        }

        private PredicateBuffers GetPredicateBuffers(int count)
        {
            if (!predicateBuffersByCount.TryGetValue(count, out PredicateBuffers buffers))
            {
                buffers = new PredicateBuffers(count);
                predicateBuffersByCount.Add(count, buffers);
            }
            return buffers;
        }

        private bool TryFilter(object item, out bool accepted, out string error)
        {
            if (Filter != null)
            {
                accepted = Filter(item);
                error = string.Empty;
                return true;
            }
            return TryEvaluatePredicate(filter, item, out accepted, out error);
        }

        private bool TryEvaluatePredicate(
            ViewDataListPredicate predicate,
            object item,
            out bool accepted,
            out string error)
        {
            accepted = true;
            error = string.Empty;
            if (predicate == null || !predicate.Enabled ||
                predicate.Condition == null || !predicate.Condition.Enabled)
            {
                return true;
            }

            int count = predicate.Sources?.Count ?? 0;
            if (count == 0)
            {
                return true;
            }

            PredicateBuffers buffers = GetPredicateBuffers(count);
            object[] values = buffers.Values;
            BindingMemberMetadata[] metadata = buffers.Metadata;
            try
            {
                using (BindingResolutionScope.Push(this, contextResolver, null))
                {
                    for (int i = 0; i < count; i++)
                    {
                        BindingEndpoint endpoint = predicate.Sources[i]?.Endpoint;
                        PrepareItemEndpoint(endpoint, item);
                        if (!BindingBackendRegistry.MemberBackend.TryGetMetadata(endpoint, out metadata[i], out error) ||
                            !BindingBackendRegistry.MemberBackend.TryRead(endpoint, out values[i], out error))
                        {
                            return false;
                        }
                    }
                }

                return EventBindingConditionEvaluator.TryEvaluate(
                    predicate.Condition,
                    values,
                    metadata,
                    out accepted,
                    out error);
            }
            finally
            {
                Array.Clear(values, 0, count);
                Array.Clear(metadata, 0, count);
            }
        }

        private bool TryResolveKey(
            object item,
            int sourceIndex,
            out object key,
            out string error)
        {
            error = string.Empty;
            if (KeySelector != null)
            {
                key = KeySelector(item);
                return true;
            }
            if (keyEndpoint != null && !string.IsNullOrWhiteSpace(keyEndpoint.MemberPath))
            {
                PrepareItemEndpoint(keyEndpoint, item);
                using (BindingResolutionScope.Push(this, contextResolver, null))
                {
                    return BindingBackendRegistry.MemberBackend.TryRead(
                        keyEndpoint,
                        out key,
                        out error);
                }
            }
            key = item.GetType().IsValueType
                ? (object)new ViewDataListIndexKey(sourceIndex)
                : new ViewDataListReferenceKey(item);
            return true;
        }

        private bool TryResolveSortValues(
            object item,
            out int valuesOffset,
            out string error)
        {
            error = string.Empty;
            valuesOffset = sortValuesBuffer.Count;
            if (sorting.Count == 0)
            {
                return true;
            }
            using (BindingResolutionScope.Push(this, contextResolver, null))
            {
                for (int i = 0; i < sorting.Count; i++)
                {
                    BindingEndpoint endpoint = sorting[i]?.Endpoint;
                    PrepareItemEndpoint(endpoint, item);
                    if (endpoint == null ||
                        !BindingBackendRegistry.MemberBackend.TryRead(
                            endpoint,
                            out object value,
                            out error))
                    {
                        error = $"Sort {i + 1}: {error}";
                        return false;
                    }
                    sortValuesBuffer.Add(value);
                }
            }
            return true;
        }

        private int CompareProjected(ProjectedItem left, ProjectedItem right)
        {
            for (int i = 0; i < sorting.Count; i++)
            {
                ViewDataListSortDescriptor descriptor = sorting[i];
                object a = sortValuesBuffer[left.SortValuesOffset + i];
                object b = sortValuesBuffer[right.SortValuesOffset + i];
                int result;
                bool comparedNull = IsNull(a) || IsNull(b);
                if (comparedNull)
                {
                    result = IsNull(a) && IsNull(b)
                        ? 0
                        : IsNull(a)
                            ? (descriptor.NullOrder == ViewDataListNullOrder.First ? -1 : 1)
                            : (descriptor.NullOrder == ViewDataListNullOrder.First ? 1 : -1);
                }
                else if (a is IComparable comparable)
                {
                    try { result = comparable.CompareTo(b); }
                    catch { result = string.CompareOrdinal(a.ToString(), b.ToString()); }
                }
                else
                {
                    result = string.CompareOrdinal(a.ToString(), b.ToString());
                }
                if (!comparedNull &&
                    descriptor.Direction == ViewDataListSortDirection.Descending)
                {
                    result = -result;
                }
                if (result != 0)
                {
                    return result;
                }
            }
            return left.StableOrder.CompareTo(right.StableOrder);
        }

        private bool TryResolvePrefab(
            ProjectedItem projected,
            int count,
            out GameObject prefab,
            out string error)
        {
            error = string.Empty;
            if (PrefabResolver != null)
            {
                ViewDataListItemContext previewContext = CreateContext(projected, count, null);
                prefab = PrefabResolver(previewContext) ?? defaultPrefab;
                return true;
            }
            for (int i = 0; i < prefabRules.Count; i++)
            {
                ViewDataListPrefabRule rule = prefabRules[i];
                if (rule == null || !rule.Enabled || rule.Prefab == null)
                {
                    continue;
                }
                if (!TryEvaluatePredicate(
                        rule.Predicate,
                        projected.Item,
                        out bool accepted,
                        out error))
                {
                    error = $"Prefab rule '{rule.Name}': {error}";
                    prefab = null;
                    return false;
                }
                if (accepted)
                {
                    prefab = rule.Prefab;
                    return true;
                }
            }
            prefab = defaultPrefab;
            return true;
        }

        private void Reconcile(int token, bool fromScroll, bool forceRebind)
        {
            if (token != generation)
            {
                return;
            }
            GetRequiredRange(out int first, out int last);
            desiredKeys.Clear();
            for (int i = first; i <= last; i++)
            {
                if (i >= 0 && i < projection.Count && projection[i].Prefab != null)
                {
                    desiredKeys.Add(projection[i].Key);
                }
            }
            RemoveUndesired(desiredKeys);
            for (int i = first; i <= last; i++)
            {
                if (i >= 0 && i < projection.Count)
                {
                    EnsureItem(projection[i], forceRebind);
                }
            }
            if (virtualization.Enabled)
            {
                UpdateContentSize();
            }
            if (!fromScroll)
            {
                ReorderNonVirtualized();
            }
            RefreshActiveItemsView();
        }

        private IEnumerator ReconcileBatched(
            int token,
            int sourceCount,
            bool completeRefresh,
            bool forceRebind = false)
        {
            yield return null;
            if (token != generation)
            {
                yield break;
            }
            GetRequiredRange(out int first, out int last);
            desiredKeys.Clear();
            for (int i = first; i <= last; i++)
            {
                if (i >= 0 && i < projection.Count && projection[i].Prefab != null)
                {
                    desiredKeys.Add(projection[i].Key);
                }
            }
            RemoveUndesired(desiredKeys);

            int frameCount = 0;
            var stopwatch = Stopwatch.StartNew();
            for (int i = first; i <= last; i++)
            {
                if (token != generation)
                {
                    yield break;
                }
                if (i >= 0 && i < projection.Count)
                {
                    EnsureItem(projection[i], forceRebind);
                }
                frameCount++;
                if (frameCount >= ItemsPerFrame ||
                    (FrameBudgetMilliseconds > 0f &&
                     stopwatch.Elapsed.TotalMilliseconds >= FrameBudgetMilliseconds))
                {
                    RefreshActiveItemsView();
                    frameCount = 0;
                    stopwatch.Restart();
                    yield return null;
                }
            }
            batchRoutine = null;
            UpdateContentSize();
            ReorderNonVirtualized();
            RefreshActiveItemsView();
            if (completeRefresh)
            {
                Complete(ViewDataListBindStatus.Success, string.Empty, sourceCount);
            }
            else
            {
                lastResult = new ViewDataListBindResult(
                    ViewDataListBindStatus.Success,
                    string.Empty,
                    sourceCount,
                    projection.Count,
                    active.Count);
            }
        }

        private void EnsureItem(ProjectedItem projected, bool forceRebind)
        {
            if (projected.Prefab == null)
            {
                ReportWarning($"No prefab resolved for item at projected index {projected.Index}.");
                return;
            }
            if (active.TryGetValue(projected.Key, out ItemInstance existing))
            {
                if (existing.Prefab == projected.Prefab)
                {
                    statistics.Reused++;
                    if (!forceRebind && !NeedsRebind(existing.Context, projected))
                    {
                        return;
                    }
                    BindInstance(existing, projected);
                    return;
                }
                ReleaseInstance(existing, false);
                active.Remove(projected.Key);
            }

            GameObject gameObject = Acquire(projected, out bool created);
            if (gameObject == null)
            {
                return;
            }
            var instance = new ItemInstance
            {
                GameObject = gameObject,
                Prefab = projected.Prefab,
                Key = projected.Key,
                Context = CreateContext(projected, projection.Count, gameObject)
            };
            active[projected.Key] = instance;
            EnsureItemHierarchyCache(gameObject);
            BindInstance(instance, projected, created);
        }

        private bool NeedsRebind(ViewDataListItemContext context, ProjectedItem projected)
        {
            if (context == null ||
                context.Index != projected.Index ||
                context.SourceIndex != projected.SourceIndex ||
                context.Count != projection.Count)
            {
                return true;
            }

            object currentItem = context.Item;
            object nextItem = projected.Item;
            return nextItem.GetType().IsValueType
                ? !Equals(currentItem, nextItem)
                : !ReferenceEquals(currentItem, nextItem);
        }

        private void BindInstance(ItemInstance instance, ProjectedItem projected, bool created = false)
        {
            ViewDataListItemContext context = instance.Context;
            ItemHierarchyCache hierarchy = EnsureItemHierarchyCache(instance.GameObject);
            context.Item = projected.Item;
            context.Key = GetPublishedKey(projected.Key);
            context.Index = projected.Index;
            context.SourceIndex = projected.SourceIndex;
            context.Count = projection.Count;
            context.GameObject = instance.GameObject;

            if (instance.GameObject.transform.parent != targetParent)
            {
                instance.GameObject.transform.SetParent(targetParent, false);
            }
            context.ParentItem = projectionParentItem;
            PublishContexts(hierarchy, context);
            InjectChildBinderInstances(hierarchy, context.Item);
            if (created)
            {
                InvokeCreated(hierarchy, context);
                itemCreated.Invoke(context);
                ItemCreated?.Invoke(context);
                statistics.Created++;
            }
            if (!instance.GameObject.activeSelf)
            {
                instance.GameObject.SetActive(true);
            }
            SynchronizeChildBinders(hierarchy);
            InvokeBound(hierarchy, context);
            itemBound.Invoke(context);
            ItemBound?.Invoke(context);
            statistics.Bound++;
            if (!instance.Visible)
            {
                instance.Visible = true;
                InvokeVisibilityChanged(hierarchy, context, true);
                itemVisibilityChanged.Invoke(context, true);
                ItemVisibilityChanged?.Invoke(context, true);
            }
            PositionVirtualItem(instance.GameObject.transform as RectTransform, projected.Index);
        }

        private GameObject Acquire(ProjectedItem projected, out bool created)
        {
            GameObject prefab = projected.Prefab;
            TemplateState template = EnsureTemplate(prefab);
            if (inactiveByKey.TryGetValue(projected.Key, out ItemInstance keyed) &&
                keyed != null &&
                keyed.GameObject != null &&
                keyed.Prefab == prefab)
            {
                RemoveInactiveRegistration(keyed.GameObject, projected.Key);
                created = false;
                return keyed.GameObject;
            }
            if (template != null && template.OriginalAvailable && template.Original != null)
            {
                template.OriginalAvailable = false;
                created = true;
                return template.Original;
            }
            if (inactiveByPrefab.TryGetValue(prefab, out Stack<GameObject> pool))
            {
                reservedPoolBuffer.Clear();
                GameObject selected = null;
                while (pool.Count > 0)
                {
                    GameObject pooled = pool.Pop();
                    if (pooled != null &&
                        inactiveContexts.ContainsKey(pooled))
                    {
                        if (!virtualization.Enabled && inactiveKeys.ContainsKey(pooled))
                        {
                            reservedPoolBuffer.Add(pooled);
                            continue;
                        }
                        if (inactiveKeys.TryGetValue(pooled, out EffectiveKey oldKey))
                        {
                            RemoveInactiveRegistration(pooled, oldKey);
                        }
                        selected = pooled;
                        break;
                    }
                }
                for (int i = reservedPoolBuffer.Count - 1; i >= 0; i--)
                {
                    pool.Push(reservedPoolBuffer[i]);
                }
                if (selected != null)
                {
                    created = false;
                    return selected;
                }
            }
            GameObject sourcePrefab = template?.CloneSource ?? prefab;
            GameObject instance = Instantiate(sourcePrefab, staging);
            instance.name = prefab.name;
            instance.SetActive(false);
            created = true;
            return instance;
        }

        private void RemoveInactiveRegistration(GameObject gameObject, EffectiveKey key)
        {
            inactiveContexts.Remove(gameObject);
            inactiveKeys.Remove(gameObject);
            if (inactiveByKey.TryGetValue(key, out ItemInstance registered) &&
                registered.GameObject == gameObject)
            {
                inactiveByKey.Remove(key);
            }
        }

        private TemplateState EnsureTemplate(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }
            if (templates.TryGetValue(prefab, out TemplateState state))
            {
                return state;
            }
            state = new TemplateState();
            if (targetParent != null && prefab.transform.IsChildOf(targetParent))
            {
                prefab.SetActive(false);
                GameObject snapshot = Instantiate(prefab, staging);
                snapshot.name = prefab.name + " (Protected Template)";
                snapshot.SetActive(false);
                state.Original = prefab;
                state.OriginalAvailable = true;
                state.CloneSource = snapshot;
            }
            else
            {
                state.CloneSource = prefab;
            }
            templates[prefab] = state;
            return state;
        }

        private void RemoveUndesired(HashSet<EffectiveKey> desired)
        {
            removeKeys.Clear();
            foreach (KeyValuePair<EffectiveKey, ItemInstance> pair in active)
            {
                if (!desired.Contains(pair.Key))
                {
                    ReleaseInstance(pair.Value, false);
                    removeKeys.Add(pair.Key);
                }
            }
            for (int i = 0; i < removeKeys.Count; i++)
            {
                active.Remove(removeKeys[i]);
            }
        }

        private void ReleaseInstance(ItemInstance instance, bool destroying)
        {
            ViewDataListItemContext context = instance.Context;
            ItemHierarchyCache hierarchy = EnsureItemHierarchyCache(instance.GameObject);
            if (instance.Visible)
            {
                instance.Visible = false;
                InvokeVisibilityChanged(hierarchy, context, false);
                itemVisibilityChanged.Invoke(context, false);
                ItemVisibilityChanged?.Invoke(context, false);
            }
            InvokeUnbound(hierarchy, context);
            itemUnbound.Invoke(context);
            ItemUnbound?.Invoke(context);
            statistics.Unbound++;

            if (destroying || surplusPolicy == ViewDataListSurplusPolicy.Destroy)
            {
                InvokeDestroyed(hierarchy, context);
                itemDestroyed.Invoke(context);
                ItemDestroyed?.Invoke(context);
                statistics.Destroyed++;
                PreserveTemplateBeforeDestroy(instance);
                itemHierarchyCaches.Remove(instance.GameObject);
                DestroySafely(instance.GameObject);
                return;
            }

            instance.GameObject.SetActive(false);
            instance.GameObject.transform.SetParent(staging, false);
            inactiveContexts[instance.GameObject] = context;
            inactiveByKey[instance.Key] = instance;
            inactiveKeys[instance.GameObject] = instance.Key;
            if (queuedReservations.Add(instance.GameObject))
            {
                inactiveReservationOrder.Enqueue(instance.GameObject);
            }
            TrimInactiveReservations();
            itemDeactivated.Invoke(context);
            ItemDeactivated?.Invoke(context);
            if (!inactiveByPrefab.TryGetValue(instance.Prefab, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>();
                inactiveByPrefab[instance.Prefab] = pool;
            }
            pool.Push(instance.GameObject);
        }

        private void TrimInactiveReservations()
        {
            int capacity = virtualization != null && virtualization.Enabled
                ? 0
                : ReservedKeyCapacity;
            while (inactiveByKey.Count > capacity &&
                   inactiveReservationOrder.Count > 0)
            {
                GameObject candidate = inactiveReservationOrder.Dequeue();
                queuedReservations.Remove(candidate);
                if (candidate == null ||
                    !inactiveKeys.TryGetValue(candidate, out EffectiveKey key))
                {
                    continue;
                }
                inactiveKeys.Remove(candidate);
                if (inactiveByKey.TryGetValue(key, out ItemInstance registered) &&
                    registered.GameObject == candidate)
                {
                    inactiveByKey.Remove(key);
                }
            }
        }

        private void PublishContexts(ItemHierarchyCache hierarchy, ViewDataListItemContext context)
        {
            BindingDataContext dataContext = hierarchy.DataContext;
            if (dataContext == null)
            {
                dataContext = hierarchy.GameObject.AddComponent<BindingDataContext>();
                hierarchy.DataContext = dataContext;
            }
            dataContext.SetContext("$Item", context.Item);
            if (context.ParentItem != null) dataContext.SetContext("$ParentItem", context.ParentItem);
            else dataContext.RemoveRuntimeContext("$ParentItem");
            dataContext.SetContext("$List", this);
            dataContext.SetContext("$Index", context.Index);
            dataContext.SetContext("$SourceIndex", context.SourceIndex);
            dataContext.SetContext("$Key", context.Key);
            dataContext.SetContext("$IsFirst", context.IsFirst);
            dataContext.SetContext("$IsLast", context.IsLast);
        }

        private static void InjectChildBinderInstances(ItemHierarchyCache hierarchy, object item)
        {
            ViewDataBinder[] binders = hierarchy.DataBinders;
            for (int b = 0; b < binders.Length; b++)
            {
                ViewDataBinder binder = binders[b];
                if (binder == null)
                {
                    continue;
                }
                for (int i = 0; i < binder.Bindings.Count; i++)
                {
                    ViewDataBinding binding = binder.Bindings[i];
                    for (int s = 0; s < binding.Sources.Count; s++)
                    {
                        if (binding.Sources[s]?.Endpoint?.Instance?.Kind == BindingInstanceKind.Runtime)
                        {
                            binder.SetSourceInstance(i, s, item);
                        }
                    }
                }
                for (int p = 0; p < binder.Profiles.Count; p++)
                {
                    binder.SetProfileSourceRoot(p, item);
                }
            }

            ViewDataEventBinder[] eventBinders = hierarchy.EventBinders;
            for (int b = 0; b < eventBinders.Length; b++)
            {
                ViewDataEventBinder binder = eventBinders[b];
                if (binder == null)
                {
                    continue;
                }
                for (int i = 0; i < binder.EventBindings.Count; i++)
                {
                    ViewDataEventBinding binding = binder.EventBindings[i];
                    for (int s = 0; s < binding.Sources.Count; s++)
                    {
                        if (binding.Sources[s]?.Endpoint?.Instance?.Kind == BindingInstanceKind.Runtime)
                        {
                            binder.SetSourceInstance(i, s, item);
                        }
                    }
                }
            }
        }

        private void SynchronizeChildBinders(ItemHierarchyCache hierarchy)
        {
            ViewDataBinder[] binders = hierarchy.DataBinders;
            for (int i = 0; i < binders.Length; i++)
            {
                if (binders[i] != null)
                {
                    binders[i].SynchronizeAll();
                }
            }
            ViewDataEventBinder[] eventBinders = hierarchy.EventBinders;
            for (int i = 0; i < eventBinders.Length; i++)
            {
                if (eventBinders[i] != null)
                {
                    eventBinders[i].ProcessAll();
                }
            }
            ViewDataListBinder[] nested = hierarchy.ListBinders;
            for (int i = 0; i < nested.Length; i++)
            {
                if (nested[i] != null && nested[i] != this)
                {
                    nested[i].Refresh();
                }
            }
        }

        private ItemHierarchyCache EnsureItemHierarchyCache(GameObject target)
        {
            if (itemHierarchyCaches.TryGetValue(target, out ItemHierarchyCache cached))
            {
                return cached;
            }

            MonoBehaviour[] behaviours = target.GetComponentsInChildren<MonoBehaviour>(true);
            var contracts = new List<IViewDataListItem>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IViewDataListItem contract)
                {
                    contracts.Add(contract);
                }
            }

            cached = new ItemHierarchyCache
            {
                GameObject = target,
                DataContext = target.GetComponent<BindingDataContext>(),
                DataBinders = target.GetComponentsInChildren<ViewDataBinder>(true),
                EventBinders = target.GetComponentsInChildren<ViewDataEventBinder>(true),
                ListBinders = target.GetComponentsInChildren<ViewDataListBinder>(true),
                Contracts = contracts.ToArray()
            };
            itemHierarchyCaches[target] = cached;
            return cached;
        }

        private static void InvokeCreated(
            ItemHierarchyCache hierarchy,
            ViewDataListItemContext context)
        {
            for (int i = 0; i < hierarchy.Contracts.Length; i++)
            {
                IViewDataListItem contract = hierarchy.Contracts[i];
                if (!IsDestroyedContract(contract))
                {
                    contract.OnListItemCreated(context);
                }
            }
        }

        private static void InvokeBound(
            ItemHierarchyCache hierarchy,
            ViewDataListItemContext context)
        {
            for (int i = 0; i < hierarchy.Contracts.Length; i++)
            {
                IViewDataListItem contract = hierarchy.Contracts[i];
                if (!IsDestroyedContract(contract))
                {
                    contract.OnListItemBound(context);
                }
            }
        }

        private static void InvokeUnbound(
            ItemHierarchyCache hierarchy,
            ViewDataListItemContext context)
        {
            for (int i = 0; i < hierarchy.Contracts.Length; i++)
            {
                IViewDataListItem contract = hierarchy.Contracts[i];
                if (!IsDestroyedContract(contract))
                {
                    contract.OnListItemUnbound(context);
                }
            }
        }

        private static void InvokeVisibilityChanged(
            ItemHierarchyCache hierarchy,
            ViewDataListItemContext context,
            bool visible)
        {
            for (int i = 0; i < hierarchy.Contracts.Length; i++)
            {
                IViewDataListItem contract = hierarchy.Contracts[i];
                if (!IsDestroyedContract(contract))
                {
                    contract.OnListItemVisibilityChanged(context, visible);
                }
            }
        }

        private static void InvokeDestroyed(
            ItemHierarchyCache hierarchy,
            ViewDataListItemContext context)
        {
            for (int i = 0; i < hierarchy.Contracts.Length; i++)
            {
                IViewDataListItem contract = hierarchy.Contracts[i];
                if (!IsDestroyedContract(contract))
                {
                    contract.OnListItemDestroyed(context);
                }
            }
        }

        private static bool IsDestroyedContract(IViewDataListItem contract)
        {
            return contract == null ||
                   (contract is UnityEngine.Object unityObject && unityObject == null);
        }

        private static object FindItemContext(Transform current)
        {
            while (current != null)
            {
                if (current.TryGetComponent(out BindingDataContext context) &&
                    context.TryResolveContext("$Item", out BindingInstanceHandle handle, out _))
                {
                    return handle.Instance;
                }
                current = current.parent;
            }
            return null;
        }

        private void GetRequiredRange(out int first, out int last)
        {
            if (!virtualization.Enabled || projection.Count == 0)
            {
                first = 0;
                last = projection.Count - 1;
                return;
            }
            ScrollRect scrollRect = virtualization.ScrollRect;
            RectTransform content = targetParent as RectTransform;
            RectTransform viewport = scrollRect != null ? scrollRect.viewport : null;
            if (scrollRect == null || content == null || viewport == null)
            {
                first = 0;
                last = projection.Count - 1;
                return;
            }

            Vector2 step = virtualization.ItemSize + virtualization.Spacing;
            int constraint = virtualization.LayoutMode == ViewDataListLayoutMode.Grid
                ? virtualization.GridConstraint
                : 1;
            bool horizontal = virtualization.LayoutMode == ViewDataListLayoutMode.Horizontal;
            float axisStep = horizontal
                ? Mathf.Max(1f, step.x)
                : Mathf.Max(1f, step.y);
            ScrollRectViewportUtility.CalculateUniformRange(
                content,
                viewport,
                horizontal,
                horizontal ? virtualization.Padding.left : virtualization.Padding.top,
                axisStep,
                constraint,
                virtualization.BufferItems,
                projection.Count,
                out first,
                out last);
        }

        private void PositionVirtualItem(RectTransform rect, int index)
        {
            if (!virtualization.Enabled || rect == null)
            {
                return;
            }
            int columns = virtualization.LayoutMode == ViewDataListLayoutMode.Grid
                ? virtualization.GridConstraint
                : virtualization.LayoutMode == ViewDataListLayoutMode.Horizontal
                    ? projection.Count
                    : 1;
            int row = index / Mathf.Max(1, columns);
            int column = index % Mathf.Max(1, columns);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = virtualization.ItemSize;
            rect.anchoredPosition = new Vector2(
                virtualization.Padding.left + column * (virtualization.ItemSize.x + virtualization.Spacing.x),
                -virtualization.Padding.top - row * (virtualization.ItemSize.y + virtualization.Spacing.y));
        }

        private void UpdateContentSize()
        {
            if (!virtualization.Enabled || !(targetParent is RectTransform content))
            {
                return;
            }
            int columns = virtualization.LayoutMode == ViewDataListLayoutMode.Grid
                ? virtualization.GridConstraint
                : virtualization.LayoutMode == ViewDataListLayoutMode.Horizontal
                    ? Mathf.Max(1, projection.Count)
                    : 1;
            int rows = Mathf.CeilToInt(projection.Count / (float)Mathf.Max(1, columns));
            float width = virtualization.Padding.horizontal +
                          columns * virtualization.ItemSize.x +
                          Mathf.Max(0, columns - 1) * virtualization.Spacing.x;
            float height = virtualization.Padding.vertical +
                           rows * virtualization.ItemSize.y +
                           Mathf.Max(0, rows - 1) * virtualization.Spacing.y;
            if (!Mathf.Approximately(content.rect.width, width))
            {
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }
            if (!Mathf.Approximately(content.rect.height, height))
            {
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }

        private void ReorderNonVirtualized()
        {
            if (virtualization.Enabled)
            {
                return;
            }
            for (int i = 0; i < projection.Count; i++)
            {
                if (active.TryGetValue(projection[i].Key, out ItemInstance instance))
                {
                    Transform itemTransform = instance.GameObject.transform;
                    if (itemTransform.GetSiblingIndex() != i)
                    {
                        itemTransform.SetSiblingIndex(i);
                    }
                }
            }
        }

        private void ConfigureScrollListener()
        {
            RemoveScrollListener();
            if (virtualization.Enabled && virtualization.ScrollRect != null)
            {
                virtualization.ScrollRect.onValueChanged.AddListener(OnScrollChanged);
                if (targetParent != null &&
                    targetParent.GetComponent<LayoutGroup>() is LayoutGroup layout &&
                    layout.enabled)
                {
                    ReportWarning("An enabled LayoutGroup conflicts with ViewDataListBinder virtualization.");
                }
            }
        }

        private void RemoveScrollListener()
        {
            if (virtualization?.ScrollRect != null)
            {
                virtualization.ScrollRect.onValueChanged.RemoveListener(OnScrollChanged);
            }
        }

        private void OnScrollChanged(Vector2 _)
        {
            CancelPendingRefresh();
            int token = ++generation;
            if (creationMode == ViewDataListCreationMode.Batched &&
                isActiveAndEnabled)
            {
                lastResult = new ViewDataListBindResult(
                    ViewDataListBindStatus.Scheduled,
                    string.Empty,
                    lastSourceCount,
                    projection.Count,
                    active.Count);
                batchRoutine = StartCoroutine(
                    ReconcileBatched(token, lastSourceCount, false));
            }
            else
            {
                Reconcile(token, true, false);
                lastResult = new ViewDataListBindResult(
                    ViewDataListBindStatus.Success,
                    string.Empty,
                    lastSourceCount,
                    projection.Count,
                    active.Count);
            }
        }

        private void EnsureInfrastructure()
        {
            if (stagingObject == null)
            {
                stagingObject = new GameObject("ViewDataListBinder Staging");
                stagingObject.hideFlags = HideFlags.HideInHierarchy;
                staging = stagingObject.transform;
                staging.SetParent(transform, false);
                stagingObject.SetActive(false);
            }
            EnsureTemplate(defaultPrefab);
            for (int i = 0; i < prefabRules.Count; i++)
            {
                EnsureTemplate(prefabRules[i]?.Prefab);
            }
        }

        private bool TryValidateConfiguration(out string error)
        {
            if (targetParent == null)
            {
                error = "Target Parent is required.";
                return false;
            }
            if (defaultPrefab == null && PrefabResolver == null)
            {
                error = "A default prefab or runtime PrefabResolver is required as fallback.";
                return false;
            }
            if (!IsValidTemplate(defaultPrefab))
            {
                error = "Default Prefab must be a prefab asset or a child of Target Parent.";
                return false;
            }
            for (int i = 0; i < prefabRules.Count; i++)
            {
                ViewDataListPrefabRule rule = prefabRules[i];
                if (rule != null && rule.Enabled && !IsValidTemplate(rule.Prefab))
                {
                    error = $"Prefab rule '{rule.Name}' must use a prefab asset or a child of Target Parent.";
                    return false;
                }
            }
            if (virtualization.Enabled)
            {
                if (!(targetParent is RectTransform))
                {
                    error = "Virtualization requires Target Parent to be a RectTransform.";
                    return false;
                }
                if (virtualization.ScrollRect == null ||
                    virtualization.ScrollRect.viewport == null ||
                    virtualization.ScrollRect.content == null)
                {
                    error = "Virtualization requires a ScrollRect with content and viewport.";
                    return false;
                }
                if (virtualization.ScrollRect.content != targetParent)
                {
                    error = "Virtualization requires Target Parent to be the ScrollRect content.";
                    return false;
                }
                if (virtualization.ItemSize.x <= 0f || virtualization.ItemSize.y <= 0f)
                {
                    error = "Virtualized item size must be greater than zero.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private bool IsValidTemplate(GameObject prefab)
        {
            return prefab == null ||
                   !prefab.scene.IsValid() ||
                   (staging != null && prefab.transform.IsChildOf(staging)) ||
                   (targetParent != null && prefab.transform.IsChildOf(targetParent));
        }

        private void PreserveTemplateBeforeDestroy(ItemInstance instance)
        {
            if (instance?.GameObject == null ||
                !templates.TryGetValue(instance.Prefab, out TemplateState template) ||
                template.Original != instance.GameObject ||
                template.CloneSource == null)
            {
                return;
            }

            GameObject protectedSource = template.CloneSource;
            if (defaultPrefab == instance.Prefab)
            {
                defaultPrefab = protectedSource;
            }
            for (int i = 0; i < prefabRules.Count; i++)
            {
                if (prefabRules[i]?.Prefab == instance.Prefab)
                {
                    prefabRules[i].Prefab = protectedSource;
                }
            }
            template.Original = null;
            template.OriginalAvailable = false;
            templates[protectedSource] = template;
        }

        private void RefreshActiveItemsView()
        {
            activeItems.Clear();
            if (activeItems.Capacity < active.Count)
            {
                activeItems.Capacity = active.Count;
            }
            for (int i = 0; i < projection.Count; i++)
            {
                if (active.TryGetValue(projection[i].Key, out ItemInstance instance))
                {
                    activeItems.Add(instance.Context);
                }
            }
        }

        private void DestroyAllInstances()
        {
            foreach (ItemInstance instance in active.Values)
            {
                ReleaseInstance(instance, true);
            }
            active.Clear();
            activeItems.Clear();
            foreach (Stack<GameObject> pool in inactiveByPrefab.Values)
            {
                while (pool.Count > 0)
                {
                    GameObject value = pool.Pop();
                    if (value == null) continue;
                    if (inactiveContexts.TryGetValue(value, out ViewDataListItemContext context))
                    {
                        InvokeDestroyed(EnsureItemHierarchyCache(value), context);
                        itemDestroyed.Invoke(context);
                        ItemDestroyed?.Invoke(context);
                        statistics.Destroyed++;
                    }
                    itemHierarchyCaches.Remove(value);
                    DestroySafely(value);
                }
            }
            inactiveByPrefab.Clear();
            inactiveContexts.Clear();
            inactiveByKey.Clear();
            inactiveKeys.Clear();
            inactiveReservationOrder.Clear();
            queuedReservations.Clear();
            foreach (TemplateState template in templates.Values)
            {
                if (template.CloneSource != null &&
                    template.CloneSource.transform.parent == staging)
                {
                    DestroySafely(template.CloneSource);
                }
            }
            templates.Clear();
            itemHierarchyCaches.Clear();
            projectedItems.Clear();
        }

        private ViewDataListBindResult Complete(
            ViewDataListBindStatus status,
            string message,
            int sourceCount)
        {
            lastResult = new ViewDataListBindResult(
                status,
                message,
                sourceCount,
                projection.Count,
                active.Count);
            refreshCompleted.Invoke();
            RefreshCompleted?.Invoke(lastResult);
            return lastResult;
        }

        private ViewDataListBindResult Fail(
            ViewDataListBindStatus status,
            string error,
            int sourceCount,
            int projectedCount)
        {
            lastResult = new ViewDataListBindResult(
                status,
                error,
                sourceCount,
                projectedCount,
                active.Count);
            ReportError(error);
            refreshCompleted.Invoke();
            RefreshCompleted?.Invoke(lastResult);
            return lastResult;
        }

        private void ReportWarning(string message)
        {
            if (errorPolicy == BindingErrorPolicy.LogEveryTime ||
                (errorPolicy == BindingErrorPolicy.LogOnce &&
                 !string.Equals(lastLoggedError, message, StringComparison.Ordinal)))
            {
                Debug.LogWarning(message, this);
                lastLoggedError = message;
            }
        }

        private void ReportError(string message)
        {
            switch (errorPolicy)
            {
                case BindingErrorPolicy.LogOnce:
                    if (!string.Equals(lastLoggedError, message, StringComparison.Ordinal))
                    {
                        Debug.LogWarning(message, this);
                        lastLoggedError = message;
                    }
                    break;
                case BindingErrorPolicy.LogEveryTime:
                    Debug.LogWarning(message, this);
                    break;
                case BindingErrorPolicy.DisableUntilReset:
                    enabled = false;
                    Debug.LogWarning(message, this);
                    break;
                case BindingErrorPolicy.ThrowException:
                    throw new InvalidOperationException(message);
            }
        }

        private ViewDataListItemContext CreateContext(
            ProjectedItem projected,
            int count,
            GameObject gameObject)
        {
            return new ViewDataListItemContext
            {
                Binder = this,
                Item = projected.Item,
                ParentItem = projectionParentItem,
                Key = GetPublishedKey(projected.Key),
                Index = projected.Index,
                SourceIndex = projected.SourceIndex,
                Count = count,
                GameObject = gameObject
            };
        }

        private static void PrepareItemEndpoint(BindingEndpoint endpoint, object item)
        {
            if (endpoint?.Instance?.Kind == BindingInstanceKind.Runtime)
            {
                endpoint.Instance.SetRuntimeInstance(item);
            }
        }

        private static object GetPublishedKey(EffectiveKey key)
        {
            return key.Occurrence == 0
                ? key.BaseKey
                : new ViewDataListDuplicateKey(key.BaseKey, key.Occurrence);
        }

        private static bool IsNull(object value)
        {
            return value == null || (value is Object unityObject && unityObject == null);
        }

        private static void DestroySafely(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private struct ProjectedItem
        {
            public object Item;
            public int SourceIndex;
            public int Index;
            public int StableOrder;
            public EffectiveKey Key;
            public int SortValuesOffset;
            public GameObject Prefab;
        }

        private sealed class ItemInstance
        {
            public GameObject GameObject;
            public GameObject Prefab;
            public EffectiveKey Key;
            public ViewDataListItemContext Context;
            public bool Visible;
        }

        private sealed class TemplateState
        {
            public GameObject Original;
            public GameObject CloneSource;
            public bool OriginalAvailable;
        }

        private sealed class PredicateBuffers
        {
            public PredicateBuffers(int count)
            {
                Values = new object[count];
                Metadata = new BindingMemberMetadata[count];
            }

            public object[] Values { get; }
            public BindingMemberMetadata[] Metadata { get; }
        }

        private sealed class ItemHierarchyCache
        {
            public GameObject GameObject;
            public BindingDataContext DataContext;
            public ViewDataBinder[] DataBinders;
            public ViewDataEventBinder[] EventBinders;
            public ViewDataListBinder[] ListBinders;
            public IViewDataListItem[] Contracts;
        }

        private readonly struct EffectiveKey : IEquatable<EffectiveKey>
        {
            public EffectiveKey(object baseKey, int occurrence)
            {
                BaseKey = baseKey;
                Occurrence = occurrence;
            }
            public object BaseKey { get; }
            public int Occurrence { get; }
            public bool Equals(EffectiveKey other) =>
                Equals(BaseKey, other.BaseKey) && Occurrence == other.Occurrence;
            public override bool Equals(object obj) => obj is EffectiveKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((BaseKey?.GetHashCode() ?? 0) * 397) ^ Occurrence; }
            }
        }

    }
}
