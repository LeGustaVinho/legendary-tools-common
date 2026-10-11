using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeSortedMapTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SortJob : IJob
        {
            internal NativeDeterministicSortedDictionary<int, int, Int32Policy> Tree;
            internal NativeDeterministicSortedList<int, int, Int32Policy> List;
            internal NativeDeterministicSortedSet<int, Int32Policy> Set;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                for (int i = 30; i >= 0; i--) { Tree.Add(i, i * 10); List.Add(i, i * 10); Set.Add(i); }
                Tree.TryCapture(out var ts); List.TryCapture(out var ls); Set.TryCapture(out var ss);
                Tree.Clear(); List.Clear(); Set.Clear(); Tree.TryRestore(ts); List.TryRestore(ls); Set.TryRestore(ss); ts.Dispose(); ls.Dispose(); ss.Dispose();
                for (int i = 0; i < 31; i += 3) { Tree.Remove(i); List.Remove(i); Set.Remove(i); }
                int result = 1; foreach (var entry in Tree) Results[result++] = entry.Key;
                int failed = 0;
                for (int i = 0; i < Tree.Count; i++) { Tree.TryGetAt(i, out var a); List.TryGetAt(i, out var b); if (a.Key != b.Key || a.Value != b.Value) failed++; }
                int count = 0; foreach (var item in Set) { if (Results[1 + count] != item) failed++; count++; }
                Results[21] = failed; Results[22] = Tree.Height;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void BinarySearchAndAvlCollectionsExecuteInBurst(SnapshotPolicy snapshots)
        {
            var options = new BufferOptions(0, 37, AllocationPolicy.Grow, snapshots, 2, 64);
            using (var tree = new NativeDeterministicSortedDictionary<int, int, Int32Policy>(options, Allocator.Persistent))
            using (var list = new NativeDeterministicSortedList<int, int, Int32Policy>(options, Allocator.Persistent))
            using (var set = new NativeDeterministicSortedSet<int, Int32Policy>(options, Allocator.Persistent))
            using (var results = new NativeArray<int>(23, Allocator.TempJob))
            {
                new SortJob { Tree = tree, List = list, Set = set, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0]); Assert.AreEqual(0, results[21]); Assert.LessOrEqual(results[22], 6); Assert.AreEqual(20, tree.Count);
                int index = 1; for (int i = 0; i < 31; i++) if (i % 3 != 0) Assert.AreEqual(i, results[index++]);
            }
        }
    }
}
