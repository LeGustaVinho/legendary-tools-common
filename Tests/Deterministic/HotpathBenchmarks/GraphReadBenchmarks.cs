using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class GraphExtendedHotpathBenchmarks
    {
        public enum ReadOperation { Nodes, Edges, NodeLookups, EdgeLookups, AllArcs }
        private const int ReadIterations = 8;
        private struct ReadWork
        {
            [ReadOnly] internal NativeArray<GraphNodeHandle> Nodes;
            [ReadOnly] internal NativeArray<GraphEdgeHandle> Edges;
            internal NativeArray<long> Result;
            internal ReadOperation Operation;
            internal void Run<TReader>(TReader reader) where TReader : struct, IDeterministicGraphReader<int, int>
            {
                ulong digest = DigestSeed; int count = 0, misses = 0; var s = DeterministicStatus.None;
                for (int repeat = 0; repeat < ReadIterations; repeat++)
                {
                    if (Operation == ReadOperation.Nodes)
                    {
                        var e = new GraphNodeEnumerator<int, int, TReader>(reader);
                        while ((s = e.TryMoveNext(out bool moved)).IsSuccess() && moved)
                        { digest = NodeDigest(digest, e.Current.Handle, e.Current.Data); count++; }
                    }
                    else if (Operation == ReadOperation.Edges)
                    {
                        var e = new GraphEdgeEnumerator<int, int, TReader>(reader);
                        while ((s = e.TryMoveNext(out bool moved)).IsSuccess() && moved)
                        { digest = EdgeDigest(digest, e.Current.Handle, e.Current.Edge); count++; }
                    }
                    else for (int i = 0; i < Nodes.Length; i++)
                    {
                        if (Operation == ReadOperation.NodeLookups)
                        {
                            s = reader.TryGetNode(Nodes[i], out int value); if (!s.IsSuccess()) break;
                            digest = NodeDigest(digest, Nodes[i], value); count++;
                            var stale = new GraphNodeHandle(new StableHandle(Nodes[i].Index, Nodes[i].Generation + 1));
                            s = reader.TryGetNode(stale, out _); if (s != DeterministicStatus.NotFound) { s = DeterministicStatus.InvalidEncoding; break; }
                            digest = Mix(digest, (ulong)s); count++; misses++; s = DeterministicStatus.None;
                        }
                        else if (Operation == ReadOperation.EdgeLookups)
                        {
                            s = reader.TryGetEdge(Edges[i], out var edge); if (!s.IsSuccess()) break;
                            digest = EdgeDigest(digest, Edges[i], edge); count++;
                            var stale = new GraphEdgeHandle(new StableHandle(Edges[i].Index, Edges[i].Generation + 1));
                            s = reader.TryGetEdge(stale, out _); if (s != DeterministicStatus.NotFound) { s = DeterministicStatus.InvalidEncoding; break; }
                            digest = Mix(digest, (ulong)s); count++; misses++; s = DeterministicStatus.None;
                        }
                        else
                        {
                            for (int direction = 0; direction < 2; direction++)
                            {
                                var e = new GraphArcEnumerator<int, int, TReader>(reader, Nodes[i], (GraphTraversal)direction);
                                while ((s = e.TryMoveNext(out bool moved)).IsSuccess() && moved)
                                { var arc = e.Current; digest = ArcDigest(digest, arc.Edge, arc.Neighbor, arc.Side); count++; }
                                if (!s.IsSuccess()) break;
                            }
                        }
                        if (!s.IsSuccess()) break;
                    }
                    if (!s.IsSuccess()) break;
                }
                Result[1] = (long)s; Result[2] = Complete; Result[3] = unchecked((long)digest); Result[4] = count; Result[5] = misses;
            }
        }
        private static ulong NodeDigest(ulong h, GraphNodeHandle node, int value) => Mix(Mix(h, node.Value.Packed), unchecked((ulong)value));
        private static ulong EdgeDigest(ulong h, GraphEdgeHandle edge, GraphEdge<int> value)
            => Mix(Mix(Mix(Mix(h, edge.Value.Packed), value.Source.Value.Packed), value.Target.Value.Packed), unchecked((ulong)value.Data));
        private static ulong ArcDigest(ulong h, GraphEdgeHandle edge, GraphNodeHandle neighbor, byte side)
            => Mix(Mix(Mix(h, edge.Value.Packed), neighbor.Value.Packed), side);
        [BurstCompile(CompileSynchronously = true)] private struct MutableReadJob : IJob
        {
            [ReadOnly] internal NativeGraphReadOnly<int, int> Reader;
            internal ReadWork Work;
            public void Execute() { int managed = 0; MarkManaged(ref managed); Work.Result[0] = managed; Work.Run(Reader); }
        }
        [BurstCompile(CompileSynchronously = true)] private struct FrozenReadJob : IJob
        {
            [ReadOnly] internal NativeFrozenGraphReadOnly<int, int> Reader;
            internal ReadWork Work;
            public void Execute() { int managed = 0; MarkManaged(ref managed); Work.Result[0] = managed; Work.Run(Reader); }
        }
        private static IEnumerable<TestCaseData> ReadCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int extent in new[] { 256, 8192 })
            foreach (bool frozen in new[] { false, true })
            foreach (ReadOperation op in Enum.GetValues(typeof(ReadOperation)))
                yield return new TestCaseData(backend, extent, frozen, op).SetName($"G11_Read_{backend}_{extent}_{(frozen ? "Frozen" : "Mutable")}_{op}");
        }
        [TestCaseSource(nameof(ReadCases)), Performance]
        public void ReadHandlesAndEnumerateHoles(Backend backend, int extent, bool frozen, ReadOperation operation)
        {
            using var owner = new Owner(backend, RingOptions(extent, slots: 0));
            var liveNodes = PopulateHoleyRing(owner, 256, extent, out var liveEdges);
            using var nodes = new NativeArray<GraphNodeHandle>(liveNodes, Allocator.Persistent);
            using var edges = new NativeArray<GraphEdgeHandle>(liveEdges, Allocator.Persistent);
            using var result = new NativeArray<long>(6, Allocator.Persistent);
            var work = new ReadWork { Nodes = nodes, Edges = edges, Result = result, Operation = operation };
            FrozenDeterministicGraph<int, int> managedFrozen = null; NativeFrozenDeterministicGraph<int, int> nativeFrozen = default;
            try
            {
                Action action;
                if (backend == Backend.Managed)
                {
                    if (frozen)
                    { managedFrozen = new FrozenDeterministicGraph<int, int>(owner.Options, 256, 256); owner.Managed.TryFreeze(managedFrozen).ThrowIfFailure(); var reader = managedFrozen.AsReadOnly(); action = () => work.Run(reader); }
                    else { var reader = owner.Managed.AsReadOnly(); action = () => work.Run(reader); }
                }
                else if (frozen)
                {
                    nativeFrozen = new NativeFrozenDeterministicGraph<int, int>(owner.Options, Allocator.Persistent, 256, 256);
                    owner.Native.TryFreeze(ref nativeFrozen).ThrowIfFailure(); var job = new FrozenReadJob { Reader = nativeFrozen.AsReadOnly(), Work = work }; action = () => job.Schedule().Complete();
                }
                else { var job = new MutableReadJob { Reader = owner.Native.AsReadOnly(), Work = work }; action = () => job.Schedule().Complete(); }
                ulong expected = DigestSeed;
                for (int repeat = 0; repeat < ReadIterations; repeat++) for (int i = 0; i < 256; i++)
                {
                    if (operation == ReadOperation.Nodes || operation == ReadOperation.NodeLookups)
                    {
                        expected = NodeDigest(expected, liveNodes[i], (int)liveNodes[i].Index);
                        if (operation == ReadOperation.NodeLookups) expected = Mix(expected, (ulong)DeterministicStatus.NotFound);
                    }
                    else if (operation == ReadOperation.Edges || operation == ReadOperation.EdgeLookups)
                    {
                        expected = EdgeDigest(expected, liveEdges[i], new GraphEdge<int>(liveNodes[i], liveNodes[(i + 1) % 256], (int)liveEdges[i].Index));
                        if (operation == ReadOperation.EdgeLookups) expected = Mix(expected, (ulong)DeterministicStatus.NotFound);
                    }
                    else
                    {
                        expected = ArcDigest(expected, liveEdges[i], liveNodes[(i + 1) % 256], 0);
                        expected = ArcDigest(expected, liveEdges[(i + 255) % 256], liveNodes[(i + 255) % 256], 1);
                    }
                }
                int items = ReadIterations * 256 * (operation == ReadOperation.Nodes || operation == ReadOperation.Edges ? 1 : 2);
                Benchmark("G11", operation.ToString(), backend, frozen ? "Frozen" : "Mutable", extent, items, ReadIterations, action, () =>
                {
                    CheckResult(backend, result); Assert.AreEqual(unchecked((long)expected), result[3]); Assert.AreEqual(items, result[4]);
                    Assert.AreEqual(operation == ReadOperation.NodeLookups || operation == ReadOperation.EdgeLookups ? ReadIterations * 256 : 0, result[5]);
                }, () => Reset(result), () =>
                {
                    var m = frozen ? Stats(backend == Backend.Managed ? managedFrozen.MemoryStats : nativeFrozen.MemoryStats) : owner.Metrics();
                    m.nodes = m.edges = 256; m.nodeExtent = m.edgeExtent = frozen ? 256 : extent;
                    if (frozen) m.nodeCapacity = m.edgeCapacity = 256;
                    m.auxiliaryBytes = nodes.Length * sizeof(GraphNodeHandle) + edges.Length * sizeof(GraphEdgeHandle);
                    m.observedItems = result[4]; m.observedMisses = result[5]; m.contentDigest = Hex(expected); return m;
                });
            }
            finally { managedFrozen?.Dispose(); if (nativeFrozen.IsCreated) nativeFrozen.Dispose(); }
        }
    }
}
