using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicUncommonUseCase")]
    public sealed unsafe class UncommonCollectionUseCaseTests
    {
        private static BufferOptions Options(SnapshotPolicy policy, int capacity) =>
            new BufferOptions(capacity, snapshots: policy, snapshotSlots: 2, pageBytes: 64);

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CyclicDependencyTraversalTerminatesOncePerVertexAfterGraphRollback(SnapshotPolicy policy)
        {
            using (var dependencies = new DeterministicMultiMap<int, int, Int32Policy>(Options(policy, 8), Options(policy, 16)))
            using (var pending = new DeterministicQueue<int>(Options(policy, 8)))
            using (var visited = new DeterministicBitSet(130, policy, 2))
            {
                dependencies.Add(0, 64); dependencies.Add(0, 64); dependencies.Add(0, 63);
                var removed = dependencies.Add(64, 129); dependencies.Add(129, 0);
                dependencies.Add(63, 128); dependencies.Add(128, 64); dependencies.Add(129, 129);
                dependencies.TryCapture(out var graph).ThrowIfFailure();
                try
                {
                    CollectionAssert.AreEqual(new[] { 0, 64, 63, 129, 128 }, Traverse(dependencies, pending, visited));
                    Assert.AreEqual(5, visited.Count); Assert.AreEqual(0, pending.Count);
                    Assert.IsTrue(dependencies.RemoveAll(64));
                    CollectionAssert.AreEqual(new[] { 0, 64, 63, 128 }, Traverse(dependencies, pending, visited));
                    var next = dependencies.Add(64, 127); Assert.AreEqual(new StableHandle(removed.Index, 2), next);
                    dependencies.TryRestore(graph).ThrowIfFailure();
                    CollectionAssert.AreEqual(new[] { 0, 64, 63, 129, 128 }, Traverse(dependencies, pending, visited));
                    dependencies.TryGetPair(removed, out var pair).ThrowIfFailure(); Assert.AreEqual(129, pair.Value);
                    Assert.IsTrue(dependencies.RemoveAll(64)); Assert.AreEqual(next, dependencies.Add(64, 127));
                    CollectionAssert.AreEqual(new[] { 0, 64, 63, 127, 128 }, Traverse(dependencies, pending, visited));
                }
                finally { graph.Dispose(); }
            }
        }

        private static List<int> Traverse(DeterministicMultiMap<int, int, Int32Policy> graph,
            DeterministicQueue<int> pending, DeterministicBitSet visited)
        {
            pending.Clear(); visited.Clear(); pending.Enqueue(0); visited[0] = true;
            var order = new List<int>();
            while (pending.TryDequeue(out int vertex).IsSuccess())
            {
                order.Add(vertex);
                foreach (int next in graph[vertex]) if (!visited[next]) { visited[next] = true; pending.Enqueue(next); }
                Assert.LessOrEqual(order.Count, visited.Length, "A cycle must not cause repeated work.");
            }
            return order;
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void UndoRedoBranchRecyclesNodesAndCheckpointRestoresOriginalFuture(SnapshotPolicy policy)
        {
            using (var actions = new DeterministicLinkedList<int>(Options(policy, 8)))
            using (var loaded = new DeterministicLinkedList<int>(Options(policy, 8)))
            {
                var a = actions.AddLast(10); var b = actions.AddLast(20);
                var c = actions.AddLast(-5); var d = actions.AddLast(15);
                actions.TryCapture(out var original).ThrowIfFailure();
                byte* packet = stackalloc byte[8192]; byte* work = stackalloc byte[16384];
                var writer = new DeterministicWriter(packet, 8192); actions.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                try
                {
                    // Undo twice; a new edit discards the redo suffix, newest node first.
                    actions.Remove(d); actions.Remove(c);
                    var firstBranch = actions.AddAfter(b, 7); var secondBranch = actions.AddLast(3);
                    Assert.AreEqual(new StableHandle(3, 2), firstBranch); Assert.AreEqual(new StableHandle(2, 2), secondBranch);
                    Assert.AreEqual(40, Sum(actions)); CollectionAssert.AreEqual(new[] { 10, 20, 7, 3 }, actions);
                    Assert.AreEqual(DeterministicStatus.NotFound, actions.TrySet(c, 100));
                    actions.TryRestore(original).ThrowIfFailure();
                    CollectionAssert.AreEqual(new[] { 10, 20, -5, 15 }, actions); Assert.AreEqual(40, Sum(actions));
                    Assert.AreEqual(a, actions.First); Assert.AreEqual(d, actions.Last);
                    var reader = new DeterministicReader(packet, writer.Position); var scratch = new CanonicalScratch(work, 16384);
                    loaded.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)).ThrowIfFailure();
                    Assert.AreEqual(writer.Position, reader.Position); Assert.AreEqual(-5, loaded[c]);
                    foreach (var list in new[] { actions, loaded })
                    {
                        list.Remove(d); list.Remove(c);
                        Assert.AreEqual(firstBranch, list.AddAfter(b, 7)); Assert.AreEqual(secondBranch, list.AddLast(3));
                        list.TryGetLinks(firstBranch, out var previous, out var next).ThrowIfFailure();
                        Assert.AreEqual(b, previous); Assert.AreEqual(secondBranch, next);
                    }
                }
                finally { original.Dispose(); }
            }
        }

        private static int Sum(DeterministicLinkedList<int> list)
        { int total = 0; foreach (int value in list) total += value; return total; }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void OrderedComponentJoinIsStableAcrossOppositeDenseHistories(SnapshotPolicy policy)
        {
            using (var left = new DeterministicSparseSet<int>(Options(policy, 6), Options(policy, 130)))
            using (var right = new DeterministicSparseSet<int>(Options(policy, 6), Options(policy, 130)))
            using (var modifiers = new DeterministicSparseSet<int>(Options(policy, 6), Options(policy, 130)))
            {
                uint[] indices = { 129, 0, 64, 63, 127 };
                for (int i = 0; i < indices.Length; i++)
                { left.Add(new StableHandle(indices[i], 1), (int)indices[i]); right.Add(new StableHandle(indices[indices.Length - i - 1], 1), (int)indices[indices.Length - i - 1]); }
                var old = new StableHandle(64, 1); var replacement = new StableHandle(64, 2);
                left.Remove(old); right.Remove(old); left.Add(replacement, 640); right.Add(replacement, 640);
                modifiers.Add(new StableHandle(129, 1), 3); modifiers.Add(replacement, 2); modifiers.Add(new StableHandle(0, 1), 1);
                var denseLeft = new List<uint>(); foreach (var item in left) denseLeft.Add(item.Handle.Index);
                var denseRight = new List<uint>(); foreach (var item in right) denseRight.Add(item.Handle.Index);
                CollectionAssert.AreNotEqual(denseLeft, denseRight);
                var expected = new[] { 1, 6402, 1293 }; var orderedHandles = new[] { new StableHandle(0, 1), replacement, new StableHandle(129, 1) };
                foreach (var components in new[] { left, right })
                {
                    int rank = 0, digest = 0;
                    foreach (var item in components.OrderedByEntity) if (modifiers.TryGetValue(item.Handle, out int modifier))
                    {
                        Assert.AreEqual(orderedHandles[rank], item.Handle);
                        int result = item.Value * 10 + modifier; Assert.AreEqual(expected[rank++], result);
                        digest = unchecked(digest * 31 + result);
                    }
                    Assert.AreEqual(3, rank); Assert.AreEqual(unchecked((1 * 31 + 6402) * 31 + 1293), digest);
                    Assert.IsFalse(components.ContainsKey(old));
                }
            }
        }
    }
}
