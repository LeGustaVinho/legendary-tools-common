using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.Benchmarks
{
    public sealed unsafe partial class OperationBenchmarks
    {
        private static IEnumerable<TestCaseData> MergeCases()
        {
            foreach (var backend in new[] { ExecutionBackend.Managed, ExecutionBackend.NativeHost })
            foreach (int size in new[] { 128, 2048, 16384 })
            foreach (InputOrder order in Enum.GetValues(typeof(InputOrder))) yield return new TestCaseData(backend, size, order);
        }
        [TestCaseSource(nameof(MergeCases)), Performance]
        public void CommandHeapsortMerge(ExecutionBackend backend, int size, InputOrder order)
        {
            var context = new DeterministicRuntimeContext(backend == ExecutionBackend.Managed ? DeterministicBackend.Managed : DeterministicBackend.BurstCompiler,
                backend == ExecutionBackend.Managed ? null : new NativeDeterministicMemoryFactory());
            uint[] producers = { 37, 11, 71, 3 }; int[] keys = Keys(size, order);
            var oracle = new DeterministicCommand<int>[size]; var sequences = new ulong[producers.Length];
            using (var commands = new DeterministicCommandBuffer<int>(context, producers, size / producers.Length))
            {
                for (int i = 0; i < size; i++)
                {
                    int producer = i % producers.Length; commands.TryGetWriter(producers[producer], out var writer).ThrowIfFailure();
                    var entity = new StableHandle((uint)keys[i], 1);
                    writer.TryEnqueue(0, 1, entity, i).ThrowIfFailure();
                    oracle[i] = new DeterministicCommand<int>(new DeterministicCommandHeader(0, 1, entity, sequences[producer]++, producers[producer]), i);
                }
                Array.Sort(oracle, CompareCommands);
                DeterministicStatus status = DeterministicStatus.None; IntPtr mergedAddress = IntPtr.Zero; int mergedCount = 0;
                Benchmark("Command.HeapsortMerge", backend, order.ToString(), size, size, 0,
                    () =>
                    {
                        status = commands.TryMerge(out var sorted); mergedCount = sorted.Length;
                        fixed (DeterministicCommand<int>* pointer = sorted) mergedAddress = (IntPtr)pointer;
                    },
                    () =>
                    {
                        Assert.AreEqual(DeterministicStatus.None, status); Assert.AreEqual(size, commands.Count);
                        // The owner keeps the borrowed merged arena alive until its next merge/reset.
                        Assert.AreEqual(size, mergedCount); var sorted = (DeterministicCommand<int>*)mergedAddress;
                        for (int i = 0; i < size; i++)
                            if (sorted[i].Payload != oracle[i].Payload || CompareCommands(sorted[i], oracle[i]) != 0) Assert.Fail("Command oracle differs at rank " + i);
                    });
            }
        }
        private static int CompareCommands(DeterministicCommand<int> a, DeterministicCommand<int> b)
        {
            int c = a.Header.Phase.CompareTo(b.Header.Phase); if (c == 0) c = a.Header.SystemId.CompareTo(b.Header.SystemId);
            if (c == 0) c = a.Header.Entity.Index.CompareTo(b.Header.Entity.Index); if (c == 0) c = a.Header.Entity.Generation.CompareTo(b.Header.Entity.Generation);
            if (c == 0) c = a.Header.Sequence.CompareTo(b.Header.Sequence); return c == 0 ? a.Header.ProducerId.CompareTo(b.Header.ProducerId) : c;
        }

        private static IEnumerable<TestCaseData> SortedCases()
        {
            foreach (var backend in new[] { ExecutionBackend.Managed, ExecutionBackend.BurstJob })
            foreach (SortedCollection kind in Enum.GetValues(typeof(SortedCollection)))
            foreach (int size in new[] { 128, 1024, 4096 })
            foreach (var order in new[] { InputOrder.Ascending, InputOrder.Descending, InputOrder.Shuffled }) yield return new TestCaseData(backend, kind, size, order);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct SortedBuildJob : IJob
        {
            internal SortedCollection Kind;
            [ReadOnly] internal NativeArray<int> Keys;
            internal NativeDeterministicSortedList<int, int, Int32Policy> List;
            internal NativeDeterministicSortedDictionary<int, int, Int32Policy> Tree;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed; int status = 0;
                for (int i = 0; i < Keys.Length; i++)
                {
                    int key = Keys[i], value = key ^ 0x5a5a5a5a;
                    status |= (int)(Kind == SortedCollection.SortedList ? List.TryAdd(key, value) : Tree.TryAdd(key, value));
                }
                Result[1] = status;
            }
        }
        [TestCaseSource(nameof(SortedCases)), Performance]
        public void SortedCollectionBuild(ExecutionBackend backend, SortedCollection kind, int size, InputOrder order)
        {
            int[] keys = Keys(size, order); string operation = kind == SortedCollection.SortedList ? "SortedList.Build" : "AVL.Build";
            if (backend == ExecutionBackend.Managed)
            {
                using (DeterministicSortedMapBase<int, int> map = kind == SortedCollection.SortedList
                    ? (DeterministicSortedMapBase<int, int>)new DeterministicSortedList<int, int, Int32Policy>(Options(size))
                    : new DeterministicSortedDictionary<int, int, Int32Policy>(Options(size)))
                {
                    int status = 0;
                    Benchmark(operation, backend, order.ToString(), size, size, 0,
                        () => { status = 0; for (int i = 0; i < size; i++) status |= (int)map.TryAdd(keys[i], keys[i] ^ 0x5a5a5a5a); },
                        () =>
                        {
                            Assert.AreEqual(0, status); Assert.AreEqual(size, map.Count);
                            for (int i = 0; i < size; i++)
                            { map.TryGetAt(i, out var item).ThrowIfFailure(); if (item.Key != i || item.Value != (i ^ 0x5a5a5a5a)) Assert.Fail("Sorted result at rank " + i); }
                        }, map.Clear);
                }
            }
            else
            {
                using (var list = new NativeDeterministicSortedList<int, int, Int32Policy>(Options(kind == SortedCollection.SortedList ? size : 0), Allocator.Persistent))
                using (var tree = new NativeDeterministicSortedDictionary<int, int, Int32Policy>(Options(kind == SortedCollection.Avl ? size : 0), Allocator.Persistent))
                using (var input = new NativeArray<int>(keys, Allocator.Persistent))
                using (var result = new NativeArray<int>(2, Allocator.Persistent))
                {
                    var job = new SortedBuildJob { Kind = kind, Keys = input, List = list, Tree = tree, Result = result };
                    Benchmark(operation, backend, order.ToString(), size, size, 0, () => job.Schedule().Complete(),
                        () =>
                        {
                            CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(size, kind == SortedCollection.SortedList ? list.Count : tree.Count);
                            for (int i = 0; i < size; i++)
                            {
                                KeyValuePair<int, int> item;
                                var status = kind == SortedCollection.SortedList ? list.TryGetAt(i, out item) : tree.TryGetAt(i, out item);
                                status.ThrowIfFailure(); if (item.Key != i || item.Value != (i ^ 0x5a5a5a5a)) Assert.Fail("Burst sorted result at rank " + i);
                            }
                        }, () => { list.TryClear().ThrowIfFailure(); tree.TryClear().ThrowIfFailure(); });
                }
            }
        }

        private static IEnumerable<TestCaseData> HeapCases()
        {
            foreach (var backend in new[] { ExecutionBackend.Managed, ExecutionBackend.BurstJob })
            foreach (int size in new[] { 128, 1024, 4096 })
            foreach (bool ties in new[] { false, true }) yield return new TestCaseData(backend, size, ties);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct HeapJob : IJob
        {
            internal bool Ties;
            [ReadOnly] internal NativeArray<int> Keys;
            internal NativeDeterministicPriorityQueue<int, int, Int32Policy> Heap;
            internal NativeArray<int> Output, Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed; int status = 0;
                for (int i = 0; i < Keys.Length; i++) status |= (int)Heap.TryEnqueue(Keys[i], Ties ? Keys[i] % 8 : Keys[i]);
                for (int i = 0; i < Keys.Length; i++) { status |= (int)Heap.TryDequeue(out int value, out _); Output[i] = value; }
                Result[1] = status;
            }
        }
        [TestCaseSource(nameof(HeapCases)), Performance]
        public void StableHeapEnqueueAndDrain(ExecutionBackend backend, int size, bool ties)
        {
            int[] keys = Keys(size, InputOrder.Shuffled); var ranks = new int[size]; for (int i = 0; i < size; i++) ranks[i] = i;
            Array.Sort(ranks, (a, b) => { int c = (ties ? keys[a] % 8 : keys[a]).CompareTo(ties ? keys[b] % 8 : keys[b]); return c == 0 ? a.CompareTo(b) : c; });
            string distribution = ties ? "EightPriorities" : "UniquePriorities";
            if (backend == ExecutionBackend.Managed)
            {
                using (var heap = new DeterministicPriorityQueue<int, int, Int32Policy>(Options(size)))
                {
                    var output = new int[size]; int status = 0;
                    Benchmark("StableHeap.EnqueueAndDrain", backend, distribution, size, size * 2, 0,
                        () =>
                        {
                            status = 0; for (int i = 0; i < size; i++) status |= (int)heap.TryEnqueue(keys[i], ties ? keys[i] % 8 : keys[i]);
                            for (int i = 0; i < size; i++) status |= (int)heap.TryDequeue(out output[i], out _);
                        },
                        () => { Assert.AreEqual(0, status); Assert.AreEqual(0, heap.Count); for (int i = 0; i < size; i++) if (output[i] != keys[ranks[i]]) Assert.Fail("Heap FIFO oracle at rank " + i); }, heap.Clear);
                }
            }
            else
            {
                using (var heap = new NativeDeterministicPriorityQueue<int, int, Int32Policy>(Options(size), Allocator.Persistent))
                using (var input = new NativeArray<int>(keys, Allocator.Persistent))
                using (var output = new NativeArray<int>(size, Allocator.Persistent))
                using (var result = new NativeArray<int>(2, Allocator.Persistent))
                {
                    var job = new HeapJob { Ties = ties, Keys = input, Heap = heap, Output = output, Result = result };
                    Benchmark("StableHeap.EnqueueAndDrain", backend, distribution, size, size * 2, 0, () => job.Schedule().Complete(),
                        () => { CheckBurst(result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(0, heap.Count); for (int i = 0; i < size; i++) if (output[i] != keys[ranks[i]]) Assert.Fail("Burst heap FIFO oracle at rank " + i); },
                        () => heap.TryClear().ThrowIfFailure());
                }
            }
        }
    }
}
