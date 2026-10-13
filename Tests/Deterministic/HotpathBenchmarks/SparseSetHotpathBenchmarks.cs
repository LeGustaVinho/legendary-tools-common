using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class DictionarySparseHotpathBenchmarks
    {
        public enum SparseQuery { GetHit, ContainsHit, EmptySlot, StaleGeneration }
        internal enum SparseOperation { Get, Contains, Remove, Clear, Add }
        private static StableHandle[] SparseHandles(int count, int universe, bool spread)
        {
            var handles = new StableHandle[count];
            for (int i = 0; i < count; i++)
                handles[i] = new StableHandle((uint)(spread ? 1 + (long)i * (universe - 2) / (count - 1) : i + 1), (uint)(i % 7 + 2));
            return handles;
        }
        private static StableHandle[] StridedSparseHandles(int count)
        {
            var handles = new StableHandle[count];
            for (int i = 0; i < count; i++) handles[i] = new StableHandle((uint)(1 + i * 8), (uint)(i % 7 + 2));
            return handles;
        }
        [BurstCompile(CompileSynchronously = true)]
        internal struct SparseOperationJob<T> : IJob where T : unmanaged, IPayload
        {
            internal NativeDeterministicSparseSet<T> Set;
            [ReadOnly] internal NativeArray<StableHandle> Inputs;
            [ReadOnly] internal NativeArray<T> Values;
            internal NativeArray<T> OutputValues;
            internal NativeArray<int> Statuses, Marker;
            internal SparseOperation Operation;
            public void Execute()
            {
                int marker = 0; MarkManaged(ref marker); Marker[0] = marker;
                for (int i = 0; i < Inputs.Length; i++)
                {
                    var handle = Inputs[i];
                    if (Operation == SparseOperation.Get)
                    {
                        Statuses[i] = (int)Set.TryGet(handle, out var value); OutputValues[i] = value;
                    }
                    else if (Operation == SparseOperation.Contains)
                    {
                        Statuses[i] = Set.ContainsKey(handle) ? 0 : (int)DeterministicStatus.NotFound; OutputValues[i] = default;
                    }
                    else
                    {
                        if (Operation == SparseOperation.Add)
                        { var value = Values[i]; Statuses[i] = (int)Set.TryAdd(handle, value); }
                        else Statuses[i] = (int)(Operation == SparseOperation.Remove ? Set.TryRemove(handle) : Set.TryClear());
                    }
                }
            }
        }
        private static void RunSparse<T>(SparseOwner<T> owner, SparseOperation operation) where T : unmanaged, IPayload
        {
            if (owner.Backend == Backend.BurstJob)
            {
                new SparseOperationJob<T> { Set = owner.Native, Inputs = owner.Inputs, Values = owner.Values,
                    OutputValues = owner.OutputValues, Statuses = owner.Statuses, Marker = owner.Marker, Operation = operation }.Schedule().Complete();
                return;
            }
            for (int i = 0; i < owner.Inputs.Length; i++)
            {
                var handle = owner.Inputs[i];
                if (operation == SparseOperation.Get)
                {
                    owner.Statuses[i] = (int)owner.Managed.TryGet(handle, out var value); owner.OutputValues[i] = value;
                }
                else if (operation == SparseOperation.Contains)
                {
                    owner.Statuses[i] = owner.Managed.ContainsKey(handle) ? 0 : (int)DeterministicStatus.NotFound; owner.OutputValues[i] = default;
                }
                else
                {
                    if (operation == SparseOperation.Add)
                    { var value = owner.Values[i]; owner.Statuses[i] = (int)owner.Managed.TryAdd(handle, value); }
                    else owner.Statuses[i] = (int)(operation == SparseOperation.Remove ? owner.Managed.TryRemove(handle) : owner.Managed.TryClear());
                }
            }
        }
        private static IEnumerable<TestCaseData> SparseQueryCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (int bytes in new[] { 64, 256 })
            foreach (SparseQuery query in Enum.GetValues(typeof(SparseQuery)))
                yield return new TestCaseData(backend, count, bytes, query).SetName($"DS7_SparseQuery_{backend}_Count{count}_Payload{bytes}_{query}");
        }
        [TestCaseSource(nameof(SparseQueryCases)), Performance]
        public void SparseLookupPayloadCopiesAndGenerationMisses(Backend backend, int count, int bytes, SparseQuery query)
        {
            if (bytes == 64) SparseQueries<Payload64>(backend, count, query);
            else SparseQueries<Payload256>(backend, count, query);
        }
        private void SparseQueries<T>(Backend backend, int count, SparseQuery query) where T : unmanaged, IPayload
        {
            int universe = count * 8 + 1; var handles = StridedSparseHandles(count);
            using var owner = new SparseOwner<T>(backend, Options(count), Options(universe), 64, 64);
            foreach (var handle in handles) owner.AddInitial(handle);
            owner.CaptureBaseline(handles, false);
            for (int i = 0; i < 64; i++)
            {
                var handle = handles[i * 73 & (count - 1)];
                owner.Inputs[i] = query == SparseQuery.EmptySlot ? new StableHandle(handle.Index + 1, handle.Generation) :
                    query == SparseQuery.StaleGeneration ? new StableHandle(handle.Index, handle.Generation - 1) : handle;
            }
            int expectedStatus = query == SparseQuery.EmptySlot || query == SparseQuery.StaleGeneration ? (int)DeterministicStatus.NotFound : 0; ulong digest = 0;
            Benchmark("DS7", "SparseSet." + query, backend, "Stride8_Payload" + sizeof(T) + "_Batch64_0Roots", count, 64, 64 * sizeof(T),
                () => RunSparse(owner, query == SparseQuery.ContainsHit ? SparseOperation.Contains : SparseOperation.Get),
                () =>
                {
                    owner.CheckStatus(expectedStatus); digest = owner.Validate(handles, false);
                    for (int i = 0; i < 64; i++)
                    {
                        digest = Mix(digest, (ulong)owner.Statuses[i]);
                        if (query == SparseQuery.GetHit) digest = VerifyPayload(owner.OutputValues[i], (int)owner.Inputs[i].Index, 0, digest);
                        else VerifyZero(owner.OutputValues[i]);
                    }
                }, diagnostics: owner.Diagnostics, digest: () => digest);
        }

        private static IEnumerable<TestCaseData> SparseRemovalCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (int bytes in new[] { 64, 256 })
            foreach (bool last in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, count, bytes, last, policy).SetName($"DS8_SparseRemove_{backend}_Count{count}_Payload{bytes}_{(last ? "Last" : "SwapFirst")}_{policy}");
        }
        [TestCaseSource(nameof(SparseRemovalCases)), Performance]
        public void SparseLastRemovalVersusSwapRemoval(Backend backend, int count, int bytes, bool last, SnapshotPolicy policy)
        {
            if (bytes == 64) SparseRemovals<Payload64>(backend, count, last, policy);
            else SparseRemovals<Payload256>(backend, count, last, policy);
        }
        private void SparseRemovals<T>(Backend backend, int count, bool last, SnapshotPolicy policy) where T : unmanaged, IPayload
        {
            int universe = count * 8 + 1; var original = StridedSparseHandles(count); var expected = new StableHandle[count - 1];
            Array.Copy(original, expected, expected.Length); if (!last) expected[0] = original[count - 1];
            var removed = original[last ? count - 1 : 0];
            using var owner = new SparseOwner<T>(backend, Options(count, policy, 1), Options(universe, policy, 1), 1);
            foreach (var handle in original) owner.AddInitial(handle);
            owner.CaptureBaseline(original); owner.Inputs[0] = removed; ulong digest = 0;
            Benchmark("DS8", last ? "SparseSet.RemoveLast" : "SparseSet.SwapRemoveFirst", backend, "Stride8_Payload" + sizeof(T) + "_" + policy + "_1Root", count, 1, 0,
                () => RunSparse(owner, SparseOperation.Remove),
                () =>
                {
                    owner.CheckStatus(0); digest = owner.Validate(expected);
                    var cell = owner.Index((int)removed.Index); Assert.AreEqual(0, cell.DenseIndex); Assert.AreEqual(0, cell.Generation);
                    Assert.AreEqual(count, owner.Capacity); Assert.AreEqual(universe, owner.Slots);
                }, owner.Reset, owner.Diagnostics, () => digest);
        }

        private static IEnumerable<TestCaseData> SparseClearCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int universe in new[] { 1024, 1048576 })
            foreach (bool spread in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, universe, spread, policy).SetName($"DS9_SparseClear_{backend}_Universe{universe}_{(spread ? "Spread64" : "Clustered64")}_{policy}");
        }
        [TestCaseSource(nameof(SparseClearCases)), Performance]
        public void SparseClearPageLocalityWithHugeUniverse(Backend backend, int universe, bool spread, SnapshotPolicy policy)
        {
            var handles = SparseHandles(64, universe, spread);
            using var owner = new SparseOwner<Payload64>(backend, Options(64, policy, 1), Options(universe, policy, 1), 1);
            foreach (var handle in handles) owner.AddInitial(handle);
            owner.CaptureBaseline(handles); ulong digest = 0;
            Benchmark("DS9", "SparseSet.Clear64", backend, (spread ? "Spread64" : "Clustered64") + "_" + policy + "_1Root", universe, 64, 0,
                () => RunSparse(owner, SparseOperation.Clear),
                () =>
                {
                    owner.CheckStatus(0); digest = owner.Validate(Array.Empty<StableHandle>());
                    foreach (var handle in handles) { var cell = owner.Index((int)handle.Index); Assert.AreEqual(0, cell.DenseIndex); Assert.AreEqual(0, cell.Generation); }
                    for (int i = 0; i < handles.Length; i++) { var entry = owner.Entry(i); Assert.IsFalse(entry.Handle.IsValid); VerifyZero(entry.Value); }
                }, owner.Reset, owner.Diagnostics, () => digest);
        }

        private static IEnumerable<TestCaseData> SparseGrowthCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int universe in new[] { 65536, 1048576 })
            foreach (bool growDense in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, universe, growDense, policy).SetName($"DS10_SparseGrow_{backend}_Universe{universe}_{(growDense ? "BothBuffers" : "SparseOnly")}_{policy}");
        }
        [TestCaseSource(nameof(SparseGrowthCases)), Performance]
        public void SparseInsertionAtHighIndexAndBothCapacityBoundaries(Backend backend, int universe, bool growDense, SnapshotPolicy policy)
        {
            var original = SparseHandles(64, 128, false); var added = new StableHandle((uint)(universe - 1), 3);
            var expected = new StableHandle[65]; Array.Copy(original, expected, 64); expected[64] = added;
            SparseOwner<Payload64> owner = null; ulong digest = 0;
            Action setup = () =>
            {
                owner?.Dispose(); owner = new SparseOwner<Payload64>(backend, Growing(growDense ? 64 : 128, 128, policy), Growing(128, universe, policy), 1);
                foreach (var handle in original) owner.AddInitial(handle);
                owner.CaptureBaseline(original); owner.Inputs[0] = added; owner.Values[0] = Payload<Payload64>((int)added.Index);
            };
            try
            {
                Benchmark("DS10", growDense ? "SparseSet.GrowBoth" : "SparseSet.GrowSparse", backend, "64Live_Payload64_" + policy + "_1Root_RecreatedOwner", universe, 1, 64,
                    () => RunSparse(owner, SparseOperation.Add),
                    () => { owner.CheckStatus(0); Assert.AreEqual(128, owner.Capacity); Assert.AreEqual(universe, owner.Slots); digest = owner.Validate(expected); },
                    setup, () => owner.Diagnostics(), () => digest, true);
            }
            finally { owner?.Dispose(); }
        }
    }
}

