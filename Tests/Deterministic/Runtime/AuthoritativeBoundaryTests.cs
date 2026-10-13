using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class AuthoritativeBoundaryTests
    {
        // Intentionally retain the default incoming-boundary validator: the registry
        // must enforce the authoritative getter even when a binding only implements it.
        private class ValueBinding : DeterministicStateBinding
        {
            internal long Value;
            internal long FailCompletionValue = -1;
            internal long CorruptReadValue = -1, CorruptReadTo;
            internal long CorruptCompletionTick = -1, CorruptCompletionTo;
            internal ValueBinding CompletionTarget;
            internal readonly List<long> Completions = new List<long>();

            internal ValueBinding(uint id, long value) : base(id, 0xCA000000UL + id, 8, 0) { Value = value; }
            public override DeterministicBackend Backend => DeterministicBackend.Managed;
            public override DeterministicStatus TryWrite(ref DeterministicWriter writer) => writer.TryUInt((ulong)Value, 8);
            public override DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch)
            {
                var trial = reader;
                var status = trial.TryUInt(8, out ulong value);
                if (!status.IsSuccess()) return status;
                if (value > long.MaxValue) return DeterministicStatus.InvalidEncoding;
                reader = trial;
                return DeterministicStatus.None;
            }
            public override DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch)
            {
                var status = reader.TryUInt(8, out ulong value);
                if (!status.IsSuccess()) return status;
                Value = (long)value;
                if (Value == CorruptReadValue) Value = CorruptReadTo;
                return DeterministicStatus.None;
            }
            public override DeterministicStatus TryCompleteRestore(object historyOwner, long tick, ulong localLease)
            {
                Completions.Add(tick);
                if (Value == FailCompletionValue) return DeterministicStatus.InvalidConfiguration;
                if (tick == CorruptCompletionTick) (CompletionTarget ?? this).Value = CorruptCompletionTo;
                return DeterministicStatus.None;
            }
        }

        private sealed class ClockBinding : ValueBinding
        {
            internal ClockBinding(uint id, long value) : base(id, value) { }
            public override bool TryGetAuthoritativeBoundary(out long boundary) { boundary = Value; return true; }
        }

        private static DeterministicStateRegistry Registry(params ValueBinding[] bindings)
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var profile = new DeterministicCompatibilityProfile(1, 1, 1, 1, 1);
            var registry = new DeterministicStateRegistry(context, profile, bindings.Length);
            foreach (var binding in bindings) registry.TryRegister(binding).ThrowIfFailure();
            registry.TryFreeze().ThrowIfFailure();
            return registry;
        }

        private static byte[] Capture(DeterministicStateRegistry registry, long tick)
        {
            registry.TryEncode(tick, out byte* bytes, out int length).ThrowIfFailure();
            return new ReadOnlySpan<byte>(bytes, length).ToArray();
        }

        private static void WriteUInt64(byte[] packet, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) packet[offset + i] = (byte)(value >> (8 * i));
        }

        // Wire mutation is independent of the binding's reader/writer, and retains a
        // valid checksum so tests exercise publication, rather than checksum rejection.
        private static byte[] Incoming(byte[] packet, long boundary, params long[] values)
        {
            var result = (byte[])packet.Clone();
            WriteUInt64(result, 14, (ulong)boundary);
            for (int i = 0; i < values.Length; i++) WriteUInt64(result, 26 + i * 24 + 16, (ulong)values[i]);
            fixed (byte* bytes = result)
                WriteUInt64(result, result.Length - 8, DeterministicHash.Xxh64(bytes, result.Length - 8));
            return result;
        }

        private static DeterministicStatus Read(DeterministicStateRegistry registry, byte[] packet,
            out long tick, out int cursor, int prefix = 0)
        {
            var framed = new byte[packet.Length + prefix];
            Array.Copy(packet, 0, framed, prefix, packet.Length);
            fixed (byte* bytes = framed)
            {
                var reader = new DeterministicReader(bytes, framed.Length, prefix);
                var status = registry.TryReadCanonical(ref reader, out tick);
                cursor = reader.Position;
                return status;
            }
        }

        [Test]
        public void SingleProviderDisagreeingWithImportedHeaderRestoresThePreviousWorld()
        {
            var clock = new ClockBinding(1, 7);
            using var registry = Registry(clock);
            var original = Capture(registry, 7);
            var incoming = Incoming(original, 13, 14);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, Read(registry, incoming, out long tick, out int cursor, 3));
            Assert.AreEqual(0, tick);
            Assert.AreEqual(3, cursor);
            Assert.AreEqual(7, clock.Value);
            CollectionAssert.AreEqual(new long[] { 13, 7 }, clock.Completions);
            Assert.IsFalse(registry.IsFaulted);
            CollectionAssert.AreEqual(original, Capture(registry, 7));
        }

        [TestCase(13, 14)]
        [TestCase(14, 14)]
        public void MultipleProvidersMustAgreeWithEachOtherAndTheImportedHeader(long firstValue, long secondValue)
        {
            var first = new ClockBinding(1, 7);
            var second = new ClockBinding(2, 7);
            using var registry = Registry(second, first);
            var original = Capture(registry, 7);
            var incoming = Incoming(original, 13, firstValue, secondValue);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, Read(registry, incoming, out long tick, out int cursor));
            Assert.AreEqual(0, tick);
            Assert.AreEqual(0, cursor);
            Assert.AreEqual(7, first.Value);
            Assert.AreEqual(7, second.Value);
            Assert.IsFalse(registry.IsFaulted);
            CollectionAssert.AreEqual(original, Capture(registry, 7));
        }

        [TestCase(14)]
        [TestCase(-1)]
        public void SuccessfulCompletionCannotPublishAnIncorrectAuthoritativeClock(long completionValue)
        {
            var clock = new ClockBinding(1, 7) { CorruptCompletionTick = 13, CorruptCompletionTo = completionValue };
            using var registry = Registry(clock);
            var original = Capture(registry, 7);
            Assert.IsFalse(Read(registry, Incoming(original, 13, 13), out long tick, out int cursor).IsSuccess());
            Assert.AreEqual(0, tick);
            Assert.AreEqual(0, cursor);
            Assert.AreEqual(7, clock.Value);
            CollectionAssert.AreEqual(new long[] { 13, 7 }, clock.Completions);
            Assert.IsFalse(registry.IsFaulted);
            CollectionAssert.AreEqual(original, Capture(registry, 7));
        }

        [Test]
        public void FinalCoherenceCheckIncludesClockChangedByALaterCompletionCallback()
        {
            var clock = new ClockBinding(1, 7);
            var later = new ValueBinding(2, 42)
            { CorruptCompletionTick = 13, CorruptCompletionTo = 14, CompletionTarget = clock };
            using var registry = Registry(later, clock);
            var original = Capture(registry, 7);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict,
                Read(registry, Incoming(original, 13, 13, 42), out _, out int cursor));
            Assert.AreEqual(0, cursor);
            Assert.AreEqual(7, clock.Value);
            Assert.AreEqual(42, later.Value);
            CollectionAssert.AreEqual(new long[] { 13, 7 }, later.Completions);
            Assert.IsFalse(registry.IsFaulted);
            CollectionAssert.AreEqual(original, Capture(registry, 7));
        }

        [TestCase(false, 8)]
        [TestCase(false, -1)]
        [TestCase(true, 8)]
        [TestCase(true, -1)]
        public void IncorrectRecoveryClockFaultsTheRegistryWhetherReadOrCompletionReportsSuccess(bool duringRead, long wrongBoundary)
        {
            var clock = new ClockBinding(1, 7) { FailCompletionValue = 13 };
            using var registry = Registry(clock);
            var original = Capture(registry, 7);
            if (duringRead) { clock.CorruptReadValue = 7; clock.CorruptReadTo = wrongBoundary; }
            else { clock.CorruptCompletionTick = 7; clock.CorruptCompletionTo = wrongBoundary; }
            var incoming = Incoming(original, 13, 13);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, Read(registry, incoming, out long tick, out int cursor));
            Assert.AreEqual(0, tick);
            Assert.AreEqual(0, cursor);
            Assert.IsTrue(registry.IsFaulted);
            Assert.AreEqual(wrongBoundary, clock.Value);
            CollectionAssert.AreEqual(new long[] { 13, 7 }, clock.Completions);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, registry.TryEncode(7, out byte* bytes, out int length));
            Assert.IsTrue(bytes == null);
            Assert.AreEqual(0, length);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, Read(registry, original, out _, out cursor));
            Assert.AreEqual(0, cursor);
        }

        [Test]
        public void RecoveryProvidersDisagreeingWithEachOtherFaultTheRegistry()
        {
            var first = new ClockBinding(1, 7) { FailCompletionValue = 13, CorruptCompletionTick = 7, CorruptCompletionTo = 8 };
            var second = new ClockBinding(2, 7);
            using var registry = Registry(first, second);
            var original = Capture(registry, 7);
            Assert.AreEqual(DeterministicStatus.ProtocolConflict,
                Read(registry, Incoming(original, 13, 13, 13), out _, out int cursor));
            Assert.AreEqual(0, cursor);
            Assert.AreEqual(8, first.Value);
            Assert.AreEqual(7, second.Value);
            Assert.IsTrue(registry.IsFaulted);
        }

        [Test]
        public void ConsistentRecoveryUsesThePreviousNonzeroBoundaryAndPreservesTheOriginalFailure()
        {
            var clock = new ClockBinding(1, 7) { FailCompletionValue = 13 };
            using var registry = Registry(clock);
            var original = Capture(registry, 7);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration,
                Read(registry, Incoming(original, 13, 13), out long tick, out int cursor));
            Assert.AreEqual(0, tick);
            Assert.AreEqual(0, cursor);
            Assert.AreEqual(7, clock.Value);
            CollectionAssert.AreEqual(new long[] { 13, 7 }, clock.Completions);
            Assert.IsFalse(registry.IsFaulted);
            CollectionAssert.AreEqual(original, Capture(registry, 7));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CoherentImportedProvidersPublishTheRequestedBoundary(bool twoProviders)
        {
            var first = new ClockBinding(1, 7);
            var second = new ClockBinding(2, 7);
            using var registry = twoProviders ? Registry(first, second) : Registry(first);
            var original = Capture(registry, 7);
            var incoming = twoProviders ? Incoming(original, 13, 13, 13) : Incoming(original, 13, 13);
            Assert.AreEqual(DeterministicStatus.None, Read(registry, incoming, out long tick, out int cursor, 3));
            Assert.AreEqual(13, tick);
            Assert.AreEqual(incoming.Length + 3, cursor);
            Assert.AreEqual(13, first.Value);
            if (twoProviders) Assert.AreEqual(13, second.Value);
            CollectionAssert.AreEqual(new long[] { 13 }, first.Completions);
            Assert.IsFalse(registry.IsFaulted);
            CollectionAssert.AreEqual(incoming, Capture(registry, 13));
        }

        [Test]
        public void CurrentProviderDisagreementRejectsImportBeforePublication()
        {
            var first = new ClockBinding(1, 7);
            var second = new ClockBinding(2, 7);
            using var registry = Registry(first, second);
            var original = Capture(registry, 7);
            second.Value = 8;
            Assert.AreEqual(DeterministicStatus.ProtocolConflict, Read(registry, original, out _, out int cursor));
            Assert.AreEqual(0, cursor);
            Assert.AreEqual(7, first.Value);
            Assert.AreEqual(8, second.Value);
            Assert.IsEmpty(first.Completions);
            Assert.IsEmpty(second.Completions);
            Assert.IsFalse(registry.IsFaulted);
        }

        [Test]
        public void DefaultBindingWithoutProviderRetainsArbitraryCheckpointBoundaries()
        {
            var value = new ValueBinding(1, 14);
            using var registry = Registry(value);
            var incoming = Incoming(Capture(registry, 777), 333, 16);
            Assert.AreEqual(DeterministicStatus.None, Read(registry, incoming, out long tick, out int cursor));
            Assert.AreEqual(333, tick);
            Assert.AreEqual(incoming.Length, cursor);
            Assert.AreEqual(16, value.Value);
            Assert.AreEqual(DeterministicStatus.None, registry.TryEncode(999, out _, out _));
            Assert.IsFalse(registry.IsFaulted);
        }

        [Test]
        public void DefaultBindingRecoveryRetainsLegacyZeroBoundaryFallback()
        {
            var value = new ValueBinding(1, 14) { FailCompletionValue = 99 };
            using var registry = Registry(value);
            var original = Capture(registry, 777);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration,
                Read(registry, Incoming(original, 333, 99), out long tick, out int cursor));
            Assert.AreEqual(0, tick);
            Assert.AreEqual(0, cursor);
            Assert.AreEqual(14, value.Value);
            CollectionAssert.AreEqual(new long[] { 333, 0 }, value.Completions);
            Assert.IsFalse(registry.IsFaulted);
            CollectionAssert.AreEqual(original, Capture(registry, 777));
        }
    }
}
