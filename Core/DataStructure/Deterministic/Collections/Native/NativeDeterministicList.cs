using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LegendaryTools.Deterministic
{
    /// <summary>Native ownership follows Unity Collections: copies alias; dispose exactly once after all jobs complete.</summary>
    [NativeContainer]
    public unsafe partial struct NativeDeterministicList<T> : IDisposable where T : unmanaged
    {
        internal NativeStorage<T> Storage;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif
        public NativeDeterministicList(int capacity, Allocator allocator) : this(new BufferOptions(capacity), allocator) { }
        public NativeDeterministicList(BufferOptions options, Allocator allocator)
        {
            this = default; Storage = NativeStorage<T>.Create(options, allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }
        public bool IsCreated => Storage.IsCreated;
        public int Count { get { CheckRead(); return IsCreated ? Storage.Data->State.Count : 0; } }
        public int Length => Count;
        public int Capacity { get { CheckRead(); return Storage.Capacity; } }
        public T this[int index]
        {
            get { var status = TryGet(index, out T value); NativeChecks.Status(status); return value; }
            set { NativeChecks.Status(TrySet(index, value)); }
        }
        public DeterministicStatus TryGet(int index, out T value)
        {
            CheckRead(); value = default;
            return !IsCreated ? DeterministicStatus.Disposed : SequenceKernel<T, NativeStorage<T>>.TryGet(ref Storage, Storage.Data->State, index, out value);
        }
        public DeterministicStatus TrySet(int index, in T value)
        { CheckWrite(); return !IsCreated ? DeterministicStatus.Disposed : SequenceKernel<T, NativeStorage<T>>.TrySet(ref Storage, Storage.Data->State, index, value); }
        public DeterministicStatus TryAdd(in T value)
        { CheckWrite(); return !IsCreated ? DeterministicStatus.Disposed : SequenceKernel<T, NativeStorage<T>>.TryInsert(ref Storage, ref Storage.Data->State, Storage.Data->State.Count, value); }
        public DeterministicStatus TryInsert(int index, in T value)
        { CheckWrite(); return !IsCreated ? DeterministicStatus.Disposed : SequenceKernel<T, NativeStorage<T>>.TryInsert(ref Storage, ref Storage.Data->State, index, value); }
        public DeterministicStatus TryRemoveAt(int index)
        { CheckWrite(); return !IsCreated ? DeterministicStatus.Disposed : SequenceKernel<T, NativeStorage<T>>.TryRemoveAt(ref Storage, ref Storage.Data->State, index); }
        public DeterministicStatus TryRemoveAtSwapBack(int index)
        { CheckWrite(); return !IsCreated ? DeterministicStatus.Disposed : SequenceKernel<T, NativeStorage<T>>.TryRemoveAt(ref Storage, ref Storage.Data->State, index, true); }
        public DeterministicStatus TryClear()
        { CheckWrite(); return !IsCreated ? DeterministicStatus.Disposed : SequenceKernel<T, NativeStorage<T>>.TryClear(ref Storage, ref Storage.Data->State); }
        public int IndexOf<TEquality>(in T value, TEquality equality) where TEquality : unmanaged, IDeterministicEquality<T>
        {
            var status = TryIndexOf(value, equality, out int index); NativeChecks.Status(status); return index;
        }
        public DeterministicStatus TryIndexOf<TEquality>(in T value, TEquality equality, out int index) where TEquality : unmanaged, IDeterministicEquality<T>
        {
            CheckRead(); index = -1; if (!IsCreated) return DeterministicStatus.Disposed;
            return SequenceKernel<T, NativeStorage<T>>.TryIndexOf(ref Storage, Storage.Data->State, value, equality, out index);
        }
        public void Add(T value) => NativeChecks.Status(TryAdd(value));
        public void Insert(int index, T value) => NativeChecks.Status(TryInsert(index, value));
        public void RemoveAt(int index) => NativeChecks.Status(TryRemoveAt(index));
        public void Clear() => NativeChecks.Status(TryClear());
        public DeterministicStatus TryCapture(out NativeSequenceSnapshot<T> snapshot)
        {
            CheckWrite(); snapshot = default;
            var status = Storage.TryCapture(out int slot, out ulong lease); if (!status.IsSuccess()) return status;
            snapshot = new NativeSequenceSnapshot<T> { Storage = Storage, State = Storage.Data->State, Slot = slot, Lease = lease };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = m_Safety;
#endif
            return DeterministicStatus.None;
        }
        public DeterministicStatus TryRestore(in NativeSequenceSnapshot<T> snapshot)
        {
            CheckWrite();
            if (!IsCreated) return DeterministicStatus.Disposed;
            if (snapshot.Storage.Data != Storage.Data || snapshot.Ring) return DeterministicStatus.InvalidOwner;
            var status = Storage.TryRestore(snapshot.Slot, snapshot.Lease);
            if (status.IsSuccess()) Storage.Data->State = snapshot.State; return status;
        }
        public NativeSequenceReadOnly<T> AsReadOnly()
        {
            CheckRead();
            var view = new NativeSequenceReadOnly<T> { Storage = Storage, State = IsCreated ? Storage.Data->State : default, Revision = Storage.Revision };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            view.m_Safety = m_Safety;
#endif
            return view;
        }
        public NativeSequenceEnumerator<T> GetEnumerator()
        {
            CheckRead();
            var enumerator = new NativeSequenceEnumerator<T> { Storage = Storage, State = IsCreated ? Storage.Data->State : default, Revision = Storage.Revision, Index = -1 };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            enumerator.m_Safety = m_Safety;
#endif
            return enumerator;
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
