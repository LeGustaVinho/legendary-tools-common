using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeFamilyTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct FamilyJob : IJob
        {
            internal NativeDeterministicArray<int> Array;
            internal NativeDeterministicCell<int> Cell;
            internal NativeDeterministicQueue<int> Queue;
            internal NativeDeterministicDeque<int> Deque;
            internal NativeDeterministicRingBuffer<int> Ring;
            internal NativeDeterministicStack<int> Stack;
            internal NativeDeterministicBitSet Bits;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                Array[1] = 7; Results[1] = Array[1]; Cell.Value = 9; Results[2] = Cell.Value;
                Queue.Enqueue(1); Queue.Enqueue(2); Queue.Enqueue(3); Queue.Dequeue(); Queue.Dequeue();
                Queue.Enqueue(4); Queue.Enqueue(5); Queue.TryCapture(out var queueSnapshot);
                Queue.Enqueue(6); Queue.Enqueue(7); int sum = 0; foreach (var value in Queue) sum += value;
                Results[3] = sum; Results[4] = (int)Queue.TryEnqueue(8);
                Queue.TryRestore(queueSnapshot); queueSnapshot.Dispose(); Results[5] = Queue.Dequeue();
                Deque.AddFirst(2); Deque.AddFirst(1); Deque.AddLast(3); Results[6] = Deque.RemoveLast(); Results[7] = Deque.RemoveFirst();
                Ring.Enqueue(1); Ring.Enqueue(2); Ring.Enqueue(3, true); Results[8] = Ring.Dequeue();
                Stack.Push(1); Stack.Push(2); Stack.Push(3); int encoded = 0; foreach (var value in Stack) encoded = encoded * 10 + value;
                Results[9] = encoded; Results[10] = Stack.Pop();
                Bits[0] = true; Bits[64] = true; Bits[129] = true; Bits.TryCapture(out var bitSnapshot); Bits.Clear();
                Bits.TryRestore(bitSnapshot); bitSnapshot.Dispose(); Results[11] = Bits.Count; Results[12] = Bits.NextSetBit(1);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void EveryNativeSequenceRunsInBurstAndPreservesItsContract(SnapshotPolicy policy)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var array = new NativeDeterministicArray<int>(new BufferOptions(2, snapshots: policy), Allocator.Persistent))
            using (var cell = new NativeDeterministicCell<int>(new BufferOptions(1, snapshots: policy), Allocator.Persistent))
            using (var queue = new NativeDeterministicQueue<int>(new BufferOptions(3, 5, AllocationPolicy.Grow, policy), Allocator.Persistent))
            using (var deque = new NativeDeterministicDeque<int>(new BufferOptions(3, snapshots: policy), Allocator.Persistent))
            using (var ring = new NativeDeterministicRingBuffer<int>(new BufferOptions(2, snapshots: policy), Allocator.Persistent))
            using (var stack = new NativeDeterministicStack<int>(new BufferOptions(3, snapshots: policy), Allocator.Persistent))
            using (var bits = new NativeDeterministicBitSet(130, Allocator.Persistent, policy))
            using (var results = new NativeArray<int>(13, Allocator.TempJob))
            {
                new FamilyJob { Array = array, Cell = cell, Queue = queue, Deque = deque, Ring = ring, Stack = stack, Bits = bits, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new[] { 0, 7, 9, 25, (int)DeterministicStatus.CapacityExceeded, 3, 3, 1, 2, 321, 3, 3, 64 }, results.ToArray());
            }
        }
    }
}
