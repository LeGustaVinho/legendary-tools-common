using System;
using LegendaryTools.FixedPoint;

namespace LegendaryTools.Deterministic
{
    public enum SpatialIndexStrategy : byte { DynamicBvh, SparseGrid, StaticBvh }
    public enum SpatialStateOwnership : byte { Autonomous, Derived }
    public enum SpatialRollbackPolicy : byte { Compact, FastRestore }
    public enum SpatialMutationKind : byte { Add, UpdateBounds, SetData, SetMetadata, Remove, Clear }
    public enum SpatialQueryKind : byte { Point, Aabb, Sphere, Ray, Segment, Nearest }

    /// <summary>Immutable limits. Bounds representation belongs to FixedBoundsRuntime, not this index.</summary>
    public readonly struct SpatialIndexOptions
    {
        public readonly BufferOptions Records;
        public readonly SpatialIndexStrategy Strategy;
        public readonly SpatialStateOwnership Ownership;
        public readonly SpatialRollbackPolicy Rollback;
        public readonly Fixed64 CellSize, FatMargin;
        public readonly int MaxCellsPerObject, MaxCellsPerQuery;
        public SpatialIndexOptions(int capacity, SpatialIndexStrategy strategy = SpatialIndexStrategy.DynamicBvh,
            SpatialStateOwnership ownership = SpatialStateOwnership.Autonomous,
            SpatialRollbackPolicy rollback = SpatialRollbackPolicy.Compact, Fixed64 cellSize = default,
            Fixed64 fatMargin = default, int maxCellsPerObject = 64, int maxCellsPerQuery = 4096)
            : this(new BufferOptions(capacity, snapshotSlots: 0), strategy, ownership, rollback,
                cellSize, fatMargin, maxCellsPerObject, maxCellsPerQuery) { }
        public SpatialIndexOptions(BufferOptions records, SpatialIndexStrategy strategy = SpatialIndexStrategy.DynamicBvh,
            SpatialStateOwnership ownership = SpatialStateOwnership.Autonomous,
            SpatialRollbackPolicy rollback = SpatialRollbackPolicy.Compact, Fixed64 cellSize = default,
            Fixed64 fatMargin = default, int maxCellsPerObject = 64, int maxCellsPerQuery = 4096)
        { Records = records; Strategy = strategy; Ownership = ownership; Rollback = rollback;
            CellSize = cellSize; FatMargin = fatMargin; MaxCellsPerObject = maxCellsPerObject; MaxCellsPerQuery = maxCellsPerQuery; }
        public bool IsValid => FixedRuntime.IsInitialized && FixedBoundsRuntime.IsInitialized && Records.IsValid &&
            Records.MaxCapacity <= (1 << 27) && Strategy <= SpatialIndexStrategy.StaticBvh &&
            Ownership <= SpatialStateOwnership.Derived && Rollback <= SpatialRollbackPolicy.FastRestore &&
            Fixed64.IsFinite(FatMargin) && FatMargin >= Fixed64.Zero && MaxCellsPerObject > 0 && MaxCellsPerQuery > 0 &&
            (Strategy != SpatialIndexStrategy.SparseGrid || Fixed64.IsFinite(CellSize) && CellSize > Fixed64.Zero) &&
            (Strategy != SpatialIndexStrategy.SparseGrid || (long)Records.MaxCapacity * MaxCellsPerObject <= (1 << 29));
        /// <summary>Only future-affecting logical limits. Acceleration choices never change query results.</summary>
        public ulong SemanticFingerprint
        {
            get
            {
                ulong h = DeterministicHash.Mix(0x4c44535041544931UL ^ (byte)Ownership);
                h = DeterministicHash.Mix(h ^ (uint)Records.InitialCapacity ^ ((ulong)(uint)Records.MaxCapacity << 32));
                return DeterministicHash.Mix(h ^ (byte)Records.Allocation);
            }
        }
    }

    public readonly struct SpatialMetadata
    {
        public readonly uint Layer, Flags;
        public SpatialMetadata(uint layer, uint flags = 0) { Layer = layer; Flags = flags; }
        public bool IsValid => Layer < 32;
    }
    public readonly struct SpatialFilter
    {
        public readonly uint LayerMask, RequiredFlags, ExcludedFlags;
        public static SpatialFilter All => new SpatialFilter(uint.MaxValue);
        public SpatialFilter(uint layerMask, uint requiredFlags = 0, uint excludedFlags = 0)
        { LayerMask = layerMask; RequiredFlags = requiredFlags; ExcludedFlags = excludedFlags; }
        public bool Accept(in SpatialMetadata metadata) => metadata.IsValid &&
            (LayerMask & (1u << (int)metadata.Layer)) != 0 && (metadata.Flags & RequiredFlags) == RequiredFlags &&
            (metadata.Flags & ExcludedFlags) == 0;
    }
    public struct SpatialRecord<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    {
        public TKey Key;
        public TData Data;
        public TBounds Bounds;
        public SpatialMetadata Metadata;
        public SpatialRecord(in TKey key, in TBounds bounds, in TData data, SpatialMetadata metadata = default)
        { Key = key; Bounds = bounds; Data = data; Metadata = metadata; }
    }
    public struct SpatialMutation<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    {
        public SpatialMutationKind Kind;
        public TKey Key;
        public TData Data;
        public TBounds Bounds;
        public SpatialMetadata Metadata;
    }
    public interface ISpatialPredicate<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    { bool Accept(in SpatialRecord<TKey, TData, TBounds> record); }
    public struct SpatialAcceptAll<TKey, TData, TBounds> : ISpatialPredicate<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    { public bool Accept(in SpatialRecord<TKey, TData, TBounds> record) => true; }
    public interface ISpatialPairPredicate<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    { bool Accept(in SpatialRecord<TKey, TData, TBounds> left, in SpatialRecord<TKey, TData, TBounds> right); }
    public struct SpatialAcceptAllPairs<TKey, TData, TBounds> : ISpatialPairPredicate<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    { public bool Accept(in SpatialRecord<TKey, TData, TBounds> left, in SpatialRecord<TKey, TData, TBounds> right) => true; }

