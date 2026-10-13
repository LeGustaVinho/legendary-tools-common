using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicCommonUseCase")]
    public sealed unsafe class CollectionUseCaseTests
    {
        private static BufferOptions Options(SnapshotPolicy policy, int capacity = 16) =>
            new BufferOptions(capacity, snapshots: policy, snapshotSlots: 2, pageBytes: 64);

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void EntityDespawnAndRespawnReplayPreservesComponentsAndFutureIdentity(SnapshotPolicy policy)
        {
            using (var entities = new DeterministicSlotMap<int>(Options(policy, 5)))
            using (var positions = new DeterministicSparseSet<int>(Options(policy, 5), Options(policy, 5)))
            using (var health = new DeterministicSparseSet<int>(Options(policy, 5), Options(policy, 5)))
            {
                var handles = new StableHandle[4];
                for (int i = 0; i < handles.Length; i++)
                {
                    handles[i] = entities.Add(100 + i);
                    positions.Add(handles[i], i * 10); health.Add(handles[i], 100);
                }
                entities.TryCapture(out var entitySnapshot).ThrowIfFailure();
                positions.TryCapture(out var positionSnapshot).ThrowIfFailure();
                health.TryCapture(out var healthSnapshot).ThrowIfFailure();
                try
                {
                    byte* first = stackalloc byte[8192]; byte* replay = stackalloc byte[8192];
                    var expectedHandles = new[] { new StableHandle(3, 2), new StableHandle(1, 2) }; int firstLength = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        foreach (int index in new[] { 3, 1 })
                        {
                            Assert.IsTrue(positions.Remove(handles[index])); Assert.IsTrue(health.Remove(handles[index]));
                            Assert.IsTrue(entities.Remove(handles[index]));
                        }
                        for (int i = 0; i < 2; i++)
                        {
                            var spawned = entities.Add(200 + i); Assert.AreEqual(expectedHandles[i], spawned);
                            positions.Add(spawned, 90 + i); health.Add(spawned, 20 + i);
                        }
                        positions[handles[0]] += 12; health[handles[0]] -= 10;
                        Assert.AreEqual(4, entities.Count);
                        Assert.IsFalse(health.ContainsKey(handles[1])); Assert.IsFalse(entities.ContainsKey(handles[3]));
                        var orderedHandles = new[] { handles[0], expectedHandles[1], handles[2], expectedHandles[0] };
                        var expectedPositions = new[] { 12, 91, 20, 90 };
                        var expectedHealth = new[] { 90, 21, 100, 20 };
                        int rank = 0;
                        foreach (var component in positions.OrderedByEntity)
                        {
                            Assert.AreEqual(orderedHandles[rank], component.Handle);
                            Assert.AreEqual(expectedPositions[rank], component.Value);
                            Assert.AreEqual(expectedHealth[rank++], health[component.Handle]);
                        }
                        Assert.AreEqual(4, rank);
                        var writer = new DeterministicWriter(pass == 0 ? first : replay, 8192);
                        entities.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                        positions.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                        health.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                        if (pass == 0)
                        {
                            firstLength = writer.Position;
                            entities.TryRestore(entitySnapshot).ThrowIfFailure();
                            positions.TryRestore(positionSnapshot).ThrowIfFailure(); health.TryRestore(healthSnapshot).ThrowIfFailure();
                        }
                        else
                        { Assert.AreEqual(firstLength, writer.Position); for (int i = 0; i < writer.Position; i++) Assert.AreEqual(first[i], replay[i], "canonical byte " + i); }
                    }
                }
                finally { entitySnapshot.Dispose(); positionSnapshot.Dispose(); healthSnapshot.Dispose(); }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void InventoryReceiptsAndUnlocksSurviveSaveLoadWithInsertionOrder(SnapshotPolicy policy)
        {
            using (var inventory = new DeterministicDictionary<int, int, Int32Policy>(Options(policy)))
            using (var unlocked = new DeterministicHashSet<int, Int32Policy>(Options(policy)))
            using (var loadedInventory = new DeterministicDictionary<int, int, Int32Policy>(Options(policy)))
            using (var loadedUnlocks = new DeterministicHashSet<int, Int32Policy>(Options(policy)))
            {
                int[] ids = { 10, 7, 10, 5, 10, 7, 5 }; int[] amounts = { 3, 2, -3, 4, 1, 2, -1 };
                var oracle = new Dictionary<int, int>(); var order = new List<int>();
                for (int i = 0; i < ids.Length; i++)
                {
                    int id = ids[i]; oracle.TryGetValue(id, out int quantity);
                    if (!oracle.ContainsKey(id)) order.Add(id);
                    quantity += amounts[i];
                    if (quantity == 0) { oracle.Remove(id); order.Remove(id); }
                    else oracle[id] = quantity;
                    inventory.TryGet(id, out int actual); actual += amounts[i];
                    if (actual == 0) inventory.Remove(id); else inventory.TrySet(id, actual).ThrowIfFailure();
                    if (amounts[i] > 0) unlocked.Add(id);
                    CollectionAssert.AreEqual(order, inventory.Keys);
                    foreach (int key in order) Assert.AreEqual(oracle[key], inventory[key]);
                }
                CollectionAssert.AreEqual(new[] { 7, 5, 10 }, inventory.Keys);
                CollectionAssert.AreEqual(new[] { 10, 7, 5 }, unlocked);
                byte* packet = stackalloc byte[8192]; byte* rewritten = stackalloc byte[8192]; byte* work = stackalloc byte[32768];
                var writer = new DeterministicWriter(packet, 8192);
                inventory.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                unlocked.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                var reader = new DeterministicReader(packet, writer.Position); var scratch = new CanonicalScratch(work, 32768);
                loadedInventory.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                loadedUnlocks.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(writer.Position, reader.Position); CollectionAssert.AreEqual(order, loadedInventory.Keys);
                foreach (int key in order) Assert.AreEqual(oracle[key], loadedInventory[key]);
                CollectionAssert.AreEqual(new[] { 10, 7, 5 }, loadedUnlocks);
                var secondWriter = new DeterministicWriter(rewritten, 8192);
                loadedInventory.TryWriteCanonical(ref secondWriter, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                loadedUnlocks.TryWriteCanonical(ref secondWriter, default(Int32Policy)).ThrowIfFailure();
                Assert.AreEqual(writer.Position, secondWriter.Position);
                for (int i = 0; i < writer.Position; i++) Assert.AreEqual(packet[i], rewritten[i]);
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ScheduledEventsKeepFifoTiesAndCancellationAcrossRollback(SnapshotPolicy policy)
        {
            using (var schedule = new DeterministicPriorityQueue<int, int, Int32Policy>(Options(policy, 32)))
            using (var dispatch = new DeterministicQueue<int>(Options(policy, 32)))
            using (var cancelled = new DeterministicHashSet<int, Int32Policy>(Options(policy, 8)))
            {
                var model = new List<(int Due, int Sequence, int Id)>();
                for (int id = 1; id <= 20; id++)
                {
                    int due = id * 7 % 11; schedule.Enqueue(id, due); model.Add((due, id, id));
                    if (id % 5 == 0) cancelled.Add(id);
                }
                model.Sort((a, b) => { int c = a.Due.CompareTo(b.Due); return c == 0 ? a.Sequence.CompareTo(b.Sequence) : c; });
                var expected = new List<int>(); foreach (var entry in model) if (entry.Id % 5 != 0) expected.Add(entry.Due * 100 + entry.Id);
                schedule.TryCapture(out var checkpoint).ThrowIfFailure(); dispatch.TryCapture(out var emptyDispatch).ThrowIfFailure();
                try
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int tick = 0; tick <= 10; tick++)
                            while (schedule.TryPeek(out int id, out int due).IsSuccess() && due <= tick)
                            { schedule.TryDequeue(out _, out _).ThrowIfFailure(); if (!cancelled.Contains(id)) dispatch.Enqueue(tick * 100 + id); }
                        CollectionAssert.AreEqual(expected, dispatch); Assert.AreEqual(0, schedule.Count);
                        Assert.AreEqual(20UL, schedule.NextSequence);
                        if (pass == 0) { schedule.TryRestore(checkpoint).ThrowIfFailure(); dispatch.TryRestore(emptyDispatch).ThrowIfFailure(); }
                    }
                }
                finally { checkpoint.Dispose(); emptyDispatch.Dispose(); }
            }
        }

        // Directed graph with equal-cost routes, a stale heap entry, a cycle and an unreachable vertex.
        private static readonly int[,] Edges = { { 0, 1, 2 }, { 0, 2, 2 }, { 0, 4, 20 }, { 1, 3, 2 },
            { 1, 4, 7 }, { 2, 3, 2 }, { 2, 4, 3 }, { 3, 5, 1 }, { 4, 5, 2 }, { 5, 0, 1 } };

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void ShortestPathFrontierMatchesIndependentBellmanFordAfterRestart(SnapshotPolicy policy)
        {
            var oracle = new int[7]; for (int i = 1; i < oracle.Length; i++) oracle[i] = int.MaxValue;
            for (int pass = 0; pass < 6; pass++) for (int edge = 0; edge < Edges.GetLength(0); edge++)
            {
                int from = Edges[edge, 0], to = Edges[edge, 1], cost = Edges[edge, 2];
                if (oracle[from] != int.MaxValue) oracle[to] = Math.Min(oracle[to], oracle[from] + cost);
            }
            using (var distances = new DeterministicArray<int>(Options(policy, 7)))
            using (var previous = new DeterministicArray<int>(Options(policy, 7)))
            using (var settled = new DeterministicBitSet(7, policy, 2))
            using (var frontier = new DeterministicPriorityQueue<int, int, Int32Policy>(Options(policy, 32)))
            {
                for (int i = 0; i < 7; i++) { distances[i] = i == 0 ? 0 : int.MaxValue; previous[i] = -1; }
                frontier.Enqueue(0, 0);
                distances.TryCapture(out var ds).ThrowIfFailure(); previous.TryCapture(out var ps).ThrowIfFailure();
                settled.TryCapture(out var ss).ThrowIfFailure(); frontier.TryCapture(out var fs).ThrowIfFailure();
                try
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var visitOrder = new List<int>();
                        while (frontier.TryDequeue(out int from, out int due).IsSuccess())
                        {
                            if (settled[from] || due != distances[from]) continue;
                            settled[from] = true; visitOrder.Add(from);
                            for (int edge = 0; edge < Edges.GetLength(0); edge++) if (Edges[edge, 0] == from)
                            {
                                int to = Edges[edge, 1], candidate = due + Edges[edge, 2];
                                if (candidate >= distances[to]) continue;
                                distances[to] = candidate; previous[to] = from; frontier.Enqueue(to, candidate);
                            }
                        }
                        CollectionAssert.AreEqual(oracle, distances);
                        CollectionAssert.AreEqual(new[] { -1, 0, 0, 1, 2, 3, -1 }, previous);
                        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, visitOrder);
                        Assert.AreEqual(6, settled.Count); Assert.IsFalse(settled[6]);
                        if (pass == 0)
                        { distances.TryRestore(ds).ThrowIfFailure(); previous.TryRestore(ps).ThrowIfFailure(); settled.TryRestore(ss).ThrowIfFailure(); frontier.TryRestore(fs).ThrowIfFailure(); }
                    }
                }
                finally { ds.Dispose(); ps.Dispose(); ss.Dispose(); fs.Dispose(); }
            }
        }
    }
}
