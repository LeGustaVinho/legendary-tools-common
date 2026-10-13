using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NativeUseCaseTests
    {
        private static BufferOptions Options(SnapshotPolicy policy, int capacity, int slots = 2) =>
            new BufferOptions(capacity, snapshots: policy, snapshotSlots: slots, pageBytes: 64);

        [BurstCompile(CompileSynchronously = true)]
        private struct LifecycleJob : IJob
        {
            internal NativeDeterministicSlotMap<int> Entities;
            internal NativeDeterministicSparseSet<int> Positions, Health;
            internal NativeArray<byte> Packet;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                var a = Entities.Add(100); var b = Entities.Add(101); var c = Entities.Add(102); var d = Entities.Add(103);
                Positions.Add(a, 0); Positions.Add(b, 10); Positions.Add(c, 20); Positions.Add(d, 30);
                Health.Add(a, 100); Health.Add(b, 100); Health.Add(c, 100); Health.Add(d, 100);
                var es = Entities.TryCapture(out var entities); var ps = Positions.TryCapture(out var positions); var hs = Health.TryCapture(out var health);
                Results[1] = (int)es | (int)ps | (int)hs;
                if (!es.IsSuccess() || !ps.IsSuccess() || !hs.IsSuccess()) { entities.Dispose(); positions.Dispose(); health.Dispose(); return; }
                for (int pass = 0; pass < 2; pass++)
                {
                    Positions.Remove(d); Health.Remove(d); Entities.Remove(d);
                    Positions.Remove(b); Health.Remove(b); Entities.Remove(b);
                    var first = Entities.Add(200); var second = Entities.Add(201);
                    Positions.Add(first, 90); Health.Add(first, 20); Positions.Add(second, 91); Health.Add(second, 21);
                    Positions[a] += 12; Health[a] -= 10;
                    Results[4] = (int)first.Index; Results[5] = (int)first.Generation;
                    Results[6] = (int)second.Index; Results[7] = (int)second.Generation;
                    Results[8] = (int)Health.TryGet(b, out _); Results[9] = (int)Entities.TryGet(d, out _);
                    byte* bytes = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Packet) + pass * 8192;
                    var writer = new DeterministicWriter(bytes, 8192);
                    Results[1] |= (int)Entities.TryWriteCanonical(ref writer, default(Int32Policy));
                    Results[1] |= (int)Positions.TryWriteCanonical(ref writer, default(Int32Policy));
                    Results[1] |= (int)Health.TryWriteCanonical(ref writer, default(Int32Policy)); Results[2 + pass] = writer.Position;
                    if (pass == 0)
                    {
                        Results[1] |= (int)Entities.TryRestore(entities);
                        Results[1] |= (int)Positions.TryRestore(positions); Results[1] |= (int)Health.TryRestore(health);
                    }
                }
                entities.Dispose(); positions.Dispose(); health.Dispose();
            }
        }
        [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        [Category("DeterministicCommonUseCase")]
        public void EntityLifecycleRunsInBurstAndProducesManagedCanonicalBytes(SnapshotPolicy policy)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            var options = Options(policy, 5);
            using (var entities = new NativeDeterministicSlotMap<int>(options, Allocator.Persistent))
            using (var positions = new NativeDeterministicSparseSet<int>(options, options, Allocator.Persistent))
            using (var health = new NativeDeterministicSparseSet<int>(options, options, Allocator.Persistent))
            using (var packet = new NativeArray<byte>(16384, Allocator.TempJob))
            using (var results = new NativeArray<int>(10, Allocator.TempJob))
            using (var managedEntities = new DeterministicSlotMap<int>(options))
            using (var managedPositions = new DeterministicSparseSet<int>(options, options))
            using (var managedHealth = new DeterministicSparseSet<int>(options, options))
            {
                new LifecycleJob { Entities = entities, Positions = positions, Health = health, Packet = packet, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0], "The job must execute through Burst."); Assert.AreEqual(0, results[1]);
                CollectionAssert.AreEqual(new[] { 3, 2, 1, 2, (int)DeterministicStatus.NotFound, (int)DeterministicStatus.NotFound },
                    new[] { results[4], results[5], results[6], results[7], results[8], results[9] });
                Assert.AreEqual(results[2], results[3]); Assert.AreEqual(4, entities.Count);
                var handles = new StableHandle[4];
                for (int i = 0; i < 4; i++)
                { handles[i] = managedEntities.Add(100 + i); managedPositions.Add(handles[i], i * 10); managedHealth.Add(handles[i], 100); }
                foreach (int index in new[] { 3, 1 })
                { managedPositions.Remove(handles[index]); managedHealth.Remove(handles[index]); managedEntities.Remove(handles[index]); }
                for (int i = 0; i < 2; i++)
                { var handle = managedEntities.Add(200 + i); managedPositions.Add(handle, 90 + i); managedHealth.Add(handle, 20 + i); }
                managedPositions[handles[0]] += 12; managedHealth[handles[0]] -= 10;
                byte* expected = stackalloc byte[8192]; var writer = new DeterministicWriter(expected, 8192);
                managedEntities.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                managedPositions.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure(); managedHealth.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(writer.Position, results[2]);
                for (int i = 0; i < writer.Position; i++) { Assert.AreEqual(expected[i], packet[i], "Managed/Burst byte " + i); Assert.AreEqual(packet[i], packet[8192 + i], "rollback byte " + i); }
                int rank = 0; int[] expectedPositions = { 12, 91, 20, 90 }; int[] expectedHealth = { 90, 21, 100, 20 };
                foreach (var item in positions.OrderedByEntity)
                { Assert.AreEqual((uint)rank, item.Handle.Index); Assert.AreEqual(expectedPositions[rank], item.Value); Assert.AreEqual(expectedHealth[rank++], health[item.Handle]); }
                Assert.AreEqual(4, rank);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ZeroOneGraphJob : IJob
        {
            [ReadOnly] internal NativeArray<int> Edges;
            internal NativeDeterministicMultiMap<int, int, Int32Policy> Graph;
            internal NativeDeterministicArray<int> Distances;
            internal NativeDeterministicDeque<int> Pending;
            internal NativeDeterministicBitSet Settled;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                for (int i = 0; i < Edges.Length; i += 3) Graph.Add(Edges[i], Edges[i + 1] * 2 + Edges[i + 2]);
                for (int i = 0; i < Distances.Length; i++) Distances[i] = i == 0 ? 0 : int.MaxValue;
                Pending.AddFirst(0);
                while (Pending.TryRemoveFirst(out int vertex).IsSuccess())
                {
                    if (Settled[vertex]) continue;
                    Settled[vertex] = true; Results[1]++;
                    foreach (int edge in Graph[vertex])
                    {
                        int to = edge / 2, cost = edge & 1, candidate = Distances[vertex] + cost;
                        if (candidate >= Distances[to]) continue;
                        Distances[to] = candidate; if (cost == 0) Pending.AddFirst(to); else Pending.AddLast(to);
                    }
                }
                Results[2] = Settled.Count; Results[3] = Settled.NextSetBit(1); Results[4] = Pending.Count;
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        [Category("DeterministicUncommonUseCase")]
        public void ZeroOneShortestPathsWithCyclesAndDuplicateEdgesMatchBellmanFordInBurst(SnapshotPolicy policy)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            int[] triples = { 0, 64, 1, 0, 63, 0, 64, 129, 0, 63, 64, 0, 63, 128, 1, 128, 129, 0, 129, 0, 1, 129, 129, 0, 0, 63, 0 };
            var oracle = new int[130]; for (int i = 1; i < oracle.Length; i++) oracle[i] = int.MaxValue;
            for (int pass = 0; pass < 129; pass++) for (int edge = 0; edge < triples.Length; edge += 3)
                if (oracle[triples[edge]] != int.MaxValue)
                { int candidate = oracle[triples[edge]] + triples[edge + 2]; if (candidate < oracle[triples[edge + 1]]) oracle[triples[edge + 1]] = candidate; }
            using (var graph = new NativeDeterministicMultiMap<int, int, Int32Policy>(Options(policy, 8), Options(policy, 16), Allocator.Persistent))
            using (var distances = new NativeDeterministicArray<int>(Options(policy, 130), Allocator.Persistent))
            using (var pending = new NativeDeterministicDeque<int>(Options(policy, 16), Allocator.Persistent))
            using (var settled = new NativeDeterministicBitSet(130, Allocator.Persistent, policy))
            using (var edges = new NativeArray<int>(triples, Allocator.TempJob))
            using (var results = new NativeArray<int>(5, Allocator.TempJob))
            {
                graph.TryCapture(out var gs).ThrowIfFailure(); distances.TryCapture(out var ds).ThrowIfFailure();
                pending.TryCapture(out var qs).ThrowIfFailure(); settled.TryCapture(out var ss).ThrowIfFailure();
                try
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var resultWriter = results; for (int i = 0; i < results.Length; i++) resultWriter[i] = 0;
                        new ZeroOneGraphJob { Graph = graph, Distances = distances, Pending = pending, Settled = settled, Edges = edges, Results = results }.Schedule().Complete();
                        CollectionAssert.AreEqual(new[] { 0, 5, 5, 63, 0 }, results.ToArray());
                        for (int i = 0; i < oracle.Length; i++)
                        { Assert.AreEqual(oracle[i], distances[i], "vertex " + i); Assert.AreEqual(oracle[i] != int.MaxValue, settled[i], "settled vertex " + i); }
                        Assert.AreEqual(9, graph.Count); Assert.AreEqual(5, graph.KeyCount);
                        if (pass == 0)
                        { graph.TryRestore(gs).ThrowIfFailure(); distances.TryRestore(ds).ThrowIfFailure(); pending.TryRestore(qs).ThrowIfFailure(); settled.TryRestore(ss).ThrowIfFailure(); }
                    }
                }
                finally { gs.Dispose(); ds.Dispose(); qs.Dispose(); ss.Dispose(); }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct BoundaryJob : IJob
        {
            internal NativeDeterministicSparseSet<int> Sparse;
            internal NativeDeterministicPriorityQueue<int, long, Int64Policy> Heap;
            internal NativeDeterministicQueue<int> EmptyQueue;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                var handle = new StableHandle(0, 1); Sparse.Add(handle, 7); var iterator = Sparse.GetEnumerator();
                Results[1] = (int)Sparse.TrySet(new StableHandle(uint.MaxValue, uint.MaxValue), 99);
                Results[2] = (int)Sparse.TryAdd(new StableHandle(uint.MaxValue, 0), 99);
                Results[3] = (int)Sparse.TrySet(new StableHandle(0, 2), 99);
                Results[4] = (int)Sparse.TryCapture(out var unavailable); unavailable.Dispose();
                Results[5] = iterator.MoveNext() ? iterator.Current.Value : -1;
                Results[6] = Sparse.Capacity; Results[7] = Sparse.EntitySlotCapacity;
                Results[8] = (int)EmptyQueue.TryEnqueue(1); Results[9] = (int)EmptyQueue.TryDequeue(out _);
                Heap.Enqueue(10, long.MaxValue); Heap.Enqueue(11, 0); Heap.Enqueue(12, long.MinValue); Heap.Enqueue(13, -1);
                Heap.Enqueue(14, 1); Heap.Enqueue(15, long.MinValue); Heap.Enqueue(16, long.MaxValue); Heap.Enqueue(17, 0);
                for (int i = 0; i < 8; i++) Results[10 + i] = Heap.Dequeue();
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        [Category("DeterministicEdgeCase")]
        public void ExtremeHandlesZeroBudgetsAndSignedPriorityLimitsExecuteInBurst(SnapshotPolicy policy)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            var dense = new BufferOptions(0, 4, AllocationPolicy.Grow, policy, 0, 64);
            var sparse = new BufferOptions(0, 130, AllocationPolicy.Grow, policy, 0, 64);
            using (var components = new NativeDeterministicSparseSet<int>(dense, sparse, Allocator.Persistent))
            using (var heap = new NativeDeterministicPriorityQueue<int, long, Int64Policy>(Options(policy, 8), Allocator.Persistent))
            using (var empty = new NativeDeterministicQueue<int>(Options(policy, 0), Allocator.Persistent))
            using (var results = new NativeArray<int>(18, Allocator.TempJob))
            {
                new BoundaryJob { Sparse = components, Heap = heap, EmptyQueue = empty, Results = results }.Schedule().Complete();
                CollectionAssert.AreEqual(new[] { 0, (int)DeterministicStatus.OutOfRange, (int)DeterministicStatus.InvalidConfiguration,
                    (int)DeterministicStatus.ProtocolConflict, (int)DeterministicStatus.SnapshotCapacityExceeded, 7, 1, 1,
                    (int)DeterministicStatus.CapacityExceeded, (int)DeterministicStatus.Empty, 12, 15, 13, 11, 17, 14, 10, 16 }, results.ToArray());
                Assert.AreEqual(1, components.Count); Assert.AreEqual(7, components[new StableHandle(0, 1)]); Assert.AreEqual(8UL, heap.NextSequence);
            }
        }
    }
}