    /// <summary>Normalized numeric endpoints. This is an acceleration envelope, not serialized Bounds storage.</summary>
    public struct SpatialRawBounds
    {
        public long MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public long Min(int axis) => axis == 0 ? MinX : axis == 1 ? MinY : MinZ;
        public long Max(int axis) => axis == 0 ? MaxX : axis == 1 ? MaxY : MaxZ;
        public void Set(int axis, long min, long max)
        { if (axis == 0) { MinX = min; MaxX = max; } else if (axis == 1) { MinY = min; MaxY = max; } else { MinZ = min; MaxZ = max; } }
        public bool Intersects(in SpatialRawBounds other, int dimensions) => MinX <= other.MaxX && MaxX >= other.MinX &&
            MinY <= other.MaxY && MaxY >= other.MinY && (dimensions == 2 || MinZ <= other.MaxZ && MaxZ >= other.MinZ);
        public bool Contains(in SpatialRawBounds other, int dimensions) => MinX <= other.MinX && MaxX >= other.MaxX &&
            MinY <= other.MinY && MaxY >= other.MaxY && (dimensions == 2 || MinZ <= other.MinZ && MaxZ >= other.MaxZ);
        public static SpatialRawBounds Union(in SpatialRawBounds a, in SpatialRawBounds b) => new SpatialRawBounds
        { MinX = Math.Min(a.MinX, b.MinX), MinY = Math.Min(a.MinY, b.MinY), MinZ = Math.Min(a.MinZ, b.MinZ),
            MaxX = Math.Max(a.MaxX, b.MaxX), MaxY = Math.Max(a.MaxY, b.MaxY), MaxZ = Math.Max(a.MaxZ, b.MaxZ) };
    }
    public interface ISpatialBoundsAccess<TBounds> where TBounds : unmanaged
    {
        int Dimensions { get; }
        DeterministicStatus TryRead(in TBounds bounds, out SpatialRawBounds envelope);
    }
    /// <summary>Sources expose authoritative membership and values. Revision is a local invalidation token; ordinal reads must be repeatable while it stays unchanged.</summary>
    public interface IDeterministicSpatialSource<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    {
        /// <summary>Nonzero local lifetime token. Different authoritative owners must never reuse a live token; excluded from canonical state.</summary>
        ulong Identity { get; }
        int Count { get; }
        ulong Revision { get; }
        DeterministicStatus TryGetAt(int ordinal, out SpatialRecord<TKey, TData, TBounds> record);
    }
    public interface IDeterministicSpatialReader<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    {
        int Count { get; }
        ulong Revision { get; }
        DeterministicStatus Check();
        DeterministicStatus TryGet(in TKey key, out SpatialRecord<TKey, TData, TBounds> record);
    }
    public interface IDeterministicSpatialMutator<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    { DeterministicStatus TryApply(in SpatialMutation<TKey, TData, TBounds> mutation); }

    /// <summary>The returned order uses exact metrics. Fixed64 Metric is a diagnostic conversion with its own status.</summary>
    public struct SpatialHit<TKey> where TKey : unmanaged
    {
        public TKey Key;
        public Fixed64 Metric;
        public FixedStatus MetricStatus;
    }
    public readonly struct SpatialPair<TKey> where TKey : unmanaged
    {
        public readonly TKey First, Second;
        public SpatialPair(in TKey first, in TKey second) { First = first; Second = second; }
    }
    /// <summary>Payload bytes. History includes retained roots and the transaction spare; UsedBytes counts pages reachable from active or leased roots.</summary>
    public readonly struct SpatialMemoryStats
    {
        public readonly long RecordsReservedBytes, AccelerationReservedBytes, WorkspaceReservedBytes, HistoryReservedBytes;
        public readonly long UsedBytes;
        public long ReservedBytes => RecordsReservedBytes + AccelerationReservedBytes + WorkspaceReservedBytes + HistoryReservedBytes;
        public SpatialMemoryStats(long records, long acceleration, long workspace, long history, long used)
        { RecordsReservedBytes = records; AccelerationReservedBytes = acceleration; WorkspaceReservedBytes = workspace;
            HistoryReservedBytes = history; UsedBytes = used; }
    }
}
