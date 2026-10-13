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
        public enum WireOperation { WriteCanonical, ValidateCanonical, ReadCanonical, DeltaFacade, DeltaCheckpoints, ApplyDelta, ReverseDelta }
        public enum WireScenario { Chain, AcyclicChain, FragmentedChain }
        private interface IWireGraph
        {
            GraphOptions Options { get; }
            DeterministicStatus Write(ref DeterministicWriter w);
            DeterministicStatus Read(ref DeterministicReader r, ref CanonicalScratch s, bool validate);
            DeterministicStatus Delta(in GraphCheckpoint basis, ref DeterministicWriter w, ref CanonicalScratch s, out GraphTransferKind kind, GraphDeltaMode mode);
            DeterministicStatus Apply(ref DeterministicReader r, ref CanonicalScratch s, bool reverse);
        }
        private struct ManagedWireGraph : IWireGraph
        {
            internal DeterministicGraph<int, int> Graph;
            public GraphOptions Options => Graph.Options;
            public DeterministicStatus Write(ref DeterministicWriter w) => Graph.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Read(ref DeterministicReader r, ref CanonicalScratch s, bool validate) => validate
                ? Graph.TryValidateCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy))
                : Graph.TryReadCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Delta(in GraphCheckpoint basis, ref DeterministicWriter w, ref CanonicalScratch s, out GraphTransferKind kind, GraphDeltaMode mode)
                => Graph.TryWriteDelta(basis, ref w, ref s, default(Int32Policy), default(Int32Policy), out kind, mode);
            public DeterministicStatus Apply(ref DeterministicReader r, ref CanonicalScratch s, bool reverse)
                => Graph.TryApplyDelta(ref r, ref s, default(Int32Policy), default(Int32Policy), reverse);
        }
        private struct NativeWireGraph : IWireGraph
        {
            internal NativeDeterministicGraph<int, int> Graph;
            public GraphOptions Options => Graph.Options;
            public DeterministicStatus Write(ref DeterministicWriter w) => Graph.TryWriteCanonical(ref w, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Read(ref DeterministicReader r, ref CanonicalScratch s, bool validate) => validate
                ? Graph.TryValidateCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy))
                : Graph.TryReadCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy));
            public DeterministicStatus Delta(in GraphCheckpoint basis, ref DeterministicWriter w, ref CanonicalScratch s, out GraphTransferKind kind, GraphDeltaMode mode)
                => Graph.TryWriteDelta(basis, ref w, ref s, default(Int32Policy), default(Int32Policy), out kind, mode);
            public DeterministicStatus Apply(ref DeterministicReader r, ref CanonicalScratch s, bool reverse)
                => Graph.TryApplyDelta(ref r, ref s, default(Int32Policy), default(Int32Policy), reverse);
        }
        private static void Wire<TGraph>(TGraph graph, WireOperation operation, GraphDeltaMode mode,
            NativeArray<byte> basis, NativeArray<byte> target, NativeArray<byte> packet, int packetLength,
            NativeArray<byte> memory, NativeArray<long> result, int iterations) where TGraph : struct, IWireGraph
        {
            var b = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(basis);
            var t = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(target);
            var p = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packet);
            var work = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(memory);
            for (int i = 0; i < iterations; i++)
            {
                var scratch = new CanonicalScratch(work, memory.Length); var writer = new DeterministicWriter(p, packet.Length);
                var reader = new DeterministicReader(operation == WireOperation.ApplyDelta || operation == WireOperation.ReverseDelta ? p : b,
                    operation == WireOperation.ApplyDelta || operation == WireOperation.ReverseDelta ? packetLength : basis.Length);
                var checkpoint = new GraphCheckpoint(b, basis.Length); GraphTransferKind kind = default; DeterministicStatus s;
                switch (operation)
                {
                    case WireOperation.WriteCanonical: s = graph.Write(ref writer); break;
                    case WireOperation.ValidateCanonical: s = graph.Read(ref reader, ref scratch, true); break;
                    case WireOperation.ReadCanonical: s = graph.Read(ref reader, ref scratch, false); break;
                    case WireOperation.DeltaFacade: s = graph.Delta(checkpoint, ref writer, ref scratch, out kind, mode); break;
                    case WireOperation.DeltaCheckpoints:
                        s = GraphDeltaWire<int, int, Int32Policy, Int32Policy>.TryWriteDelta(ref writer, checkpoint,
                            new GraphCheckpoint(t, target.Length), ref scratch, graph.Options, default, default, out kind, mode); break;
                    default: s = graph.Apply(ref reader, ref scratch, operation == WireOperation.ReverseDelta); break;
                }
                result[1] = (long)s; result[3] = writer.Position; result[4] = scratch.Position; result[5] = reader.Position; result[6] = (long)kind;
                if (!s.IsSuccess()) { result[2] = Complete; return; }
            }
            result[2] = Complete;
        }
        [BurstCompile(CompileSynchronously = true)] private struct WireJob : IJob
        {
            internal NativeDeterministicGraph<int, int> Graph;
            [ReadOnly] internal NativeArray<byte> Basis, Target;
            internal NativeArray<byte> Packet, Scratch;
            internal NativeArray<long> Result;
            internal WireOperation Operation;
            internal GraphDeltaMode Mode;
            internal int PacketLength, Iterations;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Wire(new NativeWireGraph { Graph = Graph }, Operation, Mode, Basis, Target, Packet, PacketLength, Scratch, Result, Iterations);
            }
        }
        private static Action WireAction(Owner owner, WireOperation op, GraphDeltaMode mode, NativeArray<byte> basis,
            NativeArray<byte> target, NativeArray<byte> packet, int length, NativeArray<byte> scratch, NativeArray<long> result, int iterations)
        {
            if (owner.Managed != null)
            {
                var graph = new ManagedWireGraph { Graph = owner.Managed };
                return () => Wire(graph, op, mode, basis, target, packet, length, scratch, result, iterations);
            }
            var job = new WireJob { Graph = owner.Native, Operation = op, Mode = mode, Basis = basis, Target = target,
                Packet = packet, PacketLength = length, Scratch = scratch, Result = result, Iterations = iterations };
            return () => job.Schedule().Complete();
        }
        private static IEnumerable<TestCaseData> WireCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (WireOperation op in new[] { WireOperation.WriteCanonical, WireOperation.ValidateCanonical, WireOperation.ReadCanonical })
            {
                foreach (int count in new[] { 256, 2048 })
                foreach (WireScenario scenario in new[] { WireScenario.Chain, WireScenario.AcyclicChain })
                    yield return new TestCaseData(b, count, scenario, op).SetName($"G6_Canonical_{b}_{count}_{scenario}_{op}");
                yield return new TestCaseData(b, 256, WireScenario.FragmentedChain, op).SetName($"G6_Canonical_{b}_256_FragmentedChain_{op}");
            }
        }
        [TestCaseSource(nameof(WireCases)), Performance]
        public void SerializeValidateAndImport(Backend backend, int count, WireScenario scenario, WireOperation operation)
        {
            const int iterations = 8; int extent = scenario == WireScenario.FragmentedChain ? count * 32 : count;
            var options = Options(extent, count, acyclic: scenario == WireScenario.AcyclicChain);
            using var source = new Owner(backend, options); PopulateSparse(source, count, extent);
            using var destination = new Owner(backend, options);
            var canonical = source.Canonical();
            using var basis = new NativeArray<byte>(canonical, Allocator.Persistent);
            using var target = new NativeArray<byte>(canonical, Allocator.Persistent);
            using var packet = new NativeArray<byte>(canonical.Length, Allocator.Persistent);
            GraphWire<int, int, Int32Policy, Int32Policy>.TryMeasureScratch(options, out int bytes).ThrowIfFailure();
            using var scratch = new NativeArray<byte>(bytes, Allocator.Persistent);
            using var result = new NativeArray<long>(7, Allocator.Persistent);
            Owner measured = operation == WireOperation.WriteCanonical ? source : destination;
            var action = WireAction(measured, operation, GraphDeltaMode.Forward, basis, target, packet, 0, scratch, result, iterations);
            Benchmark("G6", operation.ToString(), backend, scenario.ToString(), count, iterations * count, iterations, action, () =>
            {
                if (backend == Backend.BurstJob) CheckJob(result); else { Assert.AreEqual(0, result[1]); Assert.AreEqual(Complete, result[2]); }
                if (operation == WireOperation.WriteCanonical) { Assert.AreEqual(canonical.Length, result[3]); CollectionAssert.AreEqual(canonical, packet.ToArray()); }
                else
                {
                    Assert.AreEqual(canonical.Length, result[5]); Assert.LessOrEqual(result[4], bytes);
                    if (operation == WireOperation.ReadCanonical) CollectionAssert.AreEqual(canonical, destination.Canonical());
                    else Assert.AreEqual(0, destination.NodeCount);
                }
            }, () => Reset(result), () =>
            {
                var m = measured.Metrics(); m.externalScratchReservedBytes = bytes; m.scratchConsumedBytes = (int)result[4];
                m.auxiliaryBytes = basis.Length + target.Length + packet.Length; m.canonicalBytes = canonical.Length;
                m.contentDigest = Hex(CanonicalDigest(canonical)); return m;
            });
        }
        private static IEnumerable<TestCaseData> DeltaCases()
        {
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (int changes in new[] { 0, 1, count })
            foreach (WireOperation op in new[] { WireOperation.DeltaFacade, WireOperation.DeltaCheckpoints, WireOperation.ApplyDelta })
                yield return new TestCaseData(b, count, changes, GraphDeltaMode.Forward, op).SetName($"G7_Delta_{b}_{count}_{changes}_Forward_{op}");
            foreach (Backend b in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (WireOperation op in new[] { WireOperation.DeltaFacade, WireOperation.DeltaCheckpoints, WireOperation.ApplyDelta, WireOperation.ReverseDelta })
                yield return new TestCaseData(b, count, 1, GraphDeltaMode.Reversible, op).SetName($"G7_Delta_{b}_{count}_1_Reversible_{op}");
        }
        private static void Import(Owner owner, NativeArray<byte> bytes, NativeArray<byte> memory)
        {
            var r = new DeterministicReader((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytes), bytes.Length);
            var s = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(memory), memory.Length);
            (owner.Managed != null ? owner.Managed.TryReadCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy))
                : owner.Native.TryReadCanonical(ref r, ref s, default(Int32Policy), default(Int32Policy))).ThrowIfFailure();
        }
        private struct CountingIntCodec : IDeterministicCodec<int>
        {
            internal long* Counts;
            public uint SchemaId => default(Int32Policy).SchemaId;
            public ushort Version => default(Int32Policy).Version;
            public int Measure(in int value) => default(Int32Policy).Measure(value);
            public DeterministicStatus TryWrite(ref DeterministicWriter writer, in int value)
            { Counts[writer.IsMeasuring ? 0 : 1]++; return default(Int32Policy).TryWrite(ref writer, value); }
            public DeterministicStatus TryRead(ref DeterministicReader reader, out int value)
            { Counts[2]++; return default(Int32Policy).TryRead(ref reader, out value); }
        }
        [TestCaseSource(nameof(DeltaCases)), Performance]
        public void EncodeAndApplySparseOrDenseDelta(Backend backend, int count, int changes, GraphDeltaMode mode, WireOperation operation)
        {
            bool applying = operation == WireOperation.ApplyDelta || operation == WireOperation.ReverseDelta;
            int iterations = applying ? 1 : 4;
            var options = Options(count, count, SnapshotPolicy.CopyOnWrite, 2);
            using var source = new Owner(backend, options); var nodes = PopulateSparse(source, count, count);
            var original = source.Canonical();
            for (int i = 0; i < changes; i++) source.SetNode(nodes[i], 9000 + i);
            var changed = source.Canonical();
            using var basis = new NativeArray<byte>(original, Allocator.Persistent);
            using var target = new NativeArray<byte>(changed, Allocator.Persistent);
            GraphDeltaWire<int, int, Int32Policy, Int32Policy>.TryMeasureScratch(options, original.Length, changed.Length, out int bytes).ThrowIfFailure();
            // The facade also stages its own current canonical root before invoking GraphDeltaWire.
            bytes = checked(bytes + Math.Max(original.Length, changed.Length) + 64);
            using var scratch = new NativeArray<byte>(bytes, Allocator.Persistent);
            using var packet = new NativeArray<byte>(checked(original.Length + changed.Length + 512), Allocator.Persistent);
            using var result = new NativeArray<long>(7, Allocator.Persistent);
            // Encode the reference transfer through the real graph adapter, outside timing.
            var prepare = WireAction(source, WireOperation.DeltaFacade, mode, basis, target, packet, 0, scratch, result, 1);
            prepare(); if (backend == Backend.BurstJob) CheckJob(result); else Assert.AreEqual(0, result[1]);
            int packetLength = (int)result[3]; var transferKind = (GraphTransferKind)result[6]; var expectedPacket = packet.GetSubArray(0, packetLength).ToArray();
            using var destination = new Owner(backend, options); Import(destination, operation == WireOperation.ReverseDelta ? target : basis, scratch);
            using var root = new Root(destination); long baseline = destination.Memory.LivePayloadBytes;
            var action = WireAction(applying ? destination : source, operation, mode, basis, target, packet, packetLength, scratch, result, iterations);
            byte[] expectedState = operation == WireOperation.ReverseDelta ? original : changed;
            Benchmark("G7", operation.ToString(), backend, changes + "_" + mode, count, applying ? count : iterations * count, iterations, action, () =>
            {
                if (backend == Backend.BurstJob) CheckJob(result); else { Assert.AreEqual(0, result[1]); Assert.AreEqual(Complete, result[2]); }
                Assert.LessOrEqual(result[4], bytes);
                if (applying)
                { Assert.AreEqual(packetLength, result[5]); CollectionAssert.AreEqual(expectedState, destination.Canonical()); }
                else
                { Assert.AreEqual(packetLength, result[3]); Assert.AreEqual((long)transferKind, result[6]); CollectionAssert.AreEqual(expectedPacket, packet.GetSubArray(0, packetLength).ToArray()); }
            }, () => { if (applying) root.Restore(); Reset(result); }, () =>
            {
                var m = (applying ? destination : source).Metrics(); m.externalScratchReservedBytes = bytes; m.scratchConsumedBytes = (int)result[4];
                m.auxiliaryBytes = basis.Length + target.Length + packet.Length; m.canonicalBytes = original.Length; m.transferBytes = packetLength;
                m.transferKind = transferKind.ToString(); m.contentDigest = Hex(CanonicalDigest(expectedState));
                if (applying) { m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline; }
                else if (operation == WireOperation.DeltaFacade)
                {
                    using var counters = new NativeArray<long>(3, Allocator.Persistent);
                    var codec = new CountingIntCodec { Counts = (long*)NativeArrayUnsafeUtility.GetUnsafePtr(counters) };
                    var w = new DeterministicWriter((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(packet), packet.Length);
                    var s = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(scratch), scratch.Length);
                    var checkpoint = new GraphCheckpoint((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(basis), basis.Length);
                    (source.Managed != null ? source.Managed.TryWriteDelta(checkpoint, ref w, ref s, codec, codec, out _, mode)
                        : source.Native.TryWriteDelta(checkpoint, ref w, ref s, codec, codec, out _, mode)).ThrowIfFailure();
                    m.codecMeasureCalls = counters[0]; m.codecWriteCalls = counters[1]; m.codecReadCalls = counters[2];
                    Assert.AreEqual(packetLength, w.Position);
                }
                return m;
            });
        }
    }
}
