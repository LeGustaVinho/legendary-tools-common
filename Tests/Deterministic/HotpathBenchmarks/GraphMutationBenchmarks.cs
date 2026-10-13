using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class GraphHotpathBenchmarks
    {
        public enum InsertMode { Append, ReuseLowSlots }
        public enum CycleScenario { AllowLongAccept, AcyclicLongAccept, AcyclicShortcut, AcyclicReject }
        private enum MutationOperation { AddBatch, AddOne, SetBatch }
        private static DeterministicStatus Mutate<TGraph>(ref TGraph graph, MutationOperation op,
            GraphNodeHandle source, GraphNodeHandle target, NativeArray<GraphNodeHandle> nodes,
            NativeArray<GraphEdgeHandle> edges, int count) where TGraph : IDeterministicGraphMutator<int, int>
        {
            for (int i = 0; i < count; i++)
            {
                var m = op == MutationOperation.SetBatch
                    ? new GraphMutation<int, int> { Kind = GraphMutationKind.SetNode, Node = nodes[i], NodeData = 7000 + i }
                    : new GraphMutation<int, int> { Kind = GraphMutationKind.AddEdge, Source = source,
                        Target = op == MutationOperation.AddOne ? target : nodes[i], EdgeData = 7000 + i };
                var s = graph.TryApply(m, out var r); if (!s.IsSuccess()) return s;
                if (op != MutationOperation.SetBatch) edges[i] = r.Edge;
            }
            return DeterministicStatus.None;
        }
        [BurstCompile(CompileSynchronously = true)] private struct MutationJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            [ReadOnly] internal NativeArray<GraphNodeHandle> Nodes;
            internal NativeArray<GraphEdgeHandle> Edges;
            internal NativeArray<long> Result;
            internal GraphNodeHandle Source, Target;
            internal MutationOperation Operation;
            internal int Count;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (long)Mutate(ref Graph, Operation, Source, Target, Nodes, Edges, Count); Result[2] = Complete;
            }
        }
        private static Action MutationAction(Owner owner, MutationOperation op, GraphNodeHandle source,
            GraphNodeHandle target, NativeArray<GraphNodeHandle> nodes, NativeArray<GraphEdgeHandle> edges,
            NativeArray<long> result, int count)
        {
            if (owner.Managed != null) return () =>
            {
                var graph = owner.Managed; result[1] = (long)Mutate(ref graph, op, source, target, nodes, edges, count); result[2] = Complete;
            };
            var job = new MutationJob { Graph = owner.Native, Operation = op, Source = source, Target = target,
                Nodes = nodes, Edges = edges, Result = result, Count = count };
            return () => job.Schedule().Complete();
        }
        private static void CheckMutation(Backend backend, NativeArray<long> r, DeterministicStatus expected = DeterministicStatus.None)
        {
            if (backend == Backend.BurstJob) CheckJob(r, expected);
            else { Assert.AreEqual((long)expected, r[1]); Assert.AreEqual(Complete, r[2]); }
        }
        private static IEnumerable<TestCaseData> InsertionCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int degree in new[] { 256, 2048 })
            foreach (InsertMode mode in new[] { InsertMode.Append, InsertMode.ReuseLowSlots })
            foreach (bool parallel in new[] { false, true })
                yield return new TestCaseData(b, degree, mode, parallel).SetName($"G1_Insert_{b}_{degree}_{mode}_{(parallel ? "Parallel" : "Simple")}");
        }
        [TestCaseSource(nameof(InsertionCases)), Performance]
        public void InsertAtHighDegreeNode(Backend backend, int degree, InsertMode mode, bool parallel)
        {
            const int batch = 64;
            using var owner = new Owner(backend, Options(degree + batch + 1, degree + batch, SnapshotPolicy.CopyOnWrite, 1, parallel: parallel));
            var all = new GraphNodeHandle[degree + batch + 1];
            for (int i = 0; i < all.Length; i++) all[i] = owner.AddNode(i);
            var initial = new GraphEdgeHandle[degree];
            for (int i = 0; i < degree; i++) initial[i] = owner.AddEdge(all[0], all[i + 1], i);
            if (mode == InsertMode.ReuseLowSlots) for (int i = 0; i < batch; i++) owner.RemoveEdge(initial[i]);
            using var root = new Root(owner); int baselineEdges = owner.EdgeCount; long baseline = owner.Memory.LivePayloadBytes;
            using var nodes = new NativeArray<GraphNodeHandle>(batch, Allocator.Persistent);
            using var edges = new NativeArray<GraphEdgeHandle>(batch, Allocator.Persistent);
            using var result = new NativeArray<long>(3, Allocator.Persistent);
            var writableNodes = nodes;
            for (int i = 0; i < batch; i++) writableNodes[i] = all[degree + i + 1];
            var action = MutationAction(owner, MutationOperation.AddBatch, all[0], default, nodes, edges, result, batch);
            Action validate = () =>
            {
                CheckMutation(backend, result); Assert.AreEqual(baselineEdges + batch, owner.EdgeCount);
                for (int i = 0; i < batch; i++)
                {
                    Assert.AreEqual((uint)(mode == InsertMode.Append ? degree + i : i), edges[i].Index);
                    Assert.AreEqual(mode == InsertMode.Append ? 1u : 2u, edges[i].Generation);
                    GraphEdge<int> e; var s = owner.Managed != null ? owner.Managed.TryGetEdge(edges[i], out e) : owner.Native.TryGetEdge(edges[i], out e);
                    s.ThrowIfFailure(); Assert.AreEqual(all[0], e.Source); Assert.AreEqual(nodes[i], e.Target); Assert.AreEqual(7000 + i, e.Data);
                }
                if (owner.Managed != null) CheckOrderedArcs(owner.Managed.AsReadOnly(), all[0], baselineEdges + batch);
                else CheckOrderedArcs(owner.Native.AsReadOnly(), all[0], baselineEdges + batch);
                Assert.AreEqual(baselineEdges, owner.Managed != null ? root.Managed.EdgeCount : root.Native.EdgeCount);
            };
            Benchmark("G1", "AddEdges", backend, mode + "_" + (parallel ? "Parallel" : "Simple"), degree, batch, 1,
                action, validate, () => { root.Restore(); Reset(result); }, () =>
                {
                    var m = owner.Metrics(); m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline;
                    m.auxiliaryBytes = batch * (sizeof(GraphNodeHandle) + sizeof(GraphEdgeHandle)); m.contentDigest = Hex(CanonicalDigest(owner.Canonical())); return m;
                });
        }
        private static void CheckOrderedArcs<TReader>(TReader reader, GraphNodeHandle hub, int expected)
            where TReader : struct, IDeterministicGraphReader<int, int>
        {
            reader.TryFirstArc(hub, GraphTraversal.Outgoing, out int cursor).ThrowIfFailure();
            GraphEdgeHandle previous = default; int count = 0;
            while (cursor != 0)
            {
                reader.TryReadArc(cursor, out var arc, out cursor).ThrowIfFailure();
                if (count != 0) Assert.Less(previous.CompareTo(arc.Edge), 0); previous = arc.Edge; count++;
            }
            Assert.AreEqual(expected, count);
        }
        private static IEnumerable<TestCaseData> CycleCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int n in new[] { 256, 2048 })
            foreach (CycleScenario s in Enum.GetValues(typeof(CycleScenario)))
                yield return new TestCaseData(b, n, s).SetName($"G2_CycleCheck_{b}_{n}_{s}");
        }
        [TestCaseSource(nameof(CycleCases)), Performance]
        public void ValidateAcyclicInsertion(Backend backend, int count, CycleScenario scenario)
        {
            bool acyclic = scenario != CycleScenario.AllowLongAccept;
            using var owner = new Owner(backend, Options(count, count, SnapshotPolicy.CopyOnWrite, 1, acyclic: acyclic));
            var all = PopulateSparse(owner, count, count); using var root = new Root(owner);
            var source = scenario == CycleScenario.AcyclicShortcut ? all[0] : scenario == CycleScenario.AcyclicReject ? all[count - 2] : all[count - 1];
            var target = scenario == CycleScenario.AcyclicShortcut ? all[count - 2] : all[0];
            bool rejected = scenario == CycleScenario.AcyclicReject;
            byte[] before = owner.Canonical();
            using var nodes = new NativeArray<GraphNodeHandle>(1, Allocator.Persistent);
            using var edges = new NativeArray<GraphEdgeHandle>(1, Allocator.Persistent);
            using var result = new NativeArray<long>(3, Allocator.Persistent);
            var action = MutationAction(owner, MutationOperation.AddOne, source, target, nodes, edges, result, 1);
            Benchmark("G2", "AddEdgeCycleCheck", backend, scenario.ToString(), count, 1, 1, action, () =>
            {
                CheckMutation(backend, result, rejected ? DeterministicStatus.CycleDetected : DeterministicStatus.None);
                Assert.AreEqual(count - 2 + (rejected ? 0 : 1), owner.EdgeCount);
                Assert.AreEqual(count - 2, owner.Managed != null ? root.Managed.EdgeCount : root.Native.EdgeCount);
                if (rejected) CollectionAssert.AreEqual(before, owner.Canonical());
                else
                {
                    GraphEdge<int> e; var s = owner.Managed != null ? owner.Managed.TryGetEdge(edges[0], out e) : owner.Native.TryGetEdge(edges[0], out e);
                    s.ThrowIfFailure(); Assert.AreEqual(source, e.Source); Assert.AreEqual(target, e.Target);
                }
            }, () => { root.Restore(); Reset(result); }, () =>
            { var m = owner.Metrics(); m.contentDigest = Hex(CanonicalDigest(owner.Canonical())); return m; });
        }
        private static IEnumerable<TestCaseData> LocalityCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
            foreach (int page in new[] { 256, 4096 })
            foreach (bool scattered in new[] { false, true })
                yield return new TestCaseData(b, policy, page, scattered).SetName($"G3_PayloadWrites_{b}_{policy}_{page}_{(scattered ? "Scattered" : "Clustered")}");
        }
        [TestCaseSource(nameof(LocalityCases)), Performance]
        public void UpdatePayloadsAcrossRetainedPages(Backend backend, SnapshotPolicy policy, int pageBytes, bool scattered)
        {
            const int count = 4096, batch = 64;
            using var owner = new Owner(backend, Options(count, 1, policy, 1, pageBytes));
            var all = new GraphNodeHandle[count]; for (int i = 0; i < count; i++) all[i] = owner.AddNode(i);
            using var root = new Root(owner); long baseline = owner.Memory.LivePayloadBytes;
            using var nodes = new NativeArray<GraphNodeHandle>(batch, Allocator.Persistent);
            using var edges = new NativeArray<GraphEdgeHandle>(batch, Allocator.Persistent);
            using var result = new NativeArray<long>(3, Allocator.Persistent);
            var writableNodes = nodes;
            for (int i = 0; i < batch; i++) writableNodes[i] = all[scattered ? i * (count / batch) : i];
            var action = MutationAction(owner, MutationOperation.SetBatch, default, default, nodes, edges, result, batch);
            Benchmark("G3", "SetNodePayloads", backend, policy + "_" + pageBytes + "_" + (scattered ? "Scattered" : "Clustered"),
                count, batch, 1, action, () =>
                {
                    CheckMutation(backend, result);
                    for (int i = 0; i < count; i++)
                    {
                        int changed = scattered ? (i % (count / batch) == 0 ? i / (count / batch) : -1) : (i < batch ? i : -1);
                        int value; var s = owner.Managed != null ? owner.Managed.TryGetNode(all[i], out value) : owner.Native.TryGetNode(all[i], out value);
                        s.ThrowIfFailure(); Assert.AreEqual(changed >= 0 ? 7000 + changed : i, value); Assert.AreEqual(i, root.GetNode(all[i]));
                    }
                    if (policy == SnapshotPolicy.CopyOnWrite) Assert.Greater(owner.Memory.LivePayloadBytes, baseline);
                    else Assert.AreEqual(baseline, owner.Memory.LivePayloadBytes);
                }, () => { root.Restore(); Reset(result); }, () =>
                {
                    var m = owner.Metrics(); m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline;
                    m.auxiliaryBytes = batch * (sizeof(GraphNodeHandle) + sizeof(GraphEdgeHandle)); m.contentDigest = Hex(CanonicalDigest(owner.Canonical())); return m;
                });
        }
    }
}
