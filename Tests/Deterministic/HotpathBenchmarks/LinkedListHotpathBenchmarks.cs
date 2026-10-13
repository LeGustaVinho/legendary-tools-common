using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class NetworkLifecycleHotpathBenchmarks
    {
        public enum LinkedOperation { Traverse, Churn64 }
        private const int LinkedChurn = 64;
        private static IEnumerable<TestCaseData> LinkedCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int active in new[] { 256, 2048 })
            foreach (bool fragmented in new[] { false, true })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
            foreach (LinkedOperation operation in Enum.GetValues(typeof(LinkedOperation)))
                yield return new TestCaseData(backend, active, fragmented, policy, operation).SetName($"H18_LinkedList_{backend}_Active{active}_{(fragmented ? "WideSlots" : "DenseSlots")}_{policy}_{operation}");
        }
        private static StableHandle[] BuildLinked(int active, int[] permutation, Func<int, StableHandle> add, Action<StableHandle> remove)
        {
            var old = new StableHandle[permutation.Length]; for (int i = 0; i < old.Length; i++) old[i] = add(~Value(i));
            // FIFO reuse follows this seeded permutation, producing the same logical sequence in dense/wide slots.
            foreach (int index in permutation) remove(old[index]);
            var handles = new StableHandle[active];
            for (int i = 0; i < active; i++)
            {
                handles[i] = add(Value(i));
                Assert.AreEqual(new StableHandle((uint)permutation[i], 2), handles[i]);
            }
            return handles;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct LinkedJob : IJob
        {
            internal NativeDeterministicLinkedList<int> List;
            internal LinkedOperation Operation;
            internal int Active;
            internal NativeArray<int> Output, Result;
            internal NativeArray<StableHandle> Replacements;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed; int status = 0, count = 0;
                if (Operation == LinkedOperation.Traverse)
                {
                    var iterator = List.GetEnumerator();
                    while (true)
                    {
                        var next = iterator.TryMoveNext(out bool moved); status |= (int)next;
                        if (!next.IsSuccess() || !moved) break;
                        Output[count++] = iterator.Current;
                    }
                }
                else
                {
                    for (int i = 0; i < LinkedChurn; i++)
                    {
                        status |= (int)List.TryRemoveFirst(); status |= (int)List.TryAddLast(Value(Active + i), out var handle);
                        Replacements[i] = handle;
                    }
                    count = List.Count;
                }
                Result[1] = status; Result[2] = count;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        private static ulong ValidateLinked(LinkedOperation operation, int active, int[] permutation, StableHandle[] baseline,
            StableHandle first, StableHandle last, Func<StableHandle, int> get, Func<StableHandle, StableHandle> previous,
            Func<StableHandle, StableHandle> next, Func<int, int> retained, Func<StableHandle, DeterministicStatus> currentStatus,
            Func<int, StableHandle> replacement, Func<int, int> output)
        {
            bool churn = operation == LinkedOperation.Churn64; int skip = churn ? LinkedChurn : 0;
            StableHandle at = first, prev = default; ulong digest = DigestSeed;
            for (int i = 0; i < active; i++)
            {
                bool added = i >= active - skip;
                int logical = i + skip;
                var expected = added ? replacement(i - (active - skip)) : baseline[logical];
                Assert.AreEqual(expected, at); Assert.AreEqual(prev, previous(at)); Assert.AreEqual(Value(logical), get(at));
                if (!churn) Assert.AreEqual(Value(i), output(i));
                digest = Mix(digest, at.Packed); digest = Mix(digest, unchecked((uint)get(at)));
                prev = at; at = next(at);
            }
            Assert.IsFalse(at.IsValid); Assert.AreEqual(prev, last);
            // Traverse back as well: a correct forward chain can still hide a damaged Previous link.
            at = last;
            for (int i = active - 1; i >= 0; i--)
            { Assert.AreEqual(Value(i + skip), get(at)); at = previous(at); }
            Assert.IsFalse(at.IsValid);
            for (int i = 0; i < active; i++) Assert.AreEqual(Value(i), retained(i), "Retained root changed.");
            if (churn) for (int i = 0; i < LinkedChurn; i++)
            {
                Assert.AreEqual(DeterministicStatus.NotFound, currentStatus(baseline[i]));
                bool wide = permutation.Length > active;
                Assert.AreEqual(new StableHandle((uint)permutation[wide ? active + i : i], wide ? 2u : 3u), replacement(i), "FIFO/generation changed.");
            }
            return digest;
        }
        private static void CheckRetainedLinks(StableHandle[] handles, int index, in SlotMapNode<LinkedListValue<int>> node)
        {
            Assert.AreEqual(1, node.Active); Assert.AreEqual(handles[index].Generation, node.Generation);
            Assert.AreEqual(index == 0 ? default : handles[index - 1], node.Value.Previous);
            Assert.AreEqual(index == handles.Length - 1 ? default : handles[index + 1], node.Value.Next);
            Assert.AreEqual(Value(index), node.Value.Value);
        }
        [TestCaseSource(nameof(LinkedCases)), Performance]
        public void LinkedTraversalAndChurnWithFragmentedSlotsAndRetainedRoot(Backend backend, int active, bool fragmented, SnapshotPolicy policy, LinkedOperation operation)
        {
            int used = active * (fragmented ? 8 : 1); var permutation = Permutation(used);
            string distribution = (fragmented ? "WideSlots" : "DenseSlots") + "_" + policy + "_1Root";
            int items = operation == LinkedOperation.Traverse ? active : LinkedChurn * 2;
            if (backend == Backend.Managed)
            {
                using var list = new DeterministicLinkedList<int>(Options(used, policy, 1));
                var handles = BuildLinked(active, permutation, list.AddLast, list.Remove);
                var output = new int[active]; var replacements = new StableHandle[LinkedChurn];
                list.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    long baseline = list.Storage.Owner.MemoryStats.LivePayloadBytes; int status = 0, count = 0; ulong digest = 0;
                    Benchmark("H18", "LinkedList." + operation, backend, distribution, active, items, 0,
                        () =>
                        {
                            status = count = 0;
                            if (operation == LinkedOperation.Traverse)
                            {
                                var iterator = list.GetEnumerator();
                                while (true)
                                {
                                    var next = iterator.TryMoveNext(out bool moved); status |= (int)next;
                                    if (!next.IsSuccess() || !moved) break;
                                    output[count++] = iterator.Current;
                                }
                            }
                            else
                            {
                                for (int i = 0; i < LinkedChurn; i++)
                                { status |= (int)list.TryRemove(list.First); status |= (int)list.TryAddLast(Value(active + i), out replacements[i]); }
                                count = list.Count;
                            }
                        },
                        () =>
                        {
                            Assert.AreEqual(0, status); Assert.AreEqual(active, count); Assert.AreEqual(active, list.Count); Assert.AreEqual(active, root.Count);
                            digest = ValidateLinked(operation, active, permutation, handles, list.First, list.Last,
                                h => list[h], h => { list.TryGetLinks(h, out var p, out _).ThrowIfFailure(); return p; },
                                h => { list.TryGetLinks(h, out _, out var n).ThrowIfFailure(); return n; },
                                i => { root.Buffer.TryGet((int)handles[i].Index, out var node).ThrowIfFailure(); CheckRetainedLinks(handles, i, node); return node.Value.Value; }, h => list.TryGet(h, out _),
                                i => replacements[i], i => output[i]);
                        }, () => list.TryRestore(root).ThrowIfFailure(),
                        () =>
                        {
                            var d = Stats(list.Storage.Owner.MemoryStats); d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline;
                            d.universe = used; d.active = active; d.retainedRoots = 1; d.payloadElementBytes = sizeof(SlotMapNode<LinkedListValue<int>>);
                            d.externalBufferBytes = (long)permutation.Length * sizeof(int) + (long)(handles.Length + replacements.Length) * sizeof(StableHandle) + (long)output.Length * sizeof(int);
                            return d;
                        }, () => digest);
                }
                finally { root.Dispose(); }
            }
            else
            {
                using var list = new NativeDeterministicLinkedList<int>(Options(used, policy, 1), Allocator.Persistent);
                var handles = BuildLinked(active, permutation, list.AddLast, list.Remove);
                using var output = new NativeArray<int>(active, Allocator.Persistent);
                using var replacements = new NativeArray<StableHandle>(LinkedChurn, Allocator.Persistent);
                using var result = new NativeArray<int>(3, Allocator.Persistent);
                list.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    long baseline = list.Storage.MemoryStats.LivePayloadBytes; ulong digest = 0;
                    var job = new LinkedJob { List = list, Operation = operation, Active = active, Output = output, Replacements = replacements, Result = result };
                    Benchmark("H18", "LinkedList." + operation, backend, distribution, active, items, 0, () => job.Schedule().Complete(),
                        () =>
                        {
                            CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(active, result[2]); Assert.AreEqual(active, list.Count); Assert.AreEqual(active, root.Count);
                            digest = ValidateLinked(operation, active, permutation, handles, list.First, list.Last,
                                h => list[h], h => { list.TryGetLinks(h, out var p, out _).ThrowIfFailure(); return p; },
                                h => { list.TryGetLinks(h, out _, out var n).ThrowIfFailure(); return n; },
                                i => { var node = root.Storage.ReadSnapshot(root.Slot, (int)handles[i].Index); CheckRetainedLinks(handles, i, node); return node.Value.Value; }, h => list.TryGet(h, out _),
                                i => replacements[i], i => output[i]);
                        }, () => list.TryRestore(root).ThrowIfFailure(),
                        () =>
                        {
                            var d = Stats(list.Storage.MemoryStats); d.reservedNativeMetadataBytes = NativeMetadata(list.Storage);
                            d.baselineLivePayloadBytes = baseline; d.payloadGrowthBytes = d.livePayloadBytes - baseline;
                            d.universe = used; d.active = active; d.retainedRoots = 1; d.payloadElementBytes = sizeof(SlotMapNode<LinkedListValue<int>>);
                            d.externalBufferBytes = (long)permutation.Length * sizeof(int) + (long)(handles.Length + replacements.Length) * sizeof(StableHandle) + (long)(output.Length + result.Length) * sizeof(int);
                            return d;
                        }, () => digest);
                }
                finally { root.Dispose(); }
            }
        }
    }
}
