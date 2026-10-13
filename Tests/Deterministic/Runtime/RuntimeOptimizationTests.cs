using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicOptimization")]
    public sealed unsafe class RuntimeOptimizationTests
    {
        private static DeterministicRuntimeContext Context() => new DeterministicRuntimeContext(DeterministicBackend.Managed);
        [Test]
        public void ByteCopiesMatchMemmoveForEveryAlignmentLengthAndOverlapDirection()
        {
            using (var memory = Context().Memory.Allocate(160))
            {
                foreach (int length in new[] { 0, 1, 7, 8, 15, 16, 31, 64, 129 })
                for (int from = 0; from < 8; from++)
                for (int to = 0; to < 8; to++)
                {
                    var expected = new byte[160];
                    for (int i = 0; i < 160; i++) memory.Pointer[i] = expected[i] = unchecked((byte)(i * 131 + 17));
                    Array.Copy(expected, from, expected, to, length);
                    RuntimeBytes.Copy(memory.Pointer + from, memory.Pointer + to, length);
                    for (int i = 0; i < 160; i++) Assert.AreEqual(expected[i], memory.Pointer[i], "offset " + i);
                    Assert.IsTrue(RuntimeBytes.Equal(memory.Pointer + to, memory.Pointer + to, length));
                }
                RuntimeBytes.Copy(null, null, 0); RuntimeBytes.Clear(null, 0); Assert.IsTrue(RuntimeBytes.Equal(null, null, 0));
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(7)] [TestCase(8)] [TestCase(64)] [TestCase(4097)]
        public void ClearAndEqualityRespectUnalignedBoundsAndTailMismatches(int length)
        {
            using (var left = Context().Memory.Allocate(length + 16))
            using (var right = Context().Memory.Allocate(length + 16))
            {
                for (int i = 0; i < length + 16; i++) left.Pointer[i] = right.Pointer[i] = 0x5a;
                RuntimeBytes.Clear(left.Pointer + 3, length); RuntimeBytes.Clear(right.Pointer + 5, length);
                Assert.IsTrue(RuntimeBytes.Equal(left.Pointer + 3, right.Pointer + 5, length));
                for (int i = 0; i < length + 16; i++) Assert.AreEqual(i >= 3 && i < length + 3 ? 0 : 0x5a, left.Pointer[i]);
                if (length != 0)
                    foreach (int index in new[] { 0, length / 2, length - 1 })
                    {
                        right.Pointer[index + 5] = 1; Assert.IsFalse(RuntimeBytes.Equal(left.Pointer + 3, right.Pointer + 5, length));
                        right.Pointer[index + 5] = 0;
                    }
            }
        }

        public enum FailureMode { MeasureFailure, ActualFailure, OversizedMeasure, ShortActual, ActualOverflow }
        private sealed class Gate { internal FailureMode? Failure; internal int MeasuringCalls, ActualCalls; }
        private struct ControlledAdapter : ICanonicalStateAdapter
        {
            internal Gate Gate;
            public DeterministicBackend Backend => DeterministicBackend.Managed;
            public DeterministicStatus TryWrite(ref DeterministicWriter writer)
            {
                if (writer.IsMeasuring) Gate.MeasuringCalls++; else Gate.ActualCalls++;
                if (writer.IsMeasuring && Gate.Failure == FailureMode.MeasureFailure) return DeterministicStatus.UnsupportedType;
                if (!writer.IsMeasuring && Gate.Failure == FailureMode.ActualFailure) return DeterministicStatus.InvalidEncoding;
                int count = writer.IsMeasuring ? Gate.Failure == FailureMode.OversizedMeasure ? 9 : 3 :
                    Gate.Failure == FailureMode.ShortActual ? 2 : Gate.Failure == FailureMode.ActualOverflow ? 64 : 3;
                for (int i = 0; i < count; i++)
                { var status = writer.TryUInt((byte)(0x51 + i), 1); if (!status.IsSuccess()) return status; }
                return DeterministicStatus.None;
            }
            public DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch)
            { if (!reader.Has(3)) return DeterministicStatus.BufferTooSmall; reader.Position += 3; return DeterministicStatus.None; }
            public DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch) => TryValidate(ref reader, ref scratch);
        }

        [TestCase(FailureMode.MeasureFailure, DeterministicStatus.UnsupportedType)]
        [TestCase(FailureMode.ActualFailure, DeterministicStatus.InvalidEncoding)]
        [TestCase(FailureMode.OversizedMeasure, DeterministicStatus.CapacityExceeded)]
        [TestCase(FailureMode.ShortActual, DeterministicStatus.ProtocolConflict)]
        [TestCase(FailureMode.ActualOverflow, DeterministicStatus.BufferTooSmall)]
        public void FailedOwnedEncodingNeverPublishesBytesOrChangesCallerDestination(FailureMode mode, DeterministicStatus expected)
        {
            using (var registry = new DeterministicStateRegistry(Context(), new DeterministicCompatibilityProfile(1, 2, 3, 4, 5), 2))
            {
                var first = new Gate(); var second = new Gate { Failure = mode };
                registry.TryRegister(new DeterministicStateBinding<ControlledAdapter>(2, 22, 8, 0, new ControlledAdapter { Gate = second })).ThrowIfFailure();
                registry.TryRegister(new DeterministicStateBinding<ControlledAdapter>(1, 11, 8, 0, new ControlledAdapter { Gate = first })).ThrowIfFailure();
                registry.TryFreeze().ThrowIfFailure();
                Assert.AreEqual(expected, registry.TryEncode(7, out byte* packet, out int length));
                Assert.IsTrue(packet == null); Assert.AreEqual(0, length); Assert.IsFalse(registry.IsFaulted);
                byte* destination = stackalloc byte[256]; for (int i = 0; i < 256; i++) destination[i] = 0xcc;
                var writer = new DeterministicWriter(destination, 256, 7);
                Assert.AreEqual(expected, registry.TryWriteCanonical(ref writer, 7)); Assert.AreEqual(7, writer.Position);
                for (int i = 0; i < 256; i++) Assert.AreEqual(0xcc, destination[i]);
                second.Failure = null; first.MeasuringCalls = first.ActualCalls = second.MeasuringCalls = second.ActualCalls = 0;
                registry.TryEncode(7, out packet, out length).ThrowIfFailure();
                Assert.AreEqual(1, first.MeasuringCalls); Assert.AreEqual(1, first.ActualCalls);
                Assert.AreEqual(1, second.MeasuringCalls); Assert.AreEqual(1, second.ActualCalls);
                var oracle = new DeterministicWriter(destination, 256);
                oracle.TryUInt(0x3157444c, 4); oracle.TryUInt(1, 2); oracle.TryUInt(registry.Fingerprint, 8); oracle.TryUInt(7, 8); oracle.TryUInt(2, 4);
                foreach (uint id in new uint[] { 1, 2 })
                {
                    oracle.TryUInt(id, 4); oracle.TryUInt(id == 1 ? 11UL : 22UL, 8); oracle.TryUInt(3, 4);
                    for (int i = 0; i < 3; i++) oracle.TryUInt((byte)(0x51 + i), 1);
                }
                oracle.TryUInt(DeterministicHash.Xxh64(destination, oracle.Position), 8); Assert.AreEqual(oracle.Position, length);
                Assert.IsTrue(RuntimeBytes.Equal(packet, destination, length));
                var reader = new DeterministicReader(packet, length); registry.TryValidateCanonical(ref reader, out long tick).ThrowIfFailure();
                Assert.AreEqual(7, tick); Assert.AreEqual(length, reader.Position);
            }
        }

        [Test]
        public void CowReuseHandlesLengthChangesEvictionAndBranchingWithoutChangingRetainedBytes()
        {
            using (var history = new DeterministicWorldHistory(Context(), 129, 3, SnapshotPolicy.CopyOnWrite, 1, 64))
            {
                byte* packet = stackalloc byte[129]; for (int i = 0; i < 129; i++) packet[i] = (byte)i;
                history.TryCapture(0, packet, 129).ThrowIfFailure();
                history.TryCapture(1, packet, 129).ThrowIfFailure(); history.TryGetStoredBytes(1, out int stored).ThrowIfFailure();
                Assert.AreEqual(0, stored); Assert.AreEqual(3, history.LivePages);
                history.TryCapture(2, packet, 65).ThrowIfFailure(); history.TryGetStoredBytes(2, out stored).ThrowIfFailure();
                Assert.AreEqual(64, stored);
                history.TryCapture(3, packet, 129).ThrowIfFailure(); history.TryGetStoredBytes(3, out stored).ThrowIfFailure();
                Assert.AreEqual(128, stored);
                for (int tick = 1; tick <= 3; tick++) Verify(history, tick, tick == 2 ? 65 : 129);
                history.TryTruncate(2).ThrowIfFailure(); packet[128] = 0xff;
                history.TryCapture(3, packet, 129).ThrowIfFailure(); Verify(history, 1, 129); Verify(history, 2, 65);
                history.TryRestore(3, out byte* branch, out int length).ThrowIfFailure();
                Assert.AreEqual(129, length); Assert.AreEqual(0xff, branch[128]);
            }
        }
        private static void Verify(DeterministicWorldHistory history, long tick, int expectedLength)
        {
            history.TryRestore(tick, out byte* bytes, out int length).ThrowIfFailure(); Assert.AreEqual(expectedLength, length);
            for (int i = 0; i < length; i++) Assert.AreEqual((byte)i, bytes[i]);
        }
    }
}

