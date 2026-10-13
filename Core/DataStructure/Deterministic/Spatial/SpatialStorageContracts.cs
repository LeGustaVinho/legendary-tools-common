namespace LegendaryTools.Deterministic
{
    internal struct SpatialIndexState
    {
        internal OrderedMapState Records, Cells;
        internal int Root, NodeUsed, FreeNode, LinkUsed, FreeLink, OverflowCount;
        internal bool Dirty;
        internal ulong Revision, SourceRevision, SourceIdentity;
        internal bool SourceSynchronized;
        internal static SpatialIndexState Empty => new SpatialIndexState { Root = -1, FreeNode = -1, FreeLink = -1 };
    }
    internal struct SpatialPlacement
    {
        internal SpatialRawBounds Bounds;
        internal int Leaf, GridHead;
        internal bool Overflow;
    }
    internal struct SpatialBvhNode<TKey> where TKey : unmanaged
    {
        internal SpatialRawBounds Bounds;
        internal TKey MinimumKey;
        internal int Parent, Left, Right, Height, RecordSlot, NextFree;
        internal bool IsLeaf => Left < 0;
    }
    internal readonly struct SpatialCellKey
    {
        internal readonly long X, Y, Z;
        internal SpatialCellKey(long x, long y, long z) { X = x; Y = y; Z = z; }
    }
    internal struct SpatialCellPolicy : IDeterministicEquality<SpatialCellKey>, IDeterministicComparer<SpatialCellKey>
    {
        public bool IsValid => true;
        public bool Equals(in SpatialCellKey a, in SpatialCellKey b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public ulong Hash(in SpatialCellKey key) => DeterministicHash.Mix(DeterministicHash.Mix(unchecked((ulong)key.X)) ^
            DeterministicHash.Mix(unchecked((ulong)key.Y)) ^ DeterministicHash.Mix(unchecked((ulong)key.Z) + 0x9e3779b97f4a7c15UL));
        public int Compare(in SpatialCellKey a, in SpatialCellKey b)
        { int c = a.X.CompareTo(b.X); if (c != 0) return c; c = a.Y.CompareTo(b.Y); return c != 0 ? c : a.Z.CompareTo(b.Z); }
    }
    internal struct SpatialGridLink
    {
        internal SpatialCellKey Cell;
        internal int RecordSlot, NextCell, PreviousCell, NextObject, NextFree;
    }
    internal struct SpatialBuildTask { internal int Begin, Count, Parent, Side; }

    /// <summary>Constrained dispatch. Backends adapt the existing ManagedStorage/NativeStorage and hash kernels.</summary>
    internal interface ISpatialStorage<TKey, TData, TBounds>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
    {
        bool IsCreated { get; }
        SpatialIndexOptions Options { get; }
        ref SpatialIndexState State { get; }
        int Capacity { get; }
        int NodeCapacity { get; }
        int LinkCapacity { get; }
        int Find(in TKey key);
        OrderedHashEntry<TKey, SpatialRecord<TKey, TData, TBounds>> ReadEntry(int slot);
        DeterministicStatus Put(in SpatialRecord<TKey, TData, TBounds> record, bool addOnly, out int slot);
        DeterministicStatus Remove(in TKey key);
        DeterministicStatus ClearRecords();
        SpatialPlacement ReadPlacement(int slot);
        void WritePlacement(int slot, in SpatialPlacement placement);
        SpatialBvhNode<TKey> ReadNode(int slot);
        void WriteNode(int slot, in SpatialBvhNode<TKey> node);
        SpatialGridLink ReadLink(int slot);
        void WriteLink(int slot, in SpatialGridLink link);
        int FindCell(in SpatialCellKey cell);
        OrderedHashEntry<SpatialCellKey, int> ReadCell(int slot);
        void PutCell(in SpatialCellKey cell, int head);
        void RemoveCell(in SpatialCellKey cell);
        void ClearCells();
        int ReadOrder(int index);
        void WriteOrder(int index, int value);
        SpatialBuildTask ReadTask(int index);
        void WriteTask(int index, in SpatialBuildTask value);
    }
}
