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
    public sealed unsafe partial class GraphExtendedHotpathBenchmarks
    {
        public enum WalkScenario { DirectedChain, UndirectedChain, Forest, LateRing, EarlyParallel, EarlySelf }
        private struct WalkWork
        {
            internal NativeArray<byte> Scratch;
            internal NativeArray<GraphNodeHandle> Output;
            internal NativeArray<long> Result;
            internal GraphNodeHandle First;
            internal bool DepthFirst;
            internal void Run<TReader>(TReader reader) where TReader : struct, IDeterministicGraphReader<int, int>
            {
                var status = DeterministicStatus.None; int consumed = 0, count = 0; bool cyclic = false;
                for (int i = 0; i < 4; i++)
                {
                    var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                    if (DepthFirst)
                    {
                        var output = new GraphWorkBuffer<GraphNodeHandle>((GraphNodeHandle*)NativeArrayUnsafeUtility.GetUnsafePtr(Output), Output.Length);
                        status = GraphAlgorithms<int, int>.TryDepthFirst(reader, First, ref scratch, ref output, out count);
                    }
                    else status = GraphAlgorithms<int, int>.TryHasCycle(reader, ref scratch, out cyclic);
                    consumed = scratch.Position; if (!status.IsSuccess()) break;
                }
                Result[1] = (long)status; Result[2] = Complete; Result[3] = DepthFirst ? count : cyclic ? 1 : 0; Result[4] = consumed;
            }
        }
        [BurstCompile(CompileSynchronously = true)] private struct MutableWalkJob : IJob
        {
            [ReadOnly] internal NativeGraphReadOnly<int, int> Reader;
            internal WalkWork Work;
            public void Execute() { int managed = 0; MarkManaged(ref managed); Work.Result[0] = managed; Work.Run(Reader); }
        }
        [BurstCompile(CompileSynchronously = true)] private struct FrozenWalkJob : IJob
        {
            [ReadOnly] internal NativeFrozenGraphReadOnly<int, int> Reader;
            internal WalkWork Work;
            public void Execute() { int managed = 0; MarkManaged(ref managed); Work.Result[0] = managed; Work.Run(Reader); }
        }
        private static IEnumerable<TestCaseData> WalkCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (bool frozen in new[] { false, true })
            foreach (WalkScenario scenario in Enum.GetValues(typeof(WalkScenario)))
                yield return new TestCaseData(backend, count, frozen, scenario).SetName($"G12_Walk_{backend}_{count}_{(frozen ? "Frozen" : "Mutable")}_{scenario}");
        }
        [TestCaseSource(nameof(WalkCases)), Performance]
        public void DepthAndUndirectedCycles(Backend backend, int count, bool frozen, WalkScenario scenario)
        {
            bool dfs = scenario == WalkScenario.DirectedChain || scenario == WalkScenario.UndirectedChain;
            var options = new GraphOptions(new BufferOptions(count, snapshotSlots: 0), new BufferOptions(count + 1, snapshotSlots: 0),
                direction: scenario == WalkScenario.DirectedChain ? GraphDirection.Directed : GraphDirection.Undirected,
                allowParallelEdges: true, allowSelfEdges: true);
            using var owner = new Owner(backend, options); var nodes = new GraphNodeHandle[count];
            for (int i = 0; i < count; i++) nodes[i] = owner.AddNode(i);
            if (scenario == WalkScenario.EarlySelf) owner.AddEdge(nodes[0], nodes[0], 777);
            if (scenario == WalkScenario.EarlyParallel) owner.AddEdge(nodes[0], nodes[1], 777);
            for (int i = 1; i < count; i++) owner.AddEdge(nodes[i - 1], nodes[i], i);
            if (scenario == WalkScenario.LateRing) owner.AddEdge(nodes[count - 1], nodes[0], 777);
            GraphScratch.TryMeasure(count, count + 1, out int bytes).ThrowIfFailure();
            using var scratch = new NativeArray<byte>(bytes, Allocator.Persistent);
            using var output = new NativeArray<GraphNodeHandle>(count, Allocator.Persistent);
            using var result = new NativeArray<long>(5, Allocator.Persistent);
            var work = new WalkWork { Scratch = scratch, Output = output, Result = result, First = nodes[0], DepthFirst = dfs };
            FrozenDeterministicGraph<int, int> managedFrozen = null; NativeFrozenDeterministicGraph<int, int> nativeFrozen = default;
            try
            {
                Action action;
                if (backend == Backend.Managed)
                {
                    if (frozen) { managedFrozen = new FrozenDeterministicGraph<int, int>(options, count, owner.EdgeCount); owner.Managed.TryFreeze(managedFrozen).ThrowIfFailure(); var reader = managedFrozen.AsReadOnly(); action = () => work.Run(reader); }
                    else { var reader = owner.Managed.AsReadOnly(); action = () => work.Run(reader); }
                }
                else if (frozen)
                {
                    nativeFrozen = new NativeFrozenDeterministicGraph<int, int>(options, Allocator.Persistent, count, owner.EdgeCount);
                    owner.Native.TryFreeze(ref nativeFrozen).ThrowIfFailure(); var job = new FrozenWalkJob { Reader = nativeFrozen.AsReadOnly(), Work = work }; action = () => job.Schedule().Complete();
                }
                else { var job = new MutableWalkJob { Reader = owner.Native.AsReadOnly(), Work = work }; action = () => job.Schedule().Complete(); }
                ulong digest = Mix(CanonicalDigest(owner.Canonical()), (ulong)(dfs ? count : scenario == WalkScenario.Forest ? 0 : 1));
                Benchmark("G12", dfs ? "DepthFirst" : "UndirectedHasCycle", backend, (frozen ? "Frozen_" : "Mutable_") + scenario, count,
                    4 * count, 4, action, () =>
                    {
                        CheckResult(backend, result); Assert.AreEqual(dfs ? count : scenario == WalkScenario.Forest ? 0 : 1, result[3]);
                        Assert.LessOrEqual(result[4], bytes);
                        if (dfs) for (int i = 0; i < count; i++) Assert.AreEqual(nodes[i], output[i]);
                    }, () => Reset(result), () =>
                    {
                        var m = frozen ? Stats(backend == Backend.Managed ? managedFrozen.MemoryStats : nativeFrozen.MemoryStats) : owner.Metrics();
                        m.nodes = count; m.edges = owner.EdgeCount; m.nodeExtent = count; m.edgeExtent = owner.EdgeCount;
                        m.externalScratchReservedBytes = bytes; m.scratchConsumedBytes = (int)result[4]; m.auxiliaryBytes = count * sizeof(GraphNodeHandle);
                        m.contentDigest = Hex(digest); return m;
                    });
            }
            finally { managedFrozen?.Dispose(); if (nativeFrozen.IsCreated) nativeFrozen.Dispose(); }
        }
    }
}
