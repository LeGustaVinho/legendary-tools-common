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
        public enum LifecycleOperation { Capture, Restore, Release }
        private const int SnapshotBatch = 8;
        private static NativeGraphSnapshot<int, int> BindSnapshot(NativeGraphSnapshot<int, int> snapshot, NativeDeterministicGraph<int, int> graph)
        {
            // Job copies have scope-specific safety handles. Match the existing Native persistent
            // store: only rebind the descriptor to its owner, keeping every safety check enabled.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            snapshot.m_Safety = graph.m_Safety;
#endif
            return snapshot;
        }
        private static IEnumerable<TestCaseData> LifecycleCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int extent in new[] { 256, 8192 })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
            foreach (LifecycleOperation operation in Enum.GetValues(typeof(LifecycleOperation)))
                yield return new TestCaseData(backend, extent, policy, operation).SetName($"G9_Snapshots_{backend}_{extent}_{policy}_{operation}");
        }
        [BurstCompile(CompileSynchronously = true)] private struct LifecycleJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            internal NativeArray<byte> SnapshotMemory;
            internal NativeArray<long> Result;
            internal LifecycleOperation Operation;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                // NativeContainer snapshots cannot be elements of a scheduled NativeArray<T>.
                // This owned byte buffer holds their descriptors; the concrete graph provides safety.
                var roots = (NativeGraphSnapshot<int, int>*)NativeArrayUnsafeUtility.GetUnsafePtr(SnapshotMemory);
                var status = DeterministicStatus.None;
                if (Operation == LifecycleOperation.Restore) status = Graph.TryRestore(BindSnapshot(roots[SnapshotBatch], Graph));
                else for (int i = 0; i < SnapshotBatch; i++)
                {
                    if (Operation == LifecycleOperation.Capture) status = Graph.TryCapture(out roots[i]);
                    else { var bound = BindSnapshot(roots[i], Graph); bound.Dispose(); roots[i] = default; }
                    if (!status.IsSuccess()) break;
                }
                Result[1] = (long)status; Result[2] = Complete;
            }
        }
        [TestCaseSource(nameof(LifecycleCases)), Performance]
        public void SnapshotLifecycle(Backend backend, int extent, SnapshotPolicy policy, LifecycleOperation operation)
        {
            using var owner = new Owner(backend, RingOptions(extent, policy, SnapshotBatch + 1));
            var nodes = PopulateHoleyRing(owner, 256, extent, out _); var expected = owner.Canonical();
            using var basis = new Root(owner);
            var managedRoots = new ManagedGraphSnapshot<int, int>[SnapshotBatch];
            using var memory = new NativeArray<byte>(sizeof(NativeGraphSnapshot<int, int>) * (SnapshotBatch + 1), Allocator.Persistent);
            using var result = new NativeArray<long>(3, Allocator.Persistent);
            var nativeRoots = (NativeGraphSnapshot<int, int>*)NativeArrayUnsafeUtility.GetUnsafePtr(memory);
            nativeRoots[SnapshotBatch] = basis.Native;
            long baseline = owner.Memory.LivePayloadBytes;
            Action release = () =>
            {
                for (int i = 0; i < SnapshotBatch; i++)
                    if (backend == Backend.Managed) { managedRoots[i].Dispose(); managedRoots[i] = default; }
                    else { var bound = BindSnapshot(nativeRoots[i], owner.Native); bound.Dispose(); nativeRoots[i] = default; }
            };
            Action capture = () =>
            {
                for (int i = 0; i < SnapshotBatch; i++)
                    if (backend == Backend.Managed) owner.Managed.TryCapture(out managedRoots[i]).ThrowIfFailure();
                    else owner.Native.TryCapture(out nativeRoots[i]).ThrowIfFailure();
            };
            var job = new LifecycleJob { Graph = owner.Native, SnapshotMemory = memory, Result = result, Operation = operation };
            var managedResult = result;
            Action action = backend == Backend.Managed ? (Action)(() =>
            {
                var status = DeterministicStatus.None;
                if (operation == LifecycleOperation.Restore) status = owner.Managed.TryRestore(basis.Managed);
                else for (int i = 0; i < SnapshotBatch; i++)
                {
                    if (operation == LifecycleOperation.Capture) status = owner.Managed.TryCapture(out managedRoots[i]);
                    else managedRoots[i].Dispose();
                    if (!status.IsSuccess()) break;
                }
                managedResult[1] = (long)status; managedResult[2] = Complete;
            }) : () => job.Schedule().Complete();
            try
            {
                Benchmark("G9", operation.ToString(), backend, policy.ToString(), extent,
                    operation == LifecycleOperation.Restore ? 1 : SnapshotBatch, 1, action, () =>
                    {
                        CheckResult(backend, result);
                        CollectionAssert.AreEqual(expected, owner.Canonical());
                        Assert.AreEqual((int)nodes[0].Index, basis.GetNode(nodes[0]));
                        if (operation != LifecycleOperation.Restore)
                            for (int i = 0; i < SnapshotBatch; i++)
                            {
                                var bound = BindSnapshot(nativeRoots[i], owner.Native);
                                bool created = backend == Backend.Managed ? managedRoots[i].IsCreated : bound.IsCreated;
                                Assert.AreEqual(operation == LifecycleOperation.Capture, created);
                                if (created)
                                {
                                    int value;
                                    (backend == Backend.Managed ? managedRoots[i].TryGetNode(nodes[255], out value)
                                        : bound.TryGetNode(nodes[255], out value)).ThrowIfFailure();
                                    Assert.AreEqual((int)nodes[255].Index, value);
                                }
                            }
                    }, () =>
                    {
                        release(); basis.Restore();
                        if (operation == LifecycleOperation.Restore) for (int i = 0; i < 64; i++) owner.SetNode(nodes[i * 4], -100 - i);
                        if (operation == LifecycleOperation.Release) capture();
                        baseline = owner.Memory.LivePayloadBytes;
                        Reset(result);
                    }, () =>
                    {
                        var m = owner.Metrics(); m.snapshotsPerSample = operation == LifecycleOperation.Restore ? 1 : SnapshotBatch;
                        m.auxiliaryBytes = memory.Length; m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline;
                        m.canonicalBytes = expected.Length; m.contentDigest = Hex(CanonicalDigest(expected)); return m;
                    });
            }
            finally { release(); }
        }
        private static IEnumerable<TestCaseData> ClearCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int extent in new[] { 256, 8192 })
            foreach (SnapshotPolicy policy in new[] { SnapshotPolicy.FullPerTick, SnapshotPolicy.CopyOnWrite })
                yield return new TestCaseData(backend, extent, policy, false).SetName($"G10_Clear_{backend}_{extent}_{policy}_Active");
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
                yield return new TestCaseData(backend, 8192, SnapshotPolicy.CopyOnWrite, true).SetName($"G10_Clear_{backend}_8192_CopyOnWrite_AlreadyEmpty");
        }
        [BurstCompile(CompileSynchronously = true)] private struct ClearJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            internal NativeArray<long> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (long)Graph.TryClear(); Result[2] = Complete;
            }
        }
        [TestCaseSource(nameof(ClearCases)), Performance]
        public void ClearFragmentedSlots(Backend backend, int extent, SnapshotPolicy policy, bool empty)
        {
            using var owner = new Owner(backend, RingOptions(extent, policy, 1));
            var nodes = PopulateHoleyRing(owner, 256, extent, out var edges);
            if (empty) (owner.Managed != null ? owner.Managed.TryClear() : owner.Native.TryClear()).ThrowIfFailure();
            using var basis = new Root(owner); long baseline = owner.Memory.LivePayloadBytes;
            using var result = new NativeArray<long>(3, Allocator.Persistent); var write = result;
            var job = new ClearJob { Graph = owner.Native, Result = result };
            Action action = backend == Backend.Managed ? (Action)(() => { write[1] = (long)owner.Managed.TryClear(); write[2] = Complete; }) : () => job.Schedule().Complete();
            byte[] cleared = null;
            Benchmark("G10", "Clear", backend, policy + "_" + (empty ? "Empty" : "Active"), extent, empty ? 1 : 256, 1, action, () =>
            {
                CheckResult(backend, result); Assert.AreEqual(0, owner.NodeCount); Assert.AreEqual(0, owner.EdgeCount);
                var bytes = owner.Canonical(); if (cleared == null) cleared = bytes; else CollectionAssert.AreEqual(cleared, bytes);
                int data; GraphEdge<int> edge;
                Assert.AreEqual(DeterministicStatus.NotFound, owner.Managed != null ? owner.Managed.TryGetNode(nodes[0], out data) : owner.Native.TryGetNode(nodes[0], out data));
                Assert.AreEqual(DeterministicStatus.NotFound, owner.Managed != null ? owner.Managed.TryGetEdge(edges[0], out edge) : owner.Native.TryGetEdge(edges[0], out edge));
                Assert.AreEqual(empty ? 0 : 256, backend == Backend.Managed ? basis.Managed.NodeCount : basis.Native.NodeCount);
                if (!empty) Assert.AreEqual((int)nodes[255].Index, basis.GetNode(nodes[255]));
            }, () => { basis.Restore(); Reset(result); }, () =>
            {
                var m = owner.Metrics(); m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline;
                m.canonicalBytes = cleared.Length; m.contentDigest = Hex(CanonicalDigest(cleared)); return m;
            });
        }
    }
}
