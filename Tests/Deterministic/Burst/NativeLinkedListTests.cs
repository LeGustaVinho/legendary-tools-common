using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeLinkedListTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct LinkJob : IJob
        {
            internal NativeDeterministicLinkedList<int> List;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                var a = List.AddLast(10); List.AddFirst(5); var b = List.AddAfter(a, 20); List.AddBefore(b, 15);
                List.TryCapture(out var snapshot); List.Clear(); List.TryRestore(snapshot); snapshot.Dispose();
                List.Remove(a); var replacement = List.AddAfter(List.First, 11);
                int i = 1; foreach (var value in List) Results[i++] = value;
                Results[5] = (int)List.TryGet(a, out _); Results[6] = (int)replacement.Generation;
                List.TryGetLinks(replacement, out var previous, out var next); Results[7] = List[previous]; Results[8] = List[next];
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void GenerationalLinkedNodesExecuteInsideBurst(SnapshotPolicy snapshots)
        {
            using (var list = new NativeDeterministicLinkedList<int>(new BufferOptions(0, 5, AllocationPolicy.Grow, snapshots, 2, 64), Allocator.Persistent))
            using (var results = new NativeArray<int>(9, Allocator.TempJob))
            {
                new LinkJob { List = list, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new[] { 0, 5, 11, 15, 20, (int)DeterministicStatus.NotFound, 2, 5, 15 }, results.ToArray());
            }
        }
    }
}
