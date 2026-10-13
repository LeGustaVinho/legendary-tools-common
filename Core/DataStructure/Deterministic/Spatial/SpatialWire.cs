namespace LegendaryTools.Deterministic
{
    /// <summary>Logical payload only. The outer ordered-map codec writes the external key once.</summary>
    public struct SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>
        : IDeterministicCodec<SpatialRecord<TKey, TData, TBounds>>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TDataCodec : unmanaged, IDeterministicCodec<TData>
        where TBoundsCodec : unmanaged, IDeterministicCodec<TBounds>
        where TBoundsAccess : unmanaged, ISpatialBoundsAccess<TBounds>
    {
        private TDataCodec dataCodec;
        private TBoundsCodec boundsCodec;
        private TBoundsAccess access;
        public SpatialRecordCodec(TDataCodec data, TBoundsCodec bounds, TBoundsAccess access = default)
        { dataCodec = data; boundsCodec = bounds; this.access = access; }
        public uint SchemaId => CanonicalFields.CombineSchema(0xd320, dataCodec.SchemaId, dataCodec.Version, boundsCodec.SchemaId, boundsCodec.Version);
        public ushort Version => 1;
        public int Measure(in SpatialRecord<TKey, TData, TBounds> record)
        { int a = dataCodec.Measure(record.Data), b = boundsCodec.Measure(record.Bounds); return a < 0 || b < 0 || (long)a + b + 8 > int.MaxValue ? -1 : a + b + 8; }
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in SpatialRecord<TKey, TData, TBounds> record)
        {
            if (!record.Metadata.IsValid || !access.TryRead(record.Bounds, out _).IsSuccess()) return DeterministicStatus.InvalidEncoding;
            int bytes = Measure(record); if (bytes < 0) return DeterministicStatus.InvalidConfiguration;
            var measure = DeterministicWriter.CreateMeasure(); var s = dataCodec.TryWrite(ref measure, record.Data); if (!s.IsSuccess()) return s;
            s = boundsCodec.TryWrite(ref measure, record.Bounds); if (!s.IsSuccess()) return s;
            if (measure.Position != bytes - 8) return DeterministicStatus.ProtocolConflict;
            if (!writer.Has(bytes)) return DeterministicStatus.BufferTooSmall;
            // Components were validated above. A measuring destination has no bytes to publish.
            if (writer.IsMeasuring) { writer.Position += bytes; return DeterministicStatus.None; }
            var trial = writer; s = dataCodec.TryWrite(ref trial, record.Data); if (!s.IsSuccess()) return s;
            s = boundsCodec.TryWrite(ref trial, record.Bounds); if (!s.IsSuccess()) return s;
            trial.TryUInt(record.Metadata.Layer, 4); trial.TryUInt(record.Metadata.Flags, 4); writer = trial; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRead(ref DeterministicReader reader, out SpatialRecord<TKey, TData, TBounds> record)
        {
            record = default; var trial = reader;
            var s = dataCodec.TryRead(ref trial, out TData data); if (!s.IsSuccess()) return s;
            s = boundsCodec.TryRead(ref trial, out TBounds bounds); if (!s.IsSuccess()) return s;
            if (!access.TryRead(bounds, out _).IsSuccess()) return DeterministicStatus.InvalidEncoding;
            s = trial.TryUInt(4, out ulong layer); if (!s.IsSuccess()) return s;
            s = trial.TryUInt(4, out ulong flags); if (!s.IsSuccess()) return s;
            if (layer >= 32) return DeterministicStatus.InvalidEncoding;
            record = new SpatialRecord<TKey, TData, TBounds>(default, bounds, data, new SpatialMetadata((uint)layer, (uint)flags));
            reader = trial; return DeterministicStatus.None;
        }
    }
    internal static unsafe class SpatialWire<TKey, TData, TBounds, TBoundsAccess, TPolicy, TKeyCodec, TDataCodec, TBoundsCodec, TEntries, TBuckets>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TBoundsAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
        where TKeyCodec : unmanaged, IDeterministicCodec<TKey>
        where TDataCodec : unmanaged, IDeterministicCodec<TData>
        where TBoundsCodec : unmanaged, IDeterministicCodec<TBounds>
        where TEntries : struct, IDeterministicImportBuffer<OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>>
        where TBuckets : struct, IDeterministicImportBuffer<int>
    {
        private const uint Magic = 0x3153504c; // LPS1. Followed by logical options fingerprint and the existing LDC1 map.
        private static DeterministicCollectionKind Kind(TBoundsAccess access) => access.Dimensions == 2 ? DeterministicCollectionKind.SpatialIndex2 : DeterministicCollectionKind.SpatialIndex3;
        private static SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess> ValueCodec(TDataCodec data, TBoundsCodec bounds, TBoundsAccess access)
            => new SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>(data, bounds, access);
        internal static DeterministicStatus Write(ref DeterministicWriter writer, ref TEntries entries, in OrderedMapState state,
            SpatialIndexOptions options, TBoundsAccess access, TPolicy policy, TKeyCodec keyCodec, TDataCodec dataCodec, TBoundsCodec boundsCodec)
        {
            if (options.Ownership != SpatialStateOwnership.Autonomous) return DeterministicStatus.InvalidConfiguration;
            var codec = new OrderedHashEntryCodec<TKey, SpatialRecord<TKey, TData, TBounds>, TKeyCodec,
                SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>>(keyCodec, ValueCodec(dataCodec, boundsCodec, access));
            var layout = new OrderedMapWireLayout<TKey, SpatialRecord<TKey, TData, TBounds>, TPolicy>(policy);
            if (writer.IsMeasuring)
            {
                var counting = writer;
                var counted = counting.TryUInt(Magic, 4); if (!counted.IsSuccess()) return counted;
                counted = counting.TryUInt(options.SemanticFingerprint, 8); if (!counted.IsSuccess()) return counted;
                counted = StagedBufferWire<OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>, OrderedMapState,
                    OrderedHashEntryCodec<TKey, SpatialRecord<TKey, TData, TBounds>, TKeyCodec, SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>>,
                    OrderedMapStateCodec, OrderedMapWireLayout<TKey, SpatialRecord<TKey, TData, TBounds>, TPolicy>, TEntries>.TryWrite(
                        ref counting, ref entries, state, Kind(access), codec, default, layout);
                if (counted.IsSuccess()) writer = counting;
                return counted;
            }
            var measure = DeterministicWriter.CreateMeasure();
            var s = StagedBufferWire<OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>, OrderedMapState,
                OrderedHashEntryCodec<TKey, SpatialRecord<TKey, TData, TBounds>, TKeyCodec, SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>>,
                OrderedMapStateCodec, OrderedMapWireLayout<TKey, SpatialRecord<TKey, TData, TBounds>, TPolicy>, TEntries>.TryWrite(
                    ref measure, ref entries, state, Kind(access), codec, default, layout);
            if (!s.IsSuccess()) return s; if (measure.Position > int.MaxValue - 12 || !writer.Has(measure.Position + 12)) return DeterministicStatus.BufferTooSmall;
            var trial = writer;
            int previousLimit = trial.PremeasuredEnd, expectedEnd = trial.Position + measure.Position + 12;
            // This exact body was just validated and sized. Bound the nested write to that
            // receipt so StagedBufferWire does not perform another full measurement pass.
            trial.PremeasuredEnd = expectedEnd;
            trial.TryUInt(Magic, 4); trial.TryUInt(options.SemanticFingerprint, 8);
            s = StagedBufferWire<OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>, OrderedMapState,
                OrderedHashEntryCodec<TKey, SpatialRecord<TKey, TData, TBounds>, TKeyCodec, SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>>,
                OrderedMapStateCodec, OrderedMapWireLayout<TKey, SpatialRecord<TKey, TData, TBounds>, TPolicy>, TEntries>.TryWrite(
                    ref trial, ref entries, state, Kind(access), codec, default, layout);
            if (s.IsSuccess() && trial.Position != expectedEnd) s = DeterministicStatus.ProtocolConflict;
            trial.PremeasuredEnd = previousLimit;
            if (s.IsSuccess()) writer = trial; return s;
        }
        internal static DeterministicStatus Stage(ref DeterministicReader reader, ref CanonicalScratch scratch, ref TEntries entries, ref TBuckets buckets,
            SpatialIndexOptions options, TBoundsAccess access, TPolicy policy, TKeyCodec keyCodec, TDataCodec dataCodec, TBoundsCodec boundsCodec,
            out CanonicalBufferStage<OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>, OrderedMapState> stage)
        {
            stage = default;
            if (options.Ownership != SpatialStateOwnership.Autonomous) return DeterministicStatus.InvalidConfiguration;
            var trial = reader; var work = scratch;
            var s = trial.TryUInt(4, out ulong magic); if (!s.IsSuccess()) return s;
            s = trial.TryUInt(8, out ulong fingerprint); if (!s.IsSuccess()) return s;
            if (magic != Magic || fingerprint != options.SemanticFingerprint) return DeterministicStatus.ProfileMismatch;
            var codec = new OrderedHashEntryCodec<TKey, SpatialRecord<TKey, TData, TBounds>, TKeyCodec,
                SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>>(keyCodec, ValueCodec(dataCodec, boundsCodec, access));
            var layout = new OrderedMapWireLayout<TKey, SpatialRecord<TKey, TData, TBounds>, TPolicy>(policy);
            s = StagedBufferWire<OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>, OrderedMapState,
                OrderedHashEntryCodec<TKey, SpatialRecord<TKey, TData, TBounds>, TKeyCodec, SpatialRecordCodec<TKey, TData, TBounds, TDataCodec, TBoundsCodec, TBoundsAccess>>,
                OrderedMapStateCodec, OrderedMapWireLayout<TKey, SpatialRecord<TKey, TData, TBounds>, TPolicy>, TEntries>.TryReadStaged(
                    ref trial, ref work, ref entries, Kind(access), codec, default, layout, out stage);
            if (!s.IsSuccess()) return s;
            s = buckets.CanReplaceCapacity(OrderedHashRules.BucketCapacity(stage.Capacity)); if (!s.IsSuccess()) return s;
            for (int i = 0; i < stage.State.Used; i++) if (stage.Items[i].Active != 0) stage.Items[i].Value.Key = stage.Items[i].Key;
            reader = trial; scratch = work; return DeterministicStatus.None;
        }
        internal static DeterministicStatus Publish(ref TEntries entries, ref TBuckets buckets,
            in CanonicalBufferStage<OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>>, OrderedMapState> stage, TPolicy policy)
        {
            var s = entries.PrepareReplace(stage.Capacity); if (!s.IsSuccess()) return s;
            s = buckets.PrepareReplace(OrderedHashRules.BucketCapacity(stage.Capacity)); if (!s.IsSuccess()) return s;
            var layout = new OrderedMapWireLayout<TKey, SpatialRecord<TKey, TData, TBounds>, TPolicy>(policy);
            // PublishPrepared is codec-independent; these codec parameters are not invoked here.
            for (int i = 0; i < stage.State.Used; i++) entries.Write(i, stage.Items[i]);
            entries.CommitWrite(); layout.RebuildValidatedBuckets(ref entries, ref buckets, stage.State); return DeterministicStatus.None;
        }
    }
}
