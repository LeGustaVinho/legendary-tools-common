using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativePriorityQueueTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct QueueJob : IJob
        {
            internal NativeDeterministicPriorityQueue<int, int, Int32Policy> Queue;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                Queue.Enqueue(1, 5); Queue.Enqueue(2, 3); Queue.Enqueue(3, 5); Queue.Enqueue(4, 3);
                Queue.TryCapture(out var snapshot); Queue.Dequeue(); Queue.Enqueue(99, -1); Queue.TryRestore(snapshot); snapshot.Dispose();
                Results[1] = Queue.Dequeue(); Results[2] = Queue.Dequeue(); Results[3] = Queue.Dequeue(); Results[4] = Queue.Dequeue();
                Results[5] = (int)Queue.NextSequence; Results[6] = (int)Queue.TryDequeue(out _, out _);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void StableHeapAndSequenceRestoreRunInNativeBurst(SnapshotPolicy snapshots)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var queue = new NativeDeterministicPriorityQueue<int, int, Int32Policy>(new BufferOptions(1, 9, AllocationPolicy.Grow, snapshots, 2, 64), Allocator.Persistent))
            using (var results = new NativeArray<int>(7, Allocator.TempJob))
            {
                new QueueJob { Queue = queue, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new[] { 0, 2, 4, 1, 3, 4, (int)DeterministicStatus.Empty }, results.ToArray());
            }
        }
    }
}
