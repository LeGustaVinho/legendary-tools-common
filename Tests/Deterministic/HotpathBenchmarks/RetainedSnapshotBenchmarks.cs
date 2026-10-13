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
        public enum ChurnFamily { Heap, Avl }
        private const int ChurnBatch = 32, RetainedRoots = 4;
        private static IEnumerable<TestCaseData> ChurnCases()
        {
            foreach (var backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (ChurnFamily family in Enum.GetValues(typeof(ChurnFamily)))
            foreach (int count in new[] { 256, 4096 })
            foreach (var policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, family, count, policy);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct ChurnJob : IJob
        {
            internal ChurnFamily Family;
            internal int Count;
            internal NativeDeterministicPriorityQueue<int, int, Int32Policy> Heap;
            internal NativeDeterministicSortedDictionary<int, int, Int32Policy> Tree;
            internal NativeArray<int> Output, Result;
            [BurstDiscard] private static void MarkManaged(ref int marker) { marker = 1; }
            public void Execute()
            {
                int marker = 0; MarkManaged(ref marker); Result[0] = marker; int status = 0;
                for (int i = 0; i < ChurnBatch; i++)
                {
                    if (Family == ChurnFamily.Heap)
                    {
                        status |= (int)Heap.TryDequeue(out int value, out int priority);
                        Output[i] = value; Output[ChurnBatch + i] = priority;
                        status |= (int)Heap.TryEnqueue(Count + i, Count + i);
                    }
                    else
                    {
                        status |= (int)Tree.TryRemove(i);
                        status |= (int)Tree.TryAdd(Count + i, WorldValue(Count + i));
                    }
                }
                Result[1] = status;
            }
        }
        private static void ValidateHeap(int count, Func<int, PriorityQueueNode<int, int>> read)
        {
            var seen = new bool[count];
            for (int i = 0; i < count; i++)
            {
                var node = read(i); int rank = node.Element - ChurnBatch;
                if ((uint)rank >= (uint)count || seen[rank]) Assert.Fail("Heap membership at " + i);
                seen[rank] = true;
                if (node.Priority != node.Element || node.Sequence != (ulong)node.Element) Assert.Fail("Heap priority/sequence at " + i);
                if (i > 0 && read((i - 1) / 2).Priority > node.Priority) Assert.Fail("Heap parent at " + i);
            }
        }
        [TestCaseSource(nameof(ChurnCases)), Performance]
        public void MutationWithRetainedSnapshots(Backend backend, ChurnFamily family, int count, SnapshotPolicy policy)
        {
            string operation = family + ".RemoveAndAddRetained", distribution = policy + "_4Roots";
            if (backend == Backend.Managed)
            {
                using (var heap = new DeterministicPriorityQueue<int, int, Int32Policy>(Options(family == ChurnFamily.Heap ? count : 0, policy, family == ChurnFamily.Heap ? RetainedRoots : 0)))
                using (var tree = new DeterministicSortedDictionary<int, int, Int32Policy>(Options(family == ChurnFamily.Avl ? count : 0, policy, family == ChurnFamily.Avl ? RetainedRoots : 0)))
                {
                    var hs = new ManagedPriorityQueueSnapshot<int, int>[RetainedRoots]; var ts = new ManagedAvlSnapshot<int, int>[RetainedRoots];
                    try
                    {
                        for (int i = 0; i < count; i++)
                            if (family == ChurnFamily.Heap) heap.TryEnqueue(i, i).ThrowIfFailure(); else tree.TryAdd(i, WorldValue(i)).ThrowIfFailure();
                        for (int i = 0; i < RetainedRoots; i++)
                            if (family == ChurnFamily.Heap) heap.TryCapture(out hs[i]).ThrowIfFailure(); else tree.TryCapture(out ts[i]).ThrowIfFailure();
                        int status = 0; var output = new int[ChurnBatch * 2];
                        Benchmark("H3", operation, backend, distribution, count, ChurnBatch * 2, 0,
                            () =>
                            {
                                status = 0;
                                for (int i = 0; i < ChurnBatch; i++)
                                    if (family == ChurnFamily.Heap)
                                    {
                                        status |= (int)heap.TryDequeue(out output[i], out output[ChurnBatch + i]);
                                        status |= (int)heap.TryEnqueue(count + i, count + i);
                                    }
                                    else { status |= (int)tree.TryRemove(i); status |= (int)tree.TryAdd(count + i, WorldValue(count + i)); }
                            },
                            () =>
                            {
                                Assert.AreEqual(0, status);
                                if (family == ChurnFamily.Heap)
                                {
                                    Assert.AreEqual(count, heap.Count); Assert.AreEqual((ulong)(count + ChurnBatch), heap.NextSequence);
                                    for (int i = 0; i < ChurnBatch; i++) { Assert.AreEqual(i, output[i]); Assert.AreEqual(i, output[ChurnBatch + i]); }
                                    ValidateHeap(count, i => { heap.TryGetUnorderedItem(i, out var node).ThrowIfFailure(); return node; });
                                }
                                else
                                {
                                    Assert.AreEqual(count, tree.Count);
                                    for (int i = 0; i < count; i++) { tree.TryGetAt(i, out var pair).ThrowIfFailure(); if (pair.Key != i + ChurnBatch || pair.Value != WorldValue(pair.Key)) Assert.Fail("AVL content/rank at " + i); }
                                }
                                for (int s = 0; s < RetainedRoots; s++)
                                for (int i = 0; i < count; i++)
                                    if (family == ChurnFamily.Heap) { hs[s].TryGetUnorderedItem(i, out var node).ThrowIfFailure(); if (node.Element != i || node.Priority != i || node.Sequence != (ulong)i) Assert.Fail("Retained heap changed at " + i); }
                                    else { ts[s].TryGetAt(i, out var pair).ThrowIfFailure(); if (pair.Key != i || pair.Value != WorldValue(i)) Assert.Fail("Retained AVL changed at " + i); }
                            },
                            () => { if (family == ChurnFamily.Heap) heap.TryRestore(hs[0]).ThrowIfFailure(); else tree.TryRestore(ts[0]).ThrowIfFailure(); },
                            () => { var d = Stats(family == ChurnFamily.Heap ? heap.Storage.Owner.MemoryStats : tree.Storage.Owner.MemoryStats); d.active = count; d.retainedRoots = RetainedRoots; return d; });
                    }
                    finally { for (int i = 0; i < RetainedRoots; i++) { hs[i].Dispose(); ts[i].Dispose(); } }
                }
            }
            else
            {
                using (var heap = new NativeDeterministicPriorityQueue<int, int, Int32Policy>(Options(family == ChurnFamily.Heap ? count : 0, policy, family == ChurnFamily.Heap ? RetainedRoots : 0), Allocator.Persistent))
                using (var tree = new NativeDeterministicSortedDictionary<int, int, Int32Policy>(Options(family == ChurnFamily.Avl ? count : 0, policy, family == ChurnFamily.Avl ? RetainedRoots : 0), Allocator.Persistent))
                using (var output = new NativeArray<int>(ChurnBatch * 2, Allocator.Persistent))
                using (var result = new NativeArray<int>(2, Allocator.Persistent))
                {
                    var hs = new NativePriorityQueueSnapshot<int, int>[RetainedRoots]; var ts = new NativeAvlSnapshot<int, int>[RetainedRoots];
                    try
                    {
                        for (int i = 0; i < count; i++)
                            if (family == ChurnFamily.Heap) heap.TryEnqueue(i, i).ThrowIfFailure(); else tree.TryAdd(i, WorldValue(i)).ThrowIfFailure();
                        for (int i = 0; i < RetainedRoots; i++)
                            if (family == ChurnFamily.Heap) heap.TryCapture(out hs[i]).ThrowIfFailure(); else tree.TryCapture(out ts[i]).ThrowIfFailure();
                        var job = new ChurnJob { Family = family, Count = count, Heap = heap, Tree = tree, Output = output, Result = result };
                        Benchmark("H3", operation, backend, distribution, count, ChurnBatch * 2, 0, () => job.Schedule().Complete(),
                            () =>
                            {
                                CheckBurst(result[0]); Assert.AreEqual(0, result[1]);
                                if (family == ChurnFamily.Heap)
                                {
                                    Assert.AreEqual(count, heap.Count); Assert.AreEqual((ulong)(count + ChurnBatch), heap.NextSequence);
                                    for (int i = 0; i < ChurnBatch; i++) { Assert.AreEqual(i, output[i]); Assert.AreEqual(i, output[ChurnBatch + i]); }
                                    ValidateHeap(count, i => { heap.TryGetUnorderedItem(i, out var node).ThrowIfFailure(); return node; });
                                }
                                else
                                {
                                    Assert.AreEqual(count, tree.Count);
                                    for (int i = 0; i < count; i++) { tree.TryGetAt(i, out var pair).ThrowIfFailure(); if (pair.Key != i + ChurnBatch || pair.Value != WorldValue(pair.Key)) Assert.Fail("Burst AVL content/rank at " + i); }
                                }
                                for (int s = 0; s < RetainedRoots; s++)
                                for (int i = 0; i < count; i++)
                                    if (family == ChurnFamily.Heap) { hs[s].TryGetUnorderedItem(i, out var node).ThrowIfFailure(); if (node.Element != i || node.Priority != i || node.Sequence != (ulong)i) Assert.Fail("Retained native heap changed at " + i); }
                                    else { ts[s].TryGetAt(i, out var pair).ThrowIfFailure(); if (pair.Key != i || pair.Value != WorldValue(i)) Assert.Fail("Retained native AVL changed at " + i); }
                            },
                            () => { if (family == ChurnFamily.Heap) heap.TryRestore(hs[0]).ThrowIfFailure(); else tree.TryRestore(ts[0]).ThrowIfFailure(); },
                            () => { var d = Stats(family == ChurnFamily.Heap ? heap.Storage.MemoryStats : tree.Storage.MemoryStats); d.reservedNativeMetadataBytes = family == ChurnFamily.Heap ? NativeMetadata(heap.Storage) : NativeMetadata(tree.Storage); d.active = count; d.retainedRoots = RetainedRoots; return d; });
                    }
                    finally { for (int i = 0; i < RetainedRoots; i++) { hs[i].Dispose(); ts[i].Dispose(); } }
                }
            }
        }
    }
}
