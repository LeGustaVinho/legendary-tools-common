using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicOptimization")]
    public sealed unsafe class NativeOptimizationTests
    {
        private struct Triple
        {
            internal int A, B, C;
            internal Triple(int value) { A = value; B = ~value; C = unchecked(value * 104729); }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct StructMoveJob : IJob
        {
            internal NativeDeterministicList<Triple> List;
            internal NativeArray<int> Results;
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed; int status = 0;
                for (int i = 0; i < 64; i++) status |= (int)List.TryAdd(new Triple(i));
                status |= (int)List.TryCapture(out var snapshot);
                if (status != 0) { snapshot.Dispose(); Results[1] = status; return; }
                for (int step = 0; step < 128; step++)
                {
                    status |= (int)List.TryRemoveAt(step * 17 % List.Count);
                    status |= (int)List.TryInsert(step * 29 % (List.Count + 1), new Triple(step + 1000));
                }
                Results[1] = status;
                for (int i = 0; i < 64; i++)
                { var value = List[i]; Results[2 + i * 3] = value.A; Results[3 + i * 3] = value.B; Results[4 + i * 3] = value.C; }
                Results[1] |= (int)List.TryRestore(snapshot); snapshot.Dispose();
            }
        }
        [TestCase(64)] [TestCase(128)]
        public void BurstStructMovesHandleNonPowerOfTwoPageElementCounts(int pageBytes)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var list = new NativeDeterministicList<Triple>(new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: pageBytes), Allocator.Persistent))
            using (var results = new NativeArray<int>(194, Allocator.TempJob))
            {
                new StructMoveJob { List = list, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0]); Assert.AreEqual(0, results[1]);
                var expected = new List<Triple>(); for (int i = 0; i < 64; i++) expected.Add(new Triple(i));
                for (int step = 0; step < 128; step++)
                { expected.RemoveAt(step * 17 % expected.Count); expected.Insert(step * 29 % (expected.Count + 1), new Triple(step + 1000)); }
                for (int i = 0; i < 64; i++)
                {
                    Assert.AreEqual(expected[i].A, results[2 + i * 3]); Assert.AreEqual(expected[i].B, results[3 + i * 3]); Assert.AreEqual(expected[i].C, results[4 + i * 3]);
                    Assert.AreEqual(new Triple(i), list[i]);
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PreparedMoveJob : IJob
        {
            internal NativeDeterministicList<int> List;
            internal NativeDeterministicSortedList<int, int, Int32Policy> Sorted;
            internal NativeArray<int> Results;
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed; int status = 0;
                for (int i = 0; i < 73; i++) status |= (int)List.TryAdd(i * 104729);
                for (int i = 128; i >= 0; i--) status |= (int)Sorted.TryAdd(i * 2, -i);
                status |= (int)List.TryCapture(out var listSnapshot); status |= (int)Sorted.TryCapture(out var sortedSnapshot);
                if (status != 0) { Results[1] = status; listSnapshot.Dispose(); sortedSnapshot.Dispose(); return; }
                uint random = 0x714c973d;
                for (int step = 0; step < 512; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    if (step % 3 != 0 && List.Count < 129)
                    {
                        int index = (int)(random % (uint)(List.Count + 1)), value = unchecked((int)random);
                        status |= (int)List.TryInsert(index, value);
                    }
                    else status |= (int)List.TryRemoveAt((int)(random % (uint)List.Count));
                    if (step % 53 == 52) status |= (int)List.TryRestore(listSnapshot);
                }
                for (int step = 0; step < 387; step++)
                {
                    int index = step % 129, parity = step / 129 % 2;
                    status |= (int)Sorted.TryRemove(index * 2 + parity);
                    status |= (int)Sorted.TryAdd(index * 2 + 1 - parity, step);
                }
                status |= (int)Sorted.TryRestore(sortedSnapshot);
                Results[1] = status; Results[2] = (int)Sorted.TryAdd(0, 999);
                Results[3] = (int)Sorted.TryAdd(int.MaxValue, 999);
                listSnapshot.Dispose(); sortedSnapshot.Dispose();
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.CopyOnWrite, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.FullPerTick, AllocationPolicy.Grow)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, AllocationPolicy.Grow)]
        [TestCase(SnapshotPolicy.CopyOnWrite, AllocationPolicy.Grow)]
        public void BurstMovesAcrossPagesAndRollbackMatchIndependentModels(SnapshotPolicy policy, AllocationPolicy allocation)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            var options = new BufferOptions(allocation == AllocationPolicy.Fixed ? 129 : 0, 129, allocation, policy, 2, 64);
            using (var list = new NativeDeterministicList<int>(options, Allocator.Persistent))
            using (var sorted = new NativeDeterministicSortedList<int, int, Int32Policy>(options, Allocator.Persistent))
            using (var results = new NativeArray<int>(4, Allocator.TempJob))
            {
                new PreparedMoveJob { List = list, Sorted = sorted, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0], "The job must execute through Burst."); Assert.AreEqual(0, results[1]);
                Assert.AreEqual((int)DeterministicStatus.DuplicateKey, results[2]);
                Assert.AreEqual((int)DeterministicStatus.CapacityExceeded, results[3]);
                var initial = new List<int>(); for (int i = 0; i < 73; i++) initial.Add(i * 104729);
                var expected = new List<int>(initial); uint random = 0x714c973d;
                for (int step = 0; step < 512; step++)
                {
                    random = unchecked(random * 1664525u + 1013904223u);
                    if (step % 3 != 0 && expected.Count < 129)
                        expected.Insert((int)(random % (uint)(expected.Count + 1)), unchecked((int)random));
                    else expected.RemoveAt((int)(random % (uint)expected.Count));
                    if (step % 53 == 52) expected = new List<int>(initial);
                }
                Assert.AreEqual(expected.Count, list.Count);
                for (int i = 0; i < expected.Count; i++) Assert.AreEqual(expected[i], list[i], "rank " + i);
                Assert.AreEqual(129, sorted.Count);
                for (int i = 0; i < 129; i++)
                { sorted.TryGetAt(i, out var pair).ThrowIfFailure(); Assert.AreEqual(i * 2, pair.Key); Assert.AreEqual(-i, pair.Value); }
            }
        }

        [TestCase(0)] [TestCase(3072)] [TestCase(4095)]
        public void NativeSwapBackClonesOnlyWrittenPagesAndRestoresCanonicalValues(int index)
        {
            using (var list = new NativeDeterministicList<int>(
                new BufferOptions(4096, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 4096), Allocator.Persistent))
            {
                for (int i = 0; i < 4096; i++) list.Add(i);
                list.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    int* before = stackalloc int[4]; for (int i = 0; i < 4; i++) before[i] = list.Storage.Data->Pages[i];
                    list.TryRemoveAtSwapBack(index).ThrowIfFailure();
                    for (int page = 0; page < 4; page++)
                        Assert.AreEqual(page != index / 1024 && page != 3, before[page] == list.Storage.Data->Pages[page], "page " + page);
                    Assert.AreEqual(4095, list.Count);
                    list.TryRestore(snapshot).ThrowIfFailure();
                    for (int i = 0; i < 4096; i++) Assert.AreEqual(i, list[i]);
                }
                finally { snapshot.Dispose(); }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick, 0)] [TestCase(SnapshotPolicy.FullPerTick, 4)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, 0)] [TestCase(SnapshotPolicy.KeyframesAndDeltas, 4)]
        public void NativeGrowBackingPreservesSnapshotsWhenAddingSlotsAndReusingLeases(SnapshotPolicy policy, int initialCapacity)
        {
            var storage = NativeStorage<int>.Create(new BufferOptions(initialCapacity, 1048576, AllocationPolicy.Grow, policy, 2), Allocator.Persistent);
            try
            {
                Assert.AreEqual(0, PayloadBytes(ref storage));
                Assert.AreEqual(initialCapacity * sizeof(int), storage.MemoryStats.ReservedPayloadBytes);
                storage.TryCapture(out int emptySlot, out ulong emptyLease).ThrowIfFailure();
                storage.PrepareWrite(37, 0, 37).ThrowIfFailure();
                for (int i = 0; i < 37; i++) storage.Write(i, i * 7);
                storage.TryCapture(out int retainedSlot, out ulong retainedLease).ThrowIfFailure();
                long reserved = ((long)initialCapacity + storage.Capacity) * sizeof(int); Assert.AreEqual(reserved, PayloadBytes(ref storage));
                storage.TryReserveSnapshotSlots(4).ThrowIfFailure(); Assert.AreEqual(reserved, PayloadBytes(ref storage));
                Assert.AreEqual(reserved + storage.Data->PhysicalCapacity * sizeof(int), storage.MemoryStats.ReservedPayloadBytes);
                for (int i = 0; i < 37; i++) Assert.AreEqual(i * 7, storage.ReadSnapshot(retainedSlot, i));
                storage.TryRestore(emptySlot, emptyLease).ThrowIfFailure(); Assert.AreEqual(initialCapacity, storage.Capacity);
                storage.ReleaseSnapshot(emptySlot, emptyLease);
                storage.PrepareWrite(129, 0, 129).ThrowIfFailure();
                for (int i = 0; i < 129; i++) storage.Write(i, -i);
                storage.TryCapture(out int replacementSlot, out ulong replacementLease).ThrowIfFailure();
                Assert.AreEqual(emptySlot, replacementSlot);
                storage.ReleaseSnapshot(emptySlot, emptyLease); Assert.IsTrue(storage.ValidSnapshot(replacementSlot, replacementLease));
                storage.TryRestore(retainedSlot, retainedLease).ThrowIfFailure();
                for (int i = 0; i < 37; i++) Assert.AreEqual(i * 7, storage.Read(i));
                storage.ReleaseSnapshot(retainedSlot, retainedLease); storage.ReleaseSnapshot(replacementSlot, replacementLease);
            }
            finally { storage.Destroy(); }
        }
        private static long PayloadBytes(ref NativeStorage<int> storage)
        {
            long bytes = 0;
            for (int i = 0; i < storage.Data->Options.SnapshotSlots; i++) bytes += (long)storage.Data->GrowSnapshotCapacities[i] * sizeof(int);
            return bytes;
        }
    }
}

