using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class GraphPersistentDeterminismValidationTests
    {
        private struct Root { internal ManagedGraphVersion<int, int> Version; internal byte[] Bytes; }
        private static byte[] Encode(ManagedGraphVersion<int, int> version)
        {
            var bytes = new byte[8192]; int length;
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, bytes.Length); version.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); length = writer.Position; }
            Array.Resize(ref bytes, length); return bytes;
        }
        private static byte[] Encode(PersistentDeterministicGraph<int, int> store)
        {
            var bytes = new byte[8192]; int length;
            fixed (byte* p = bytes) { var writer = new DeterministicWriter(p, bytes.Length); store.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); length = writer.Position; }
            Array.Resize(ref bytes, length); return bytes;
        }
        [TestCase(GraphDirection.Directed, GraphVersionRetention.Leases)]
        [TestCase(GraphDirection.Undirected, GraphVersionRetention.Leases)]
        [TestCase(GraphDirection.Directed, GraphVersionRetention.UntilOwnerDisposed)]
        [TestCase(GraphDirection.Undirected, GraphVersionRetention.UntilOwnerDisposed)]
        public void SeededBranchesRemainImmutableAcrossSelectionExpansionAndLeaseReuse(GraphDirection direction, GraphVersionRetention retention)
        {
            var options = new GraphOptions(new BufferOptions(0, 16, AllocationPolicy.Grow), new BufferOptions(0, 32, AllocationPolicy.Grow),
                direction, allowParallelEdges: true, allowSelfEdges: true);
            using var store = new PersistentDeterministicGraph<int, int>(options, new GraphPersistenceOptions(4, 256, AllocationPolicy.Grow, retention));
            store.TryCaptureCurrent(out var initial).ThrowIfFailure(); var selected = Encode(store);
            var roots = new List<Root> { new Root { Version = initial, Bytes = selected } };
            var scratchBytes = new byte[131072]; var outputBytes = new byte[8192]; uint random = 0x97e21;
            ManagedGraphVersion<int, int> retired = default; int publications = 0, rejected = 0;
            for (int step = 0; step < 128; step++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                var basis = roots[(int)(random % (uint)roots.Count)];
                using var oracle = new DeterministicGraph<int, int>(options);
                fixed (byte* b = basis.Bytes) fixed (byte* p = scratchBytes)
                { var reader = new DeterministicReader(b, basis.Bytes.Length); var work = new CanonicalScratch(p, scratchBytes.Length); oracle.TryReadCanonical(ref reader, ref work, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); }
                var nodes = new List<GraphNodeHandle>(); foreach (var node in oracle.Nodes) nodes.Add(node.Handle);
                var mutation = new GraphMutation<int, int> { NodeData = step * 123, EdgeData = step };
                mutation.Kind = nodes.Count == 0 ? GraphMutationKind.AddNode : new[] { GraphMutationKind.AddNode, GraphMutationKind.SetNode, GraphMutationKind.AddEdge, GraphMutationKind.RemoveNode }[step % 4];
                if (nodes.Count != 0) { mutation.Node = nodes[0]; mutation.Source = nodes[0]; mutation.Target = nodes[nodes.Count - 1]; }
                if (step % 19 == 18) { mutation.Kind = GraphMutationKind.RemoveNode; mutation.Node = new GraphNodeHandle(new StableHandle(999, 1)); }
                var expected = oracle.TryApply(mutation, out var expectedResult);
                if (store.VersionCount == store.VersionCapacity) store.TryReserveVersionCapacity(store.VersionCount + 1).ThrowIfFailure();
                int versionCount = store.VersionCount;
                var actual = store.TryWith(basis.Version, mutation, out var version, out var result);
                Assert.AreEqual(expected, actual); Assert.AreEqual(expectedResult.Node, result.Node); Assert.AreEqual(expectedResult.Edge, result.Edge);
                retired.Dispose(); CollectionAssert.AreEqual(basis.Bytes, Encode(basis.Version)); CollectionAssert.AreEqual(selected, Encode(store));
                if (actual.IsSuccess())
                {
                    Assert.IsTrue(version.IsCreated); int length;
                    fixed (byte* p = outputBytes) { var writer = new DeterministicWriter(p, outputBytes.Length); oracle.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure(); length = writer.Position; }
                    var expectedBytes = new byte[length]; Array.Copy(outputBytes, expectedBytes, length); CollectionAssert.AreEqual(expectedBytes, Encode(version));
                    roots.Add(new Root { Version = version, Bytes = expectedBytes }); publications++;
                    if (step % 7 == 0) { store.TrySelectCurrent(version).ThrowIfFailure(); selected = expectedBytes; }
                    if (retention == GraphVersionRetention.Leases && roots.Count > 8)
                    { retired = roots[0].Version; retired.Dispose(); roots.RemoveAt(0); Assert.IsFalse(retired.IsCreated); }
                }
                else { Assert.AreEqual(versionCount, store.VersionCount); Assert.IsFalse(version.IsCreated); rejected++; }
                foreach (var root in roots) { Assert.IsTrue(root.Version.IsCreated); CollectionAssert.AreEqual(root.Bytes, Encode(root.Version)); }
                CollectionAssert.AreEqual(selected, Encode(store));
            }
            Assert.Greater(publications, 32); Assert.Greater(rejected, 0); Assert.AreEqual(roots.Count, store.VersionCount);
            store.Dispose(); foreach (var root in roots) Assert.IsFalse(root.Version.IsCreated);
            TestContext.WriteLine($"published={publications}; rejected={rejected}; retention={retention}");
        }
    }
}
