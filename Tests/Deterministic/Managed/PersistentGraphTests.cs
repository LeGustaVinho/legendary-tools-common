using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class PersistentGraphTests
    {
        [Test]
        public void BranchesShareRootsWithoutChangingTheirBasisOrTheSelectedState()
        {
            using var store = new PersistentDeterministicGraph<int, int>(new GraphOptions(4, 8), new GraphPersistenceOptions(6));
            store.TryCaptureCurrent(out var empty).ThrowIfFailure();
            store.TryWithAddNode(empty, 10, out var basis, out var node).ThrowIfFailure();
            store.TryWithSetNode(basis, node, 20, out var left).ThrowIfFailure();
            store.TryWithSetNode(basis, node, 30, out var right).ThrowIfFailure();
            Assert.AreEqual(0, store.Current.NodeCount);
            basis.AsReadOnly().TryGetNode(node, out int b).ThrowIfFailure(); left.AsReadOnly().TryGetNode(node, out int l).ThrowIfFailure(); right.AsReadOnly().TryGetNode(node, out int r).ThrowIfFailure();
            Assert.AreEqual(10, b); Assert.AreEqual(20, l); Assert.AreEqual(30, r);
            store.TrySelectCurrent(left).ThrowIfFailure(); left.Dispose();
            store.Current.TryGetNode(node, out l).ThrowIfFailure(); Assert.AreEqual(20, l);
            var id = right.Id; var copy = right; right.Dispose(); Assert.IsFalse(copy.IsCreated);
            store.TryWithSetNode(basis, node, 40, out var newer).ThrowIfFailure(); copy.Dispose(); Assert.IsTrue(newer.IsCreated); Assert.AreNotEqual(id, newer.Id);
            empty.Dispose(); basis.Dispose(); newer.Dispose();
        }
        [TestCase(GraphVersionRetention.Leases)]
        [TestCase(GraphVersionRetention.UntilOwnerDisposed)]
        public void HostExpansionPreservesExistingVersionLeases(GraphVersionRetention retention)
        {
            using var store = new PersistentDeterministicGraph<int, int>(new GraphOptions(3, 4), new GraphPersistenceOptions(1, 4, AllocationPolicy.Grow, retention));
            store.TryCaptureCurrent(out var initial).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, store.TryWithAddNode(initial, 10, out _, out _));
            store.TryReserveVersionCapacity(3).ThrowIfFailure(); Assert.AreEqual(4, store.VersionCapacity); Assert.IsTrue(initial.IsCreated);
            store.TryWithAddNode(initial, 10, out var version, out var node).ThrowIfFailure();
            initial.Dispose(); Assert.AreEqual(retention == GraphVersionRetention.UntilOwnerDisposed, initial.IsCreated);
            version.AsReadOnly().TryGetNode(node, out int value).ThrowIfFailure(); Assert.AreEqual(10, value);
            Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, store.TryReserveVersionCapacity(5)); version.Dispose();
        }
        [Test]
        public void FailedBatchDoesNotPublishPartOfATopologyOrConsumeFutureGraphIds()
        {
            using var store = new PersistentDeterministicGraph<int, int>(new GraphOptions(1, 1), new GraphPersistenceOptions(3)); store.TryCaptureCurrent(out var empty).ThrowIfFailure();
            var ops = new[] { new GraphMutation<int, int> { Kind = GraphMutationKind.AddNode, NodeData = 1 }, new GraphMutation<int, int> { Kind = GraphMutationKind.AddNode, NodeData = 2 } };
            fixed (GraphMutation<int, int>* p = ops)
            { var buffer = new GraphWorkBuffer<GraphMutation<int, int>>(p, ops.Length); Assert.AreEqual(DeterministicStatus.CapacityExceeded, store.TryWithBatch(empty, ref buffer, 2, out var failed)); Assert.IsFalse(failed.IsCreated); }
            Assert.AreEqual(1, store.VersionCount); Assert.AreEqual(0, store.Current.NodeCount); Assert.AreEqual(0, empty.AsReadOnly().NodeCount);
            store.TryWithAddNode(empty, 9, out var good, out var h).ThrowIfFailure(); Assert.AreEqual(new StableHandle(0, 1), h.Value); good.Dispose(); empty.Dispose();
        }
        [Test]
        public void SelectedRootUsesTheSameCanonicalFormatAsMutableGraphs()
        {
            var options = new GraphOptions(3, 4); using var store = new PersistentDeterministicGraph<int, int>(options, new GraphPersistenceOptions(3)); using var mutable = new DeterministicGraph<int, int>(options);
            store.TryCaptureCurrent(out var empty).ThrowIfFailure(); store.TryWithAddNode(empty, 5, out var populated, out var node).ThrowIfFailure(); store.TrySelectCurrent(populated).ThrowIfFailure(); mutable.AddNode(5);
            var first = new byte[4096]; var second = new byte[4096]; var scratchBytes = new byte[8192];
            fixed (byte* p = first) fixed (byte* q = second) fixed (byte* w = scratchBytes)
            {
                var a = new DeterministicWriter(p, first.Length); var b = new DeterministicWriter(q, second.Length);
                store.TryWriteCanonical(ref a, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); mutable.TryWriteCanonical(ref b, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(a.Position, b.Position); Assert.AreEqual(DeterministicHash.Xxh64(p, a.Position), DeterministicHash.Xxh64(q, b.Position));
                mutable.TrySetNode(node, 50).ThrowIfFailure(); b = new DeterministicWriter(q, second.Length); mutable.TryWriteCanonical(ref b, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                var reader = new DeterministicReader(q, b.Position); var scratch = new CanonicalScratch(w, scratchBytes.Length); store.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
            }
            store.Current.TryGetNode(node, out int value).ThrowIfFailure(); Assert.AreEqual(50, value); populated.AsReadOnly().TryGetNode(node, out value).ThrowIfFailure(); Assert.AreEqual(5, value);
            populated.Dispose(); empty.Dispose();
        }
    }
}
