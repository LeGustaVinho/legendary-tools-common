using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicDictionarySparseOptimization")]
    public sealed unsafe class NativeDictionarySparseOptimizationTests
    {
        private struct Payload
        {
            internal fixed ulong Words[32];
            internal static ulong Word(int key, int word) => unchecked((ulong)(uint)key * 0x9e3779b97f4a7c15UL ^ (ulong)(word + 1) * 0xd6e8feb86659fd93UL);
            internal static Payload Create(int key)
            { Payload value = default; for (int i = 0; i < 32; i++) value.Words[i] = Word(key, i); return value; }
        }
        private static void Verify(Payload value, int key)
        { for (int i = 0; i < 32; i++) Assert.AreEqual(Payload.Word(key, i), value.Words[i], "word " + i); }
        private struct HashPolicy : IDeterministicEquality<int>
        {
            internal bool Collisions;
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int key) => Collisions ? 1UL : default(Int32Policy).Hash(key);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct ReadRootJob : IJob
        {
            [ReadOnly] internal NativeDictionarySnapshot<int, Payload> Root;
            internal NativeArray<int> Keys, Status, Marker;
            internal NativeArray<Payload> Values;
            public void Execute()
            {
                int flag = 0; MarkManaged(ref flag); Marker[0] = flag;
                for (int i = 0; i < Root.Count; i++)
                {
                    int ordinal = (i & 1) == 0 ? i / 2 : Root.Count - 1 - i / 2;
                    var status = Root.TryGetAt(ordinal, out var pair);
                    Status[ordinal] = (int)status; Keys[ordinal] = pair.Key; Values[ordinal] = pair.Value;
                }
                Status[Root.Count] = (int)Root.TryGetAt(-1, out _);
                Status[Root.Count + 1] = (int)Root.TryGetAt(Root.Count, out _);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static IEnumerable<object[]> RootCases()
        {
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.KeyframesAndDeltas, SnapshotPolicy.CopyOnWrite })
                foreach (int count in new[] { 0, 1, 63, 64 }) yield return new object[] { policy, count };
        }
        [TestCaseSource(nameof(RootCases))]
        public void BurstSnapshotReadsBothDirectionsAfterReuseAndLiveGrowth(SnapshotPolicy policy, int count)
        {
            using var map = new NativeDeterministicDictionary<int, Payload, HashPolicy>(
                new BufferOptions(0, 128, AllocationPolicy.Grow, policy, 1, 256), Allocator.Persistent, new HashPolicy { Collisions = true });
            var order = new List<int>();
            for (int i = 0; i < count; i++) { map.Add(i, Payload.Create(i)); order.Add(i); }
            for (int i = 1; i < count; i += 3) { map.Remove(i); order.Remove(i); }
            if (count > 1) for (int i = 0; i < 7; i++) { map.Add(100 + i, Payload.Create(100 + i)); order.Add(100 + i); }
            map.TryCapture(out var root).ThrowIfFailure(); var stale = root;
            try
            {
                map.Clear(); for (int i = 0; i < 128; i++) map.Add(-i - 1, Payload.Create(-i - 1));
                using var keys = new NativeArray<int>(order.Count, Allocator.TempJob);
                using var values = new NativeArray<Payload>(order.Count, Allocator.TempJob);
                using var statuses = new NativeArray<int>(order.Count + 2, Allocator.TempJob);
                using var marker = new NativeArray<int>(1, Allocator.TempJob);
                new ReadRootJob { Root = root, Keys = keys, Values = values, Status = statuses, Marker = marker }.Schedule().Complete();
                Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, marker[0]);
                for (int i = 0; i < order.Count; i++)
                { Assert.AreEqual(0, statuses[i]); Assert.AreEqual(order[i], keys[i]); Verify(values[i], keys[i]); }
                Assert.AreEqual((int)DeterministicStatus.OutOfRange, statuses[order.Count]);
                Assert.AreEqual((int)DeterministicStatus.OutOfRange, statuses[order.Count + 1]);
                map.TryRestore(root).ThrowIfFailure(); int position = 0;
                foreach (var pair in map) { Assert.AreEqual(order[position++], pair.Key); Verify(pair.Value, pair.Key); }
                Assert.AreEqual(order.Count, position);
            }
            finally { root.Dispose(); }
            map.TryCapture(out var reused).ThrowIfFailure();
            try { stale.Dispose(); Assert.IsFalse(stale.IsCreated); Assert.IsTrue(reused.IsCreated); }
            finally { reused.Dispose(); }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct MutationJob : IJob
        {
            internal NativeDeterministicDictionary<int, Payload, HashPolicy> Map;
            internal NativeDeterministicSparseSet<Payload> Set;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int flag = 0; MarkManaged(ref flag); Result[0] = flag;
                Result[1] = (int)Map.TryClear();
                Result[2] = Map.Count;
                Result[3] = Set.ContainsKey(new StableHandle(1, 7)) ? 1 : 0;
                Result[4] = Set.ContainsKey(new StableHandle(1, 6)) ? 1 : 0;
                Result[5] = (int)Set.TryRemove(new StableHandle(1, 7));
                Result[6] = (int)Set.TryRemove(new StableHandle(31, 9));
                Result[7] = (int)Set.TryAdd(new StableHandle(31, 10), Payload.Create(32));
                Result[8] = Set.ContainsKey(new StableHandle(31, 9)) ? 1 : 0;
                Result[9] = Set.ContainsKey(new StableHandle(31, 10)) ? 1 : 0;
                Result[10] = (int)Set.TryRemove(new StableHandle(uint.MaxValue, 1));
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static IEnumerable<object[]> ClearCases()
        {
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.KeyframesAndDeltas, SnapshotPolicy.CopyOnWrite })
                for (int mode = 0; mode < 4; mode++) yield return new object[] { policy, mode };
        }
        [TestCaseSource(nameof(ClearCases))]
        public void BurstClearAndSparseMutationsPreserveRetainedRoots(SnapshotPolicy policy, int mode)
        {
            const int capacity = 512;
            var options = new BufferOptions(capacity, snapshots: policy, snapshotSlots: 1, pageBytes: 256);
            using var map = new NativeDeterministicDictionary<int, Payload, HashPolicy>(options, Allocator.Persistent, new HashPolicy { Collisions = mode == 1 });
            using var set = new NativeDeterministicSparseSet<Payload>(
                new BufferOptions(4, snapshots: policy, snapshotSlots: 1, pageBytes: 256),
                new BufferOptions(64, snapshots: policy, snapshotSlots: 1, pageBytes: 256), Allocator.Persistent);
            int used = mode == 2 ? 8 : capacity; var order = new List<int>();
            for (int i = 0; i < used; i++) { map.Add(i, Payload.Create(i)); order.Add(i); }
            for (int i = 0; i < used; i++) if (mode == 3 || mode == 1 && i % 64 != 0) { map.Remove(i); order.Remove(i); }
            foreach (var handle in new[] { new StableHandle(1, 7), new StableHandle(31, 9), new StableHandle(63, uint.MaxValue) })
                set.Add(handle, Payload.Create((int)handle.Index));
            map.TryCapture(out var mapRoot).ThrowIfFailure(); set.TryCapture(out var setRoot).ThrowIfFailure();
            try
            {
                using var result = new NativeArray<int>(11, Allocator.TempJob);
                new MutationJob { Map = map, Set = set, Result = result }.Schedule().Complete();
                Assert.IsTrue(BurstCompiler.IsEnabled);
                CollectionAssert.AreEqual(new[] { 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, (int)DeterministicStatus.NotFound }, result.ToArray());
                Assert.AreEqual(0, map.Entries.Data->MapState.Used);
                for (int i = 0; i < capacity; i++) Assert.AreEqual(0, map.Entries.Read(i).Active);
                for (int i = 0; i < map.Buckets.Capacity; i++) Assert.AreEqual(0, map.Buckets.Read(i));
                for (int i = 0; i < order.Count; i++)
                { mapRoot.TryGetAt(i, out var pair).ThrowIfFailure(); Assert.AreEqual(order[i], pair.Key); Verify(pair.Value, pair.Key); }
                Verify(set[new StableHandle(63, uint.MaxValue)], 63); Verify(set[new StableHandle(31, 10)], 32);
                foreach (var handle in new[] { new StableHandle(1, 7), new StableHandle(31, 9), new StableHandle(63, uint.MaxValue) })
                { setRoot.TryGet(handle, out var payload).ThrowIfFailure(); Verify(payload, (int)handle.Index); }
                map.Add(999, Payload.Create(999)); Assert.AreEqual(1, map.Entries.Data->MapState.Used);
                map.TryRestore(mapRoot).ThrowIfFailure(); set.TryRestore(setRoot).ThrowIfFailure();
                Assert.AreEqual(order.Count, map.Count); Assert.AreEqual(3, set.Count);
                Assert.IsTrue(set.ContainsKey(new StableHandle(31, 9))); Assert.IsFalse(set.ContainsKey(new StableHandle(31, 10)));
            }
            finally { mapRoot.Dispose(); setRoot.Dispose(); }
        }
    }
}

