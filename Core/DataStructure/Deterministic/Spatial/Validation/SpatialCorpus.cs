using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    public unsafe interface ISpatialCorpusOwner<TBounds> where TBounds : unmanaged
    {
        DeterministicStatus Apply(in SpatialMutation<int, int, TBounds> mutation);
        DeterministicStatus Rebuild();
        DeterministicStatus Capture();
        DeterministicStatus Restore();
        void Release();
        DeterministicStatus Query(in SpatialQuery query, SpatialQueryScratch scratch, SpatialHit<int>* hits, out int written);
        DeterministicStatus Write(ref DeterministicWriter writer);
    }
    public unsafe struct SpatialManagedCorpusOwner<TBounds, TAccess, TCodec> : ISpatialCorpusOwner<TBounds>
        where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
    {
        public DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy> Index;
        private ManagedSpatialSnapshot<int, int, TBounds, TAccess, Int32Policy> snapshot;
        public DeterministicStatus Apply(in SpatialMutation<int, int, TBounds> mutation) => Index.TryApply(mutation);
        public DeterministicStatus Rebuild() => Index.TryRebuild();
        public DeterministicStatus Capture() => Index.TryCapture(out snapshot);
        public DeterministicStatus Restore() => Index.TryRestore(snapshot);
        public void Release() => snapshot.Dispose();
        public DeterministicStatus Query(in SpatialQuery query, SpatialQueryScratch scratch, SpatialHit<int>* hits, out int written) => Index.TryQuery(query, scratch, hits, SpatialCorpus.Records, out written);
        public DeterministicStatus Write(ref DeterministicWriter writer) => Index.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy), default(TCodec));
    }
    /// <summary>Reproducible public-API traces, including exact hit order, metric statuses, full canonical bytes and replay after restore.</summary>
    public static unsafe class SpatialCorpus
    {
        public const int Version = 1, Records = 64, Steps = 48, Checkpoint = 16, WordsPerStep = 2048, Capacity = Steps * WordsPerStep, Cases = 36;
        public static ulong ExpectedChecksum(int dimensions) => FixedBoundsRuntime.Settings.Representation == FixedBoundsRepresentation.MinMax ?
            dimensions == 2 ? 16508232988413857770UL : 16896690778666421457UL : dimensions == 2 ? 17258734144282011790UL : 13757347639453172141UL;
        private struct Trace
        {
            internal ulong* Words;
            internal int Count, Limit;
            internal bool Add(ulong word) { if (Count >= Limit) return false; Words[Count++] = word; return true; }
        }
        public static SpatialIndexOptions Options(int scenario)
        {
            int strategy = scenario % 3, rollback = scenario / 3 % 2;
            return new SpatialIndexOptions(new BufferOptions(Records, snapshotSlots: 2, pageBytes: 128), (SpatialIndexStrategy)strategy,
                rollback: (SpatialRollbackPolicy)rollback, cellSize: Fixed64.FromRaw(16), fatMargin: Fixed64.FromRaw(2), maxCellsPerObject: 64, maxCellsPerQuery: 64);
        }
        private static TBounds Box<TBounds, TAccess>(int key, int tick, int seed) where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        {
            ulong h = DeterministicHash.Mix((ulong)(uint)(key + tick * 127 + seed * 673));
            long x = (long)(h % 256) - 128, y = (long)((h >> 21) % 128) - 64, z = (long)((h >> 42) % 128) - 64;
            long width = key % 17;
            TBounds result = default;
            if (key == 0 && tick == 0)
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
        private static DeterministicStatus Populate<TBounds, TAccess, TOwner>(ref TOwner owner, int tick, int seed)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TOwner : struct, ISpatialCorpusOwner<TBounds>
        {
            for (int i = 0; i < Records; i++)
            {
                int key = i * 7 % Records;
                var s = owner.Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Add, Key = key,
                    Bounds = Box<TBounds, TAccess>(key, tick, seed), Data = key * 13 + tick, Metadata = new SpatialMetadata((uint)(key % 4), (uint)(key % 8)) });
                if (!s.IsSuccess()) return s;
            }
            return owner.Rebuild();
        }
        private static DeterministicStatus Mutate<TBounds, TAccess, TOwner>(ref TOwner owner, int tick, int seed)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TOwner : struct, ISpatialCorpusOwner<TBounds>
        {
            int key = (tick * 17 + seed * 3) % Records;
            if (tick == 24)
            { var clear = owner.Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Clear }); return clear.IsSuccess() ? Populate<TBounds, TAccess, TOwner>(ref owner, tick, seed) : clear; }
            SpatialMutationKind kind = tick % 5 == 0 ? SpatialMutationKind.SetData : tick % 5 == 1 ? SpatialMutationKind.SetMetadata : SpatialMutationKind.UpdateBounds;
            if (tick % 11 == 0)
            {
                var removed = owner.Apply(new SpatialMutation<int, int, TBounds> { Kind = SpatialMutationKind.Remove, Key = key }); if (!removed.IsSuccess()) return removed;
                kind = SpatialMutationKind.Add;
            }
            var status = owner.Apply(new SpatialMutation<int, int, TBounds> { Kind = kind, Key = key,
                Bounds = Box<TBounds, TAccess>(key, tick, seed), Data = tick * 7 - key, Metadata = new SpatialMetadata((uint)(key % 4), (uint)(tick % 8)) });
            return status.IsSuccess() && tick % 7 == 0 ? owner.Rebuild() : status;
        }
        private static DeterministicStatus Observe<TBounds, TAccess, TOwner>(ref TOwner owner, int tick, ref Trace trace, SpatialQueryScratch scratch,
            SpatialHit<int>* hits, byte* packet) where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TOwner : struct, ISpatialCorpusOwner<TBounds>
        {
            int dimensions = default(TAccess).Dimensions;
            var point = FixedVector3.FromRaw(tick * 3 - 70, tick % 17 - 8, dimensions == 2 ? 0 : tick % 23 - 11);
            for (int q = 0; q < 6; q++)
            {
                SpatialQuery query;
                if (q == 0) query = dimensions == 2 ? SpatialQuery.ForNearest((FixedVector2)point, 9) : SpatialQuery.ForNearest(point, 9);
                else if (q == 1) query = dimensions == 2 ? SpatialQuery.ForCircle((FixedVector2)point, Fixed64.FromRaw(64)) : SpatialQuery.ForSphere(point, Fixed64.FromRaw(64));
                else if (q == 2) query = dimensions == 2 ? SpatialQuery.ForRay(new FixedRay2((FixedVector2)point, FixedVector2.FromRaw(1, 0))) : SpatialQuery.ForRay(new FixedRay3(point, FixedVector3.FromRaw(1, 0, 0)));
                else if (q == 3) query = dimensions == 2 ? SpatialQuery.ForSegment(FixedVector2.FromRaw(Fixed64.MinValue.RawValue, 0), FixedVector2.FromRaw(Fixed64.MaxValue.RawValue, 0)) :
                    SpatialQuery.ForSegment(FixedVector3.FromRaw(Fixed64.MinValue.RawValue, 0, 0), FixedVector3.FromRaw(Fixed64.MaxValue.RawValue, 0, 0));
                else if (q == 4) query = SpatialQuery.FromEnvelope(new SpatialRawBounds { MinX = -32, MinY = -32, MinZ = -32, MaxX = 32, MaxY = 32, MaxZ = 32 }, dimensions);
                else query = dimensions == 2 ? SpatialQuery.ForPoint((FixedVector2)point) : SpatialQuery.ForPoint(point);
                var status = owner.Query(query, scratch, hits, out int n); if (!trace.Add((ulong)status) || !trace.Add((uint)n)) return DeterministicStatus.CapacityExceeded;
                if (!status.IsSuccess()) return status;
                for (int i = 0; i < n; i++) if (!trace.Add((uint)hits[i].Key) || !trace.Add(unchecked((ulong)hits[i].Metric.RawValue)) || !trace.Add((uint)hits[i].MetricStatus)) return DeterministicStatus.CapacityExceeded;
            }
            var writer = new DeterministicWriter(packet, 8192); var written = owner.Write(ref writer); if (!written.IsSuccess()) return written;
            if (!trace.Add((uint)writer.Position)) return DeterministicStatus.CapacityExceeded;
            for (int i = 0; i < writer.Position; i += 8)
            {
                ulong word = 0; int n = writer.Position - i < 8 ? writer.Position - i : 8;
                for (int j = 0; j < n; j++) word |= (ulong)packet[i + j] << (j * 8);
                if (!trace.Add(word)) return DeterministicStatus.CapacityExceeded;
            }
            return DeterministicStatus.None;
        }
        internal static DeterministicStatus ObserveState<TBounds, TAccess, TOwner>(ref TOwner owner, int tick, ulong* output, int capacity,
            SpatialQueryScratch scratch, SpatialHit<int>* hits, byte* packet, out int words)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TOwner : struct, ISpatialCorpusOwner<TBounds>
        {
            var trace = new Trace { Words = output, Limit = capacity };
            var status = Observe<TBounds, TAccess, TOwner>(ref owner, tick, ref trace, scratch, hits, packet);
            words = trace.Count; return status;
        }
        public static DeterministicStatus Evaluate<TBounds, TAccess, TOwner>(ref TOwner owner, int seed, ulong* output, int capacity,
            out int words, out int replayed) where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TOwner : struct, ISpatialCorpusOwner<TBounds>
        {
            words = replayed = 0;
            if (capacity < Capacity || output == null) return DeterministicStatus.BufferTooSmall;
            SpatialQueryScratch.TryMeasure<int>(Records, Records * 2, out int bytes);
            byte* work = stackalloc byte[bytes]; byte* packet = stackalloc byte[8192]; SpatialHit<int>* hits = stackalloc SpatialHit<int>[Records];
            int* starts = stackalloc int[Steps + 1]; ulong* repeated = stackalloc ulong[WordsPerStep];
            var trace = new Trace { Words = output, Limit = capacity }; var scratch = new SpatialQueryScratch(work, bytes);
            var status = Populate<TBounds, TAccess, TOwner>(ref owner, 0, seed); if (!status.IsSuccess()) return status;
            for (int tick = 0; tick < Steps; tick++)
            {
                status = Mutate<TBounds, TAccess, TOwner>(ref owner, tick, seed); if (!status.IsSuccess()) { owner.Release(); return status; }
                starts[tick] = trace.Count;
                status = Observe<TBounds, TAccess, TOwner>(ref owner, tick, ref trace, scratch, hits, packet); if (!status.IsSuccess()) { owner.Release(); return status; }
                if (tick == Checkpoint) { status = owner.Capture(); if (!status.IsSuccess()) return status; }
            }
            starts[Steps] = trace.Count; status = owner.Restore();
            for (int tick = Checkpoint + 1; tick < Steps && status.IsSuccess(); tick++)
            {
                status = Mutate<TBounds, TAccess, TOwner>(ref owner, tick, seed); if (!status.IsSuccess()) break;
                var replay = new Trace { Words = repeated, Limit = WordsPerStep };
                status = Observe<TBounds, TAccess, TOwner>(ref owner, tick, ref replay, scratch, hits, packet); if (!status.IsSuccess()) break;
                if (replay.Count != starts[tick + 1] - starts[tick]) { status = DeterministicStatus.ProtocolConflict; break; }
                for (int i = 0; i < replay.Count; i++) if (repeated[i] != output[starts[tick] + i]) { status = DeterministicStatus.ProtocolConflict; break; }
                if (status.IsSuccess()) replayed++;
            }
            owner.Release(); words = trace.Count; return status;
        }
        public static ulong Hash(ulong* trace, int words)
        { ulong hash = 1; for (int i = 0; i < words; i++) hash = DeterministicHash.Mix(hash ^ trace[i]); return hash; }
    }
}
