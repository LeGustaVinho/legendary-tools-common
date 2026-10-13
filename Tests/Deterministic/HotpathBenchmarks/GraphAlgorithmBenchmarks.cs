using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class GraphHotpathBenchmarks
    {
        public enum Algorithm { BreadthFirst, Topological, Components, HasCycle, ReachNear, ReachMiss }
        private static void Algorithms<TReader>(TReader reader, Algorithm algorithm, GraphNodeHandle first,
            GraphNodeHandle near, GraphNodeHandle isolated, NativeArray<byte> memory, NativeArray<byte> output,
            NativeArray<long> result, int iterations) where TReader : struct, IDeterministicGraphReader<int, int>
        {
            var ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(memory);
            var destination = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(output);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var scratch = new CanonicalScratch(ptr, memory.Length);
                var nodes = new GraphWorkBuffer<GraphNodeHandle>((GraphNodeHandle*)destination, reader.NodeCount);
                var components = new GraphWorkBuffer<GraphComponent>((GraphComponent*)destination, reader.NodeCount);
                int count = 0, groupCount = 0; bool flag = false; DeterministicStatus s;
                switch (algorithm)
                {
                    case Algorithm.BreadthFirst: s = GraphAlgorithms<int, int>.TryBreadthFirst(reader, first, ref scratch, ref nodes, out count); break;
                    case Algorithm.Topological: s = GraphAlgorithms<int, int>.TryTopologicalOrder(reader, ref scratch, ref nodes, out count); break;
                    case Algorithm.Components: s = GraphAlgorithms<int, int>.TryComponents(reader, ref scratch, ref components, out count, out groupCount); break;
                    case Algorithm.HasCycle: s = GraphAlgorithms<int, int>.TryHasCycle(reader, ref scratch, out flag); count = flag ? 1 : 0; break;
                    default: s = GraphAlgorithms<int, int>.TryReachable(reader, first, algorithm == Algorithm.ReachNear ? near : isolated, ref scratch, out flag); count = flag ? 1 : 0; break;
                }
                result[1] = (long)s; if (!s.IsSuccess()) { result[2] = Complete; return; }
                result[3] = count; result[4] = groupCount; result[6] = scratch.Position;
            }
            result[2] = Complete;
        }
        private struct AlgorithmWork
        {
            internal NativeArray<byte> Scratch, Output;
            internal NativeArray<long> Result;
            internal Algorithm Operation;
            internal GraphNodeHandle First, Near, Isolated;
            internal int Iterations;
            internal void Execute<TReader>(TReader reader) where TReader : struct, IDeterministicGraphReader<int, int>
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Algorithms(reader, Operation, First, Near, Isolated, Scratch, Output, Result, Iterations);
            }
        }
        // Concrete job entry points let Burst discover both specializations without generic-job registration.
        [BurstCompile(CompileSynchronously = true)] private struct MutableAlgorithmJob : IJob
        {
            [ReadOnly] internal NativeGraphReadOnly<int, int> Reader;
            internal AlgorithmWork Work;
            public void Execute() => Work.Execute(Reader);
        }
        [BurstCompile(CompileSynchronously = true)] private struct FrozenAlgorithmJob : IJob
        {
            [ReadOnly] internal NativeFrozenGraphReadOnly<int, int> Reader;
            internal AlgorithmWork Work;
            public void Execute() => Work.Execute(Reader);
        }
        private static AlgorithmWork Work(Algorithm algorithm, GraphNodeHandle[] nodes,
            NativeArray<byte> scratch, NativeArray<byte> output, NativeArray<long> result, int iterations)
            => new AlgorithmWork { Operation = algorithm, First = nodes[0], Near = nodes[1], Isolated = nodes[nodes.Length - 1],
                Scratch = scratch, Output = output, Result = result, Iterations = iterations };
        private static Action AlgorithmJobAction(NativeGraphReadOnly<int, int> reader, Algorithm algorithm, GraphNodeHandle[] nodes,
            NativeArray<byte> scratch, NativeArray<byte> output, NativeArray<long> result, int iterations)
        {
            var job = new MutableAlgorithmJob { Reader = reader, Work = Work(algorithm, nodes, scratch, output, result, iterations) };
            return () => job.Schedule().Complete();
        }
        private static Action AlgorithmJobAction(NativeFrozenGraphReadOnly<int, int> reader, Algorithm algorithm, GraphNodeHandle[] nodes,
            NativeArray<byte> scratch, NativeArray<byte> output, NativeArray<long> result, int iterations)
        {
            var job = new FrozenAlgorithmJob { Reader = reader, Work = Work(algorithm, nodes, scratch, output, result, iterations) };
            return () => job.Schedule().Complete();
        }
        private static ulong ValidateAlgorithmOutput(Algorithm algorithm, int count, NativeArray<byte> output, GraphNodeHandle[] expectedNodes)
        {
            // Validate and hash outside the timed action, including for the Burst-produced buffers.
            var p = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(output); ulong digest = Mix(DigestSeed, (ulong)count);
            if (algorithm == Algorithm.Components)
                for (int i = 0; i < count; i++)
                {
                    var c = ((GraphComponent*)p)[i]; int expectedComponent = i == expectedNodes.Length - 1 ? 1 : 0;
                    Assert.AreEqual(expectedNodes[i], c.Node); Assert.AreEqual(expectedComponent, c.Component);
                    digest = Mix(Mix(digest, c.Node.Value.Packed), (ulong)c.Component);
                }
            else if (algorithm == Algorithm.BreadthFirst || algorithm == Algorithm.Topological)
                for (int i = 0; i < count; i++)
                { var h = ((GraphNodeHandle*)p)[i]; Assert.AreEqual(expectedNodes[i], h); digest = Mix(digest, h.Value.Packed); }
            return digest;
        }
        private static IEnumerable<TestCaseData> AlgorithmCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int extent in new[] { 256, 8192 })
            foreach (bool frozen in new[] { false, true })
            foreach (Algorithm a in Enum.GetValues(typeof(Algorithm)))
                yield return new TestCaseData(b, 256, extent, frozen, a, false).SetName($"G4_Algorithm_{b}_{extent}_{(frozen ? "Frozen" : "Mutable")}_{a}_Chain");
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (bool frozen in new[] { false, true })
                yield return new TestCaseData(b, count, count, frozen, Algorithm.Topological, true).SetName($"G4_Algorithm_{b}_{count}_{(frozen ? "Frozen" : "Mutable")}_Topological_Wide");
        }
        [TestCaseSource(nameof(AlgorithmCases)), Performance]
        public void TraverseSparseAndFrozenGraphs(Backend backend, int count, int extent, bool frozen, Algorithm algorithm, bool wide)
        {
            const int iterations = 8;
            using var owner = new Owner(backend, Options(extent, count));
            var nodes = PopulateSparse(owner, count, extent, wide);
            FrozenDeterministicGraph<int, int> managedFrozen = null; NativeFrozenDeterministicGraph<int, int> nativeFrozen = default;
            try
            {
                if (frozen)
                {
                    if (owner.Managed != null) { managedFrozen = new FrozenDeterministicGraph<int, int>(owner.Options, count, count); managedFrozen.TryBuild(owner.Managed.AsReadOnly()).ThrowIfFailure(); }
                    else { nativeFrozen = new NativeFrozenDeterministicGraph<int, int>(owner.Options, Allocator.Persistent, count, count); nativeFrozen.TryBuild(owner.Native.AsReadOnly()).ThrowIfFailure(); }
                }
                int readerExtent = frozen ? count : extent; GraphScratch.TryMeasure(readerExtent, count, out int scratchBytes).ThrowIfFailure();
                using var scratch = new NativeArray<byte>(scratchBytes, Allocator.Persistent);
                using var output = new NativeArray<byte>(count * sizeof(GraphComponent), Allocator.Persistent);
                using var result = new NativeArray<long>(7, Allocator.Persistent);
                Action action;
                if (owner.Managed != null)
                {
                    if (frozen) { var r = managedFrozen.AsReadOnly(); action = () => Algorithms(r, algorithm, nodes[0], nodes[1], nodes[count - 1], scratch, output, result, iterations); }
                    else { var r = owner.Managed.AsReadOnly(); action = () => Algorithms(r, algorithm, nodes[0], nodes[1], nodes[count - 1], scratch, output, result, iterations); }
                }
                else action = frozen ? AlgorithmJobAction(nativeFrozen.AsReadOnly(), algorithm, nodes, scratch, output, result, iterations)
                    : AlgorithmJobAction(owner.Native.AsReadOnly(), algorithm, nodes, scratch, output, result, iterations);
                int expectedCount = algorithm == Algorithm.ReachNear ? 1 : algorithm == Algorithm.ReachMiss || algorithm == Algorithm.HasCycle ? 0
                    : algorithm == Algorithm.BreadthFirst ? count - 1 : count;
                ulong expected = Mix(DigestSeed, (ulong)expectedCount);
                if (algorithm == Algorithm.Components)
                    for (int i = 0; i < count; i++) expected = Mix(Mix(expected, nodes[i].Value.Packed), (ulong)(i == count - 1 ? 1 : 0));
                else if (algorithm == Algorithm.BreadthFirst || algorithm == Algorithm.Topological)
                    for (int i = 0; i < expectedCount; i++) expected = Mix(expected, nodes[i].Value.Packed);
                Benchmark("G4", algorithm.ToString(), backend, (frozen ? "Frozen" : "Mutable") + "_" + extent + "_" + (wide ? "Wide" : "Chain"),
                    count, iterations * (algorithm == Algorithm.ReachNear ? 1 : count), iterations, action, () =>
                    {
                        if (backend == Backend.BurstJob) CheckJob(result); else { Assert.AreEqual(0, result[1]); Assert.AreEqual(Complete, result[2]); }
                        Assert.AreEqual(expectedCount, result[3]); Assert.AreEqual(algorithm == Algorithm.Components ? 2 : 0, result[4]);
                        Assert.AreEqual(expected, ValidateAlgorithmOutput(algorithm, expectedCount, output, nodes));
                        Assert.GreaterOrEqual(result[6], 0); Assert.LessOrEqual(result[6], scratch.Length);
                    }, () => Reset(result), () =>
                    {
                        var m = frozen ? Stats(owner.Managed != null ? managedFrozen.MemoryStats : nativeFrozen.MemoryStats) : owner.Metrics();
                        m.nodes = count; m.edges = count - 2; m.nodeExtent = readerExtent; m.edgeExtent = count - 2;
                        m.internalScratchReservedBytes = frozen ? 0 : InternalScratch(owner.Options); m.externalScratchReservedBytes = scratchBytes;
                        m.scratchConsumedBytes = (int)result[6]; m.auxiliaryBytes = output.Length;
                        m.contentDigest = Hex(ValidateAlgorithmOutput(algorithm, expectedCount, output, nodes)); return m;
                    });
            }
            finally { if (managedFrozen != null) managedFrozen.Dispose(); if (nativeFrozen.IsCreated) nativeFrozen.Dispose(); }
        }
        [BurstCompile(CompileSynchronously = true)] private struct FreezeJob : IJob
        {
            [ReadOnly] internal NativeGraphReadOnly<int, int> Source;
            internal NativeFrozenDeterministicGraph<int, int> Destination;
            internal NativeArray<long> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (long)Destination.TryBuild(Source); Result[2] = Complete;
            }
        }
        private static IEnumerable<TestCaseData> FreezeCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            {
                yield return new TestCaseData(b, 256, true).SetName($"G5_Freeze_{b}_256_Tight");
                yield return new TestCaseData(b, 8192, true).SetName($"G5_Freeze_{b}_8192_Tight");
                yield return new TestCaseData(b, 8192, false).SetName($"G5_Freeze_{b}_8192_FullCapacity");
            }
        }
        [TestCaseSource(nameof(FreezeCases)), Performance]
        public void BuildFrozenWithSparseSource(Backend backend, int extent, bool tight)
        {
            const int count = 256;
            using var owner = new Owner(backend, Options(extent, count)); var nodes = PopulateSparse(owner, count, extent);
            using var result = new NativeArray<long>(3, Allocator.Persistent);
            FrozenDeterministicGraph<int, int> mf = null; NativeFrozenDeterministicGraph<int, int> nf = default;
            var job = new FreezeJob { Source = backend == Backend.BurstJob ? owner.Native.AsReadOnly() : default, Result = result };
            Action setup = () =>
            {
                Reset(result);
                if (backend == Backend.Managed)
                { mf?.Dispose(); mf = new FrozenDeterministicGraph<int, int>(owner.Options, tight ? count : extent, count); }
                else
                { if (nf.IsCreated) nf.Dispose(); nf = new NativeFrozenDeterministicGraph<int, int>(owner.Options, Allocator.Persistent, tight ? count : extent, count); job.Destination = nf; }
            };
            var writableResult = result;
            Action action = backend == Backend.Managed ? (Action)(() => { writableResult[1] = (long)mf.TryBuild(owner.Managed.AsReadOnly()); writableResult[2] = Complete; }) : () => job.Schedule().Complete();
            try
            {
                Benchmark("G5", "FreezeBuild", backend, extent + "_" + (tight ? "Tight" : "FullCapacity"), count, count, 1, action, () =>
                {
                    if (backend == Backend.BurstJob) CheckJob(result); else Assert.AreEqual(0, result[1]);
                    Assert.AreEqual(count, backend == Backend.Managed ? mf.NodeCount : nf.NodeCount);
                    for (int i = 0; i < count; i++)
                    {
                        int value; var s = backend == Backend.Managed ? mf.AsReadOnly().TryGetNode(nodes[i], out value) : nf.AsReadOnly().TryGetNode(nodes[i], out value);
                        s.ThrowIfFailure(); Assert.AreEqual((int)nodes[i].Index, value);
                    }
                }, setup, () =>
                {
                    var m = Stats(backend == Backend.Managed ? mf.MemoryStats : nf.MemoryStats); m.nodes = count; m.edges = count - 2;
                    m.nodeExtent = count; m.edgeExtent = count - 2; m.internalScratchReservedBytes = 0;
                    m.contentDigest = Hex(CanonicalDigest(owner.Canonical())); return m;
                });
            }
            finally { mf?.Dispose(); if (nf.IsCreated) nf.Dispose(); }
        }
    }
}
