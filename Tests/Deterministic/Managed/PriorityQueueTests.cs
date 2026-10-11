using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class PriorityQueueTests
    {
        private struct ModelNode { internal int Element, Priority; internal ulong Sequence; }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void HeapMatchesStablePriorityOrderingAcrossGrowthAndRestoration(SnapshotPolicy snapshots)
        {
            using (var queue = new DeterministicPriorityQueue<int, int, Int32Policy>(new BufferOptions(0, 71, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                var model = new List<ModelNode>(); uint random = 4881; ulong sequence = 0;
                for (int step = 0; step < 4000; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    if ((random & 7) < 4 && model.Count < 71)
                    {
                        int priority = (int)((random >> 12) % 11) - 5;
                        queue.Enqueue(step, priority); model.Add(new ModelNode { Element = step, Priority = priority, Sequence = sequence++ });
                    }
                    else if (model.Count != 0)
                    {
                        int best = 0;
                        for (int i = 1; i < model.Count; i++) if (model[i].Priority < model[best].Priority ||
                            model[i].Priority == model[best].Priority && model[i].Sequence < model[best].Sequence) best = i;
                        queue.TryDequeue(out int element, out int priority).ThrowIfFailure();
                        Assert.AreEqual(model[best].Element, element); Assert.AreEqual(model[best].Priority, priority); model.RemoveAt(best);
                    }
                    if (step % 47 == 0)
                    {
                        queue.TryCapture(out var snapshot).ThrowIfFailure(); queue.Clear(); queue.Enqueue(999, -99);
                        queue.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
                    }
                    Assert.AreEqual(model.Count, queue.Count); Assert.AreEqual(sequence, queue.NextSequence);
                }
            }
        }
        [Test]
        public void EqualPrioritiesAreFifoAndRejectedEnqueuesDoNotConsumeSequences()
        {
            using (var queue = new DeterministicPriorityQueue<int, int, Int32Policy>(3))
            {
                queue.Enqueue(1, 5); queue.Enqueue(2, 5); queue.Enqueue(3, 5);
                Assert.AreEqual(DeterministicStatus.CapacityExceeded, queue.TryEnqueue(4, 5)); Assert.AreEqual(3UL, queue.NextSequence);
                Assert.AreEqual(1, queue.Dequeue()); Assert.AreEqual(2, queue.Dequeue()); Assert.AreEqual(3, queue.Dequeue());
                Assert.AreEqual(DeterministicStatus.Empty, queue.TryDequeue(out _, out _)); queue.Clear(); Assert.AreEqual(3UL, queue.NextSequence);
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void PriorityQueueFixedHotPathAndSnapshotsDoNotAllocate(SnapshotPolicy snapshots)
        {
            using (var queue = new DeterministicPriorityQueue<int, int, Int32Policy>(new BufferOptions(64, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64)))
            {
                for (int i = 0; i < 32; i++) queue.Enqueue(i, i % 5);
                for (int i = 0; i < 20; i++) Cycle(queue);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(queue);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicPriorityQueue<int, int, Int32Policy> queue)
        {
            queue.TryCapture(out var snapshot).ThrowIfFailure(); queue.Dequeue(); queue.Enqueue(100, -5);
            queue.TryRestore(snapshot).ThrowIfFailure(); snapshot.Dispose();
            if (queue.Count != 32 || queue.NextSequence != 32) throw new InvalidOperationException();
        }
    }
}
