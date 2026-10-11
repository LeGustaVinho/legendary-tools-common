using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    [NativeContainer]
    public unsafe partial struct NativeDeterministicBitSet : IDisposable
    {
        internal NativeStorage<ulong> Storage;
        private int length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicBitSet(int length, Allocator allocator, SnapshotPolicy snapshots = SnapshotPolicy.KeyframesAndDeltas, int snapshotSlots = 4)
        {
            this = default;
            if (length < 0) DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            this.length = length;
            int words = (int)(((long)length + 63) / 64);
            Storage = NativeStorage<ulong>.Create(new BufferOptions(words, snapshots: snapshots, snapshotSlots: snapshotSlots), allocator);
            Storage.Data->State.Count = words;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public int Length => length;
        public bool IsCreated => Storage.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? BitSetKernel<NativeStorage<ulong>>.Count(ref Storage) : 0; } }
        public bool this[int index]
        {
            get { var status = TryGet(index, out bool value); NativeChecks.Status(status); return value; }
            set { NativeChecks.Status(TrySet(index, value)); }
        }
        public DeterministicStatus TryGet(int index, out bool value)
        { CheckRead(); return BitSetKernel<NativeStorage<ulong>>.TryGet(ref Storage, length, index, out value); }
        public DeterministicStatus TrySet(int index, bool value)
        { CheckWrite(); return BitSetKernel<NativeStorage<ulong>>.TrySet(ref Storage, length, index, value); }
        public DeterministicStatus TryNextSetBit(int start, out int index)
        {
            CheckRead(); index = -1;
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (start < 0) return DeterministicStatus.OutOfRange;
            index = BitSetKernel<NativeStorage<ulong>>.NextSetBit(ref Storage, length, start); return DeterministicStatus.None;
        }
        public int NextSetBit(int start) { var status = TryNextSetBit(start, out int index); NativeChecks.Status(status); return index; }
        public DeterministicStatus TryClear()
        {
            CheckWrite(); var status = Storage.PrepareWrite(Storage.Capacity, 0, Storage.Capacity); if (!status.IsSuccess()) return status;
            ulong zero = 0; for (int i = 0; i < Storage.Capacity; i++) Storage.Write(i, zero);
            Storage.CommitWrite(); return DeterministicStatus.None;
        }
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryCapture(out NativeSequenceSnapshot<ulong> snapshot)
        {
            CheckWrite(); snapshot = default; var status = Storage.TryCapture(out int slot, out ulong lease);
            if (!status.IsSuccess()) return status;
            snapshot = new NativeSequenceSnapshot<ulong> { Storage = Storage, State = Storage.Data->State, Slot = slot, Lease = lease };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeSequenceSnapshot<ulong> snapshot)
        {
            CheckWrite(); if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Storage.Data != Storage.Data || snapshot.Ring) return DeterministicStatus.InvalidOwner;
            return Storage.TryRestore(snapshot.Slot, snapshot.Lease);
        }
        public void Dispose()
        {
            if (!IsCreated) return; CheckWrite();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            Storage.Destroy();
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (IsCreated) AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (IsCreated) AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
    }
}
