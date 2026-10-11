using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeHashSetTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SetJob : IJob
        {
            internal NativeDeterministicHashSet<int, Int32Policy> Set;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                Set.Add(1); Set.Add(2); Set.Add(3); Results[1] = (int)Set.TryAdd(2);
                Set.TryCapture(out var snapshot); Set.Remove(2); Set.Add(2);
                int encoded = 0; foreach (int value in Set) encoded = encoded * 10 + value; Results[2] = encoded;
                Set.TryRestore(snapshot); snapshot.Dispose();
                encoded = 0; foreach (int value in Set) encoded = encoded * 10 + value; Results[3] = encoded;
                Results[4] = Set.Contains(2) ? 1 : 0;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void NativeHashSetPreservesInsertionOrderThroughRollbackInBurst(SnapshotPolicy snapshots)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var set = new NativeDeterministicHashSet<int, Int32Policy>(new BufferOptions(4, snapshots: snapshots), Allocator.Persistent))
            using (var results = new NativeArray<int>(5, Allocator.TempJob))
            {
                new SetJob { Set = set, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new[] { 0, (int)DeterministicStatus.DuplicateKey, 132, 123, 1 }, results.ToArray());
            }
        }
    }
}
