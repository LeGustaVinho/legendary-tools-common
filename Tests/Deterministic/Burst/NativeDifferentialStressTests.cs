using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NativeDifferentialStressTests
    {
        private struct CollisionPolicy : IDeterministicEquality<int>
        { public bool IsValid => true; public bool Equals(in int a, in int b) => a == b; public ulong Hash(in int value) => 1; }
        private struct Operation { internal int Kind, Key, Value; }
        [BurstCompile(CompileSynchronously = true)]
        private struct FuzzJob : IJob
        {
            internal NativeDeterministicList<int> Values;
            internal NativeDeterministicDictionary<int, int, CollisionPolicy> Map;
            internal NativeDeterministicSortedDictionary<int, int, Int32Policy> Tree;
            internal NativeDeterministicPriorityQueue<int, int, Int32Policy> Heap;
            [ReadOnly] internal NativeArray<Operation> Operations;
            internal NativeArray<ulong> Trace;
            internal NativeArray<int> Results;
            internal NativeArray<byte> Packet;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                for (int i = 0; i < Operations.Length; i++)
                {
                    var op = Operations[i]; var status = DeterministicStatus.None; int value = 0, priority = 0;
                    int index = Values.Count == 0 ? 0 : op.Key % Values.Count;
                    switch (op.Kind)
                    {
                        case 0: status = Values.TryAdd(op.Value); break;
                        case 1: status = Values.TryRemoveAt(index); break;
                        case 2: status = Map.TrySet(op.Key, op.Value); break;
                        case 3: status = Map.TryRemove(op.Key); break;
                        case 4: status = Tree.TrySet(op.Key, op.Value); break;
                        case 5: status = Tree.TryRemove(op.Key); break;
                        case 6: status = Heap.TryEnqueue(op.Value, op.Key % 4); break;
                        case 7: status = Heap.TryDequeue(out value, out priority); break;
                        case 8: status = Values.TrySet(index, op.Value); break;
                        case 9:
                            status = Values.TryCapture(out var list); if (!status.IsSuccess()) break;
                            status = Map.TryCapture(out var map); if (!status.IsSuccess()) { list.Dispose(); break; }
                            status = Tree.TryCapture(out var tree); if (!status.IsSuccess()) { map.Dispose(); list.Dispose(); break; }
                            status = Heap.TryCapture(out var heap); if (!status.IsSuccess()) { tree.Dispose(); map.Dispose(); list.Dispose(); break; }
                            Values.TryClear(); Map.TryClear(); Tree.TryClear(); Heap.TryClear();
                            Values.TryAdd(91); Map.TrySet(7, 91); Tree.TrySet(7, 91); Heap.TryEnqueue(91, 1);
                            status = Values.TryRestore(list); if (status.IsSuccess()) status = Map.TryRestore(map);
                            if (status.IsSuccess()) status = Tree.TryRestore(tree); if (status.IsSuccess()) status = Heap.TryRestore(heap);
                            heap.Dispose(); tree.Dispose(); map.Dispose(); list.Dispose(); break;
                    }
                    var writer = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Packet), Packet.Length);
                    var encoded = Values.TryWriteCanonical(ref writer, default(Int32Policy));
                    if (encoded.IsSuccess()) encoded = Map.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy));
                    if (encoded.IsSuccess()) encoded = Tree.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy));
                    if (encoded.IsSuccess()) encoded = Heap.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy));
                    if (!encoded.IsSuccess()) { Results[1] = (int)encoded; return; }
                    Trace[i] = TraceHash(writer.Buffer, writer.Position, status, value, priority); Results[2] = writer.Position;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        private static ulong TraceHash(byte* bytes, int length, DeterministicStatus status, int value, int priority)
            => DeterministicHash.Mix(DeterministicHash.Xxh64(bytes, length) ^ (byte)status ^ ((ulong)(uint)value << 32) ^ (uint)priority);
        private static uint Next(ref uint seed) { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return seed; }
        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (uint seed in new uint[] { 7, 0x93782165 })
            foreach (SnapshotPolicy snapshots in Enum.GetValues(typeof(SnapshotPolicy)))
            foreach (AllocationPolicy allocation in Enum.GetValues(typeof(AllocationPolicy)))
                yield return new TestCaseData(seed, snapshots, allocation);
        }
        [TestCaseSource(nameof(Cases))]
        public void BurstAndManagedAgreeWithIndependentModelsAtEveryMutationAndRollback(uint seed, SnapshotPolicy snapshots, AllocationPolicy allocation)
        {
            const int count = 2048, capacity = 48;
            var options = new BufferOptions(allocation == AllocationPolicy.Fixed ? capacity : 1, capacity, allocation, snapshots, 2, 64);
            using (var values = new DeterministicList<int>(options)) using (var map = new DeterministicDictionary<int, int, CollisionPolicy>(options))
            using (var tree = new DeterministicSortedDictionary<int, int, Int32Policy>(options)) using (var heap = new DeterministicPriorityQueue<int, int, Int32Policy>(options))
            using (var nv = new NativeDeterministicList<int>(options, Allocator.Persistent))
            using (var nm = new NativeDeterministicDictionary<int, int, CollisionPolicy>(options, Allocator.Persistent))
            using (var nt = new NativeDeterministicSortedDictionary<int, int, Int32Policy>(options, Allocator.Persistent))
            using (var nh = new NativeDeterministicPriorityQueue<int, int, Int32Policy>(options, Allocator.Persistent))
            using (var operations = new NativeArray<Operation>(count, Allocator.TempJob)) using (var trace = new NativeArray<ulong>(count, Allocator.TempJob))
            using (var results = new NativeArray<int>(3, Allocator.TempJob)) using (var packet = new NativeArray<byte>(16384, Allocator.TempJob))
            {
                var operationWriter = operations;
                for (int i = 0; i < count; i++) operationWriter[i] = new Operation { Kind = (int)(Next(ref seed) % 10), Key = (int)(Next(ref seed) % 32), Value = unchecked((int)Next(ref seed)) };
                new FuzzJob { Values = nv, Map = nm, Tree = nt, Heap = nh, Operations = operations, Trace = trace, Packet = packet, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0], "Managed fallback"); Assert.AreEqual(0, results[1], "Native canonical write");
                var listModel = new List<int>(); var mapModel = new Dictionary<int, int>(); var keyOrder = new List<int>();
                var treeModel = new SortedDictionary<int, int>(); var heapModel = new List<(int Value, int Priority, ulong Sequence)>(); ulong sequence = 0;
                byte* bytes = stackalloc byte[16384];
                for (int i = 0; i < count; i++)
                {
                    var op = operations[i]; var status = DeterministicStatus.None; var expected = DeterministicStatus.None; int value = 0, priority = 0;
                    int index = listModel.Count == 0 ? 0 : op.Key % listModel.Count;
                    switch (op.Kind)
                    {
                        case 0:
                            expected = listModel.Count == capacity ? DeterministicStatus.CapacityExceeded : DeterministicStatus.None;
                            status = values.TryAdd(op.Value); if (expected.IsSuccess()) listModel.Add(op.Value); break;
                        case 1:
                            expected = listModel.Count == 0 ? DeterministicStatus.OutOfRange : DeterministicStatus.None;
                            status = values.TryRemoveAt(index); if (expected.IsSuccess()) listModel.RemoveAt(index); break;
                        case 2:
                            status = map.TrySet(op.Key, op.Value); if (!mapModel.ContainsKey(op.Key)) keyOrder.Add(op.Key); mapModel[op.Key] = op.Value; break;
                        case 3:
                            expected = mapModel.Remove(op.Key) ? DeterministicStatus.None : DeterministicStatus.NotFound;
                            status = map.TryRemove(op.Key); if (expected.IsSuccess()) keyOrder.Remove(op.Key); break;
                        case 4: status = tree.TrySet(op.Key, op.Value); treeModel[op.Key] = op.Value; break;
                        case 5: expected = treeModel.Remove(op.Key) ? DeterministicStatus.None : DeterministicStatus.NotFound; status = tree.TryRemove(op.Key); break;
                        case 6:
                            expected = heapModel.Count == capacity ? DeterministicStatus.CapacityExceeded : DeterministicStatus.None;
                            status = heap.TryEnqueue(op.Value, op.Key % 4); if (expected.IsSuccess()) heapModel.Add((op.Value, op.Key % 4, sequence++)); break;
                        case 7:
                            expected = heapModel.Count == 0 ? DeterministicStatus.Empty : DeterministicStatus.None; status = heap.TryDequeue(out value, out priority);
                            if (expected.IsSuccess())
                            {
                                int best = 0; for (int j = 1; j < heapModel.Count; j++) if (heapModel[j].Priority < heapModel[best].Priority ||
                                    (heapModel[j].Priority == heapModel[best].Priority && heapModel[j].Sequence < heapModel[best].Sequence)) best = j;
                                Assert.AreEqual(heapModel[best].Value, value); Assert.AreEqual(heapModel[best].Priority, priority); heapModel.RemoveAt(best);
                            }
                            break;
                        case 8:
                            expected = listModel.Count == 0 ? DeterministicStatus.OutOfRange : DeterministicStatus.None;
                            status = values.TrySet(index, op.Value); if (expected.IsSuccess()) listModel[index] = op.Value; break;
                        case 9:
                            values.TryCapture(out var vs).ThrowIfFailure(); map.TryCapture(out var ms).ThrowIfFailure();
                            tree.TryCapture(out var ts).ThrowIfFailure(); heap.TryCapture(out var hs).ThrowIfFailure();
                            values.Clear(); map.Clear(); tree.Clear(); heap.Clear(); values.Add(91); map[7] = 91; tree[7] = 91; heap.Enqueue(91, 1);
                            values.TryRestore(vs).ThrowIfFailure(); map.TryRestore(ms).ThrowIfFailure(); tree.TryRestore(ts).ThrowIfFailure(); heap.TryRestore(hs).ThrowIfFailure();
                            hs.Dispose(); ts.Dispose(); ms.Dispose(); vs.Dispose(); break;
                    }
                    Assert.AreEqual(expected, status, "status at " + i); CollectionAssert.AreEqual(listModel, values);
                    Assert.AreEqual(sequence, heap.NextSequence); Assert.AreEqual(heapModel.Count, heap.Count);
                    int at = 0; foreach (var pair in map) { Assert.AreEqual(keyOrder[at++], pair.Key); Assert.AreEqual(mapModel[pair.Key], pair.Value); } Assert.AreEqual(mapModel.Count, at);
                    at = 0; foreach (var pair in treeModel) { tree.TryGetAt(at++, out var item).ThrowIfFailure(); Assert.AreEqual(pair.Key, item.Key); Assert.AreEqual(pair.Value, item.Value); } Assert.AreEqual(treeModel.Count, tree.Count);
                    var writer = new DeterministicWriter(bytes, 16384); values.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
                    map.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                    tree.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                    heap.TryWriteCanonical(ref writer, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                    Assert.AreEqual(TraceHash(bytes, writer.Position, status, value, priority), trace[i], "canonical trace at " + i);
                    if (i == count - 1) { Assert.AreEqual(writer.Position, results[2]); for (int j = 0; j < writer.Position; j++) Assert.AreEqual(bytes[j], packet[j]); }
                }
            }
        }
    }
}
