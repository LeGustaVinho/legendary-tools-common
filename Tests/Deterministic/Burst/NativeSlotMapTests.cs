using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeSlotMapTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SlotJob : IJob
        {
            internal NativeDeterministicSlotMap<int> Map;
            internal NativeArray<ulong> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = (ulong)managed;
                var a = Map.Add(10); var b = Map.Add(20); Map.Add(30); Map.Remove(b); Map.Remove(a);
                Map.TryCapture(out var snapshot); var next = Map.Add(40); Map.Add(50);
                Map.Clear(); Map.TryRestore(snapshot); snapshot.Dispose();
                var again = Map.Add(40); Results[1] = next.Packed; Results[2] = again.Packed;
                Results[3] = Map.Add(50).Packed; Results[4] = Map.ContainsKey(a) ? 1UL : 0UL;
                ulong sum = 0; foreach (var item in Map) sum += (ulong)item.Value; Results[5] = sum;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void GenerationsAndFifoReuseMatchManagedInsideBurst(SnapshotPolicy snapshots)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var map = new NativeDeterministicSlotMap<int>(new BufferOptions(0, 5, AllocationPolicy.Grow, snapshots, 2, 64), Allocator.Persistent))
            using (var results = new NativeArray<ulong>(6, Allocator.TempJob))
            {
                new SlotJob { Map = map, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new ulong[] { 0, new StableHandle(1, 2).Packed, new StableHandle(1, 2).Packed, new StableHandle(0, 2).Packed, 0, 120 }, results.ToArray());
                Assert.AreEqual(3, map.Count);
            }
        }
    }
}
