using System;

namespace LegendaryTools.Deterministic.Samples
{
    /// <summary>One external key per volume. Entity generations distinguish a reused entity slot.</summary>
    public readonly struct SpatialVolumeKey
    {
        public readonly StableHandle Entity;
        public readonly ushort Volume;
        public SpatialVolumeKey(StableHandle entity, ushort volume) { Entity = entity; Volume = volume; }
    }
    public unsafe struct SpatialVolumePolicy : IDeterministicEquality<SpatialVolumeKey>, IDeterministicComparer<SpatialVolumeKey>, IDeterministicCodec<SpatialVolumeKey>
    {
        public const ulong Fingerprint = 0x4c4453564f4c3031UL;
        public bool IsValid => true;
        public uint SchemaId => 0xd380;
        public ushort Version => 1;
        public bool Equals(in SpatialVolumeKey a, in SpatialVolumeKey b) => a.Entity == b.Entity && a.Volume == b.Volume;
        public ulong Hash(in SpatialVolumeKey key) => DeterministicHash.Mix(DeterministicHash.Mix(key.Entity.Index ^ ((ulong)key.Entity.Generation << 32)) ^ key.Volume);
        public int Compare(in SpatialVolumeKey a, in SpatialVolumeKey b) { int c = a.Entity.CompareTo(b.Entity); return c != 0 ? c : a.Volume.CompareTo(b.Volume); }
        public int Measure(in SpatialVolumeKey value) => 10;
        public DeterministicStatus TryWrite(ref DeterministicWriter writer, in SpatialVolumeKey value)
        {
            if (!value.Entity.IsValid) return DeterministicStatus.InvalidEncoding;
            if (!writer.Has(10)) return DeterministicStatus.BufferTooSmall;
            var trial = writer; trial.TryUInt(value.Entity.Index, 4); trial.TryUInt(value.Entity.Generation, 4); trial.TryUInt(value.Volume, 2); writer = trial; return DeterministicStatus.None;
        }
        public DeterministicStatus TryRead(ref DeterministicReader reader, out SpatialVolumeKey value)
        {
            value = default; if (!reader.Has(10)) return DeterministicStatus.BufferTooSmall;
            var trial = reader; trial.TryUInt(4, out ulong index); trial.TryUInt(4, out ulong generation); trial.TryUInt(2, out ulong volume);
            var entity = new StableHandle((uint)index, (uint)generation); if (!entity.IsValid) return DeterministicStatus.InvalidEncoding;
            value = new SpatialVolumeKey(entity, (ushort)volume); reader = trial; return DeterministicStatus.None;
        }
    }
    /// <summary>Adapter for an authoritative dense array owned/serialized by the application's ECS or state binding.</summary>
    public sealed class SpatialArraySourceOwner<TKey, TData, TBounds> where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    {
        private readonly SpatialRecord<TKey, TData, TBounds>[] rows;
        public ulong Identity { get; } = SpatialSourceIdentity.Create();
        public ulong Revision { get; private set; }
        public int Count { get; private set; }
        public SpatialArraySourceOwner(SpatialRecord<TKey, TData, TBounds>[] authoritativeRows, int count)
        { rows = authoritativeRows ?? throw new ArgumentNullException(nameof(authoritativeRows)); NotifyChanged(count).ThrowIfFailure(); }
        /// <summary>Call after any relevant array mutation/restore, before reading or synchronizing the projection.</summary>
        public DeterministicStatus NotifyChanged(int count)
        { if ((uint)count > (uint)rows.Length) return DeterministicStatus.OutOfRange; Count = count; Revision++; return DeterministicStatus.None; }
        public SpatialArraySource<TKey, TData, TBounds> AsSource() => new SpatialArraySource<TKey, TData, TBounds>(this);
        internal DeterministicStatus Read(int ordinal, out SpatialRecord<TKey, TData, TBounds> record)
        { record = default; if ((uint)ordinal >= (uint)Count) return DeterministicStatus.OutOfRange; record = rows[ordinal]; return DeterministicStatus.None; }
    }
    public readonly struct SpatialArraySource<TKey, TData, TBounds> : IDeterministicSpatialSource<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    {
        private readonly SpatialArraySourceOwner<TKey, TData, TBounds> owner;
        internal SpatialArraySource(SpatialArraySourceOwner<TKey, TData, TBounds> owner) { this.owner = owner; }
        public ulong Identity => owner == null ? 0 : owner.Identity;
        public ulong Revision => owner == null ? 0 : owner.Revision;
        public int Count => owner == null ? 0 : owner.Count;
        public DeterministicStatus TryGetAt(int ordinal, out SpatialRecord<TKey, TData, TBounds> record)
        { record = default; return owner == null ? DeterministicStatus.Disposed : owner.Read(ordinal, out record); }
    }
    /// <summary>All producers finish before merge. Caller scratch stages the already ordered payloads for one atomic publication.</summary>
    public static unsafe class SpatialCommandApplication
    {
        public static DeterministicStatus TryApply<TKey, TData, TBounds, TAccess, TPolicy>(
            DeterministicCommandBuffer<SpatialMutation<TKey, TData, TBounds>> commands,
            DeterministicSpatialIndex<TKey, TData, TBounds, TAccess, TPolicy> index, Span<SpatialMutation<TKey, TData, TBounds>> scratch)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
            where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
        {
            var status = commands.TryMerge(out var merged); if (!status.IsSuccess()) return status;
            if (merged.Length > scratch.Length) return DeterministicStatus.BufferTooSmall;
            for (int i = 0; i < merged.Length; i++) scratch[i] = merged[i].Payload;
            return index.TryApplyBatch(scratch.Slice(0, merged.Length));
        }
#if !FIXEDPOINT_STANDALONE
        public static DeterministicStatus TryApply<TKey, TData, TBounds, TAccess, TPolicy>(
            DeterministicCommandBuffer<SpatialMutation<TKey, TData, TBounds>> commands,
            NativeDeterministicSpatialIndex<TKey, TData, TBounds, TAccess, TPolicy> index, Span<SpatialMutation<TKey, TData, TBounds>> scratch)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
            where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
        {
            var status = commands.TryMerge(out var merged); if (!status.IsSuccess()) return status;
            if (merged.Length > scratch.Length) return DeterministicStatus.BufferTooSmall;
            for (int i = 0; i < merged.Length; i++) scratch[i] = merged[i].Payload;
            fixed (SpatialMutation<TKey, TData, TBounds>* p = scratch) return index.TryApplyBatch(p, merged.Length);
        }
#endif
    }
}
