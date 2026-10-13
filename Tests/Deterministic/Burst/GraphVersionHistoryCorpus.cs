using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public static unsafe class GraphVersionHistoryCorpus
    {
        public struct Result { public ulong Hash; public int Branches, RejectedBatches; }
        private const int Steps = 96, Stride = 4096;
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        [BurstCompile(CompileSynchronously = true)]
        private struct Job : IJob
        {
            public NativePersistentDeterministicGraph<int, int> Store;
            public NativeArray<byte> Bytes;
            public NativeArray<int> Lengths, Control;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Control[0] = managed; Control[2] = -1;
                var s = Store.TryCaptureCurrent(out var empty); if (!s.IsSuccess()) { Control[2] = 0; return; }
                s = Store.TryWithAddNode(empty, 42, out var initial, out var node); if (!s.IsSuccess()) { Control[2] = 1; return; }
                s = Store.TrySelectCurrent(initial); var initialId = initial.Id; empty.Dispose(); initial.Dispose();
                if (!s.IsSuccess()) { Control[2] = 2; return; }
                var stale = default(NativeGraphVersion<int, int>); int previous = 42, rejected = 0;
                byte* bytes = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Bytes);
                for (int i = 0; i < Steps; i++)
                {
                    s = Store.TryCaptureCurrent(out var basis); if (!s.IsSuccess()) { Control[2] = i + 10; return; }
                    stale.Dispose(); if (!basis.IsCreated) { Control[2] = i + 110; return; }
                    int value = unchecked(i * 65537 + 17);
                    s = Store.TryWithSetNode(basis, node, value, out var left); if (!s.IsSuccess()) { Control[2] = i + 210; return; }
                    s = Store.TryWithSetNode(basis, node, -value, out var right); if (!s.IsSuccess()) { Control[2] = i + 310; return; }
                    s = basis.AsReadOnly().TryGetNode(node, out int oldValue);
                    if (!s.IsSuccess() || oldValue != previous) { Control[2] = i + 410; return; }
                    GraphMutation<int, int>* mutations = stackalloc GraphMutation<int, int>[2];
                    mutations[0] = new GraphMutation<int, int> { Kind = GraphMutationKind.SetNode, Node = node, NodeData = int.MinValue };
                    mutations[1] = new GraphMutation<int, int> { Kind = GraphMutationKind.RemoveNode, Node = new GraphNodeHandle(new StableHandle(999, 1)) };
                    var batch = new GraphWorkBuffer<GraphMutation<int, int>>(mutations, 2); int before = Store.VersionCount;
                    s = Store.TryWithBatch(basis, ref batch, 2, out var failed);
                    if (s != DeterministicStatus.NotFound || failed.IsCreated || Store.VersionCount != before) { Control[2] = i + 510; return; }
                    rejected++;
                    s = Store.Current.TryGetNode(node, out int currentValue);
                    if (!s.IsSuccess() || currentValue != previous) { Control[2] = i + 610; return; }
                    s = Store.TrySelectCurrent(i % 2 == 0 ? left : right); if (!s.IsSuccess()) { Control[2] = i + 710; return; }
                    previous = i % 2 == 0 ? value : -value;
                    var writer = new DeterministicWriter(bytes + i * Stride, Stride);
                    s = Store.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)); Lengths[i] = writer.Position;
                    if (!s.IsSuccess()) { Control[2] = i + 810; return; }
                    stale = basis; basis.Dispose(); left.Dispose(); right.Dispose();
                    if (Store.Persistence.Retention == GraphVersionRetention.UntilOwnerDisposed)
                    {
                        s = Store.TryGetVersion(initialId, out var original);
                        if (!s.IsSuccess()) { Control[2] = i + 910; return; }
                        s = original.AsReadOnly().TryGetNode(node, out int originalValue);
                        if (!s.IsSuccess() || originalValue != 42) { Control[2] = i + 1010; return; }
                    }
                }
                Control[3] = Store.VersionCount; Control[4] = rejected; Control[1] = 1234;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        public static Result Run(GraphDirection direction, GraphVersionRetention retention, AllocationPolicy allocation)
        {
            Require(BurstCompiler.IsEnabled, "Burst is disabled");
            int capacity = retention == GraphVersionRetention.UntilOwnerDisposed ? 320 : 6;
            var persistence = allocation == AllocationPolicy.Fixed ? new GraphPersistenceOptions(capacity, retention: retention) :
                new GraphPersistenceOptions(2, capacity, AllocationPolicy.Grow, retention);
            var options = new GraphOptions(2, 2, direction);
            using var managed = new PersistentDeterministicGraph<int, int>(options, persistence);
            using var native = new NativePersistentDeterministicGraph<int, int>(options, persistence, Allocator.Persistent);
            if (allocation == AllocationPolicy.Grow) { managed.TryReserveVersionCapacity(capacity).ThrowIfFailure(); native.TryReserveVersionCapacity(capacity).ThrowIfFailure(); }
            managed.TryCaptureCurrent(out var empty).ThrowIfFailure(); managed.TryWithAddNode(empty, 42, out var initial, out var node).ThrowIfFailure();
            managed.TrySelectCurrent(initial).ThrowIfFailure(); var initialId = initial.Id; empty.Dispose(); initial.Dispose();
            var expectedBytes = new byte[Steps * Stride]; var lengths = new int[Steps]; var stale = default(ManagedGraphVersion<int, int>); int previous = 42;
            var report = new Result { Branches = Steps * 2, RejectedBatches = Steps };
            fixed (byte* p = expectedBytes)
            for (int i = 0; i < Steps; i++)
            {
                managed.TryCaptureCurrent(out var basis).ThrowIfFailure(); stale.Dispose(); Require(basis.IsCreated, "A stale lease released a newer managed root");
                int value = unchecked(i * 65537 + 17);
                managed.TryWithSetNode(basis, node, value, out var left).ThrowIfFailure(); managed.TryWithSetNode(basis, node, -value, out var right).ThrowIfFailure();
                basis.AsReadOnly().TryGetNode(node, out int old).ThrowIfFailure(); Require(old == previous, "Branch modified its basis");
                var mutations = new[] { new GraphMutation<int, int> { Kind = GraphMutationKind.SetNode, Node = node, NodeData = int.MinValue },
                    new GraphMutation<int, int> { Kind = GraphMutationKind.RemoveNode, Node = new GraphNodeHandle(new StableHandle(999, 1)) } };
                fixed (GraphMutation<int, int>* m = mutations)
                {
                    var batch = new GraphWorkBuffer<GraphMutation<int, int>>(m, 2); int before = managed.VersionCount;
                    var s = managed.TryWithBatch(basis, ref batch, 2, out var failed);
                    Require(s == DeterministicStatus.NotFound && !failed.IsCreated && managed.VersionCount == before, "Failed batch published a managed version");
                }
                managed.Current.TryGetNode(node, out int current).ThrowIfFailure(); Require(current == previous, "Failed batch changed selected root");
                managed.TrySelectCurrent(i % 2 == 0 ? left : right).ThrowIfFailure(); previous = i % 2 == 0 ? value : -value;
                var writer = new DeterministicWriter(p + i * Stride, Stride); managed.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                lengths[i] = writer.Position; report.Hash = DeterministicHash.Mix(report.Hash ^ DeterministicHash.Xxh64(writer.Buffer, writer.Position));
                stale = basis; basis.Dispose(); left.Dispose(); right.Dispose();
                if (retention == GraphVersionRetention.UntilOwnerDisposed)
                {
                    managed.TryGetVersion(initialId, out var original).ThrowIfFailure(); original.AsReadOnly().TryGetNode(node, out int valueAtOrigin).ThrowIfFailure();
                    Require(valueAtOrigin == 42, "Retained managed root changed");
                }
            }
            using var bytes = new NativeArray<byte>(expectedBytes.Length, Allocator.Persistent); using var nativeLengths = new NativeArray<int>(Steps, Allocator.Persistent);
            using var control = new NativeArray<int>(5, Allocator.Persistent); var write = control; for (int i = 0; i < 5; i++) write[i] = -999;
            new Job { Store = native, Bytes = bytes, Lengths = nativeLengths, Control = control }.Schedule().Complete();
            int expectedCount = retention == GraphVersionRetention.Leases ? 0 : 2 + Steps * 3;
            Require(control[0] == 0 && control[1] == 1234 && control[2] == -1 && control[3] == expectedCount && control[4] == Steps,
                $"Persistent history job failed at {control[2]}");
            byte* actual = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytes);
            fixed (byte* expected = expectedBytes)
            for (int i = 0; i < Steps; i++)
            {
                Require(lengths[i] == nativeLengths[i], $"Persistent frame length differs at {i}");
                for (int j = 0; j < lengths[i]; j++) Require(expected[i * Stride + j] == actual[i * Stride + j], $"Persistent frame bytes differ at {i}");
            }
            return report;
        }
    }
}
