using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeDictionaryTests
    {
        private struct CollisionPolicy : IDeterministicEquality<int>
        {
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int value) => 0;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct MapJob : IJob
        {
            internal NativeDeterministicDictionary<int, int, CollisionPolicy> Map;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                for (int i = 0; i < 16; i++) { var status = Map.TryAdd(i, i * 2); if (!status.IsSuccess()) { Results[1] = (int)status; return; } }
                Map.TryCapture(out var snapshot); Map.TrySet(2, 999); Map.TryRemove(1); Map.TryAdd(1, 88);
                int position = 0;
                foreach (var pair in Map) Results[3 + position++] = pair.Key;
                Map.TryRestore(snapshot); snapshot.Dispose(); Results[2] = Map[2];
                int sum = 0; foreach (var pair in Map) sum += pair.Value; Results[19] = sum;
                Map.TryRemove(0); Map.TryRemove(8); Map.TryRemove(15);
                Map.TryAdd(20, 20); Map.TryAdd(21, 21); Map.TryAdd(22, 22);
                Results[20] = Map.Count; Results[21] = (int)Map.TryAdd(2, 4);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void NativeDictionaryUsesSharedCollisionAndOrderingKernelInBurst(SnapshotPolicy snapshots)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var map = new NativeDeterministicDictionary<int, int, CollisionPolicy>(new BufferOptions(1, 17, AllocationPolicy.Grow, snapshots, 2, 64), Allocator.Persistent))
            using (var results = new NativeArray<int>(22, Allocator.TempJob))
            {
                new MapJob { Map = map, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0]); Assert.AreEqual(0, results[1]); Assert.AreEqual(4, results[2]);
                Assert.AreEqual(0, results[3]); for (int i = 1; i < 15; i++) Assert.AreEqual(i + 1, results[3 + i]);
                Assert.AreEqual(1, results[18]); Assert.AreEqual(240, results[19]); Assert.AreEqual(16, results[20]);
                Assert.AreEqual((int)DeterministicStatus.DuplicateKey, results[21]);
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 20, 21, 22 }, Keys(map));
            }
        }
        private static int[] Keys(NativeDeterministicDictionary<int, int, CollisionPolicy> map)
        { var keys = new int[map.Count]; int index = 0; foreach (var pair in map) keys[index++] = pair.Key; return keys; }
    }
}
