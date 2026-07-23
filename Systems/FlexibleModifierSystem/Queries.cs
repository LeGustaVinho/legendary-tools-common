using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LegendaryTools.ModifierSystem
{
    public interface IDeterministicRandom
    {
        int NextInt(int minimumInclusive, int maximumExclusive);
        ulong State { get; }
    }

    public interface IRewindableDeterministicRandom : IDeterministicRandom
    {
        void RestoreState(ulong state);
    }

    public sealed class XorShiftRandom : IRewindableDeterministicRandom
    {
        private ulong _state;
        public ulong State => _state;

        public XorShiftRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        public void RestoreState(ulong state) => _state = state == 0 ? 0x9E3779B97F4A7C15UL : state;

        public int NextInt(int minimumInclusive, int maximumExclusive)
        {
            if (maximumExclusive <= minimumInclusive) throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
            ulong value = _state;
            value ^= value << 13;
            value ^= value >> 7;
            value ^= value << 17;
            _state = value;
            return minimumInclusive + (int)(value % (uint)(maximumExclusive - minimumInclusive));
        }
    }

    internal enum QueryDependencyKind
    {
        World,
        Structure,
        Attribute,
        EntityAttribute,
        Relation,
        SourceRelation
    }

    internal readonly struct QueryDependencyKey : IEquatable<QueryDependencyKey>
    {
        public QueryDependencyKind Kind { get; }
        public object Definition { get; }
        public EntityId? Entity { get; }

        public QueryDependencyKey(QueryDependencyKind kind, object definition = null, EntityId? entity = null)
        {
            Kind = kind;
            Definition = definition;
            Entity = entity;
        }

        public bool Equals(QueryDependencyKey other) => Kind == other.Kind &&
            ReferenceEquals(Definition, other.Definition) && Nullable.Equals(Entity, other.Entity);
        public override bool Equals(object obj) => obj is QueryDependencyKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (Definition == null ? 0 : RuntimeHelpers.GetHashCode(Definition));
                hash = (hash * 397) ^ (Entity.HasValue ? Entity.Value.GetHashCode() : 0);
                return hash;
            }
        }
    }

    public sealed class QueryDependency
    {
        internal Func<SimulationWorld, long> Version { get; }
        internal QueryDependencyKey Key { get; }

        internal QueryDependency(Func<SimulationWorld, long> version, QueryDependencyKey key)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Key = key;
        }
    }

    public sealed class PreparedQuery<T>
    {
        private readonly Func<SimulationWorld, IEnumerable<T>> _execute;
        private readonly QueryDependency[] _dependencies;
        private readonly IReadOnlyList<QueryDependencyKey> _dependencyKeys;
        private SimulationWorld _cachedWorld;
        private long[] _cachedDependencyVersions;
        private IReadOnlyList<T> _cache;

        public long ExecutionCount { get; private set; }

        public PreparedQuery(Func<SimulationWorld, IEnumerable<T>> execute)
            : this(execute, new QueryDependency(world => world.Version,
                new QueryDependencyKey(QueryDependencyKind.World)))
        {
        }

        internal PreparedQuery(Func<SimulationWorld, IEnumerable<T>> execute,
            params QueryDependency[] dependencies)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            if (dependencies == null || dependencies.Length == 0)
                throw new ArgumentException("At least one query dependency is required.", nameof(dependencies));
            if (dependencies.Any(item => item == null))
                throw new ArgumentException("Query dependencies cannot contain null.", nameof(dependencies));
            _dependencies = dependencies.ToArray();
            _dependencyKeys = _dependencies.Select(item => item.Key).Distinct().ToArray();
            _cachedDependencyVersions = new long[_dependencies.Length];
        }

        public IReadOnlyList<T> Execute(SimulationWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!IsCacheValid(world))
            {
                long[] dependencyVersions = CaptureDependencyVersions(world);
                IReadOnlyList<T> cache = _execute(world).ToArray();
                _cache = cache;
                ExecutionCount++;
                _cachedWorld = world;
                _cachedDependencyVersions = dependencyVersions;
            }
            return _cache;
        }

        private long[] CaptureDependencyVersions(SimulationWorld world)
        {
            var versions = new long[_dependencies.Length];
            for (int index = 0; index < _dependencies.Length; index++)
                versions[index] = _dependencies[index].Version(world);
            return versions;
        }

        private bool IsCacheValid(SimulationWorld world)
        {
            if (!ReferenceEquals(world, _cachedWorld) || _cache == null) return false;
            for (int index = 0; index < _dependencies.Length; index++)
                if (_cachedDependencyVersions[index] != _dependencies[index].Version(world)) return false;
            return true;
        }

        internal bool TryGetCurrentSnapshot(SimulationWorld world, out IReadOnlyList<T> snapshot)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (IsCacheValid(world))
            {
                snapshot = _cache;
                return true;
            }
            snapshot = null;
            return false;
        }

        public PreparedQuery<T> Where(Func<T, bool> predicate, params QueryDependency[] dependencies)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            Func<SimulationWorld, IEnumerable<T>> execute = world => Execute(world).Where(predicate);
            return HasDependencies(dependencies)
                ? new PreparedQuery<T>(execute, CombineDependencies(dependencies))
                : new PreparedQuery<T>(execute);
        }

        public PreparedQuery<T> Ordered<TKey>(Func<T, TKey> keySelector, bool descending = false,
            params QueryDependency[] dependencies)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            Func<SimulationWorld, IEnumerable<T>> execute = world => descending
                ? Execute(world).OrderByDescending(keySelector)
                : Execute(world).OrderBy(keySelector);
            return HasDependencies(dependencies)
                ? new PreparedQuery<T>(execute, CombineDependencies(dependencies))
                : new PreparedQuery<T>(execute);
        }

        public PreparedQuery<T> OrderBy<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies) => Ordered(keySelector, false, dependencies);

        public PreparedQuery<T> OrderByDescending<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies) => Ordered(keySelector, true, dependencies);

        public PreparedQuery<T> Take(int count) =>
            new PreparedQuery<T>(world => Execute(world).Take(count), _dependencies);
        public PreparedQuery<TResult> Select<TResult>(Func<T, TResult> selector,
            params QueryDependency[] dependencies)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            Func<SimulationWorld, IEnumerable<TResult>> execute = world => Execute(world).Select(selector);
            return HasDependencies(dependencies)
                ? new PreparedQuery<TResult>(execute, CombineDependencies(dependencies))
                : new PreparedQuery<TResult>(execute);
        }

        public PreparedQuery<TResult> SelectMany<TResult>(Func<T, IEnumerable<TResult>> selector,
            params QueryDependency[] dependencies)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            Func<SimulationWorld, IEnumerable<TResult>> execute = world => Execute(world).SelectMany(selector);
            return HasDependencies(dependencies)
                ? new PreparedQuery<TResult>(execute, CombineDependencies(dependencies))
                : new PreparedQuery<TResult>(execute);
        }

        public PreparedQuery<QueryGroup<TKey, T>> GroupBy<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            Func<SimulationWorld, IEnumerable<QueryGroup<TKey, T>>> execute = world => Execute(world)
                .GroupBy(keySelector).Select(group => new QueryGroup<TKey, T>(group.Key, group));
            return HasDependencies(dependencies)
                ? new PreparedQuery<QueryGroup<TKey, T>>(execute, CombineDependencies(dependencies))
                : new PreparedQuery<QueryGroup<TKey, T>>(execute);
        }

        public PreparedQuery<TResult> Join<TOther, TKey, TResult>(PreparedQuery<TOther> other,
            Func<T, TKey> leftKey, Func<TOther, TKey> rightKey, Func<T, TOther, TResult> result,
            params QueryDependency[] dependencies)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (leftKey == null) throw new ArgumentNullException(nameof(leftKey));
            if (rightKey == null) throw new ArgumentNullException(nameof(rightKey));
            if (result == null) throw new ArgumentNullException(nameof(result));
            Func<SimulationWorld, IEnumerable<TResult>> execute = world =>
                Execute(world).Join(other.Execute(world), leftKey, rightKey, result);
            return HasDependencies(dependencies)
                ? new PreparedQuery<TResult>(execute,
                    CombineDependencies(other._dependencies.Concat(dependencies).ToArray()))
                : new PreparedQuery<TResult>(execute);
        }

        public bool Any(SimulationWorld world, Func<T, bool> predicate = null)
        {
            IReadOnlyList<T> values = Execute(world);
            if (predicate == null) return values.Count != 0;
            for (int index = 0; index < values.Count; index++)
                if (predicate(values[index])) return true;
            return false;
        }

        public bool All(SimulationWorld world, Func<T, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            IReadOnlyList<T> values = Execute(world);
            for (int index = 0; index < values.Count; index++)
                if (!predicate(values[index])) return false;
            return true;
        }
        public bool None(SimulationWorld world, Func<T, bool> predicate = null) => !Any(world, predicate);
        public int Count(SimulationWorld world) => Execute(world).Count;
        public TValue Sum<TValue>(SimulationWorld world, Func<T, TValue> selector, Func<TValue, TValue, TValue> add,
            TValue zero)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (add == null) throw new ArgumentNullException(nameof(add));
            IReadOnlyList<T> values = Execute(world);
            TValue result = zero;
            for (int index = 0; index < values.Count; index++) result = add(result, selector(values[index]));
            return result;
        }

        public double Average(SimulationWorld world, Func<T, double> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            IReadOnlyList<T> values = Execute(world);
            if (values.Count == 0) throw new InvalidOperationException("Sequence contains no elements.");
            double sum = 0d;
            for (int index = 0; index < values.Count; index++) sum += selector(values[index]);
            return sum / values.Count;
        }

        public T MaxBy<TKey>(SimulationWorld world, Func<T, TKey> selector) =>
            ExtremeBy(world, selector, true);
        public T MinBy<TKey>(SimulationWorld world, Func<T, TKey> selector) =>
            ExtremeBy(world, selector, false);
        public TValue Max<TValue>(SimulationWorld world, Func<T, TValue> selector) =>
            ExtremeValue(world, selector, true);
        public TValue Min<TValue>(SimulationWorld world, Func<T, TValue> selector) =>
            ExtremeValue(world, selector, false);

        public T Random(SimulationWorld world, IDeterministicRandom random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            IReadOnlyList<T> values = Execute(world);
            if (values.Count == 0) throw new InvalidOperationException("Cannot select from an empty query.");
            return values[random.NextInt(0, values.Count)];
        }

        private static bool HasDependencies(QueryDependency[] dependencies) =>
            dependencies != null && dependencies.Length != 0;

        private T ExtremeBy<TKey>(SimulationWorld world, Func<T, TKey> selector, bool maximum)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            IReadOnlyList<T> values = Execute(world);
            if (values.Count == 0) return default;
            var comparer = Comparer<TKey>.Default;
            T best = values[0];
            TKey bestKey = selector(best);
            for (int index = 1; index < values.Count; index++)
            {
                T candidate = values[index];
                TKey candidateKey = selector(candidate);
                int comparison = comparer.Compare(candidateKey, bestKey);
                if ((maximum && comparison > 0) || (!maximum && comparison < 0))
                {
                    best = candidate;
                    bestKey = candidateKey;
                }
            }
            return best;
        }

        private TValue ExtremeValue<TValue>(SimulationWorld world, Func<T, TValue> selector, bool maximum)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            IReadOnlyList<T> values = Execute(world);
            if (values.Count == 0) throw new InvalidOperationException("Sequence contains no elements.");
            var comparer = Comparer<TValue>.Default;
            TValue best = selector(values[0]);
            for (int index = 1; index < values.Count; index++)
            {
                TValue candidate = selector(values[index]);
                int comparison = comparer.Compare(candidate, best);
                if ((maximum && comparison > 0) || (!maximum && comparison < 0)) best = candidate;
            }
            return best;
        }

        private QueryDependency[] CombineDependencies(QueryDependency[] dependencies)
        {
            var combined = new QueryDependency[_dependencies.Length + dependencies.Length];
            Array.Copy(_dependencies, combined, _dependencies.Length);
            for (int index = 0; index < dependencies.Length; index++)
            {
                if (dependencies[index] == null)
                    throw new ArgumentException("Query dependencies cannot contain null.", nameof(dependencies));
                combined[_dependencies.Length + index] = dependencies[index];
            }
            return combined;
        }

        internal IReadOnlyList<QueryDependencyKey> DependencyKeys => _dependencyKeys;
    }

    public readonly struct QueryItemUpdate<TKey, T>
    {
        public TKey Key { get; }
        public T Previous { get; }
        public T Current { get; }

        public QueryItemUpdate(TKey key, T previous, T current)
        {
            Key = key;
            Previous = previous;
            Current = current;
        }
    }

    public sealed class QueryDelta<T, TKey>
    {
        private static readonly QueryDelta<T, TKey> EmptyDelta = new QueryDelta<T, TKey>(false,
            Array.Empty<T>(), Array.Empty<T>(), Array.Empty<QueryItemUpdate<TKey, T>>(), false);

        public bool IsInitial { get; }
        public IReadOnlyList<T> Added { get; }
        public IReadOnlyList<T> Removed { get; }
        public IReadOnlyList<QueryItemUpdate<TKey, T>> Updated { get; }
        public bool OrderChanged { get; }
        public bool HasChanges => IsInitial || Added.Count != 0 || Removed.Count != 0 ||
            Updated.Count != 0 || OrderChanged;

        internal static QueryDelta<T, TKey> Empty => EmptyDelta;

        internal QueryDelta(bool isInitial, IEnumerable<T> added, IEnumerable<T> removed,
            IEnumerable<QueryItemUpdate<TKey, T>> updated, bool orderChanged)
        {
            IsInitial = isInitial;
            Added = new List<T>(added).AsReadOnly();
            Removed = new List<T>(removed).AsReadOnly();
            Updated = new List<QueryItemUpdate<TKey, T>>(updated).AsReadOnly();
            OrderChanged = orderChanged;
        }
    }

    public sealed class MaterializedQuery<T, TKey>
    {
        private readonly PreparedQuery<T> _query;
        private readonly Func<T, TKey> _keySelector;
        private readonly IEqualityComparer<TKey> _keyComparer;
        private readonly IEqualityComparer<T> _valueComparer;
        private Dictionary<TKey, T> _items;
        private List<TKey> _orderedKeys;
        private IReadOnlyList<T> _current = Array.Empty<T>();
        private IReadOnlyList<T> _sourceSnapshot;
        private SimulationWorld _world;
        private bool _initialized;

        public event Action<QueryDelta<T, TKey>> Changed;

        public IReadOnlyList<T> Current => _current;
        public bool IsInitialized => _initialized;
        public long RefreshCount { get; private set; }
        public long DiffCount { get; private set; }

        public MaterializedQuery(PreparedQuery<T> query, Func<T, TKey> keySelector,
            IEqualityComparer<TKey> keyComparer = null, IEqualityComparer<T> valueComparer = null)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            _valueComparer = valueComparer ?? EqualityComparer<T>.Default;
            _items = new Dictionary<TKey, T>(_keyComparer);
            _orderedKeys = new List<TKey>();
        }

        public QueryDelta<T, TKey> Refresh(SimulationWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (_world != null && !ReferenceEquals(_world, world))
                throw new InvalidOperationException("A materialized query cannot be shared between simulation worlds.");

            IReadOnlyList<T> source = _query.Execute(world);
            RefreshCount++;
            if (_initialized && ReferenceEquals(source, _sourceSnapshot)) return QueryDelta<T, TKey>.Empty;

            var nextItems = new Dictionary<TKey, T>(_keyComparer);
            var nextKeys = new List<TKey>(source.Count);
            var nextValues = new List<T>(source.Count);
            foreach (T item in source)
            {
                TKey key = _keySelector(item);
                if (ReferenceEquals(key, null))
                    throw new InvalidOperationException("Materialized query keys cannot be null.");
                if (nextItems.ContainsKey(key))
                    throw new InvalidOperationException($"Materialized query produced duplicate key '{key}'.");
                nextItems.Add(key, item);
                nextKeys.Add(key);
                nextValues.Add(item);
            }

            bool initial = !_initialized;
            var added = new List<T>();
            var removed = new List<T>();
            var updated = new List<QueryItemUpdate<TKey, T>>();

            foreach (TKey key in _orderedKeys)
                if (!nextItems.ContainsKey(key)) removed.Add(_items[key]);

            foreach (TKey key in nextKeys)
            {
                T current = nextItems[key];
                if (!_items.TryGetValue(key, out T previous)) added.Add(current);
                else if (!_valueComparer.Equals(previous, current))
                    updated.Add(new QueryItemUpdate<TKey, T>(key, previous, current));
            }

            bool orderChanged = !initial && HasOrderChanged(nextItems, nextKeys);
            var delta = new QueryDelta<T, TKey>(initial, added, removed, updated, orderChanged);
            _world = world;
            _items = nextItems;
            _orderedKeys = nextKeys;
            _current = nextValues.AsReadOnly();
            _sourceSnapshot = source;
            _initialized = true;
            DiffCount++;
            if (delta.HasChanges) Changed?.Invoke(delta);
            return delta;
        }

        private bool HasOrderChanged(IReadOnlyDictionary<TKey, T> nextItems, IReadOnlyList<TKey> nextKeys)
        {
            var previousRetained = new List<TKey>();
            foreach (TKey key in _orderedKeys)
                if (nextItems.ContainsKey(key)) previousRetained.Add(key);
            var nextRetained = new List<TKey>();
            foreach (TKey key in nextKeys)
                if (_items.ContainsKey(key)) nextRetained.Add(key);
            if (previousRetained.Count != nextRetained.Count) return true;
            for (int index = 0; index < previousRetained.Count; index++)
                if (!_keyComparer.Equals(previousRetained[index], nextRetained[index])) return true;
            return false;
        }

        internal bool RequiresRefresh(SimulationWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!_initialized || !ReferenceEquals(_world, world)) return true;
            return !_query.TryGetCurrentSnapshot(world, out IReadOnlyList<T> snapshot) ||
                !ReferenceEquals(snapshot, _sourceSnapshot);
        }

        internal IReadOnlyList<QueryDependencyKey> DependencyKeys => _query.DependencyKeys;
    }

    public static class PreparedQueryMaterializationExtensions
    {
        public static MaterializedQuery<T, TKey> Materialize<T, TKey>(this PreparedQuery<T> query,
            Func<T, TKey> keySelector, IEqualityComparer<TKey> keyComparer = null,
            IEqualityComparer<T> valueComparer = null) =>
            new MaterializedQuery<T, TKey>(query, keySelector, keyComparer, valueComparer);

        public static MaterializedQuery<TEntity, EntityId> Materialize<TEntity>(
            this PreparedQuery<TEntity> query) where TEntity : WorldEntity =>
            new MaterializedQuery<TEntity, EntityId>(query, item => item.Id);
    }

    public enum QueryRefreshMode
    {
        Immediate,
        Deferred
    }

    public sealed class ScheduledQueryFailure
    {
        public long RegistrationId { get; }
        public object Query { get; }
        public Exception Exception { get; }

        internal ScheduledQueryFailure(long registrationId, object query, Exception exception)
        {
            RegistrationId = registrationId;
            Query = query;
            Exception = exception;
        }
    }

    internal interface IScheduledQuery
    {
        long Id { get; }
        QueryRefreshMode Mode { get; }
        object Query { get; }
        bool IsActive { get; set; }
        bool IsPending { get; set; }
        Exception LastError { get; set; }
        IReadOnlyList<QueryDependencyKey> DependencyKeys { get; }
        bool RequiresRefresh(SimulationWorld world);
        void Refresh(SimulationWorld world);
    }

    internal sealed class ScheduledQuery<T, TKey> : IScheduledQuery
    {
        private readonly MaterializedQuery<T, TKey> _query;
        public long Id { get; }
        public QueryRefreshMode Mode { get; }
        public object Query => _query;
        public bool IsActive { get; set; } = true;
        public bool IsPending { get; set; }
        public Exception LastError { get; set; }
        public IReadOnlyList<QueryDependencyKey> DependencyKeys => _query.DependencyKeys;

        public ScheduledQuery(long id, MaterializedQuery<T, TKey> query, QueryRefreshMode mode, bool pending)
        {
            Id = id;
            _query = query;
            Mode = mode;
            IsPending = pending;
        }

        public void Refresh(SimulationWorld world) => _query.Refresh(world);
        public bool RequiresRefresh(SimulationWorld world) => LastError != null || _query.RequiresRefresh(world);
    }

    public sealed class ScheduledQueryHandle : IDisposable
    {
        private SimulationWorld _world;
        private readonly IScheduledQuery _registration;

        public long Id => _registration.Id;
        public QueryRefreshMode Mode => _registration.Mode;
        public bool IsDisposed => !_registration.IsActive;
        public bool IsPending => _registration.IsPending;
        public Exception LastError => _registration.LastError;

        internal ScheduledQueryHandle(SimulationWorld world, IScheduledQuery registration)
        {
            _world = world;
            _registration = registration;
        }

        public void Dispose()
        {
            SimulationWorld world = _world;
            if (world == null) return;
            _world = null;
            world.RemoveScheduledQuery(_registration.Id);
        }

        public bool Retry()
        {
            SimulationWorld world = _world;
            return world != null && world.RetryScheduledQuery(_registration.Id);
        }
    }

    public sealed partial class SimulationWorld
    {
        private readonly SortedDictionary<long, IScheduledQuery> _scheduledQueries =
            new SortedDictionary<long, IScheduledQuery>();
        private readonly HashSet<object> _scheduledQueryObjects = new HashSet<object>();
        private readonly Dictionary<QueryDependencyKey, SortedSet<long>> _scheduledQueryDependencyIndex =
            new Dictionary<QueryDependencyKey, SortedSet<long>>();
        private readonly HashSet<long> _failedScheduledQueries = new HashSet<long>();
        private readonly SortedSet<long> _pendingScheduledQueryIds = new SortedSet<long>();
        private long _nextScheduledQueryId = 1;
        private bool _refreshingScheduledQueries;
        private bool _scheduledRefreshRequested;
        private bool _deferredRefreshRequested;
        private readonly HashSet<long> _forcedScheduledQueryRetries = new HashSet<long>();

        public event Action<ScheduledQueryFailure> ScheduledQueryFailed;

        public int ScheduledQueryCount => _scheduledQueries.Count;
        public int ScheduledQueryDependencyIndexKeyCount => _scheduledQueryDependencyIndex.Count;
        public bool HasPendingScheduledQueries => _pendingScheduledQueryIds.Count != 0;
        public long ScheduledQueryCandidateCheckCount { get; private set; }
        public long ScheduledQueryRefreshExecutionCount { get; private set; }

        public ScheduledQueryHandle Schedule<T, TKey>(MaterializedQuery<T, TKey> query,
            QueryRefreshMode mode = QueryRefreshMode.Deferred, bool refreshImmediately = true)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (_scheduledQueryObjects.Contains(query))
                throw new InvalidOperationException("This materialized query is already scheduled in the world.");
            if (refreshImmediately) query.Refresh(this);
            long id = _nextScheduledQueryId++;
            var registration = new ScheduledQuery<T, TKey>(id, query, mode, !refreshImmediately);
            _scheduledQueries.Add(id, registration);
            _scheduledQueryObjects.Add(query);
            if (registration.IsPending) _pendingScheduledQueryIds.Add(id);
            foreach (QueryDependencyKey dependency in registration.DependencyKeys)
            {
                if (!_scheduledQueryDependencyIndex.TryGetValue(dependency, out SortedSet<long> registrations))
                    _scheduledQueryDependencyIndex.Add(dependency, registrations = new SortedSet<long>());
                registrations.Add(id);
            }
            return new ScheduledQueryHandle(this, registration);
        }

        public void FlushScheduledQueries() => RequestScheduledQueryRefresh(true);

        internal void RemoveScheduledQuery(long id)
        {
            if (!_scheduledQueries.TryGetValue(id, out IScheduledQuery registration)) return;
            registration.IsActive = false;
            registration.IsPending = false;
            _scheduledQueries.Remove(id);
            _scheduledQueryObjects.Remove(registration.Query);
            _forcedScheduledQueryRetries.Remove(id);
            _failedScheduledQueries.Remove(id);
            _pendingScheduledQueryIds.Remove(id);
            foreach (QueryDependencyKey dependency in registration.DependencyKeys)
            {
                if (!_scheduledQueryDependencyIndex.TryGetValue(dependency, out SortedSet<long> registrations))
                    continue;
                registrations.Remove(id);
                if (registrations.Count == 0) _scheduledQueryDependencyIndex.Remove(dependency);
            }
        }

        internal bool RetryScheduledQuery(long id)
        {
            if (!_scheduledQueries.TryGetValue(id, out IScheduledQuery registration) || !registration.IsActive)
                return false;
            MarkScheduledQueryPending(registration);
            _forcedScheduledQueryRetries.Add(id);
            RequestScheduledQueryRefresh(false);
            return registration.LastError == null;
        }

        partial void OnVersionAdvanced(IReadOnlyCollection<QueryDependencyKey> changes)
        {
            OnTriggerDependenciesChanged(changes);
            var candidates = new SortedSet<long>(_failedScheduledQueries);
            foreach (QueryDependencyKey change in changes)
            {
                if (_scheduledQueryDependencyIndex.TryGetValue(change, out SortedSet<long> registrations))
                    candidates.UnionWith(registrations);
            }
            foreach (long id in candidates)
            {
                if (!_scheduledQueries.TryGetValue(id, out IScheduledQuery registration) || !registration.IsActive)
                    continue;
                ScheduledQueryCandidateCheckCount++;
                try
                {
                    if (registration.RequiresRefresh(this)) MarkScheduledQueryPending(registration);
                }
                catch (Exception exception)
                {
                    registration.LastError = exception;
                    _failedScheduledQueries.Add(registration.Id);
                    MarkScheduledQueryPending(registration);
                }
            }
            RequestScheduledQueryRefresh(false);
        }

        private void RequestScheduledQueryRefresh(bool includeDeferred)
        {
            _scheduledRefreshRequested = true;
            _deferredRefreshRequested |= includeDeferred;
            if (_refreshingScheduledQueries) return;

            _refreshingScheduledQueries = true;
            bool drainDeferred = false;
            try
            {
                while (_scheduledRefreshRequested)
                {
                    drainDeferred |= _deferredRefreshRequested;
                    _scheduledRefreshRequested = false;
                    _deferredRefreshRequested = false;
                    long[] pending = _pendingScheduledQueryIds.ToArray();
                    foreach (long id in pending)
                    {
                        if (!_scheduledQueries.TryGetValue(id, out IScheduledQuery registration) ||
                            !registration.IsActive)
                        {
                            _pendingScheduledQueryIds.Remove(id);
                            continue;
                        }
                        bool forced = _forcedScheduledQueryRetries.Remove(registration.Id);
                        if (!forced && !drainDeferred && registration.Mode == QueryRefreshMode.Deferred) continue;
                        registration.IsPending = false;
                        _pendingScheduledQueryIds.Remove(registration.Id);
                        try
                        {
                            ScheduledQueryRefreshExecutionCount++;
                            registration.Refresh(this);
                            registration.LastError = null;
                            _failedScheduledQueries.Remove(registration.Id);
                        }
                        catch (Exception exception)
                        {
                            registration.LastError = exception;
                            _failedScheduledQueries.Add(registration.Id);
                            ReportScheduledQueryFailure(registration, exception);
                        }
                    }
                }
            }
            finally
            {
                _refreshingScheduledQueries = false;
            }
        }

        private void MarkScheduledQueryPending(IScheduledQuery registration)
        {
            registration.IsPending = true;
            _pendingScheduledQueryIds.Add(registration.Id);
        }

        private void ReportScheduledQueryFailure(IScheduledQuery registration, Exception exception)
        {
            Action<ScheduledQueryFailure> handler = ScheduledQueryFailed;
            if (handler == null) return;
            try { handler(new ScheduledQueryFailure(registration.Id, registration.Query, exception)); }
            catch { }
        }
    }

    public sealed class QueryGroup<TKey, T>
    {
        private readonly IReadOnlyList<T> _items;
        public TKey Key { get; }
        public IReadOnlyList<T> Items => _items;
        internal QueryGroup(TKey key, IEnumerable<T> items)
        {
            Key = key;
            _items = new List<T>(items).AsReadOnly();
        }
    }

    public readonly struct QueryBatch<T>
    {
        private readonly IReadOnlyList<T> _source;
        private readonly int _offset;
        public int Count { get; }
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _source[_offset + index];
            }
        }

        internal QueryBatch(IReadOnlyList<T> source, int offset, int count)
        {
            _source = source;
            _offset = offset;
            Count = count;
        }
    }

    public static class PreparedQueryBatchExtensions
    {
        public static int ProcessBatches<T>(this PreparedQuery<T> query, SimulationWorld world,
            int batchSize, Action<QueryBatch<T>> process)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
            if (process == null) throw new ArgumentNullException(nameof(process));
            IReadOnlyList<T> values = query.Execute(world);
            int batches = 0;
            for (int offset = 0; offset < values.Count; offset += batchSize)
            {
                process(new QueryBatch<T>(values, offset, Math.Min(batchSize, values.Count - offset)));
                batches++;
            }
            return batches;
        }
    }

    public static class Query
    {
        public static QueryDependency DependsOn<TEntity, TValue>(
            AttributeDefinition<TEntity, TValue> attribute) where TEntity : WorldEntity
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));
            return new QueryDependency(world => world.GetAttributeQueryVersion(attribute),
                new QueryDependencyKey(QueryDependencyKind.Attribute, attribute));
        }

        public static QueryDependency DependsOn<TEntity, TValue>(TEntity entity,
            AttributeDefinition<TEntity, TValue> attribute) where TEntity : WorldEntity
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (entity.World == null)
                throw new InvalidOperationException("Entity-scoped query dependencies require an attached entity.");
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));
            return new QueryDependency(world => world.GetAttributeQueryVersion(entity, attribute),
                new QueryDependencyKey(QueryDependencyKind.EntityAttribute, attribute, entity.Id));
        }

        public static QueryDependency DependsOnRelation<TFrom, TTo>(
            RelationDefinition<TFrom, TTo> relation)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return new QueryDependency(world => world.GetRelationQueryVersion(relation),
                new QueryDependencyKey(QueryDependencyKind.Relation, relation));
        }

        public static QueryDependency DependsOnRelation<TFrom, TTo>(TFrom source,
            RelationDefinition<TFrom, TTo> relation)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.World == null)
                throw new InvalidOperationException("Source-scoped query dependencies require an attached entity.");
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return new QueryDependency(world => world.GetRelationQueryVersion(relation, source),
                new QueryDependencyKey(QueryDependencyKind.SourceRelation, relation, source.Id));
        }

        public static PreparedQuery<TEntity> All<TEntity>() where TEntity : WorldEntity =>
            new PreparedQuery<TEntity>(world => world.All<TEntity>(),
                new QueryDependency(world => world.StructureQueryVersion,
                    new QueryDependencyKey(QueryDependencyKind.Structure)));

        public static PreparedQuery<TTo> Related<TFrom, TTo>(TFrom source,
            RelationDefinition<TFrom, TTo> relation)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.World == null)
                throw new InvalidOperationException("Related queries require an attached source entity.");
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return new PreparedQuery<TTo>(world => world.Related(source, relation),
                new QueryDependency(world => world.StructureQueryVersion,
                    new QueryDependencyKey(QueryDependencyKind.Structure)),
                new QueryDependency(world => world.GetRelationQueryVersion(relation, source),
                    new QueryDependencyKey(QueryDependencyKind.SourceRelation, relation, source.Id)));
        }

        public static PreparedQuery<TNext> Traverse<TFrom, TMiddle, TNext>(TFrom source,
            RelationDefinition<TFrom, TMiddle> first, RelationDefinition<TMiddle, TNext> second)
            where TFrom : WorldEntity where TMiddle : WorldEntity where TNext : WorldEntity
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.World == null)
                throw new InvalidOperationException("Traversal queries require an attached source entity.");
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            return new PreparedQuery<TNext>(world => world.Related(source, first)
                    .SelectMany(item => world.Related(item, second))
                    .OrderBy(item => item.Id).Distinct(),
                new QueryDependency(world => world.StructureQueryVersion,
                    new QueryDependencyKey(QueryDependencyKind.Structure)),
                new QueryDependency(world => world.GetRelationQueryVersion(first, source),
                    new QueryDependencyKey(QueryDependencyKind.SourceRelation, first, source.Id)),
                new QueryDependency(world => world.GetRelationQueryVersion(second),
                    new QueryDependencyKey(QueryDependencyKind.Relation, second)));
        }
    }

    public sealed class PreparedTargetQuery<TSource, TTarget>
        where TSource : WorldEntity where TTarget : WorldEntity
    {
        private readonly Func<TSource, PreparedQuery<TTarget>> _factory;
        private readonly ConditionalWeakTable<TSource, PreparedQuery<TTarget>> _plans =
            new ConditionalWeakTable<TSource, PreparedQuery<TTarget>>();
        public PreparedTargetQuery(Func<TSource, PreparedQuery<TTarget>> factory) =>
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        public IReadOnlyList<TTarget> Execute(SimulationWorld world, TSource source)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (source == null) throw new ArgumentNullException(nameof(source));
            return _plans.GetValue(source, key => _factory(key)).Execute(world);
        }
    }
}
