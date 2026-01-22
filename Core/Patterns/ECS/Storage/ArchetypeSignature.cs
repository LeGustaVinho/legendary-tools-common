using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Represents a sorted list of component types that define an archetype.
    /// Immutable and hashable.
    /// </summary>
    public sealed class ArchetypeSignature : IEquatable<ArchetypeSignature>
    {
        /// <summary>
        /// Sorted stable type ids (hashed ids). Must never be used to index large arrays.
        /// </summary>
        public readonly int[] TypeIds;

        /// <summary>
        /// Reserved for future shared components.
        /// </summary>
        public readonly int[] SharedTypeIds;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArchetypeSignature"/> class.
        /// </summary>
        /// <param name="typeIds">List of component type IDs (will be sorted and copied).</param>
        public ArchetypeSignature(ReadOnlySpan<int> typeIds)
        {
            if (typeIds.Length == 0)
            {
                TypeIds = Array.Empty<int>();
                SharedTypeIds = Array.Empty<int>();
                return;
            }

            TypeIds = typeIds.ToArray();
            Array.Sort(TypeIds);

            // Shared components not implemented yet.
            SharedTypeIds = Array.Empty<int>();
        }

        /// <summary>
        /// Internal constructor for cases where the caller already provides a sorted, owned array.
        /// </summary>
        internal ArchetypeSignature(int[] typeIdsSortedOwned, bool alreadySorted)
        {
            if (typeIdsSortedOwned == null || typeIdsSortedOwned.Length == 0)
            {
                TypeIds = Array.Empty<int>();
                SharedTypeIds = Array.Empty<int>();
                return;
            }

            TypeIds = typeIdsSortedOwned;

            if (!alreadySorted)
                Array.Sort(TypeIds);

            // Shared components not implemented yet.
            SharedTypeIds = Array.Empty<int>();
        }

        /// <summary>
        /// Computes a stable 64-bit hash of the signature.
        /// </summary>
        /// <returns>64-bit hash.</returns>
        public ulong ComputeStableHash64()
        {
            // NOTE: Intentionally hashes only TypeIds for now (shared components are not implemented).
            return ComputeStableHash64(TypeIds);
        }

        /// <summary>
        /// Computes a stable 32-bit hash of the signature.
        /// </summary>
        /// <param name="seed">Optional seed.</param>
        /// <returns>32-bit hash.</returns>
        public uint ComputeStableHash32(uint seed = 2166136261u)
        {
            // NOTE: Intentionally hashes only TypeIds for now (shared components are not implemented).
            return ComputeStableHash32(TypeIds, seed);
        }

        internal static ulong ComputeStableHash64(ReadOnlySpan<int> sortedTypeIds)
        {
            // FNV-1a 64-bit (stable across platforms).
            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong hash = OffsetBasis;

            hash ^= (ulong)sortedTypeIds.Length;
            hash *= Prime;

            for (int i = 0; i < sortedTypeIds.Length; i++)
            {
                unchecked
                {
                    uint v = (uint)sortedTypeIds[i];
                    hash ^= (byte)(v & 0xFF);
                    hash *= Prime;
                    hash ^= (byte)((v >> 8) & 0xFF);
                    hash *= Prime;
                    hash ^= (byte)((v >> 16) & 0xFF);
                    hash *= Prime;
                    hash ^= (byte)((v >> 24) & 0xFF);
                    hash *= Prime;
                }
            }

            return hash;
        }

        internal static uint ComputeStableHash32(ReadOnlySpan<int> sortedTypeIds, uint seed)
        {
            // FNV-1a 32-bit (stable across platforms).
            const uint Prime = 16777619u;

            uint hash = seed;

            hash ^= (uint)sortedTypeIds.Length;
            hash *= Prime;

            for (int i = 0; i < sortedTypeIds.Length; i++)
            {
                unchecked
                {
                    uint v = (uint)sortedTypeIds[i];
                    hash ^= (byte)(v & 0xFF);
                    hash *= Prime;
                    hash ^= (byte)((v >> 8) & 0xFF);
                    hash *= Prime;
                    hash ^= (byte)((v >> 16) & 0xFF);
                    hash *= Prime;
                    hash ^= (byte)((v >> 24) & 0xFF);
                    hash *= Prime;
                }
            }

            return hash;
        }

        /// <summary>
        /// Checks if the signature contains the specified component type.
        /// </summary>
        /// <param name="typeId">Component type ID.</param>
        /// <returns>True if present.</returns>
        public bool Contains(Components.ComponentTypeId typeId)
        {
            return Array.BinarySearch(TypeIds, typeId.Value) >= 0;
        }

        /// <summary>
        /// Creates a new signature with the added component type.
        /// </summary>
        /// <param name="typeId">Component type ID to add.</param>
        /// <returns>A new signature or the same instance if already present.</returns>
        public ArchetypeSignature WithAdded(Components.ComponentTypeId typeId)
        {
            if (Contains(typeId)) return this;

            int[] tmp = new int[TypeIds.Length + 1];
            Array.Copy(TypeIds, tmp, TypeIds.Length);
            tmp[tmp.Length - 1] = typeId.Value;
            Array.Sort(tmp);
            return new ArchetypeSignature(tmp);
        }

        /// <summary>
        /// Creates a new signature with the removed component type.
        /// </summary>
        /// <param name="typeId">Component type ID to remove.</param>
        /// <returns>A new signature or the same instance if not present.</returns>
        public ArchetypeSignature WithRemoved(Components.ComponentTypeId typeId)
        {
            int idx = Array.BinarySearch(TypeIds, typeId.Value);
            if (idx < 0) return this;

            if (TypeIds.Length == 1) return new ArchetypeSignature(ReadOnlySpan<int>.Empty);

            int[] tmp = new int[TypeIds.Length - 1];
            if (idx > 0) Array.Copy(TypeIds, 0, tmp, 0, idx);

            if (idx < TypeIds.Length - 1) Array.Copy(TypeIds, idx + 1, tmp, idx, TypeIds.Length - idx - 1);

            return new ArchetypeSignature(tmp);
        }

        /// <summary>
        /// Compares two signatures lexicographically.
        /// </summary>
        /// <param name="a">First signature.</param>
        /// <param name="b">Second signature.</param>
        /// <returns>Comparison result.</returns>
        public static int CompareLexicographic(ArchetypeSignature a, ArchetypeSignature b)
        {
            int min = a.TypeIds.Length < b.TypeIds.Length ? a.TypeIds.Length : b.TypeIds.Length;

            for (int i = 0; i < min; i++)
            {
                int cmp = a.TypeIds[i].CompareTo(b.TypeIds[i]);
                if (cmp != 0) return cmp;
            }

            return a.TypeIds.Length.CompareTo(b.TypeIds.Length);
        }

        public bool Equals(ArchetypeSignature other)
        {
            if (ReferenceEquals(this, other)) return true;

            if (other is null || TypeIds.Length != other.TypeIds.Length) return false;

            for (int i = 0; i < TypeIds.Length; i++)
            {
                if (TypeIds[i] != other.TypeIds[i]) return false;
            }

            // Shared components reserved; both are expected empty for now.
            if (SharedTypeIds.Length != other.SharedTypeIds.Length) return false;
            for (int i = 0; i < SharedTypeIds.Length; i++)
            {
                if (SharedTypeIds[i] != other.SharedTypeIds[i]) return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is ArchetypeSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + TypeIds.Length;

                if (TypeIds.Length > 0)
                {
                    h = h * 31 + TypeIds[0];
                    h = h * 31 + TypeIds[TypeIds.Length - 1];
                }

                // Shared components reserved.
                h = h * 31 + SharedTypeIds.Length;

                return h;
            }
        }
    }
}