using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class HotpathOpportunityBenchmarks
    {
        private static IEnumerable<TestCaseData> SparseCases()
        {
            foreach (var backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int universe in new[] { 1024, 65536, 1048576 })
            foreach (int active in new[] { 64, 512 })
            foreach (bool ordered in new[] { false, true })
                yield return new TestCaseData(backend, universe, active, ordered);
        }
        private static SparseSetEntry<int> SparseExpected(int logical, int universe, int active)
        {
            uint index = (uint)((long)logical * (universe - 1) / (active - 1));
            return new SparseSetEntry<int>(new StableHandle(index, (uint)(logical % 7 + 1)), WorldValue((int)index));
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct SparseScanJob : IJob
        {
            internal bool Ordered;
            [ReadOnly] internal NativeDeterministicSparseSet<int> Set;
            internal NativeArray<SparseSetEntry<int>> Output;
            internal NativeArray<int> Result;
            [BurstDiscard] private static void MarkManaged(ref int marker) { marker = 1; }
            public void Execute()
            {
                int marker = 0; MarkManaged(ref marker); Result[0] = marker;
                var e = Ordered ? Set.OrderedByEntity.GetEnumerator() : Set.GetEnumerator(); int count = 0;
                while (e.MoveNext()) Output[count++] = e.Current;
                Result[1] = count;
            }
        }
        [TestCaseSource(nameof(SparseCases)), Performance]
        public void SparseDenseAndEntityOrderedEnumeration(Backend backend, int universe, int active, bool ordered)
        {
            // The same identities/content are used by both views. Dense order is the permutation of insertion.
            var expected = new SparseSetEntry<int>[active];
            for (int i = 0; i < active; i++) expected[i] = SparseExpected(ordered ? i : i * 73 & (active - 1), universe, active);
            string distribution = active + "Active_SpreadToLastSlot", operation = ordered ? "Sparse.OrderedScan" : "Sparse.DenseScan";
            if (backend == Backend.Managed)
            {
                using (var set = new DeterministicSparseSet<int>(Options(active), Options(universe)))
                {
                    for (int i = 0; i < active; i++) { var v = SparseExpected(i * 73 & (active - 1), universe, active); set.TryAdd(v.Handle, v.Value).ThrowIfFailure(); }
                    var output = new SparseSetEntry<int>[active]; int actual = 0;
                    Benchmark("H4", operation, backend, distribution, universe, active, 0,
                        () => { actual = 0; var e = ordered ? set.OrderedByEntity.GetEnumerator() : set.GetEnumerator(); while (e.MoveNext()) output[actual++] = e.Current; },
                        () => { Assert.AreEqual(active, actual); for (int i = 0; i < active; i++) if (output[i].Handle != expected[i].Handle || output[i].Value != expected[i].Value) Assert.Fail("Sparse content/order at " + i); },
                        diagnostics: () => { var d = Stats(set.Dense.Owner.MemoryStats + set.Sparse.Owner.MemoryStats); d.universe = universe; d.active = active; d.payloadElementBytes = sizeof(SparseSetEntry<int>); return d; });
                }
            }
            else
            {
                using (var set = new NativeDeterministicSparseSet<int>(Options(active), Options(universe), Allocator.Persistent))
                using (var output = new NativeArray<SparseSetEntry<int>>(active, Allocator.Persistent))
                using (var result = new NativeArray<int>(2, Allocator.Persistent))
                {
                    for (int i = 0; i < active; i++) { var v = SparseExpected(i * 73 & (active - 1), universe, active); set.TryAdd(v.Handle, v.Value).ThrowIfFailure(); }
                    var job = new SparseScanJob { Ordered = ordered, Set = set, Output = output, Result = result };
                    Benchmark("H4", operation, backend, distribution, universe, active, 0, () => job.Schedule().Complete(),
                        () => { CheckBurst(result[0]); Assert.AreEqual(active, result[1]); for (int i = 0; i < active; i++) if (output[i].Handle != expected[i].Handle || output[i].Value != expected[i].Value) Assert.Fail("Burst sparse content/order at " + i); },
                        diagnostics: () => { var d = Stats(set.Dense.MemoryStats + set.Sparse.MemoryStats); d.reservedNativeMetadataBytes = NativeMetadata(set.Dense) + NativeMetadata(set.Sparse); d.universe = universe; d.active = active; d.payloadElementBytes = sizeof(SparseSetEntry<int>); return d; });
                }
            }
        }
        public enum BitDensity { Dense, Alternating, HighBitPerWord }
        public enum BitOperation { Count, Enumerate }
        private static IEnumerable<TestCaseData> BitCases()
        {
            foreach (var backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int length in new[] { 4096, 65536 })
            foreach (BitDensity density in Enum.GetValues(typeof(BitDensity)))
            foreach (BitOperation operation in Enum.GetValues(typeof(BitOperation)))
                yield return new TestCaseData(backend, length, density, operation);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct BitScanJob : IJob
        {
            internal BitOperation Operation;
            [ReadOnly] internal NativeDeterministicBitSet Bits;
            internal NativeArray<int> Output, Result;
            [BurstDiscard] private static void MarkManaged(ref int marker) { marker = 1; }
            public void Execute()
            {
                int marker = 0; MarkManaged(ref marker); Result[0] = marker;
                if (Operation == BitOperation.Count) Result[1] = Bits.Count;
                else
                {
                    int count = 0, next = Bits.NextSetBit(0);
                    while (next >= 0) { Output[count++] = next; next = Bits.NextSetBit(next + 1); }
                    Result[1] = count;
                }
            }
        }
        [TestCaseSource(nameof(BitCases)), Performance]
        public void BitsetPopulationAndSetBitEnumeration(Backend backend, int length, BitDensity density, BitOperation operation)
        {
            var expected = new List<int>();
            for (int i = 0; i < length; i++) if (density == BitDensity.Dense || (density == BitDensity.Alternating ? (i & 1) == 0 : (i & 63) == 63)) expected.Add(i);
            int words = (length + 63) / 64, work = operation == BitOperation.Count ? words : expected.Count;
            Func<Diagnostics> memory = () => new Diagnostics { reservedPayloadBytes = words * 8L, livePayloadBytes = words * 8L, universe = length, active = expected.Count, payloadElementBytes = 8 };
            if (backend == Backend.Managed)
            {
                using (var bits = new DeterministicBitSet(length, SnapshotPolicy.FullPerTick, 0))
                {
                    foreach (int index in expected) bits[index] = true;
                    var output = new int[expected.Count]; int count = 0;
                    Benchmark("H5", "Bitset." + operation, backend, density.ToString(), length, work, words * 8,
                        () =>
                        {
                            if (operation == BitOperation.Count) count = bits.Count;
                            else { count = 0; for (int bit = bits.NextSetBit(0); bit >= 0; bit = bits.NextSetBit(bit + 1)) output[count++] = bit; }
                        },
                        () => { Assert.AreEqual(expected.Count, count); if (operation == BitOperation.Enumerate) for (int i = 0; i < count; i++) if (output[i] != expected[i]) Assert.Fail("Bitset position " + i); },
                        diagnostics: memory);
                }
            }
            else
            {
                using (var bits = new NativeDeterministicBitSet(length, Allocator.Persistent, SnapshotPolicy.FullPerTick, 0))
                using (var output = new NativeArray<int>(expected.Count, Allocator.Persistent))
                using (var result = new NativeArray<int>(2, Allocator.Persistent))
                {
                    foreach (int index in expected) bits.TrySet(index, true).ThrowIfFailure();
                    var job = new BitScanJob { Operation = operation, Bits = bits, Output = output, Result = result };
                    Benchmark("H5", "Bitset." + operation, backend, density.ToString(), length, work, words * 8, () => job.Schedule().Complete(),
                        () => { CheckBurst(result[0]); Assert.AreEqual(expected.Count, result[1]); if (operation == BitOperation.Enumerate) for (int i = 0; i < expected.Count; i++) if (output[i] != expected[i]) Assert.Fail("Burst bitset position " + i); },
                        diagnostics: memory);
                }
            }
        }
    }
}
