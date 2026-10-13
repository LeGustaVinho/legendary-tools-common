using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NativeImmutableGraphTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct FreezeJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            internal NativeFrozenDeterministicGraph<int, int> Frozen, Imported;
            internal NativeArray<byte> Bytes, Scratch;
            internal NativeArray<ulong> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = (ulong)managed;
                var a = Graph.AddNode(10); var b = Graph.AddNode(20); var c = Graph.AddNode(30);
                var edge = Graph.AddEdge(a, c, 7); Graph.TryRemoveNode(b); Graph.TryFreeze(ref Frozen);
                Graph.TrySetNode(a, 100);
                Frozen.AsReadOnly().TryGetNode(a, out int old); Results[1] = (ulong)old;
                var writer = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Bytes), Bytes.Length);
                Results[2] = (ulong)Frozen.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy));
                var reader = new DeterministicReader(writer.Buffer, writer.Position); var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                Results[3] = (ulong)Imported.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy));
                Imported.AsReadOnly().TryGetEdge(edge, out var restored); Results[4] = (ulong)restored.Data;
                var arcs = Imported.AsReadOnly().Outgoing(a); Results[5] = arcs.MoveNext() && arcs.Current.Neighbor == c ? 1UL : 0UL;
                Results[6] = (ulong)Imported.NodeCount;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void FrozenCsrAndCanonicalImportExecuteInBurst()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled); var options = new GraphOptions(4, 8);
            using var g = new NativeDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var frozen = new NativeFrozenDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var imported = new NativeFrozenDeterministicGraph<int, int>(options, Allocator.Persistent);
            using var bytes = new NativeArray<byte>(8192, Allocator.TempJob); using var scratch = new NativeArray<byte>(8192, Allocator.TempJob); using var results = new NativeArray<ulong>(7, Allocator.TempJob);
            new FreezeJob { Graph = g, Frozen = frozen, Imported = imported, Bytes = bytes, Scratch = scratch, Results = results }.Schedule().Complete();
            CollectionAssert.AreEqual(new ulong[] { 0, 10, 0, 0, 7, 1, 2 }, results.ToArray());
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct VersionJob : IJob
        {
            internal NativePersistentDeterministicGraph<int, int> Store;
            internal NativeArray<ulong> Results;
            public void Execute()
            {
                Results[16] = 1234;
                int managed = 0; MarkManaged(ref managed); Results[0] = (ulong)managed;
                Results[1] = (ulong)Store.TryCaptureCurrent(out var empty);
                Results[2] = (ulong)Store.TryWithAddNode(empty, 10, out var basis, out var node);
                Results[3] = (ulong)Store.TryWithSetNode(basis, node, 20, out var left);
                Results[4] = (ulong)Store.TryWithSetNode(basis, node, 30, out var right);
                Results[10] = basis.IsCreated ? 1UL : 0; Results[11] = node.Value.Packed; Results[12] = (ulong)basis.AsReadOnly().NodeCount;
                Results[13] = (ulong)basis.AsReadOnly().TryGetNode(node, out int value); Results[5] = (ulong)value;
                Results[6] = (ulong)Store.TrySelectCurrent(left); left.Dispose(); Results[14] = (ulong)Store.Current.TryGetNode(node, out value); Results[7] = (ulong)value;
                Results[15] = (ulong)right.AsReadOnly().TryGetNode(node, out value); Results[8] = (ulong)value;
                empty.Dispose(); basis.Dispose(); right.Dispose(); Results[9] = (ulong)Store.VersionCount;
                Results[17] = 5678;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void PersistentVersionsBranchAndReleaseInsideBurst()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using var store = new NativePersistentDeterministicGraph<int, int>(new GraphOptions(4, 8), new GraphPersistenceOptions(6), Allocator.Persistent);
            using var results = new NativeArray<ulong>(18, Allocator.TempJob);
            var resultAccess = results; for (int i = 0; i < resultAccess.Length; i++) resultAccess[i] = 999;
            new VersionJob { Store = store, Results = results }.Schedule().Complete();
            CollectionAssert.AreEqual(new ulong[] { 0, 0, 0, 0, 0, 10, 0, 20, 30, 0, 1, new StableHandle(0, 1).Packed, 1, 0, 0, 0, 1234, 5678 }, results.ToArray(), string.Join(",", results.ToArray())); Assert.AreEqual(1, store.Current.NodeCount);
        }
        [TestCase(GraphVersionRetention.Leases)]
        [TestCase(GraphVersionRetention.UntilOwnerDisposed)]
        public void HostExpansionKeepsOldNativeLeasesAlive(GraphVersionRetention retention)
        {
            using var store = new NativePersistentDeterministicGraph<int, int>(new GraphOptions(3, 4), new GraphPersistenceOptions(1, 4, AllocationPolicy.Grow, retention), Allocator.Persistent);
            store.TryCaptureCurrent(out var initial).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, store.TryWithAddNode(initial, 1, out _, out _));
            store.TryReserveVersionCapacity(3).ThrowIfFailure(); Assert.AreEqual(4, store.VersionCapacity); Assert.IsTrue(initial.IsCreated);
            store.TryWithAddNode(initial, 42, out var version, out var node).ThrowIfFailure(); version.AsReadOnly().TryGetNode(node, out int value).ThrowIfFailure(); Assert.AreEqual(42, value);
            var copy = version; version.Dispose(); initial.Dispose(); Assert.AreEqual(retention == GraphVersionRetention.UntilOwnerDisposed, copy.IsCreated);
        }
    }
}
