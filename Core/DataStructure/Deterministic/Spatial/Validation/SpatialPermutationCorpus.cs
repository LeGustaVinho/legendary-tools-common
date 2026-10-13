using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    /// <summary>Identical logical changes in three update orders and rebuild cadences, including replay in a different order.</summary>
    public static unsafe class SpatialPermutationCorpus
    {
        public const int Version = 1, Steps = 24, Checkpoint = 7, Capacity = Steps * SpatialCorpus.WordsPerStep;
        public static ulong ExpectedChecksum(int dimensions) => FixedBoundsRuntime.Settings.Representation == FixedBoundsRepresentation.MinMax ?
            dimensions == 2 ? 17542579428927829911UL : 1302018497387400510UL : dimensions == 2 ? 3033406297091522437UL : 10608046232426256322UL;
        private static TBounds Box<TBounds, TAccess>(int key, int tick, int seed)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        {
            uint h = unchecked((uint)(key * 941 + tick * 313 + seed * 113) * 1664525u + 1013904223u);
            long x = h % 129 - 64L, y = (h >> 9) % 65 - 32L, z = (h >> 18) % 65 - 32L, width = key % 13;
            if (key == 1 || key == 2) { x = y = z = key == 1 ? Fixed64.MaxValue.RawValue : Fixed64.MinValue.RawValue; width = 0; }
            TBounds result = default;
            if (key == 0 && tick % 4 == 0)
            {
                if (default(TAccess).Dimensions == 2)
                { FixedBounds2.TryFromStorage(new FixedVector2(Fixed64.NegativeZero, Fixed64.Zero), FixedVector2.zero, out var box, out _); *(FixedBounds2*)&result = box; }
                else
                { FixedBounds3.TryFromStorage(new FixedVector3(Fixed64.NegativeZero, Fixed64.Zero, Fixed64.NegativeZero), FixedVector3.zero, out var box, out _); *(FixedBounds3*)&result = box; }
            }
            else if (default(TAccess).Dimensions == 2) *(FixedBounds2*)&result = FixedBounds2.FromMinMax(FixedVector2.FromRaw(x, y), FixedVector2.FromRaw(x + width, y + width));
            else *(FixedBounds3*)&result = FixedBounds3.FromMinMax(FixedVector3.FromRaw(x, y, z), FixedVector3.FromRaw(x + width, y + width, z + width));
            return result;
        }
        private static DeterministicStatus Mutate<TBounds, TAccess, TOwner>(ref TOwner owner, int tick, int seed, int permutation)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TOwner : struct, ISpatialCorpusOwner<TBounds>
        {
            for (int ordinal = 0; ordinal < SpatialCorpus.Records; ordinal++)
            {
                int key = permutation == 0 ? ordinal : permutation == 1 ? SpatialCorpus.Records - 1 - ordinal : (ordinal * 17 + seed * 11) % SpatialCorpus.Records;
                var command = new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.UpdateBounds, Key = key, Bounds = Box<TBounds, TAccess>(key, tick, seed) };
                var status = owner.Apply(command); if (!status.IsSuccess()) return status;
                command.Kind = SpatialMutationKind.SetData; command.Data = tick * 101 - key * 7 + seed;
                status = owner.Apply(command); if (!status.IsSuccess()) return status;
                command.Kind = SpatialMutationKind.SetMetadata; command.Metadata = new SpatialMetadata((uint)(key % 4), (uint)((key + tick) % 8));
                status = owner.Apply(command); if (!status.IsSuccess()) return status;
            }
            return (tick + permutation) % 3 == 0 ? owner.Rebuild() : DeterministicStatus.None;
        }
        public static DeterministicStatus Evaluate<TBounds, TAccess, TOwner>(ref TOwner owner, int seed, int permutation, ulong* output, int capacity,
            out int words, out int replayed)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TOwner : struct, ISpatialCorpusOwner<TBounds>
        {
            words = replayed = 0;
            if (capacity < Capacity || output == null) return DeterministicStatus.BufferTooSmall;
            if ((uint)permutation >= 3) return DeterministicStatus.OutOfRange;
            SpatialQueryScratch.TryMeasure<int>(SpatialCorpus.Records, SpatialCorpus.Records * 2, out int bytes);
            byte* work = stackalloc byte[bytes]; byte* packet = stackalloc byte[8192]; SpatialHit<int>* hits = stackalloc SpatialHit<int>[SpatialCorpus.Records];
            int* starts = stackalloc int[Steps + 1]; ulong* repeated = stackalloc ulong[SpatialCorpus.WordsPerStep];
            var scratch = new SpatialQueryScratch(work, bytes); bool captured = false;
            for (int ordinal = 0; ordinal < SpatialCorpus.Records; ordinal++)
            {
                int key = ordinal * 7 % SpatialCorpus.Records;
                var add = owner.Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Add, Key = key, Bounds = Box<TBounds, TAccess>(key, 0, seed), Data = key });
                if (!add.IsSuccess()) return add;
            }
            var status = DeterministicStatus.None;
            for (int tick = 0; tick < Steps; tick++)
            {
                status = Mutate<TBounds, TAccess, TOwner>(ref owner, tick, seed, permutation); if (!status.IsSuccess()) break;
                starts[tick] = words;
                status = SpatialCorpus.ObserveState<TBounds, TAccess, TOwner>(ref owner, tick, output + words, SpatialCorpus.WordsPerStep, scratch, hits, packet, out int added);
                if (!status.IsSuccess()) break; words += added;
                if (tick == Checkpoint) { status = owner.Capture(); if (!status.IsSuccess()) break; captured = true; }
            }
            if (!status.IsSuccess()) { if (captured) owner.Release(); return status; }
            starts[Steps] = words; status = owner.Restore();
            for (int tick = Checkpoint + 1; tick < Steps && status.IsSuccess(); tick++)
            {
                status = Mutate<TBounds, TAccess, TOwner>(ref owner, tick, seed, (permutation + 1) % 3); if (!status.IsSuccess()) break;
                status = SpatialCorpus.ObserveState<TBounds, TAccess, TOwner>(ref owner, tick, repeated, SpatialCorpus.WordsPerStep, scratch, hits, packet, out int count);
                if (!status.IsSuccess()) break;
                if (count != starts[tick + 1] - starts[tick]) { status = DeterministicStatus.ProtocolConflict; break; }
                for (int i = 0; i < count; i++) if (repeated[i] != output[starts[tick] + i]) { status = DeterministicStatus.ProtocolConflict; break; }
                if (status.IsSuccess()) replayed++;
            }
            if (captured) owner.Release(); return status;
        }
    }
}
