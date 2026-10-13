using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.PerformanceTesting;

namespace LegendaryTools.Deterministic.Tests.HotpathBenchmarks
{
    public sealed unsafe partial class GraphExtendedHotpathBenchmarks
    {
        public enum StructuralScenario { RemoveInterior, RemoveHub, ReuseSlots, GrowCapacity }
        public enum RejectedTransfer { WrongBaseHash, CorruptTargetHash, TruncatedTail, ScratchShort }
        private static GraphOptions StructuralOptions(int count, StructuralScenario scenario)
        {
            bool grow = scenario == StructuralScenario.GrowCapacity;
            var buffer = new BufferOptions(count, grow ? count * 2 : count, grow ? AllocationPolicy.Grow : AllocationPolicy.Fixed,
                SnapshotPolicy.CopyOnWrite, 2);
            return new GraphOptions(buffer, buffer);
        }
        private static GraphNodeHandle[] BuildStructuralSource(Owner source, int count, StructuralScenario scenario)
        {
            var nodes = new GraphNodeHandle[count]; for (int i = 0; i < count; i++) nodes[i] = source.AddNode(i);
            if (scenario == StructuralScenario.RemoveHub) for (int i = 1; i < count; i++) source.AddEdge(nodes[0], nodes[i], i);
            else for (int i = 0; i < count; i++) source.AddEdge(nodes[i], nodes[(i + 1) % count], i);
            return nodes;
        }
        private static void ChangeStructure(Owner source, GraphNodeHandle[] nodes, StructuralScenario scenario)
        {
            int count = nodes.Length;
            if (scenario == StructuralScenario.RemoveHub) source.RemoveNode(nodes[0]);
            else if (scenario == StructuralScenario.RemoveInterior) source.RemoveNode(nodes[count / 2]);
            else if (scenario == StructuralScenario.ReuseSlots)
            {
                const int changed = 64; for (int i = 0; i < changed; i++) source.RemoveNode(nodes[i * 4]);
                var added = new GraphNodeHandle[changed];
                for (int i = 0; i < changed; i++)
                { added[i] = source.AddNode(9000 + i); Assert.AreEqual(nodes[i * 4].Index, added[i].Index); Assert.AreEqual(2u, added[i].Generation); }
                for (int i = 0; i < changed; i++) source.AddEdge(added[i], added[(i + 1) % changed], 9000 + i);
            }
            else { var added = source.AddNode(9000); source.AddEdge(nodes[count - 1], added, 9000); }
            Assert.AreEqual(scenario == StructuralScenario.GrowCapacity ? count + 1 : scenario == StructuralScenario.ReuseSlots ? count : count - 1, source.NodeCount);
            Assert.AreEqual(scenario == StructuralScenario.RemoveHub ? 0 : scenario == StructuralScenario.RemoveInterior ? count - 2 : scenario == StructuralScenario.ReuseSlots ? count - 64 : count + 1, source.EdgeCount);
        }
        private static IEnumerable<TestCaseData> StructuralCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (StructuralScenario scenario in Enum.GetValues(typeof(StructuralScenario)))
            foreach (WireOperation operation in new[] { WireOperation.DeltaFacade, WireOperation.ApplyDelta, WireOperation.ReverseDelta })
                yield return new TestCaseData(backend, count, scenario, operation).SetName($"G13_Structural_{backend}_{count}_{scenario}_{operation}");
        }
        [TestCaseSource(nameof(StructuralCases)), Performance]
        public void StructuralDeltaAndCapacityChange(Backend backend, int count, StructuralScenario scenario, WireOperation operation)
        {
            var options = StructuralOptions(count, scenario); using var source = new Owner(backend, options);
            var nodes = BuildStructuralSource(source, count, scenario); var before = source.Canonical();
            ChangeStructure(source, nodes, scenario); var after = source.Canonical();
            using var basis = new NativeArray<byte>(before, Allocator.Persistent);
            using var target = new NativeArray<byte>(after, Allocator.Persistent);
            GraphDeltaWire<int, int, Int32Policy, Int32Policy>.TryMeasureScratch(options, before.Length, after.Length, out int bytes).ThrowIfFailure();
            bytes = checked(bytes + Math.Max(before.Length, after.Length) + 64);
            using var scratch = new NativeArray<byte>(bytes, Allocator.Persistent);
            using var packet = new NativeArray<byte>(before.Length + after.Length + 512, Allocator.Persistent);
            using var result = new NativeArray<long>(7, Allocator.Persistent);
            WireAction(source, WireOperation.DeltaFacade, GraphDeltaMode.Reversible, basis, target, packet, 0, scratch, result, 1)();
            CheckResult(backend, result); int length = (int)result[3]; var kind = (GraphTransferKind)result[6];
            var expectedPacket = packet.GetSubArray(0, length).ToArray();
            bool apply = operation != WireOperation.DeltaFacade, reverse = operation == WireOperation.ReverseDelta;
            using var destination = new Owner(backend, options); Import(destination, reverse ? target : basis, scratch);
            using var root = new Root(destination); long baseline = destination.Memory.LivePayloadBytes;
            var expected = reverse ? before : after; int iterations = apply ? 1 : 2;
            var action = WireAction(apply ? destination : source, operation, GraphDeltaMode.Reversible, basis, target, packet, length, scratch, result, iterations);
            Benchmark("G13", operation.ToString(), backend, scenario.ToString(), count, apply ? count : count * iterations, iterations, action, () =>
            {
                CheckResult(backend, result); Assert.LessOrEqual(result[4], bytes);
                if (apply)
                {
                    Assert.AreEqual(length, result[5]); CollectionAssert.AreEqual(expected, destination.Canonical());
                    Assert.AreEqual(reverse ? source.NodeCount : count, backend == Backend.Managed ? root.Managed.NodeCount : root.Native.NodeCount);
                    int index = scenario == StructuralScenario.RemoveHub || scenario == StructuralScenario.ReuseSlots ? 1 : 0;
                    Assert.AreEqual(index, root.GetNode(nodes[index]));
                }
                else { Assert.AreEqual(length, result[3]); Assert.AreEqual((long)kind, result[6]); CollectionAssert.AreEqual(expectedPacket, packet.GetSubArray(0, length).ToArray()); }
            }, () => { if (apply) root.Restore(); Reset(result); }, () =>
            {
                var m = (apply ? destination : source).Metrics(); m.externalScratchReservedBytes = bytes; m.scratchConsumedBytes = (int)result[4];
                m.auxiliaryBytes = basis.Length + target.Length + packet.Length; m.canonicalBytes = expected.Length;
                m.transferBytes = length; m.transferKind = kind.ToString(); m.transferDigest = Hex(CanonicalDigest(expectedPacket)); m.contentDigest = Hex(CanonicalDigest(expected));
                if (apply) { m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline; }
                return m;
            });
        }
        private static IEnumerable<TestCaseData> RejectionCases()
        {
            foreach (Backend backend in new[] { Backend.Managed, Backend.BurstJob })
            foreach (int count in new[] { 256, 2048 })
            foreach (RejectedTransfer failure in Enum.GetValues(typeof(RejectedTransfer)))
                yield return new TestCaseData(backend, count, failure).SetName($"G14_Rejected_{backend}_{count}_{failure}");
        }
        [TestCaseSource(nameof(RejectionCases)), Performance]
        public void RejectTransferWithoutPublishing(Backend backend, int count, RejectedTransfer failure)
        {
            var options = StructuralOptions(count, StructuralScenario.RemoveInterior); using var source = new Owner(backend, options);
            var nodes = BuildStructuralSource(source, count, StructuralScenario.RemoveInterior); var before = source.Canonical();
            ChangeStructure(source, nodes, StructuralScenario.RemoveInterior); var after = source.Canonical();
            using var basis = new NativeArray<byte>(before, Allocator.Persistent);
            using var target = new NativeArray<byte>(after, Allocator.Persistent);
            GraphDeltaWire<int, int, Int32Policy, Int32Policy>.TryMeasureScratch(options, before.Length, after.Length, out int bytes).ThrowIfFailure();
            bytes = checked(bytes + Math.Max(before.Length, after.Length) + 64);
            using var fullScratch = new NativeArray<byte>(bytes, Allocator.Persistent);
            using var packet = new NativeArray<byte>(before.Length + after.Length + 512, Allocator.Persistent);
            using var result = new NativeArray<long>(7, Allocator.Persistent);
            WireAction(source, WireOperation.DeltaFacade, GraphDeltaMode.Reversible, basis, target, packet, 0, fullScratch, result, 1)();
            CheckResult(backend, result); int length = (int)result[3]; var kind = (GraphTransferKind)result[6]; var writable = packet;
            // The fixed header places source and target XXH64 hashes at byte offsets 8 and 16.
            if (failure == RejectedTransfer.WrongBaseHash) writable[8] ^= 1;
            if (failure == RejectedTransfer.CorruptTargetHash) writable[16] ^= 1;
            if (failure == RejectedTransfer.TruncatedTail) length--;
            using var destination = new Owner(backend, options); Import(destination, basis, fullScratch);
            using var root = new Root(destination); long baseline = destination.Memory.LivePayloadBytes;
            var scratch = failure == RejectedTransfer.ScratchShort ? fullScratch.GetSubArray(0, before.Length - 1) : fullScratch;
            var expected = failure == RejectedTransfer.WrongBaseHash ? DeterministicStatus.ProtocolConflict
                : failure == RejectedTransfer.CorruptTargetHash ? DeterministicStatus.InvalidEncoding : DeterministicStatus.BufferTooSmall;
            var action = WireAction(destination, WireOperation.ApplyDelta, GraphDeltaMode.Reversible, basis, target, packet, length, scratch, result, 1);
            Benchmark("G14", "RejectedApply", backend, failure.ToString(), count, count, 1, action, () =>
            {
                CheckResult(backend, result, expected); Assert.AreEqual(0, result[4]); Assert.AreEqual(0, result[5]);
                CollectionAssert.AreEqual(before, destination.Canonical()); Assert.AreEqual(0, destination.Memory.LivePayloadBytes - baseline);
                Assert.AreEqual(0, root.GetNode(nodes[0]));
            }, () => Reset(result), () =>
            {
                var m = destination.Metrics(); m.externalScratchReservedBytes = scratch.Length; m.scratchConsumedBytes = (int)result[4];
                m.auxiliaryBytes = basis.Length + target.Length + packet.Length + fullScratch.Length - scratch.Length;
                m.canonicalBytes = before.Length; m.transferBytes = length; m.transferKind = kind.ToString();
                m.transferDigest = Hex(CanonicalDigest(packet.GetSubArray(0, length).ToArray())); m.observedStatus = (int)expected;
                m.baselineLivePayloadBytes = baseline; m.payloadGrowthBytes = m.livePayloadBytes - baseline; m.contentDigest = Hex(CanonicalDigest(before)); return m;
            });
        }
    }
}
