using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class WorldStateTests
    {
        private static DeterministicRuntimeContext Context() => new DeterministicRuntimeContext(DeterministicBackend.Managed);
        private static DeterministicCompatibilityProfile Profile(ulong settings = 5) => new DeterministicCompatibilityProfile(1, 2, 3, 4, settings);
        private static DeterministicStateRegistry Registry(DeterministicCell<int> cell, DeterministicList<int> list, bool reverse = false, ulong settings = 5)
        {
            var registry = new DeterministicStateRegistry(Context(), Profile(settings), 2);
            var a = ManagedStateBindings.Cell(10, cell, default(Int32Policy), 0x1001, 128, 128);
            var b = ManagedStateBindings.List(20, list, default(Int32Policy), 0x2004, 512, 512);
            registry.TryRegister(reverse ? b : (DeterministicStateBinding)a).ThrowIfFailure();
            registry.TryRegister(reverse ? a : (DeterministicStateBinding)b).ThrowIfFailure(); registry.TryFreeze().ThrowIfFailure(); return registry;
        }
        private static byte[] Encode(DeterministicStateRegistry registry, long tick)
        {
            registry.TryEncode(tick, out byte* bytes, out int length).ThrowIfFailure(); var result = new byte[length];
            for (int i = 0; i < length; i++) result[i] = bytes[i]; return result;
        }
        [Test]
        public void RegistryOrderIsStableAndCheckpointRestoresEveryCollection()
        {
            using (var a = new DeterministicCell<int>(7)) using (var b = new DeterministicCell<int>(7))
            using (var x = new DeterministicList<int>(4)) using (var y = new DeterministicList<int>(4))
            {
                x.Add(3); x.Add(-9); y.Add(3); y.Add(-9);
                using (var left = Registry(a, x)) using (var right = Registry(b, y, true))
                {
                    var packet = Encode(left, 12); CollectionAssert.AreEqual(packet, Encode(right, 12));
                    a.Value = 88; x.Clear(); x.Add(99);
                    fixed (byte* bytes = packet) { var reader = new DeterministicReader(bytes, packet.Length); left.TryReadCanonical(ref reader, out long tick).ThrowIfFailure(); Assert.AreEqual(12, tick); Assert.AreEqual(packet.Length, reader.Position); }
                    Assert.AreEqual(7, a.Value); CollectionAssert.AreEqual(new[] { 3, -9 }, x);
                    Assert.AreEqual(DeterministicStatus.Invalidated, left.TryRegister(ManagedStateBindings.Cell(30, a, default(Int32Policy), 1, 128, 128)));
                }
            }
        }
        [Test]
        public void BadSecondStateAndDifferentProfilesLeaveEntireWorldAndCursorUnchanged()
        {
            using (var cell = new DeterministicCell<int>(1)) using (var list = new DeterministicList<int>(4))
            using (var registry = Registry(cell, list)) using (var incompatible = Registry(cell, list, settings: 6))
            {
                list.Add(4); var packet = Encode(registry, 2); cell.Value = 55; list[0] = 66;
                packet[packet.Length - 1] ^= 1;
                fixed (byte* bytes = packet)
                {
                    var reader = new DeterministicReader(bytes, packet.Length);
                    Assert.AreEqual(DeterministicStatus.InvalidEncoding, registry.TryReadCanonical(ref reader, out _)); Assert.AreEqual(0, reader.Position);
                    reader = new DeterministicReader(bytes, packet.Length);
                    Assert.AreEqual(DeterministicStatus.ProfileMismatch, incompatible.TryReadCanonical(ref reader, out _));
                }
                Assert.AreEqual(55, cell.Value); Assert.AreEqual(66, list[0]);
            }
        }
        private sealed class FailureGate { internal bool Fail; }
        private struct FailOnceAdapter : ICanonicalStateAdapter
        {
            internal ManagedCellStateAdapter<int, Int32Policy> Cell; internal FailureGate Gate;
            public DeterministicBackend Backend => DeterministicBackend.Managed;
            public DeterministicStatus TryWrite(ref DeterministicWriter writer) => Cell.TryWrite(ref writer);
            public DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch) => Cell.TryValidate(ref reader, ref scratch);
            public DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch)
            { if (Gate.Fail) { Gate.Fail = false; return DeterministicStatus.SnapshotCapacityExceeded; } return Cell.TryRead(ref reader, ref scratch); }
        }
        [Test]
        public void FailedPublicationRestoresEarlierComponents()
        {
            using (var a = new DeterministicCell<int>(1)) using (var b = new DeterministicCell<int>(2))
            using (var registry = new DeterministicStateRegistry(Context(), Profile(), 2))
            {
                var gate = new FailureGate(); registry.TryRegister(ManagedStateBindings.Cell(1, a, default(Int32Policy), 1, 128, 128)).ThrowIfFailure();
                registry.TryRegister(new DeterministicStateBinding<FailOnceAdapter>(2, 2, 128, 128,
                    new FailOnceAdapter { Cell = new ManagedCellStateAdapter<int, Int32Policy>(b, default), Gate = gate })).ThrowIfFailure(); registry.TryFreeze().ThrowIfFailure();
                var packet = Encode(registry, 3); a.Value = 11; b.Value = 22; gate.Fail = true;
                fixed (byte* bytes = packet) { var reader = new DeterministicReader(bytes, packet.Length); Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, registry.TryReadCanonical(ref reader, out _)); Assert.AreEqual(0, reader.Position); }
                Assert.AreEqual(11, a.Value); Assert.AreEqual(22, b.Value); Assert.IsFalse(registry.IsFaulted);
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void HistoryRestoresRetainedTicksAfterEvictionAndBranchesAfterRollback(SnapshotPolicy policy)
        {
            using (var cell = new DeterministicCell<int>()) using (var list = new DeterministicList<int>(4))
            using (var registry = Registry(cell, list)) using (var history = new DeterministicWorldHistory(Context(), registry.MaxCanonicalBytes, 9, policy, 4, 64))
            {
                for (int tick = 0; tick < 40; tick++) { cell.Value = tick * 37; list.Clear(); for (int j = 0; j < tick % 5; j++) list.Add(tick + j); history.TryCapture(registry, tick).ThrowIfFailure(); }
                Assert.AreEqual(31, history.OldestTick); Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TryRestore(registry, 30));
                for (int tick = 31; tick < 40; tick++) { history.TryRestore(registry, tick).ThrowIfFailure(); Assert.AreEqual(tick * 37, cell.Value); Assert.AreEqual(tick % 5, list.Count); }
                history.TryRestore(registry, 34).ThrowIfFailure(); history.TryTruncate(34).ThrowIfFailure();
                Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TryRestore(registry, 35));
                for (int tick = 35; tick < 60; tick++) { cell.Value = -tick; history.TryCapture(registry, tick).ThrowIfFailure(); }
                for (int tick = 51; tick < 60; tick++) { history.TryRestore(registry, tick).ThrowIfFailure(); Assert.AreEqual(-tick, cell.Value); }
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void VariableLengthHistoryMatchesIndependentByteOracleAndRejectsMissingTicks(SnapshotPolicy policy)
        {
            using (var history = new DeterministicWorldHistory(Context(), 512, 11, policy, 5, 64))
            {
                var random = new Random(8761); var oracle = new byte[150][];
                for (int tick = 0; tick < oracle.Length; tick++)
                {
                    var bytes = new byte[random.Next(513)]; random.NextBytes(bytes); oracle[tick] = bytes;
                    fixed (byte* pointer = bytes) history.TryCapture(tick, pointer, bytes.Length).ThrowIfFailure();
                    for (int previous = Math.Max(0, tick - 10); previous <= tick; previous++)
                    {
                        history.TryRestore(previous, out byte* restored, out int length).ThrowIfFailure(); Assert.AreEqual(oracle[previous].Length, length);
                        for (int i = 0; i < length; i++) Assert.AreEqual(oracle[previous][i], restored[i]);
                    }
                }
                Assert.AreEqual(DeterministicStatus.OutOfRange, history.TryCapture(151, null, 0));
                Assert.AreEqual(DeterministicStatus.HistoryExhausted, history.TryRestore(138, out _, out _));
            }
        }
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)] [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void UnchangedPagesAndDeltasShareStorageWithoutSteadyGc(SnapshotPolicy policy)
        {
            using (var history = new DeterministicWorldHistory(Context(), 1024, 8, policy, 4, 64))
            {
                var bytes = new byte[1024]; fixed (byte* pointer = bytes)
                {
                    history.TryCapture(0, pointer, bytes.Length).ThrowIfFailure(); history.TryCapture(1, pointer, bytes.Length).ThrowIfFailure();
                    history.TryGetStoredBytes(1, out int stored).ThrowIfFailure(); Assert.AreEqual(0, stored);
                    if (policy == SnapshotPolicy.CopyOnWrite) Assert.AreEqual(16, history.LivePages);
                    for (int tick = 2; tick < 16; tick++) { bytes[tick]++; history.TryCapture(tick, pointer, bytes.Length).ThrowIfFailure(); history.TryRestore(tick, out _, out _).ThrowIfFailure(); }
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int tick = 16; tick < 200; tick++) { bytes[tick % bytes.Length]++; history.TryCapture(tick, pointer, bytes.Length); history.TryRestore(tick - 4, out _, out _); }
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0, allocated);
                }
            }
        }
        [Test]
        public void BackendIsExplicitAndCannotFallback()
        {
            Assert.Throws<InvalidOperationException>(() => new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler));
            Assert.Throws<InvalidOperationException>(() => new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, ManagedDeterministicMemoryFactory.Instance));
        }
    }
}
