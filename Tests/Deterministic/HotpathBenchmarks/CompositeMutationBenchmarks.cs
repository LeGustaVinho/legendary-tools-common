using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class ExtendedHotpathBenchmarks
    {
        public enum ChainLayout { Clustered, Interleaved }
        public enum MultiMapOperation { RemoveAll, RemoveTailValue }
        private const int MultiMapKeys = 8;
        private static int PairKey(int index, int count, ChainLayout layout) => layout == ChainLayout.Clustered ? index / (count / MultiMapKeys) : index % MultiMapKeys;
        private static int PairValue(int index, int count, ChainLayout layout) => layout == ChainLayout.Clustered ? index % (count / MultiMapKeys) : index / MultiMapKeys;
        private static int SlotValue(int index) => unchecked(index * 104729 ^ 0x31415926);
        private static IEnumerable<TestCaseData> MultiMapCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 4096 })
            foreach (ChainLayout layout in new[] { ChainLayout.Clustered, ChainLayout.Interleaved })
            foreach (MultiMapOperation operation in new[] { MultiMapOperation.RemoveAll, MultiMapOperation.RemoveTailValue })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, layout, operation, policy).SetName($"H10_MultiMap_{backend}_{count}_{layout}_{operation}_{policy}");
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct MultiMapRemovalJob : IJob
        {
            internal NativeDeterministicMultiMap<int, int, Int32Policy> Map;
            internal NativeArray<int> Result;
            internal MultiMapOperation Operation;
            internal int LastValue;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (int)(Operation == MultiMapOperation.RemoveAll ? Map.TryRemoveAll(0) : Map.TryRemoveValue(0, LastValue, default(Int32Policy)));
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static void ValidateMultiMap(int count, MultiMapOperation operation, int currentCount, int keyCount,
            int[] values, Func<int, int> readValues)
        {
            int chain = count / MultiMapKeys;
            Assert.AreEqual(count - (operation == MultiMapOperation.RemoveAll ? chain : 1), currentCount);
            Assert.AreEqual(operation == MultiMapOperation.RemoveAll ? MultiMapKeys - 1 : MultiMapKeys, keyCount);
            for (int key = 0; key < MultiMapKeys; key++)
            {
                int length = readValues(key); int expected = key == 0 ? (operation == MultiMapOperation.RemoveAll ? 0 : chain - 1) : chain;
                Assert.AreEqual(expected, length);
                for (int i = 0; i < length; i++) if (values[i] != i) Assert.Fail("Multimap order/value at " + key + ":" + i);
            }
        }
        [TestCaseSource(nameof(MultiMapCases)), Performance]
        public void MultiMapLongChainRemoval(Backend backend, int count, ChainLayout layout, MultiMapOperation operation, SnapshotPolicy policy)
        {
            string distribution = layout + "_8Keys_" + policy + "_1Root";
            int items = operation == MultiMapOperation.RemoveAll ? count / MultiMapKeys : 1;
            var handles = new StableHandle[count]; var validationValues = new int[count / MultiMapKeys];
            if (backend == Backend.Managed)
            {
                using var map = new DeterministicMultiMap<int, int, Int32Policy>(Options(MultiMapKeys, policy, 1), Options(count, policy, 1));
                for (int i = 0; i < count; i++) handles[i] = map.Add(PairKey(i, count, layout), PairValue(i, count, layout));
                map.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    Func<GraphMemoryStats> memory = () => root.Keys.Entries.Owner.MemoryStats + root.Keys.Buckets.Owner.MemoryStats + root.Values.Buffer.Owner.MemoryStats;
                    long baseline = memory().LivePayloadBytes; int status = 0;
                    Benchmark("H10", "MultiMap." + operation, backend, distribution, count, items, 0,
                        () => status = operation == MultiMapOperation.RemoveAll ? (int)map.TryRemoveAll(0) : map.Remove(0, count / MultiMapKeys - 1) ? 0 : -1,
                        () =>
                        {
                            Assert.AreEqual(0, status);
                            ValidateMultiMap(count, operation, map.Count, map.KeyCount, validationValues, key =>
                            {
                                int n = 0; foreach (int value in map.GetValues(key)) validationValues[n++] = value;
                                return n;
                            });
                            for (int i = 0; i < count; i++)
                            {
                                var s = root.TryGetPair(handles[i], out var pair);
                                if (!s.IsSuccess() || pair.Key != PairKey(i, count, layout) || pair.Value != PairValue(i, count, layout)) Assert.Fail("Retained multimap pair " + i);
                            }
                        }, () => map.TryRestore(root).ThrowIfFailure(),
                        () => { var d = Stats(memory()); d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.active = count; d.retainedRoots = 1; d.payloadElementBytes = sizeof(MultiMapValue<int, int>); return d; });
                }
                finally { root.Dispose(); }
            }
            else
            {
                using var map = new NativeDeterministicMultiMap<int, int, Int32Policy>(Options(MultiMapKeys, policy, 1), Options(count, policy, 1), Allocator.Persistent);
                using var result = new NativeArray<int>(2, Allocator.Persistent);
                for (int i = 0; i < count; i++) handles[i] = map.Add(PairKey(i, count, layout), PairValue(i, count, layout));
                map.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    long baseline = (map.Entries.MemoryStats + map.Buckets.MemoryStats + map.Values.MemoryStats).LivePayloadBytes;
                    var job = new MultiMapRemovalJob { Map = map, Result = result, Operation = operation, LastValue = count / MultiMapKeys - 1 };
                    Benchmark("H10", "MultiMap." + operation, backend, distribution, count, items, 0,
                        () => job.Schedule().Complete(),
                        () =>
                        {
                            CheckBurst(result[0]); Assert.AreEqual(0, result[1]);
                            ValidateMultiMap(count, operation, map.Count, map.KeyCount, validationValues, key =>
                            {
                                int n = 0; var iterator = map.GetValues(key).GetEnumerator(); while (iterator.MoveNext()) validationValues[n++] = iterator.Current;
                                return n;
                            });
                            for (int i = 0; i < count; i++)
                            {
                                var s = root.TryGetPair(handles[i], out var pair);
                                if (!s.IsSuccess() || pair.Key != PairKey(i, count, layout) || pair.Value != PairValue(i, count, layout)) Assert.Fail("Retained native multimap pair " + i);
                            }
                        }, () => map.TryRestore(root).ThrowIfFailure(),
                        () =>
                        {
                            var d = Stats(map.Entries.MemoryStats + map.Buckets.MemoryStats + map.Values.MemoryStats);
                            d.reservedNativeMetadataBytes = NativeMetadata(map.Entries) + NativeMetadata(map.Buckets) + NativeMetadata(map.Values);
                            d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.active = count; d.retainedRoots = 1; d.payloadElementBytes = sizeof(MultiMapValue<int, int>); return d;
                        });
                }
                finally { root.Dispose(); }
            }
        }
        private static IEnumerable<TestCaseData> SlotClearCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int used in new[] { 1024, 65536 })
            foreach (bool sparse in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, used, sparse, policy).SetName($"H11_SlotClear_{backend}_Used{used}_Active{(sparse ? 64 : used)}_{policy}");
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct SlotClearJob : IJob
        {
            internal NativeDeterministicSlotMap<int> Map;
            internal NativeArray<int> Result;
            public void Execute() { int managed = 0; MarkManaged(ref managed); Result[0] = managed; Result[1] = (int)Map.TryClear(); }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCaseSource(nameof(SlotClearCases)), Performance]
        public void ClearSparseSlotHighWatermark(Backend backend, int used, bool sparse, SnapshotPolicy policy)
        {
            int active = sparse ? 64 : used, stride = used / active; var handles = new StableHandle[used];
            string distribution = "Active" + active + "_" + policy + "_1Root";
            if (backend == Backend.Managed)
            {
                using var map = new DeterministicSlotMap<int>(Options(used, policy, 1));
                for (int i = 0; i < used; i++) handles[i] = map.Add(SlotValue(i));
                for (int i = 0; i < used; i++) if (i % stride != 0) map.TryRemove(handles[i]).ThrowIfFailure();
                Assert.AreEqual(active, map.Count); map.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    int status = 0; long baseline = map.Storage.Owner.MemoryStats.LivePayloadBytes;
                    Benchmark("H11", "SlotMap.Clear", backend, distribution, used, active, 0, () => status = (int)map.TryClear(),
                        () =>
                        {
                            Assert.AreEqual(0, status); Assert.AreEqual(0, map.Count);
                            for (int i = 0; i < used; i++)
                            {
                                if (map.TryGet(handles[i], out _) != DeterministicStatus.NotFound) Assert.Fail("Cleared handle " + i);
                                var s = root.TryGet(handles[i], out int value);
                                if (i % stride == 0 ? !s.IsSuccess() || value != SlotValue(i) : s != DeterministicStatus.NotFound) Assert.Fail("Retained slot " + i);
                            }
                        }, () => map.TryRestore(root).ThrowIfFailure(),
                        () => { var d = Stats(map.Storage.Owner.MemoryStats); d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.active = active; d.universe = used; d.retainedRoots = 1; d.payloadElementBytes = sizeof(SlotMapNode<int>); return d; });
                }
                finally { root.Dispose(); }
            }
            else
            {
                using var map = new NativeDeterministicSlotMap<int>(Options(used, policy, 1), Allocator.Persistent);
                using var result = new NativeArray<int>(2, Allocator.Persistent);
                for (int i = 0; i < used; i++) handles[i] = map.Add(SlotValue(i));
                for (int i = 0; i < used; i++) if (i % stride != 0) map.TryRemove(handles[i]).ThrowIfFailure();
                Assert.AreEqual(active, map.Count); map.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    long baseline = map.Storage.MemoryStats.LivePayloadBytes; var job = new SlotClearJob { Map = map, Result = result };
                    Benchmark("H11", "SlotMap.Clear", backend, distribution, used, active, 0, () => job.Schedule().Complete(),
                        () =>
                        {
                            CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(0, map.Count);
                            for (int i = 0; i < used; i++)
                            {
                                if (map.TryGet(handles[i], out _) != DeterministicStatus.NotFound) Assert.Fail("Cleared native handle " + i);
                                var s = root.TryGet(handles[i], out int value);
                                if (i % stride == 0 ? !s.IsSuccess() || value != SlotValue(i) : s != DeterministicStatus.NotFound) Assert.Fail("Retained native slot " + i);
                            }
                        }, () => map.TryRestore(root).ThrowIfFailure(),
                        () => { var d = Stats(map.Storage.MemoryStats); d.reservedNativeMetadataBytes = NativeMetadata(map.Storage); d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline; d.active = active; d.universe = used; d.retainedRoots = 1; d.payloadElementBytes = sizeof(SlotMapNode<int>); return d; });
                }
                finally { root.Dispose(); }
            }
        }
    }
}
