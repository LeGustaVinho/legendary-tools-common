using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeMultiMapTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct MultiJob : IJob
        {
            internal NativeDeterministicMultiMap<int, int, Int32Policy> Map;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                Map.Add(3, 31); Map.Add(1, 11); var removed = Map.Add(3, 32); Map.Add(2, 21); Map.Add(1, 12); Map.Add(3, 33);
                Map.TryCapture(out var snapshot); Map.Clear(); Map.TryRestore(snapshot); snapshot.Dispose();
                Map.Remove(removed); Map.RemoveAll(1); var replacement = Map.Add(1, 13);
                int i = 1; foreach (var pair in Map) Results[i++] = pair.Value;
                int sum = 0; foreach (var value in Map[3]) sum += value; Results[5] = sum;
                Results[6] = (int)Map.TryGetPair(removed, out _); Results[7] = (int)replacement.Generation; Results[8] = Map.KeyCount; Results[9] = Map.Count;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void MultiMapKeyOrderAndValueLinksRunInsideBurst(SnapshotPolicy snapshots)
        {
            using (var map = new NativeDeterministicMultiMap<int, int, Int32Policy>(new BufferOptions(0, 5, AllocationPolicy.Grow, snapshots, 2, 64), new BufferOptions(0, 9, AllocationPolicy.Grow, snapshots, 2, 64), Allocator.Persistent))
            using (var results = new NativeArray<int>(10, Allocator.TempJob))
            {
                new MultiJob { Map = map, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new[] { 0, 31, 33, 21, 13, 64, (int)DeterministicStatus.NotFound, 2, 3, 4 }, results.ToArray());
            }
        }
    }
}
