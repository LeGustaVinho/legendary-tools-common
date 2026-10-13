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
        public enum VersionOperation { Single, Batch32, FailLast }
        private static IEnumerable<TestCaseData> VersionCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int capacity in new[] { 8, 512 })
            foreach (bool nearlyFull in new[] { false, true })
            foreach (VersionOperation op in Enum.GetValues(typeof(VersionOperation)))
                yield return new TestCaseData(b, capacity, nearlyFull, op).SetName($"G8_Versions_{b}_{capacity}_{(nearlyFull ? "NearlyFull" : "LowOccupancy")}_{op}");
        }
        [BurstCompile(CompileSynchronously = true)] private struct VersionJob : IJob
        {
            internal NativePersistentDeterministicGraph<int, int> Store;
            internal GraphVersionId Basis;
            [ReadOnly] internal NativeArray<GraphMutation<int, int>> Mutations;
            internal NativeArray<long> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                var s = Store.TryGetVersion(Basis, out var basis);
                if (s.IsSuccess())
                {
                    var commands = new GraphWorkBuffer<GraphMutation<int, int>>((GraphMutation<int, int>*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Mutations), Mutations.Length);
                    s = Store.TryWithBatch(basis, ref commands, Mutations.Length, out var version);
                    Result[3] = s.IsSuccess() ? version.Id.Slot : -1; Result[4] = s.IsSuccess() ? unchecked((long)version.Id.Lease) : 0;
                }
                Result[1] = (long)s; Result[2] = Complete;
            }
        }
        [TestCaseSource(nameof(VersionCases)), Performance]
        public void PublishWithOccupiedVersionPool(Backend backend, int capacity, bool nearlyFull, VersionOperation operation)
        {
            var options = Options(8, 1); var persistence = new GraphPersistenceOptions(capacity);
            PersistentDeterministicGraph<int, int> managed = null; NativePersistentDeterministicGraph<int, int> native = default;
            ManagedGraphVersion<int, int> managedBasis = default, managedBranch = default;
            NativeGraphVersion<int, int> nativeBasis = default, nativeBranch = default;
            var managedRoots = new List<ManagedGraphVersion<int, int>>(); var nativeRoots = new List<NativeGraphVersion<int, int>>();
            int retained = nearlyFull ? capacity - 1 : 2; GraphNodeHandle node;
            try
            {
                if (backend == Backend.Managed)
                {
                    managed = new PersistentDeterministicGraph<int, int>(options, persistence);
                    managed.TryCaptureCurrent(out var empty).ThrowIfFailure(); managed.TryWithAddNode(empty, 42, out managedBasis, out node).ThrowIfFailure(); empty.Dispose();
                    managed.TrySelectCurrent(managedBasis).ThrowIfFailure();
                    while (managed.VersionCount < retained) { managed.TryCaptureCurrent(out var v).ThrowIfFailure(); managedRoots.Add(v); }
                }
                else
                {
                    native = new NativePersistentDeterministicGraph<int, int>(options, persistence, Allocator.Persistent);
                    native.TryCaptureCurrent(out var empty).ThrowIfFailure(); native.TryWithAddNode(empty, 42, out nativeBasis, out node).ThrowIfFailure(); empty.Dispose();
                    native.TrySelectCurrent(nativeBasis).ThrowIfFailure();
                    while (native.VersionCount < retained) { native.TryCaptureCurrent(out var v).ThrowIfFailure(); nativeRoots.Add(v); }
                }
                int commandCount = operation == VersionOperation.Single ? 1 : 32;
                using var mutations = new NativeArray<GraphMutation<int, int>>(commandCount, Allocator.Persistent);
                using var result = new NativeArray<long>(5, Allocator.Persistent);
                var managedResult = result;
                var write = mutations;
                for (int i = 0; i < commandCount; i++) write[i] = new GraphMutation<int, int>
                { Kind = GraphMutationKind.SetNode, Node = operation == VersionOperation.FailLast && i == commandCount - 1
                    ? new GraphNodeHandle(new StableHandle(8, 1)) : node, NodeData = 100 + i };
                long baseline = backend == Backend.Managed ? managed.MemoryStats.LivePayloadBytes : native.MemoryStats.LivePayloadBytes;
                var job = new VersionJob { Store = native, Basis = backend == Backend.BurstJob ? nativeBasis.Id : default, Mutations = mutations, Result = result };
                Action action = backend == Backend.Managed ? (Action)(() =>
                {
                    var commands = new GraphWorkBuffer<GraphMutation<int, int>>((GraphMutation<int, int>*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(mutations), commandCount);
                    var s = managed.TryWithBatch(managedBasis, ref commands, commandCount, out managedBranch);
                    managedResult[1] = (long)s; managedResult[2] = Complete; managedResult[3] = s.IsSuccess() ? managedBranch.Id.Slot : -1;
                    managedResult[4] = s.IsSuccess() ? unchecked((long)managedBranch.Id.Lease) : 0;
                }) : () => job.Schedule().Complete();
                bool failed = operation == VersionOperation.FailLast; int expectedValue = failed ? 42 : 100 + commandCount - 1;
                Action setup = () =>
                {
                    managedBranch.Dispose(); managedBranch = default; nativeBranch.Dispose(); nativeBranch = default; Reset(result);
                    Assert.AreEqual(retained, backend == Backend.Managed ? managed.VersionCount : native.VersionCount);
                };
                Benchmark("G8", operation.ToString(), backend, capacity + "_" + (nearlyFull ? "NearlyFull" : "LowOccupancy"),
                    capacity, commandCount, 1, action, () =>
                    {
                        var expected = failed ? DeterministicStatus.NotFound : DeterministicStatus.None;
                        if (backend == Backend.BurstJob) CheckJob(result, expected); else { Assert.AreEqual((long)expected, result[1]); Assert.AreEqual(Complete, result[2]); }
                        Assert.AreEqual(retained + (failed ? 0 : 1), backend == Backend.Managed ? managed.VersionCount : native.VersionCount);
                        if (backend == Backend.Managed)
                        {
                            managed.Current.TryGetNode(node, out int current).ThrowIfFailure(); Assert.AreEqual(42, current);
                            managedBasis.AsReadOnly().TryGetNode(node, out current).ThrowIfFailure(); Assert.AreEqual(42, current);
                            foreach (var v in managedRoots) { v.AsReadOnly().TryGetNode(node, out current).ThrowIfFailure(); Assert.AreEqual(42, current); }
                            if (!failed) { managedBranch.AsReadOnly().TryGetNode(node, out current).ThrowIfFailure(); Assert.AreEqual(expectedValue, current); }
                            else Assert.IsFalse(managedBranch.IsCreated);
                        }
                        else
                        {
                            native.Current.TryGetNode(node, out int current).ThrowIfFailure(); Assert.AreEqual(42, current);
                            nativeBasis.AsReadOnly().TryGetNode(node, out current).ThrowIfFailure(); Assert.AreEqual(42, current);
                            foreach (var v in nativeRoots) { v.AsReadOnly().TryGetNode(node, out current).ThrowIfFailure(); Assert.AreEqual(42, current); }
                            if (!failed)
                            {
                                native.TryGetVersion(new GraphVersionId((int)result[3], unchecked((ulong)result[4])), out nativeBranch).ThrowIfFailure();
                                nativeBranch.AsReadOnly().TryGetNode(node, out current).ThrowIfFailure(); Assert.AreEqual(expectedValue, current);
                            }
                            else Assert.AreEqual(-1, result[3]);
                        }
                    }, setup, () =>
                    {
                        var m = Stats(backend == Backend.Managed ? managed.MemoryStats : native.MemoryStats);
                        m.nodes = 1; m.nodeExtent = 1; m.retainedVersions = retained + (failed ? 0 : 1); m.versionCapacity = capacity;
                        m.internalScratchReservedBytes = InternalScratch(options); m.auxiliaryBytes = commandCount * sizeof(GraphMutation<int, int>);
                        m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline;
                        m.contentDigest = Hex(Mix(DigestSeed, (ulong)expectedValue)); return m;
                    });
            }
            finally
            {
                managedBranch.Dispose(); nativeBranch.Dispose();
                foreach (var v in managedRoots) v.Dispose(); foreach (var v in nativeRoots) v.Dispose();
                managedBasis.Dispose(); nativeBasis.Dispose(); managed?.Dispose(); if (native.IsCreated) native.Dispose();
            }
        }
    }
}
