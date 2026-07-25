using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DeterministicFixedPoint;

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

    internal readonly struct QuerySourceDelta<T>
    {
        public bool HasItem { get; }
        public bool Added { get; }
        public T Item { get; }

        private QuerySourceDelta(bool hasItem, bool added, T item)
        {
            HasItem = hasItem;
            Added = added;
            Item = item;
        }

        public static QuerySourceDelta<T> Signal() => new QuerySourceDelta<T>(false, false, default);
        public static QuerySourceDelta<T> Add(T item) => new QuerySourceDelta<T>(true, true, item);
        public static QuerySourceDelta<T> Remove(T item) => new QuerySourceDelta<T>(true, false, item);
    }

    internal interface IQueryDeltaSource<T>
    {
        IReadOnlyCollection<QueryDependencyKey> SupportedDependencies { get; }
        IDisposable Subscribe(SimulationWorld world, Action<QuerySourceDelta<T>> observer);
    }

    internal sealed class RelationQueryDeltaSource<TFrom, TTo> : IQueryDeltaSource<TTo>
        where TFrom : WorldEntity where TTo : WorldEntity
    {
        private readonly TFrom _source;
        private readonly RelationDefinition<TFrom, TTo> _definition;
        private readonly QueryDependencyKey[] _supported;

        public IReadOnlyCollection<QueryDependencyKey> SupportedDependencies => _supported;

        public RelationQueryDeltaSource(TFrom source, RelationDefinition<TFrom, TTo> definition)
        {
            _source = source;
            _definition = definition;
            _supported = new[]
            {
                new QueryDependencyKey(QueryDependencyKind.SourceRelation, definition, source.Id)
            };
        }

        public IDisposable Subscribe(SimulationWorld world, Action<QuerySourceDelta<TTo>> observer)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            void Handler(RelationMutation mutation)
            {
                if (!ReferenceEquals(mutation.Definition, _definition)) return;
                if (mutation.From.Id != _source.Id) return;
                if (!(mutation.To is TTo item)) return;
                observer(mutation.Added
                    ? QuerySourceDelta<TTo>.Add(item)
                    : QuerySourceDelta<TTo>.Remove(item));
            }
            world.RelationMutated += Handler;
            return new DelegateDisposable(() => world.RelationMutated -= Handler);
        }
    }

    internal sealed class IncomingRelationQueryDeltaSource<TFrom, TTo> : IQueryDeltaSource<TFrom>
        where TFrom : WorldEntity where TTo : WorldEntity
    {
        private readonly TTo _target;
        private readonly RelationDefinition<TFrom, TTo> _definition;
        private readonly QueryDependencyKey[] _supported;

        public IReadOnlyCollection<QueryDependencyKey> SupportedDependencies => _supported;

        public IncomingRelationQueryDeltaSource(TTo target, RelationDefinition<TFrom, TTo> definition)
        {
            _target = target;
            _definition = definition;
            _supported = new[] { new QueryDependencyKey(QueryDependencyKind.Relation, definition) };
        }

        public IDisposable Subscribe(SimulationWorld world, Action<QuerySourceDelta<TFrom>> observer)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            void Handler(RelationMutation mutation)
            {
                if (!ReferenceEquals(mutation.Definition, _definition)) return;
                if (mutation.To.Id != _target.Id)
                {
                    observer(QuerySourceDelta<TFrom>.Signal());
                    return;
                }
                if (!(mutation.From is TFrom item)) return;
                observer(mutation.Added
                    ? QuerySourceDelta<TFrom>.Add(item)
                    : QuerySourceDelta<TFrom>.Remove(item));
            }
            world.RelationMutated += Handler;
            return new DelegateDisposable(() => world.RelationMutated -= Handler);
        }
    }

    internal sealed class FilteredQueryDeltaSource<T> : IQueryDeltaSource<T>
    {
        private readonly IQueryDeltaSource<T> _inner;
        private readonly Func<T, bool> _predicate;
        public IReadOnlyCollection<QueryDependencyKey> SupportedDependencies => _inner.SupportedDependencies;

        public FilteredQueryDeltaSource(IQueryDeltaSource<T> inner, Func<T, bool> predicate)
        {
            _inner = inner;
            _predicate = predicate;
        }

        public IDisposable Subscribe(SimulationWorld world, Action<QuerySourceDelta<T>> observer) =>
            _inner.Subscribe(world, delta =>
            {
                if (!delta.HasItem)
                {
                    observer(delta);
                    return;
                }
                if (!delta.Added || _predicate(delta.Item)) observer(delta);
            });
    }

    internal sealed class DelegateDisposable : IDisposable
    {
        private Action _dispose;
        public DelegateDisposable(Action dispose) => _dispose = dispose;
        public void Dispose()
        {
            Action dispose = _dispose;
            if (dispose == null) return;
            _dispose = null;
            dispose();
        }
    }

    public sealed class PreparedQuery<T>
    {
        private readonly Func<SimulationWorld, IEnumerable<T>> _execute;
        private readonly QueryDependency[] _dependencies;
        private readonly IReadOnlyList<QueryDependencyKey> _dependencyKeys;
        private readonly IQueryDeltaSource<T> _deltaSource;
        private readonly Comparison<T> _ordering;
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
            : this(execute, null, null, dependencies)
        {
        }

        internal PreparedQuery(Func<SimulationWorld, IEnumerable<T>> execute,
            IQueryDeltaSource<T> deltaSource, Comparison<T> ordering,
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
            _deltaSource = deltaSource;
            _ordering = ordering;
        }

        public IReadOnlyList<T> Execute(SimulationWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!IsCacheValid(world))
            {
                long[] dependencyVersions = CaptureDependencyVersions(world);
                IReadOnlyList<T> cache = Materialize(_execute(world));
                _cache = cache;
                ExecutionCount++;
                _cachedWorld = world;
                _cachedDependencyVersions = dependencyVersions;
            }
            return _cache;
        }

        private static IReadOnlyList<T> Materialize(IEnumerable<T> values)
        {
            if (values == null) return Array.Empty<T>();
            var result = new List<T>();
            foreach (T value in values) result.Add(value);
            return result.AsReadOnly();
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
            Func<SimulationWorld, IEnumerable<T>> execute = world =>
            {
                IReadOnlyList<T> source = Execute(world);
                var result = new List<T>();
                for (int index = 0; index < source.Count; index++)
                    if (predicate(source[index])) result.Add(source[index]);
                return result;
            };
            return HasDependencies(dependencies)
                ? new PreparedQuery<T>(execute,
                    _deltaSource == null ? null : new FilteredQueryDeltaSource<T>(_deltaSource, predicate),
                    _ordering, CombineDependencies(dependencies))
                : new PreparedQuery<T>(execute, null, _ordering,
                    new QueryDependency(world => world.Version,
                        new QueryDependencyKey(QueryDependencyKind.World)));
        }

        public PreparedQuery<T> Ordered<TKey>(Func<T, TKey> keySelector, bool descending = false,
            params QueryDependency[] dependencies)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            Func<SimulationWorld, IEnumerable<T>> execute = world =>
            {
                IReadOnlyList<T> source = Execute(world);
                var indexed = new List<Tuple<T, int>>(source.Count);
                for (int index = 0; index < source.Count; index++)
                    indexed.Add(Tuple.Create(source[index], index));
                Comparer<TKey> comparer = Comparer<TKey>.Default;
                indexed.Sort((left, right) =>
                {
                    int value = comparer.Compare(keySelector(left.Item1), keySelector(right.Item1));
                    if (descending) value = -value;
                    return value != 0 ? value : left.Item2.CompareTo(right.Item2);
                });
                var result = new List<T>(indexed.Count);
                for (int index = 0; index < indexed.Count; index++) result.Add(indexed[index].Item1);
                return result;
            };
            Comparison<T> ordering = (left, right) =>
            {
                int value = Comparer<TKey>.Default.Compare(keySelector(left), keySelector(right));
                if (descending) value = -value;
                return value;
            };
            return HasDependencies(dependencies)
                ? new PreparedQuery<T>(execute, _deltaSource, ordering, CombineDependencies(dependencies))
                : new PreparedQuery<T>(execute, null, ordering,
                    new QueryDependency(world => world.Version,
                        new QueryDependencyKey(QueryDependencyKind.World)));
        }

        public PreparedQuery<T> OrderBy<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies) => Ordered(keySelector, false, dependencies);

        public PreparedQuery<T> OrderByDescending<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies) => Ordered(keySelector, true, dependencies);

        public PreparedQuery<T> ThenBy<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies) => ThenOrdered(keySelector, false, dependencies);

        public PreparedQuery<T> ThenByDescending<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies) => ThenOrdered(keySelector, true, dependencies);

        public PreparedQuery<T> Take(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new PreparedQuery<T>(world =>
            {
                IReadOnlyList<T> source = Execute(world);
                int length = Math.Min(count, source.Count);
                var result = new List<T>(length);
                for (int index = 0; index < length; index++) result.Add(source[index]);
                return result;
            }, _dependencies);
        }

        public PreparedQuery<T> Skip(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            return new PreparedQuery<T>(world =>
            {
                IReadOnlyList<T> source = Execute(world);
                if (count >= source.Count) return Array.Empty<T>();
                var result = new List<T>(source.Count - count);
                for (int index = count; index < source.Count; index++) result.Add(source[index]);
                return result;
            }, _dependencies);
        }

        public PreparedQuery<T> Distinct(IEqualityComparer<T> comparer = null)
        {
            comparer = comparer ?? EqualityComparer<T>.Default;
            return new PreparedQuery<T>(world =>
            {
                IReadOnlyList<T> source = Execute(world);
                var seen = new HashSet<T>(comparer);
                var result = new List<T>();
                for (int index = 0; index < source.Count; index++)
                    if (seen.Add(source[index])) result.Add(source[index]);
                return result;
            }, _dependencies);
        }
        public PreparedQuery<TResult> Select<TResult>(Func<T, TResult> selector,
            params QueryDependency[] dependencies)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            Func<SimulationWorld, IEnumerable<TResult>> execute = world =>
            {
                IReadOnlyList<T> source = Execute(world);
                var result = new List<TResult>(source.Count);
                for (int index = 0; index < source.Count; index++) result.Add(selector(source[index]));
                return result;
            };
            return HasDependencies(dependencies)
                ? new PreparedQuery<TResult>(execute, CombineDependencies(dependencies))
                : new PreparedQuery<TResult>(execute);
        }

        public PreparedQuery<TResult> SelectMany<TResult>(Func<T, IEnumerable<TResult>> selector,
            params QueryDependency[] dependencies)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            Func<SimulationWorld, IEnumerable<TResult>> execute = world =>
            {
                IReadOnlyList<T> source = Execute(world);
                var result = new List<TResult>();
                for (int index = 0; index < source.Count; index++)
                {
                    IEnumerable<TResult> selected = selector(source[index]);
                    if (selected == null) continue;
                    foreach (TResult item in selected) result.Add(item);
                }
                return result;
            };
            return HasDependencies(dependencies)
                ? new PreparedQuery<TResult>(execute, CombineDependencies(dependencies))
                : new PreparedQuery<TResult>(execute);
        }

        public PreparedQuery<QueryGroup<TKey, T>> GroupBy<TKey>(Func<T, TKey> keySelector,
            params QueryDependency[] dependencies)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            Func<SimulationWorld, IEnumerable<QueryGroup<TKey, T>>> execute = world =>
            {
                IReadOnlyList<T> source = Execute(world);
                var groups = new Dictionary<TKey, List<T>>();
                var order = new List<TKey>();
                var nullGroup = new List<T>();
                bool hasNullGroup = false;
                for (int index = 0; index < source.Count; index++)
                {
                    TKey key = keySelector(source[index]);
                    if (ReferenceEquals(key, null))
                    {
                        if (!hasNullGroup)
                        {
                            order.Add(key);
                            hasNullGroup = true;
                        }
                        nullGroup.Add(source[index]);
                        continue;
                    }
                    if (!groups.TryGetValue(key, out List<T> values))
                    {
                        groups.Add(key, values = new List<T>());
                        order.Add(key);
                    }
                    values.Add(source[index]);
                }
                var result = new List<QueryGroup<TKey, T>>(order.Count);
                foreach (TKey key in order)
                    result.Add(new QueryGroup<TKey, T>(key,
                        ReferenceEquals(key, null) ? nullGroup : groups[key]));
                return result;
            };
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
            {
                IReadOnlyList<T> left = Execute(world);
                IReadOnlyList<TOther> right = other.Execute(world);
                var lookup = new Dictionary<TKey, List<TOther>>();
                var nulls = new List<TOther>();
                for (int index = 0; index < right.Count; index++)
                {
                    TKey key = rightKey(right[index]);
                    if (ReferenceEquals(key, null)) { nulls.Add(right[index]); continue; }
                    if (!lookup.TryGetValue(key, out List<TOther> values))
                        lookup.Add(key, values = new List<TOther>());
                    values.Add(right[index]);
                }
                var joined = new List<TResult>();
                for (int index = 0; index < left.Count; index++)
                {
                    TKey key = leftKey(left[index]);
                    List<TOther> matches;
                    if (ReferenceEquals(key, null)) matches = nulls;
                    else if (!lookup.TryGetValue(key, out matches)) continue;
                    for (int match = 0; match < matches.Count; match++)
                        joined.Add(result(left[index], matches[match]));
                }
                return joined;
            };
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
        public T First(SimulationWorld world, Func<T, bool> predicate = null)
        {
            IReadOnlyList<T> values = Execute(world);
            if (predicate == null)
            {
                if (values.Count != 0) return values[0];
            }
            else
            {
                for (int index = 0; index < values.Count; index++)
                    if (predicate(values[index])) return values[index];
            }
            throw new InvalidOperationException("Sequence contains no matching element.");
        }

        public T FirstOrDefault(SimulationWorld world, Func<T, bool> predicate = null)
        {
            IReadOnlyList<T> values = Execute(world);
            if (predicate == null) return values.Count == 0 ? default : values[0];
            for (int index = 0; index < values.Count; index++)
                if (predicate(values[index])) return values[index];
            return default;
        }

        public T Single(SimulationWorld world, Func<T, bool> predicate = null)
        {
            IReadOnlyList<T> values = Execute(world);
            bool found = false;
            T result = default;
            for (int index = 0; index < values.Count; index++)
            {
                T candidate = values[index];
                if (predicate != null && !predicate(candidate)) continue;
                if (found) throw new InvalidOperationException("Sequence contains more than one matching element.");
                result = candidate;
                found = true;
            }
            if (!found) throw new InvalidOperationException("Sequence contains no matching element.");
            return result;
        }

        public T SingleOrDefault(SimulationWorld world, Func<T, bool> predicate = null)
        {
            IReadOnlyList<T> values = Execute(world);
            bool found = false;
            T result = default;
            for (int index = 0; index < values.Count; index++)
            {
                T candidate = values[index];
                if (predicate != null && !predicate(candidate)) continue;
                if (found) throw new InvalidOperationException("Sequence contains more than one matching element.");
                result = candidate;
                found = true;
            }
            return result;
        }

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

        public DetS64 Average(SimulationWorld world, Func<T, DetS64> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            IReadOnlyList<T> values = Execute(world);
            if (values.Count == 0) throw new InvalidOperationException("Sequence contains no elements.");
            DetS64 sum = DetS64.Zero;
            for (int index = 0; index < values.Count; index++) sum += selector(values[index]);
            return sum / DetS64.FromLong(values.Count);
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

        private PreparedQuery<T> ThenOrdered<TKey>(Func<T, TKey> keySelector, bool descending,
            QueryDependency[] dependencies)
        {
            if (_ordering == null)
                throw new InvalidOperationException("ThenBy requires a preceding OrderBy or OrderByDescending.");
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            Comparison<T> combined = (left, right) =>
            {
                int primary = _ordering(left, right);
                if (primary != 0) return primary;
                int value = Comparer<TKey>.Default.Compare(keySelector(left), keySelector(right));
                if (descending) value = -value;
                return value;
            };
            Func<SimulationWorld, IEnumerable<T>> execute = world =>
            {
                IReadOnlyList<T> source = Execute(world);
                var indexed = new List<Tuple<T, int>>(source.Count);
                for (int index = 0; index < source.Count; index++)
                    indexed.Add(Tuple.Create(source[index], index));
                indexed.Sort((left, right) =>
                {
                    int comparison = combined(left.Item1, right.Item1);
                    return comparison != 0 ? comparison : left.Item2.CompareTo(right.Item2);
                });
                var result = new List<T>(indexed.Count);
                for (int index = 0; index < indexed.Count; index++) result.Add(indexed[index].Item1);
                return result;
            };
            return HasDependencies(dependencies)
                ? new PreparedQuery<T>(execute, _deltaSource, combined, CombineDependencies(dependencies))
                : new PreparedQuery<T>(execute, null, combined,
                    new QueryDependency(world => world.Version,
                        new QueryDependencyKey(QueryDependencyKind.World)));
        }

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
        internal IQueryDeltaSource<T> DeltaSource => _deltaSource;
        internal Comparison<T> IncrementalOrdering => _ordering;

        internal long[] CaptureDependencyVersionVector(SimulationWorld world) =>
            CaptureDependencyVersions(world);

        internal bool AreChangesCovered(SimulationWorld world, long[] previous,
            IReadOnlyCollection<QueryDependencyKey> supported)
        {
            if (previous == null || previous.Length != _dependencies.Length || supported == null) return false;
            var keys = new HashSet<QueryDependencyKey>(supported);
            for (int index = 0; index < _dependencies.Length; index++)
            {
                long current = _dependencies[index].Version(world);
                if (current != previous[index] && !keys.Contains(_dependencies[index].Key)) return false;
            }
            return true;
        }

        internal void AcceptIncrementalSnapshot(SimulationWorld world, IReadOnlyList<T> snapshot)
        {
            _cache = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _cachedWorld = world ?? throw new ArgumentNullException(nameof(world));
            _cachedDependencyVersions = CaptureDependencyVersions(world);
        }
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

    public sealed class MaterializedQuery<T, TKey> : IDisposable
    {
        private readonly PreparedQuery<T> _query;
        private readonly Func<T, TKey> _keySelector;
        private readonly IEqualityComparer<TKey> _keyComparer;
        private readonly IEqualityComparer<T> _valueComparer;
        private Dictionary<TKey, T> _items;
        private Dictionary<TKey, int> _keyPositions;
        private List<TKey> _orderedKeys;
        private IReadOnlyList<T> _current = Array.Empty<T>();
        private IReadOnlyList<T> _sourceSnapshot;
        private SimulationWorld _world;
        private bool _initialized;
        private IDisposable _deltaSubscription;
        private readonly List<QuerySourceDelta<T>> _pendingSourceDeltas = new List<QuerySourceDelta<T>>();
        private bool _pendingSourceSignal;
        private long[] _dependencyVersions;

        public event Action<QueryDelta<T, TKey>> Changed;

        public IReadOnlyList<T> Current => _current;
        public bool IsInitialized => _initialized;
        public long RefreshCount { get; private set; }
        public long DiffCount { get; private set; }
        public long IncrementalUpdateCount { get; private set; }

        public MaterializedQuery(PreparedQuery<T> query, Func<T, TKey> keySelector,
            IEqualityComparer<TKey> keyComparer = null, IEqualityComparer<T> valueComparer = null)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            _valueComparer = valueComparer ?? EqualityComparer<T>.Default;
            _items = new Dictionary<TKey, T>(_keyComparer);
            _keyPositions = new Dictionary<TKey, int>(_keyComparer);
            _orderedKeys = new List<TKey>();
        }

        public QueryDelta<T, TKey> Refresh(SimulationWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (_world != null && !ReferenceEquals(_world, world))
                throw new InvalidOperationException("A materialized query cannot be shared between simulation worlds.");
            RefreshCount++;

            if (_initialized && _pendingSourceSignal && _query.DeltaSource != null &&
                _query.AreChangesCovered(world, _dependencyVersions,
                    _query.DeltaSource.SupportedDependencies))
                return ApplyPendingSourceDeltas(world);

            IReadOnlyList<T> source = _query.Execute(world);
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
            RebuildKeyPositions();
            _current = nextValues.AsReadOnly();
            _sourceSnapshot = source;
            _initialized = true;
            _dependencyVersions = _query.CaptureDependencyVersionVector(world);
            _pendingSourceDeltas.Clear();
            _pendingSourceSignal = false;
            EnsureDeltaSubscription(world);
            DiffCount++;
            if (delta.HasChanges) Changed?.Invoke(delta);
            return delta;
        }

        /// <summary>
        /// Applies a producer-supplied keyed delta without executing or diffing the
        /// complete prepared query. This is the preferred path for indexed stores
        /// that already know which keys changed.
        /// </summary>
        public QueryDelta<T, TKey> ApplyDelta(SimulationWorld world, IEnumerable<T> upserted,
            IEnumerable<TKey> removedKeys)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!_initialized || !ReferenceEquals(_world, world))
                throw new InvalidOperationException(
                    "Initialize the materialized query with Refresh before applying deltas.");
            var added = new List<T>();
            var removed = new List<T>();
            var updated = new List<QueryItemUpdate<TKey, T>>();
            var previousKeys = new List<TKey>(_orderedKeys);
            var previousKeySet = new HashSet<TKey>(previousKeys, _keyComparer);

            foreach (TKey key in removedKeys ?? Enumerable.Empty<TKey>())
            {
                if (ReferenceEquals(key, null))
                    throw new InvalidOperationException("Materialized query keys cannot be null.");
                if (!_items.TryGetValue(key, out T previous)) continue;
                int index = FindKeyIndex(key);
                _items.Remove(key);
                _orderedKeys.RemoveAt(index);
                _keyPositions.Remove(key);
                UpdateKeyPositionsFrom(index);
                removed.Add(previous);
            }

            foreach (T item in upserted ?? Enumerable.Empty<T>())
            {
                TKey key = _keySelector(item);
                if (ReferenceEquals(key, null))
                    throw new InvalidOperationException("Materialized query keys cannot be null.");
                if (_items.TryGetValue(key, out T previous))
                {
                    if (_valueComparer.Equals(previous, item)) continue;
                    _items[key] = item;
                    updated.Add(new QueryItemUpdate<TKey, T>(key, previous, item));
                }
                else
                {
                    _items.Add(key, item);
                    _orderedKeys.Add(key);
                    _keyPositions.Add(key, _orderedKeys.Count - 1);
                    added.Add(item);
                }
            }

            SortOrderedKeys(previousKeys);
            var values = new List<T>(_orderedKeys.Count);
            foreach (TKey key in _orderedKeys) values.Add(_items[key]);
            _current = values.AsReadOnly();
            _sourceSnapshot = null;
            bool orderChanged = HasRetainedOrderChanged(previousKeys, previousKeySet);
            var delta = new QueryDelta<T, TKey>(false, added, removed, updated, orderChanged);
            IncrementalUpdateCount++;
            DiffCount++;
            if (delta.HasChanges) Changed?.Invoke(delta);
            return delta;
        }

        private int FindKeyIndex(TKey key)
        {
            if (_keyPositions.TryGetValue(key, out int index)) return index;
            throw new InvalidOperationException($"Materialized query key '{key}' is not indexed.");
        }

        private QueryDelta<T, TKey> ApplyPendingSourceDeltas(SimulationWorld world)
        {
            var previousKeys = new List<TKey>(_orderedKeys);
            var previousKeySet = new HashSet<TKey>(previousKeys, _keyComparer);
            var touched = new Dictionary<TKey, Tuple<bool, T>>(_keyComparer);

            foreach (QuerySourceDelta<T> sourceDelta in _pendingSourceDeltas)
            {
                if (!sourceDelta.HasItem) continue;
                TKey key = _keySelector(sourceDelta.Item);
                if (ReferenceEquals(key, null))
                    throw new InvalidOperationException("Materialized query keys cannot be null.");
                if (!touched.ContainsKey(key))
                    touched.Add(key, _items.TryGetValue(key, out T previous)
                        ? Tuple.Create(true, previous)
                        : Tuple.Create(false, default(T)));

                if (sourceDelta.Added)
                {
                    if (!_items.ContainsKey(key))
                    {
                        _orderedKeys.Add(key);
                        _keyPositions.Add(key, _orderedKeys.Count - 1);
                    }
                    _items[key] = sourceDelta.Item;
                }
                else if (_items.Remove(key))
                {
                    int index = FindKeyIndex(key);
                    _orderedKeys.RemoveAt(index);
                    _keyPositions.Remove(key);
                    UpdateKeyPositionsFrom(index);
                }
            }

            SortOrderedKeys(previousKeys);

            var added = new List<T>();
            var removed = new List<T>();
            var updated = new List<QueryItemUpdate<TKey, T>>();
            foreach (TKey key in previousKeys)
            {
                if (touched.TryGetValue(key, out Tuple<bool, T> change) &&
                    change.Item1 && !_items.ContainsKey(key))
                    removed.Add(change.Item2);
            }
            foreach (TKey key in _orderedKeys)
            {
                if (!touched.TryGetValue(key, out Tuple<bool, T> change)) continue;
                T current = _items[key];
                if (!change.Item1) added.Add(current);
                else if (!_valueComparer.Equals(change.Item2, current))
                    updated.Add(new QueryItemUpdate<TKey, T>(key, change.Item2, current));
            }

            var values = new List<T>(_orderedKeys.Count);
            foreach (TKey key in _orderedKeys) values.Add(_items[key]);
            bool orderChanged = HasRetainedOrderChanged(previousKeys, previousKeySet);
            _current = values.AsReadOnly();
            _query.AcceptIncrementalSnapshot(world, _current);
            _sourceSnapshot = _current;
            _dependencyVersions = _query.CaptureDependencyVersionVector(world);
            _pendingSourceDeltas.Clear();
            _pendingSourceSignal = false;
            IncrementalUpdateCount++;
            DiffCount++;
            var delta = new QueryDelta<T, TKey>(false, added, removed, updated, orderChanged);
            if (delta.HasChanges) Changed?.Invoke(delta);
            return delta;
        }

        private bool HasRetainedOrderChanged(IReadOnlyList<TKey> previousKeys, HashSet<TKey> previousKeySet)
        {
            var previousRetained = new List<TKey>();
            foreach (TKey key in previousKeys)
                if (_items.ContainsKey(key)) previousRetained.Add(key);
            var currentRetained = new List<TKey>();
            foreach (TKey key in _orderedKeys)
                if (previousKeySet.Contains(key)) currentRetained.Add(key);
            if (previousRetained.Count != currentRetained.Count) return true;
            for (int index = 0; index < previousRetained.Count; index++)
                if (!_keyComparer.Equals(previousRetained[index], currentRetained[index])) return true;
            return false;
        }

        private void EnsureDeltaSubscription(SimulationWorld world)
        {
            if (_deltaSubscription != null || _query.DeltaSource == null) return;
            _deltaSubscription = _query.DeltaSource.Subscribe(world, delta =>
            {
                _pendingSourceSignal = true;
                if (delta.HasItem) _pendingSourceDeltas.Add(delta);
            });
        }

        private void RebuildKeyPositions()
        {
            _keyPositions = new Dictionary<TKey, int>(_keyComparer);
            for (int index = 0; index < _orderedKeys.Count; index++)
                _keyPositions.Add(_orderedKeys[index], index);
        }

        private void SortOrderedKeys(IReadOnlyList<TKey> previousKeys)
        {
            Comparison<T> ordering = _query.IncrementalOrdering;
            if (ordering == null || _orderedKeys.Count <= 1) return;
            var previousRanks = new Dictionary<TKey, int>(_keyComparer);
            for (int index = 0; index < previousKeys.Count; index++)
                previousRanks[previousKeys[index]] = index;
            int nextRank = previousRanks.Count;
            foreach (TKey key in _orderedKeys)
                if (!previousRanks.ContainsKey(key)) previousRanks.Add(key, nextRank++);
            _orderedKeys.Sort((leftKey, rightKey) =>
            {
                T left = _items[leftKey];
                T right = _items[rightKey];
                int comparison = ordering(left, right);
                if (comparison != 0) return comparison;
                if (left is WorldEntity leftEntity && right is WorldEntity rightEntity)
                    return leftEntity.Id.CompareTo(rightEntity.Id);
                return previousRanks[leftKey].CompareTo(previousRanks[rightKey]);
            });
            RebuildKeyPositions();
        }

        private void UpdateKeyPositionsFrom(int index)
        {
            for (int current = index; current < _orderedKeys.Count; current++)
                _keyPositions[_orderedKeys[current]] = current;
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

        public void Dispose()
        {
            _deltaSubscription?.Dispose();
            _deltaSubscription = null;
            _pendingSourceDeltas.Clear();
            _pendingSourceSignal = false;
        }
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
                new RelationQueryDeltaSource<TFrom, TTo>(source, relation),
                (left, right) => left.Id.CompareTo(right.Id),
                new QueryDependency(world => world.GetRelationQueryVersion(relation, source),
                    new QueryDependencyKey(QueryDependencyKind.SourceRelation, relation, source.Id)));
        }

        public static PreparedQuery<TFrom> RelatedFrom<TFrom, TTo>(TTo target,
            RelationDefinition<TFrom, TTo> relation)
            where TFrom : WorldEntity where TTo : WorldEntity
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.World == null)
                throw new InvalidOperationException("Inverse related queries require an attached target entity.");
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return new PreparedQuery<TFrom>(world => world.RelatedFrom(target, relation),
                new IncomingRelationQueryDeltaSource<TFrom, TTo>(target, relation),
                (left, right) => left.Id.CompareTo(right.Id),
                new QueryDependency(world => world.GetRelationQueryVersion(relation),
                    new QueryDependencyKey(QueryDependencyKind.Relation, relation)));
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
            return Related(source, first).Follow(second);
        }
    }

    public static class GraphQueryExtensions
    {
        /// <summary>
        /// Extends a typed graph path by one edge. Calls can be chained to traverse
        /// an arbitrary number of named relations.
        /// </summary>
        public static PreparedQuery<TNext> Follow<TCurrent, TNext>(
            this PreparedQuery<TCurrent> query,
            RelationDefinition<TCurrent, TNext> relation)
            where TCurrent : WorldEntity where TNext : WorldEntity
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return query.SelectMany(current => current.Related(relation),
                    Query.DependsOnRelation(relation))
                .Distinct()
                .OrderBy(item => item.Id);
        }

        public static PreparedQuery<TPrevious> FollowIncoming<TPrevious, TCurrent>(
            this PreparedQuery<TCurrent> query,
            RelationDefinition<TPrevious, TCurrent> relation)
            where TPrevious : WorldEntity where TCurrent : WorldEntity
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (relation == null) throw new ArgumentNullException(nameof(relation));
            return query.SelectMany(current => current.World.RelatedFrom(current, relation),
                    Query.DependsOnRelation(relation))
                .Distinct()
                .OrderBy(item => item.Id);
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
