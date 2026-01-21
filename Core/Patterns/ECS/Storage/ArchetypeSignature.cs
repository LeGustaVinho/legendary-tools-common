using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    public sealed class ArchetypeSignature : IEquatable<ArchetypeSignature>
    {
        /// <summary>
        /// Non-shared (chunk-stored) component type ids, sorted ascending.
        /// </summary>
        public readonly int[] TypeIds;

        public readonly ulong[] MaskWords;

        /// <summary>
        /// Reserved for future "shared components" support.
        /// Keep empty for now; do not use in hashing/lookup until shared components are implemented.
        /// </summary>
        public readonly int[] SharedTypeIds;

        public ArchetypeSignature(ReadOnlySpan<int> typeIds)
        {
            if (typeIds.Length == 0)
            {
                TypeIds = Array.Empty<int>();
                MaskWords = Array.Empty<ulong>();
                SharedTypeIds = Array.Empty<int>();
                return;
            }

            TypeIds = typeIds.ToArray();
            Array.Sort(TypeIds);

            MaskWords = BuildMaskFromSorted(TypeIds);

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
                MaskWords = Array.Empty<ulong>();
                SharedTypeIds = Array.Empty<int>();
                return;
            }

            TypeIds = typeIdsSortedOwned;

            if (!alreadySorted)
                Array.Sort(TypeIds);

            MaskWords = BuildMaskFromSorted(TypeIds);

            // Shared components not implemented yet.
            SharedTypeIds = Array.Empty<int>();
        }

        public ulong ComputeStableHash64()
        {
            // NOTE: Intentionally hashes only TypeIds for now (shared components are not implemented).
            return ComputeStableHash64(TypeIds);
        }

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

        public bool Contains(Components.ComponentTypeId typeId)
        {
            return Array.BinarySearch(TypeIds, typeId.Value) >= 0;
        }

        public ArchetypeSignature WithAdded(Components.ComponentTypeId typeId)
        {
            if (Contains(typeId)) return this;

            int[] tmp = new int[TypeIds.Length + 1];
            Array.Copy(TypeIds, tmp, TypeIds.Length);
            tmp[tmp.Length - 1] = typeId.Value;
            Array.Sort(tmp);
            return new ArchetypeSignature(tmp);
        }

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

        private static ulong[] BuildMaskFromSorted(ReadOnlySpan<int> sortedTypeIds)
        {
            if (sortedTypeIds.Length == 0) return Array.Empty<ulong>();

            int maxId = sortedTypeIds[sortedTypeIds.Length - 1];
            if (maxId < 0) return Array.Empty<ulong>();

            int words = (maxId >> 6) + 1;
            ulong[] mask = new ulong[words];

            for (int i = 0; i < sortedTypeIds.Length; i++)
            {
                int id = sortedTypeIds[i];
                if (id < 0) continue;

                int w = id >> 6;
                int b = id & 63;
                mask[w] |= 1UL << b;
            }

            return mask;
        }
    }
}