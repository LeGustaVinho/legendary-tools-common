using System;

using LegendaryTools.Common.Core.Patterns.ECS.Components;

namespace LegendaryTools.Common.Core.Patterns.ECS.Storage
{
    /// <summary>
    /// Ordered set of component type ids for an archetype signature.
    /// </summary>
    public sealed class ArchetypeSignature : IEquatable<ArchetypeSignature>
    {
        /// <summary>
        /// Gets the ordered component ids (ascending by id value).
        /// </summary>
        public readonly int[] TypeIds;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArchetypeSignature"/> class.
        /// The input array is copied and sorted.
        /// </summary>
        /// <param name="typeIds">Component type ids.</param>
        public ArchetypeSignature(ReadOnlySpan<int> typeIds)
        {
            if (typeIds.Length == 0)
            {
                TypeIds = Array.Empty<int>();
                return;
            }

            TypeIds = typeIds.ToArray();
            Array.Sort(TypeIds);
        }

        /// <summary>
        /// Computes a deterministic 64-bit hash for the signature.
        /// </summary>
        /// <returns>Stable 64-bit hash.</returns>
        public ulong ComputeStableHash64()
        {
            // FNV-1a 64-bit.
            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong hash = OffsetBasis;

            // Include length to distinguish different sequences that may collide otherwise.
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
        /// Checks whether the signature contains the given component type id.
        /// </summary>
        /// <param name="typeId">Component type id.</param>
        /// <returns>True if present.</returns>
        public bool Contains(ComponentTypeId typeId)
        {
            return Array.BinarySearch(TypeIds, typeId.Value) >= 0;
        }

        /// <summary>
        /// Creates a new signature with a component added (keeps ordering).
        /// </summary>
        /// <param name="typeId">Component type id to add.</param>
        /// <returns>New signature.</returns>
        public ArchetypeSignature WithAdded(ComponentTypeId typeId)
        {
            if (Contains(typeId))
            {
                return this;
            }

            int[] tmp = new int[TypeIds.Length + 1];
            Array.Copy(TypeIds, tmp, TypeIds.Length);
            tmp[tmp.Length - 1] = typeId.Value;
            Array.Sort(tmp);
            return new ArchetypeSignature(tmp);
        }

        /// <summary>
        /// Creates a new signature with a component removed (keeps ordering).
        /// </summary>
        /// <param name="typeId">Component type id to remove.</param>
        /// <returns>New signature.</returns>
        public ArchetypeSignature WithRemoved(ComponentTypeId typeId)
        {
            int idx = Array.BinarySearch(TypeIds, typeId.Value);
            if (idx < 0)
            {
                return this;
            }

            if (TypeIds.Length == 1)
            {
                return new ArchetypeSignature(ReadOnlySpan<int>.Empty);
            }

            int[] tmp = new int[TypeIds.Length - 1];
            if (idx > 0)
            {
                Array.Copy(TypeIds, 0, tmp, 0, idx);
            }

            if (idx < TypeIds.Length - 1)
            {
                Array.Copy(TypeIds, idx + 1, tmp, idx, (TypeIds.Length - idx - 1));
            }

            // Already ordered.
            return new ArchetypeSignature(tmp);
        }

        /// <summary>
        /// Lexicographic compare between two signatures.
        /// </summary>
        /// <param name="a">Signature A.</param>
        /// <param name="b">Signature B.</param>
        /// <returns>Comparison result.</returns>
        public static int CompareLexicographic(ArchetypeSignature a, ArchetypeSignature b)
        {
            int min = a.TypeIds.Length < b.TypeIds.Length ? a.TypeIds.Length : b.TypeIds.Length;

            for (int i = 0; i < min; i++)
            {
                int cmp = a.TypeIds[i].CompareTo(b.TypeIds[i]);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return a.TypeIds.Length.CompareTo(b.TypeIds.Length);
        }

        /// <inheritdoc/>
        public bool Equals(ArchetypeSignature other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null || TypeIds.Length != other.TypeIds.Length)
            {
                return false;
            }

            for (int i = 0; i < TypeIds.Length; i++)
            {
                if (TypeIds[i] != other.TypeIds[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ArchetypeSignature other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) + TypeIds.Length;

                // Cheap: sample first/last elements.
                if (TypeIds.Length > 0)
                {
                    h = (h * 31) + TypeIds[0];
                    h = (h * 31) + TypeIds[TypeIds.Length - 1];
                }

                return h;
            }
        }
    }
}
