using System;

namespace LegendaryTools.Deterministic
{
    public enum DeterministicBackend : byte { Managed, BurstCompiler }
    public enum AllocationPolicy : byte { Fixed, Grow }
    public enum SnapshotPolicy : byte { FullPerTick, KeyframesAndDeltas, CopyOnWrite }

    /// <summary>Failures never represent a successful mutation. Resource failures belong to the host, not gameplay.</summary>
    public enum DeterministicStatus : byte
    {
        None, InvalidConfiguration, OutOfRange, Empty, DuplicateKey, NotFound,
        CapacityExceeded, SnapshotCapacityExceeded, BufferTooSmall, InvalidEncoding,
        UnsupportedType, Invalidated, Disposed, InvalidOwner, GenerationExhausted,
        BackendUnavailable, ProfileMismatch, HistoryExhausted, QuorumLost, Pending, ProtocolConflict
    }

    public static class DeterministicStatusExtensions
    {
        public static bool IsSuccess(this DeterministicStatus status) => status == DeterministicStatus.None;

        /// <summary>Managed convenience only. Burst callers use the returned status.</summary>
        public static void ThrowIfFailure(this DeterministicStatus status)
        {
            switch (status)
            {
                case DeterministicStatus.None: return;
                case DeterministicStatus.OutOfRange: throw new ArgumentOutOfRangeException();
                case DeterministicStatus.Disposed: throw new ObjectDisposedException("Deterministic collection");
                case DeterministicStatus.DuplicateKey: throw new ArgumentException("An equal key already exists.");
                case DeterministicStatus.NotFound: throw new System.Collections.Generic.KeyNotFoundException();
                case DeterministicStatus.InvalidConfiguration: throw new ArgumentException("Invalid deterministic collection configuration.");
                default: throw new InvalidOperationException("Deterministic operation failed: " + status);
            }
        }
    }

    /// <summary>Immutable limits. default(BufferOptions) is invalid; construct the options explicitly.</summary>
    public readonly struct BufferOptions
    {
        public readonly int InitialCapacity, MaxCapacity, SnapshotSlots, PageBytes;
        public readonly AllocationPolicy Allocation;
        public readonly SnapshotPolicy Snapshots;

        public BufferOptions(int initialCapacity, int maxCapacity = -1,
            AllocationPolicy allocation = AllocationPolicy.Fixed,
            SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas,
            int snapshotSlots = 4, int pageBytes = 4096)
        {
            InitialCapacity = initialCapacity;
            MaxCapacity = maxCapacity < 0 ? initialCapacity : maxCapacity;
            Allocation = allocation; Snapshots = snapshots;
            SnapshotSlots = snapshotSlots; PageBytes = pageBytes;
        }

        public bool IsValid => InitialCapacity >= 0 && MaxCapacity >= InitialCapacity && SnapshotSlots >= 0 &&
            PageBytes >= 64 && (PageBytes & (PageBytes - 1)) == 0 && Allocation <= AllocationPolicy.Grow &&
            Snapshots <= SnapshotPolicy.CopyOnWrite && (Allocation != AllocationPolicy.Fixed || MaxCapacity == InitialCapacity);

        public DeterministicStatus TryCapacity(int current, int required, out int capacity)
        {
            capacity = current;
            if (!IsValid || current < 0 || current > MaxCapacity || required < 0) return DeterministicStatus.InvalidConfiguration;
            if (required <= current) return DeterministicStatus.None;
            if (Allocation == AllocationPolicy.Fixed || required > MaxCapacity) return DeterministicStatus.CapacityExceeded;
            long next = current == 0 ? 1 : current;
            while (next < required) next = Math.Min((long)MaxCapacity, next * 2);
            capacity = (int)next;
            return DeterministicStatus.None;
        }
    }

    public interface IDeterministicEquality<T> where T : unmanaged
    {
        bool IsValid { get; }
        bool Equals(in T left, in T right);
        ulong Hash(in T value);
    }

    public interface IDeterministicComparer<T> where T : unmanaged
    {
        bool IsValid { get; }
        int Compare(in T left, in T right);
    }

    /// <summary>Explicit fields only. A codec must not serialize padding, references or addresses.</summary>
    public interface IDeterministicCodec<T> where T : unmanaged
    {
        uint SchemaId { get; }
        ushort Version { get; }
        int Measure(in T value);
        DeterministicStatus TryWrite(ref DeterministicWriter writer, in T value);
        DeterministicStatus TryRead(ref DeterministicReader reader, out T value);
    }

    /// <summary>Statically specialized access used by shared kernels. Write requires a successful PrepareWrite.</summary>
    public interface IDeterministicBuffer<T> where T : unmanaged
    {
        int Capacity { get; }
        ulong Revision { get; }
        bool IsCreated { get; }
        T Read(int index);
        void Write(int index, in T value);
        DeterministicStatus CanPrepareWrite(int requiredCapacity, int start, int count);
        DeterministicStatus PrepareWrite(int requiredCapacity, int start, int count);
        void CommitWrite();
    }

    /// <summary>Checkpoint replacement is prepared after decoding and structural validation in caller-owned staging memory.</summary>
    public interface IDeterministicImportBuffer<T> : IDeterministicBuffer<T> where T : unmanaged
    {
        DeterministicStatus CanReplaceCapacity(int capacity);
        DeterministicStatus PrepareReplace(int capacity);
    }

    public struct SequenceState
    {
        public int Count;
        public int Head;
    }

    /// <summary>Portable identity. Generation zero is reserved for invalid handles.</summary>
    public readonly struct StableHandle : IEquatable<StableHandle>, IComparable<StableHandle>
    {
        public readonly uint Index, Generation;
        public StableHandle(uint index, uint generation) { Index = index; Generation = generation; }
        public bool IsValid => Generation != 0;
        public ulong Packed => ((ulong)Generation << 32) | Index;
        public bool Equals(StableHandle other) => Index == other.Index && Generation == other.Generation;
        public int CompareTo(StableHandle other)
        { int c = Index.CompareTo(other.Index); return c != 0 ? c : Generation.CompareTo(other.Generation); }
        public override bool Equals(object obj) => obj is StableHandle other && Equals(other);
        public override int GetHashCode() => unchecked((int)Index * 397 ^ (int)Generation);
        public static bool operator ==(StableHandle a, StableHandle b) => a.Equals(b);
        public static bool operator !=(StableHandle a, StableHandle b) => !a.Equals(b);
    }
}
