using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeSparseSetTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SparseJob : IJob
        {
            internal NativeDeterministicSparseSet<int> Set;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                Set.Add(new StableHandle(9, 1), 90); Set.Add(new StableHandle(2, 5), 20); Set.Add(new StableHandle(7, 2), 70);
                Set.TryCapture(out var snapshot); Set.Remove(new StableHandle(2, 5)); Set.Add(new StableHandle(3, 1), 30);
                Set.TryRestore(snapshot); snapshot.Dispose();
                Set.Remove(new StableHandle(2, 5)); Set.Add(new StableHandle(2, 6), 21);
                int i = 1; foreach (var item in Set) Results[i++] = (int)item.Handle.Index;
                foreach (var item in Set.OrderedByEntity) Results[i++] = (int)item.Handle.Index;
                Results[7] = (int)Set.TrySet(new StableHandle(2, 5), 99); Results[8] = Set[new StableHandle(2, 6)];
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SparseLookupAndBothIterationOrdersExecuteInBurst(SnapshotPolicy snapshots)
        {
            var dense = new BufferOptions(0, 5, AllocationPolicy.Grow, snapshots, 2, 64);
            var sparse = new BufferOptions(0, 13, AllocationPolicy.Grow, snapshots, 2, 64);
            using (var set = new NativeDeterministicSparseSet<int>(dense, sparse, Allocator.Persistent))
            using (var results = new NativeArray<int>(9, Allocator.TempJob))
            {
                new SparseJob { Set = set, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new[] { 0, 9, 7, 2, 2, 7, 9, (int)DeterministicStatus.ProtocolConflict, 21 }, results.ToArray()); Assert.AreEqual(3, set.Count);
            }
        }
    }
}
