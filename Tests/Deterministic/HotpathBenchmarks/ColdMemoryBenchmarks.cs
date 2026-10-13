using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class HotpathOpportunityBenchmarks
    {
        private static IEnumerable<TestCaseData> ColdCases()
        {
            foreach (var backend in new[] { Backend.Managed, Backend.NativeHost })
            foreach (AllocationPolicy allocation in Enum.GetValues(typeof(AllocationPolicy)))
            foreach (var policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
            foreach (int captures in new[] { 0, 4 })
                yield return new TestCaseData(backend, allocation, policy, captures);
        }
        [TestCaseSource(nameof(ColdCases)), Performance]
        public void ConstructionAndFirstRetainedCaptures(Backend backend, AllocationPolicy allocation, SnapshotPolicy policy, int captures)
        {
            const int live = 64, maximum = 65536;
            // These are configuration alternatives: Fixed reserves maximum; Grow starts at live.
            // Construction, 64 inserts and first captures are timed. Releasing the preceding owner is not.
            var options = new BufferOptions(allocation == AllocationPolicy.Fixed ? maximum : live, maximum, allocation, policy, 4, 4096);
            string distribution = allocation + "_" + policy + "_" + captures + "Captures_64Live";
            if (backend == Backend.Managed)
            {
                DeterministicList<int> list = null; var snapshots = new ManagedSequenceSnapshot<int>[captures];
                Action dispose = () => { for (int s = 0; s < captures; s++) { snapshots[s].Dispose(); snapshots[s] = default; } list?.Dispose(); list = null; };
                try
                {
                    Benchmark("H7", "List.ConstructAndFirstCapture", backend, distribution, maximum, 1, live * sizeof(int),
                        () => { list = new DeterministicList<int>(options); for (int i = 0; i < live; i++) list.TryAdd(WorldValue(i)).ThrowIfFailure(); for (int s = 0; s < captures; s++) list.TryCapture(out snapshots[s]).ThrowIfFailure(); },
                        () =>
                        {
                            Assert.AreEqual(live, list.Count);
                            for (int i = 0; i < live; i++) if (list[i] != WorldValue(i)) Assert.Fail("Cold list value " + i);
                            for (int s = 0; s < captures; s++) { Assert.AreEqual(live, snapshots[s].Count); for (int i = 0; i < live; i++) { snapshots[s].TryGet(i, out int value).ThrowIfFailure(); if (value != WorldValue(i)) Assert.Fail("First snapshot value " + i); } }
                        }, dispose,
                        () => { var d = Stats(list.Storage.Owner.MemoryStats); d.active = live; d.universe = maximum; d.retainedRoots = captures; d.payloadElementBytes = sizeof(int); return d; }, true);
                }
                finally { dispose(); }
            }
            else
            {
                NativeDeterministicList<int> list = default; var snapshots = new NativeSequenceSnapshot<int>[captures];
                Action dispose = () => { for (int s = 0; s < captures; s++) { snapshots[s].Dispose(); snapshots[s] = default; } if (list.IsCreated) list.Dispose(); list = default; };
                try
                {
                    Benchmark("H7", "List.ConstructAndFirstCapture", backend, distribution, maximum, 1, live * sizeof(int),
                        () => { list = new NativeDeterministicList<int>(options, Allocator.Persistent); for (int i = 0; i < live; i++) list.TryAdd(WorldValue(i)).ThrowIfFailure(); for (int s = 0; s < captures; s++) list.TryCapture(out snapshots[s]).ThrowIfFailure(); },
                        () =>
                        {
                            Assert.AreEqual(live, list.Count);
                            for (int i = 0; i < live; i++) if (list[i] != WorldValue(i)) Assert.Fail("Native cold list value " + i);
                            for (int s = 0; s < captures; s++) { Assert.AreEqual(live, snapshots[s].Count); for (int i = 0; i < live; i++) { snapshots[s].TryGet(i, out int value).ThrowIfFailure(); if (value != WorldValue(i)) Assert.Fail("Native first snapshot value " + i); } }
                        }, dispose,
                        () => { var d = Stats(list.Storage.MemoryStats); d.reservedNativeMetadataBytes = NativeMetadata(list.Storage); d.active = live; d.universe = maximum; d.retainedRoots = captures; d.payloadElementBytes = sizeof(int); return d; }, true);
                }
                finally { dispose(); }
            }
        }
    }
}
