using System;
using System.Collections.Generic;

namespace LegendaryTools.Deterministic
{
    public readonly struct ManagedAvlSnapshot<TKey, TValue> : IDisposable where TKey : unmanaged where TValue : unmanaged
    {
        internal readonly ManagedBufferSnapshot<AvlNode<TKey, TValue>> Buffer;
        internal readonly AvlState State;
        internal ManagedAvlSnapshot(ManagedBufferSnapshot<AvlNode<TKey, TValue>> buffer, AvlState state) { Buffer = buffer; State = state; }
        public bool IsCreated => Buffer.IsCreated;
        public int Count => State.Count;
        public DeterministicStatus TryGetAt(int index, out KeyValuePair<TKey, TValue> item)
        {
            item = default; if (!IsCreated) return DeterministicStatus.Disposed;
            if ((uint)index >= (uint)Count) return DeterministicStatus.OutOfRange;
            int root = State.Root;
            while (root != 0)
            {
                Buffer.TryGet(root - 1, out var node); int leftSize = 0;
                if (node.Left != 0) { Buffer.TryGet(node.Left - 1, out var left); leftSize = left.Size; }
                if (index == leftSize) { item = new KeyValuePair<TKey, TValue>(node.Key, node.Value); return DeterministicStatus.None; }
                if (index < leftSize) root = node.Left; else { root = node.Right; index -= leftSize + 1; }
            }
            return DeterministicStatus.InvalidEncoding;
        }
        public void Dispose() => Buffer.Dispose();
    }
    public sealed partial class DeterministicSortedDictionary<TKey, TValue, TComparer> : DeterministicSortedMapBase<TKey, TValue>
        where TKey : unmanaged where TValue : unmanaged where TComparer : unmanaged, IDeterministicComparer<TKey>
    {
        internal ManagedStorage<AvlNode<TKey, TValue>> Storage;
        internal AvlState State;
        private readonly TComparer comparer;
        public DeterministicSortedDictionary(int capacity, TComparer comparer = default, IDeterministicEquality<TValue> valueEquality = null) : this(new BufferOptions(capacity), comparer, valueEquality) { }
        public DeterministicSortedDictionary(BufferOptions options, TComparer comparer = default, IDeterministicEquality<TValue> valueEquality = null) : base(valueEquality)
        { if (!comparer.IsValid) DeterministicStatus.InvalidConfiguration.ThrowIfFailure(); this.comparer = comparer; Storage = new ManagedStorage<AvlNode<TKey, TValue>>(options); }
        public override int Count => State.Count;
        public override int Capacity => Storage.Capacity;
        public override bool IsCreated => Storage.IsCreated;
        public override ulong Revision => Storage.Revision;
        public int Height => IsCreated && State.Root != 0 ? Storage.Read(State.Root - 1).Height : 0;
        public override DeterministicStatus TryAdd(in TKey key, in TValue value) => AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.TryPut(ref Storage, ref State, key, value, comparer, true);
        public override DeterministicStatus TrySet(in TKey key, in TValue value) => AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.TryPut(ref Storage, ref State, key, value, comparer, false);
        public override DeterministicStatus TryGet(in TKey key, out TValue value) => AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.TryGet(ref Storage, State, key, comparer, out value);
        public override DeterministicStatus TryRemove(in TKey key) => AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.TryRemove(ref Storage, ref State, key, comparer);
        public override DeterministicStatus TryClear() => AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.TryClear(ref Storage, ref State);
        public override DeterministicStatus TryGetAt(int index, out KeyValuePair<TKey, TValue> item)
        { var status = AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.TryGetAt(ref Storage, State, index, out var entry); item = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); return status; }
        protected internal override int FirstCursor => IsCreated ? AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.First(ref Storage, State.Root) : 0;
        protected internal override bool ReadNext(ref int cursor, out KeyValuePair<TKey, TValue> item)
        {
            item = default; if (cursor == 0) return false; var entry = Storage.Read(cursor - 1);
            item = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value); cursor = AvlKernel<TKey, TValue, TComparer, ManagedStorage<AvlNode<TKey, TValue>>>.Next(ref Storage, cursor); return true;
        }
        public DeterministicStatus TryCapture(out ManagedAvlSnapshot<TKey, TValue> snapshot)
        { var status = Storage.TryCapture(out var buffer); snapshot = new ManagedAvlSnapshot<TKey, TValue>(buffer, State); return status; }
        public DeterministicStatus TryRestore(in ManagedAvlSnapshot<TKey, TValue> snapshot)
        { var status = Storage.TryRestore(snapshot.Buffer); if (status.IsSuccess()) State = snapshot.State; return status; }
        public override void Dispose() { Storage.Dispose(); State = default; }
    }
}
