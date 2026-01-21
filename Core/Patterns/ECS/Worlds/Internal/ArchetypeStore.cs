using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Owns archetype registry, stable enumeration, and archetype creation paths.
    /// </summary>
    internal sealed class ArchetypeStore
    {
        private readonly WorldState _state;

        public ArchetypeStore(WorldState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void InitializeEmptyArchetype()
        {
            ArchetypeSignature emptySig = new(ReadOnlySpan<int>.Empty);
            _state.EmptyArchetype = GetOrCreateArchetype(emptySig);
        }

        public Archetype GetEmptyArchetype()
        {
            return _state.EmptyArchetype;
        }

        public Archetype GetOrCreateArchetype(ArchetypeSignature signature)
        {
            ulong hash = signature.ComputeStableHash64();

            if (_state.ArchetypesByHash.TryGetValue(hash, out List<Archetype> existing))
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i].Signature.Equals(signature)) return existing[i];
                }

                ArchetypeId id = CreateUniqueArchetypeIdWithinBucket(signature, hash, existing);

                Archetype created = new(signature, id);
                existing.Add(created);

                existing.Sort(CompareArchetypeByIdThenSignature);

                unchecked
                {
                    _state.ArchetypeVersion++;
                }

                _state.IncrementStructuralVersion();

                return created;
            }

            uint disambiguator = signature.ComputeStableHash32();
            ArchetypeId freshId = new(hash, disambiguator);

            Archetype fresh = new(signature, freshId);
            _state.ArchetypesByHash.Add(hash, new List<Archetype>(1) { fresh });

            unchecked
            {
                _state.ArchetypeVersion++;
            }

            _state.IncrementStructuralVersion();

            return fresh;
        }

        internal Archetype GetOrCreateArchetypeWithAdded(Archetype srcArchetype, ComponentTypeId addTypeId)
        {
            ReadOnlySpan<int> src = srcArchetype.Signature.TypeIds;
            int add = addTypeId.Value;

            if (Array.BinarySearch(srcArchetype.Signature.TypeIds, add) >= 0)
                return srcArchetype;

            int required = src.Length + 1;
            int[] tmp = EcsArrayPool<int>.Rent(required);

            try
            {
                int write = 0;
                bool inserted = false;

                for (int i = 0; i < src.Length; i++)
                {
                    int v = src[i];

                    if (!inserted && add < v)
                    {
                        tmp[write++] = add;
                        inserted = true;
                    }

                    tmp[write++] = v;
                }

                if (!inserted)
                    tmp[write++] = add;

                return GetOrCreateArchetypeFromSortedTypes(tmp, write);
            }
            finally
            {
                EcsArrayPool<int>.Return(tmp, false);
            }
        }

        internal Archetype GetOrCreateArchetypeWithRemoved(Archetype srcArchetype, ComponentTypeId removeTypeId)
        {
            ReadOnlySpan<int> src = srcArchetype.Signature.TypeIds;
            int rem = removeTypeId.Value;

            int idx = Array.BinarySearch(srcArchetype.Signature.TypeIds, rem);
            if (idx < 0)
                return srcArchetype;

            int required = src.Length - 1;
            if (required <= 0)
                return _state.EmptyArchetype;

            int[] tmp = EcsArrayPool<int>.Rent(required);

            try
            {
                int write = 0;
                for (int i = 0; i < src.Length; i++)
                {
                    int v = src[i];
                    if (v == rem) continue;
                    tmp[write++] = v;
                }

                return GetOrCreateArchetypeFromSortedTypes(tmp, write);
            }
            finally
            {
                EcsArrayPool<int>.Return(tmp, false);
            }
        }

        public Archetype GetArchetypeById(ArchetypeId id)
        {
            if (_state.ArchetypesByHash.TryGetValue(id.Value, out List<Archetype> list))
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].ArchetypeId == id) return list[i];
                }

            throw new InvalidOperationException($"Archetype {id} was not found.");
        }

        public ArchetypeEnumerable EnumerateArchetypesStable()
        {
            return new ArchetypeEnumerable(_state.ArchetypesByHash);
        }

        private Archetype GetOrCreateArchetypeFromSortedTypes(int[] sortedTypesBuffer, int count)
        {
            ReadOnlySpan<int> types = new(sortedTypesBuffer, 0, count);

            ulong hash64 = ArchetypeSignature.ComputeStableHash64(types);

            if (_state.ArchetypesByHash.TryGetValue(hash64, out List<Archetype> existing))
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    Archetype a = existing[i];
                    if (SignatureEquals(a.Signature.TypeIds, types))
                        return a;
                }

                ArchetypeSignature signature = CreateSignatureOwned(types);
                ArchetypeId id = CreateUniqueArchetypeIdWithinBucket(types, hash64, existing);

                Archetype created = new(signature, id);
                existing.Add(created);
                existing.Sort(CompareArchetypeByIdThenSignature);

                unchecked
                {
                    _state.ArchetypeVersion++;
                }

                _state.IncrementStructuralVersion();

                return created;
            }
            else
            {
                ArchetypeSignature signature = CreateSignatureOwned(types);

                uint disambiguator = ArchetypeSignature.ComputeStableHash32(types, 2166136261u);
                ArchetypeId freshId = new(hash64, disambiguator);

                Archetype fresh = new(signature, freshId);
                _state.ArchetypesByHash.Add(hash64, new List<Archetype>(1) { fresh });

                unchecked
                {
                    _state.ArchetypeVersion++;
                }

                _state.IncrementStructuralVersion();

                return fresh;
            }
        }

        private static bool SignatureEquals(int[] a, ReadOnlySpan<int> b)
        {
            if (a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        private static ArchetypeSignature CreateSignatureOwned(ReadOnlySpan<int> sortedTypes)
        {
            int[] owned = new int[sortedTypes.Length];
            sortedTypes.CopyTo(owned);
            return new ArchetypeSignature(owned, true);
        }

        private static ArchetypeId CreateUniqueArchetypeIdWithinBucket(
            ReadOnlySpan<int> signatureSortedTypeIds,
            ulong bucketHash,
            List<Archetype> bucket)
        {
            uint dis = ArchetypeSignature.ComputeStableHash32(signatureSortedTypeIds, 2166136261u);

            if (!BucketContainsId(bucket, bucketHash, dis))
                return new ArchetypeId(bucketHash, dis);

            for (int attempt = 1; attempt <= 32; attempt++)
            {
                uint seed = unchecked(2166136261u ^ (uint)(attempt * 0x9E3779B9u));
                dis = ArchetypeSignature.ComputeStableHash32(signatureSortedTypeIds, seed);

                if (!BucketContainsId(bucket, bucketHash, dis))
                    return new ArchetypeId(bucketHash, dis);
            }

            dis = ArchetypeSignature.ComputeStableHash32(signatureSortedTypeIds, 2166136261u);
            uint probe = dis;
            for (uint step = 1; step != 0; step++)
            {
                probe = unchecked(dis + step);
                if (!BucketContainsId(bucket, bucketHash, probe))
                    return new ArchetypeId(bucketHash, probe);
            }

            throw new InvalidOperationException("Failed to create a unique ArchetypeId within the hash bucket.");
        }

        private static ArchetypeId CreateUniqueArchetypeIdWithinBucket(
            ArchetypeSignature signature,
            ulong bucketHash,
            List<Archetype> bucket)
        {
            uint dis = signature.ComputeStableHash32();

            if (!BucketContainsId(bucket, bucketHash, dis))
                return new ArchetypeId(bucketHash, dis);

            for (int attempt = 1; attempt <= 32; attempt++)
            {
                uint seed = unchecked(2166136261u ^ (uint)(attempt * 0x9E3779B9u));
                dis = signature.ComputeStableHash32(seed);

                if (!BucketContainsId(bucket, bucketHash, dis))
                    return new ArchetypeId(bucketHash, dis);
            }

            dis = signature.ComputeStableHash32();
            uint probe = dis;
            for (uint step = 1; step != 0; step++)
            {
                probe = unchecked(dis + step);
                if (!BucketContainsId(bucket, bucketHash, probe))
                    return new ArchetypeId(bucketHash, probe);
            }

            throw new InvalidOperationException("Failed to create a unique ArchetypeId within the hash bucket.");
        }

        private static bool BucketContainsId(List<Archetype> bucket, ulong bucketHash, uint disambiguator)
        {
            ArchetypeId candidate = new(bucketHash, disambiguator);

            for (int i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].ArchetypeId == candidate) return true;
            }

            return false;
        }

        private static int CompareArchetypeByIdThenSignature(Archetype a, Archetype b)
        {
            int cmp = a.ArchetypeId.Value.CompareTo(b.ArchetypeId.Value);
            if (cmp != 0) return cmp;

            cmp = a.ArchetypeId.Disambiguator.CompareTo(b.ArchetypeId.Disambiguator);
            if (cmp != 0) return cmp;

            return ArchetypeSignature.CompareLexicographic(a.Signature, b.Signature);
        }
    }
}