#nullable enable

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Represents an ordered, duplicate-free set of <see cref="ComponentTypeId"/> used as an archetype signature.
    /// </summary>
    /// <remarks>
    /// - Always sorted ascending by <see cref="ComponentTypeId.Value"/>.
    /// - No duplicates.
    /// - 64-bit hash is stable and depends only on the ordered contents (recommended for <see cref="ArchetypeId"/>).
    /// - Not intended for the per-entity hot path; typically used at boot/archetype creation time.
    /// </remarks>
    public readonly struct ComponentTypeSet : IEquatable<ComponentTypeSet>
    {
        private readonly ComponentTypeId[]? _ids; // null means empty

        /// <summary>
        /// Gets an empty set.
        /// </summary>
        public static ComponentTypeSet Empty => default;

        /// <summary>
        /// Gets the number of ids in the set.
        /// </summary>
        public int Count => _ids?.Length ?? 0;

        /// <summary>
        /// Creates a set from an unordered list of ids. The resulting set is sorted and de-duplicated.
        /// </summary>
        public ComponentTypeSet(ReadOnlySpan<ComponentTypeId> unorderedIds)
        {
            if (unorderedIds.Length == 0)
            {
                _ids = null;
                return;
            }

            ComponentTypeId[] rented = ArrayPool<ComponentTypeId>.Shared.Rent(unorderedIds.Length);
            int len = 0;

            try
            {
                for (int i = 0; i < unorderedIds.Length; i++)
                {
                    rented[i] = unorderedIds[i];
                }

                StableSort.Sort(rented.AsSpan(0, unorderedIds.Length));

                ComponentTypeId prev = rented[0];
                rented[len++] = prev;

                for (int i = 1; i < unorderedIds.Length; i++)
                {
                    ComponentTypeId cur = rented[i];
                    if (cur.Value != prev.Value)
                    {
                        rented[len++] = cur;
                        prev = cur;
                    }
                }

                _ids = new ComponentTypeId[len];
                Array.Copy(rented, 0, _ids, 0, len);
            }
            finally
            {
                ArrayPool<ComponentTypeId>.Shared.Return(rented, false);
            }
        }

        private ComponentTypeSet(ComponentTypeId[] orderedDistinctIds)
        {
            _ids = orderedDistinctIds.Length == 0 ? null : orderedDistinctIds;
        }

        /// <summary>
        /// Returns the ordered ids as a read-only span.
        /// </summary>
        public ReadOnlySpan<ComponentTypeId> AsSpan()
        {
            return _ids is null ? ReadOnlySpan<ComponentTypeId>.Empty : _ids;
        }

        /// <summary>
        /// Returns true if the set contains the given id.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(ComponentTypeId id)
        {
            if (_ids is null)
                return false;

            return BinarySearch(_ids, id.Value) >= 0;
        }

        /// <summary>
        /// Adds an id to the set (keeps ordering, no duplicates) and returns a new set.
        /// If already present, returns this set.
        /// </summary>
        public ComponentTypeSet Add(ComponentTypeId id)
        {
            if (_ids is null)
                return new ComponentTypeSet(new[] { id });

            int idx = BinarySearch(_ids, id.Value);
            if (idx >= 0)
                return this;

            int insertAt = ~idx;

            ComponentTypeId[] newIds = new ComponentTypeId[_ids.Length + 1];
            if (insertAt > 0)
                Array.Copy(_ids, 0, newIds, 0, insertAt);

            newIds[insertAt] = id;

            if (insertAt < _ids.Length)
                Array.Copy(_ids, insertAt, newIds, insertAt + 1, _ids.Length - insertAt);

            return new ComponentTypeSet(newIds);
        }

        /// <summary>
        /// Removes an id from the set and returns a new set.
        /// If not present, returns this set.
        /// </summary>
        public ComponentTypeSet Remove(ComponentTypeId id)
        {
            if (_ids is null)
                return this;

            int idx = BinarySearch(_ids, id.Value);
            if (idx < 0)
                return this;

            if (_ids.Length == 1)
                return default;

            ComponentTypeId[] newIds = new ComponentTypeId[_ids.Length - 1];
            if (idx > 0)
                Array.Copy(_ids, 0, newIds, 0, idx);

            if (idx < _ids.Length - 1)
                Array.Copy(_ids, idx + 1, newIds, idx, _ids.Length - idx - 1);

            return new ComponentTypeSet(newIds);
        }

        /// <summary>
        /// Computes a stable 64-bit hash of the ordered set. Suitable as a base for <see cref="ArchetypeId"/>.
        /// </summary>
        public ulong GetStableHash64()
        {
            if (_ids is null)
                return DeterministicHash.Fnv1A64Init;

            return DeterministicHash.HashComponentTypeIds64(_ids);
        }

        /// <summary>
        /// Computes a stable 32-bit hash of the ordered set (useful for <see cref="GetHashCode"/>).
        /// </summary>
        public uint GetStableHash32()
        {
            ulong h = GetStableHash64();
            return unchecked((uint)(h ^ (h >> 32)));
        }

        public bool Equals(ComponentTypeSet other)
        {
            ReadOnlySpan<ComponentTypeId> a = AsSpan();
            ReadOnlySpan<ComponentTypeId> b = other.AsSpan();

            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Value != b[i].Value)
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is ComponentTypeSet other && Equals(other);
        }

        /// <summary>
        /// Deterministic hash code for managed hash tables (folded from the stable 64-bit hash).
        /// </summary>
        public override int GetHashCode()
        {
            return unchecked((int)GetStableHash32());
        }

        public static bool operator ==(ComponentTypeSet a, ComponentTypeSet b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ComponentTypeSet a, ComponentTypeSet b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            if (_ids is null)
                return "ComponentTypeSet(Empty)";

            return $"ComponentTypeSet(Count={_ids.Length}, Hash64=0x{GetStableHash64():X16})";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BinarySearch(ComponentTypeId[] orderedIds, int value)
        {
            int lo = 0;
            int hi = orderedIds.Length - 1;

            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int midVal = orderedIds[mid].Value;

                if (midVal == value)
                    return mid;

                if (midVal < value)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return ~lo;
        }
    }
}