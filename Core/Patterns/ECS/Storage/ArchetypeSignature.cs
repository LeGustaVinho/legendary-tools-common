using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    public sealed class ArchetypeSignature : IEquatable<ArchetypeSignature>
    {
        public readonly int[] TypeIds;

        /// <summary>
        /// Bitset mask for fast matching.
        /// Bit i is set when ComponentTypeId.Value == i is present in this signature.
        /// </summary>
        public readonly ulong[] MaskWords;

        public ArchetypeSignature(ReadOnlySpan<int> typeIds)
        {
            if (typeIds.Length == 0)
            {
                TypeIds = Array.Empty<int>();
                MaskWords = Array.Empty<ulong>();
                return;
            }

            TypeIds = typeIds.ToArray();
            Array.Sort(TypeIds);

            MaskWords = BuildMask(TypeIds);
        }

        public ulong ComputeStableHash64()
        {
            // FNV-1a 64-bit
            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong hash = OffsetBasis;

            // Include length to distinguish prefixes.
            hash ^= (ulong)TypeIds.Length;
            hash *= Prime;

            for (int i = 0; i < TypeIds.Length; i++)
            {
                unchecked
                {
                    uint v = (uint)TypeIds[i];
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
        /// Computes a deterministic 32-bit hash for collision disambiguation.
        /// </summary>
        /// <param name="seed">
        /// Seed for the hash. Different seeds produce different deterministic outputs for the same signature.
        /// </param>
        public uint ComputeStableHash32(uint seed = 2166136261u)
        {
            // FNV-1a 32-bit (seeded)
            const uint Prime = 16777619u;

            uint hash = seed;

            // Include length.
            hash ^= (uint)TypeIds.Length;
            hash *= Prime;

            for (int i = 0; i < TypeIds.Length; i++)
            {
                unchecked
                {
                    uint v = (uint)TypeIds[i];
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

                return h;
            }
        }

        private static ulong[] BuildMask(int[] sortedTypeIds)
        {
            if (sortedTypeIds.Length == 0) return Array.Empty<ulong>();

            int maxId = sortedTypeIds[sortedTypeIds.Length - 1];
            if (maxId < 0) return Array.Empty<ulong>();

            int words = (maxId >> 6) + 1; // /64
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