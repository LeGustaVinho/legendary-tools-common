using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicExtendedOptimization")]
    public sealed unsafe class ExtendedRuntimeOptimizationTests
    {
        private sealed class Sink : IDeterministicEventSink<int>
        {
            internal readonly List<DeterministicEvent<int>> Events = new List<DeterministicEvent<int>>();
            public void OnEvent(DeterministicEventDelivery delivery, in DeterministicEvent<int> value)
            { if (delivery == DeterministicEventDelivery.Speculated) Events.Add(value); }
        }
        [TestCase(0)] [TestCase(1)] [TestCase(31)] [TestCase(32)] [TestCase(33)] [TestCase(97)] [TestCase(1024)]
        public void EventSortMatchesTotalIdOrderAcrossAlgorithmBoundaries(int count)
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed); var sink = new Sink();
            using var ledger = new DeterministicEventLedger<int>(context, 2, count + 1, sink);
            var expected = new DeterministicEvent<int>[count];
            for (int i = 0; i < count; i++) expected[i] = new DeterministicEvent<int>(
                new DeterministicEventId(0, (uint)(i % 7 + 1), new StableHandle((uint)(i / 7), (uint)(i % 3 + 1)), i % 2 == 0 ? ulong.MaxValue - (ulong)i : (ulong)i), i);
            ledger.TryBegin(0, out var writer).ThrowIfFailure();
            // Coprime traversal gives a reproducible permutation, including the 32/33 switch.
            var order = new int[count]; for (int i = 0; i < count; i++) order[i] = i;
            uint random = 0xabc123;
            for (int i = count - 1; i > 0; i--) { random = unchecked(random * 1664525 + 1013904223); int j = (int)(random % (uint)(i + 1)); int t = order[i]; order[i] = order[j]; order[j] = t; }
            foreach (int i in order) writer.TryEmit(expected[i].Id.SystemId, expected[i].Id.Entity, expected[i].Id.Sequence, expected[i].Payload).ThrowIfFailure();
            Array.Sort(expected, (a, b) => a.Id.CompareTo(b.Id)); ledger.TryPreparePublish(0).ThrowIfFailure(); ledger.TryPublish(0).ThrowIfFailure();
            Assert.AreEqual(count, sink.Events.Count);
            for (int i = 0; i < count; i++) { Assert.AreEqual(expected[i].Id, sink.Events[i].Id); Assert.AreEqual(expected[i].Payload, sink.Events[i].Payload); }
        }
        [TestCase(32)] [TestCase(33)] [TestCase(1024)]
        public void DuplicateEventIdsAreRejectedWithoutPublicationAndCanBeAbandoned(int count)
        {
            var sink = new Sink(); using var ledger = new DeterministicEventLedger<int>(new DeterministicRuntimeContext(DeterministicBackend.Managed), 2, count + 1, sink);
            ledger.TryBegin(0, out var writer).ThrowIfFailure();
            for (int i = count - 1; i >= 0; i--) writer.TryEmit(1, default, (ulong)i, i).ThrowIfFailure();
            writer.TryEmit(1, default, 0, -1).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.ProtocolConflict, ledger.TryPreparePublish(0));
            Assert.AreEqual(DeterministicStatus.Invalidated, ledger.TryPublish(0)); Assert.AreEqual(-1, ledger.NewestTick); Assert.AreEqual(0, sink.Events.Count);
            ledger.TryAbandon(0).ThrowIfFailure(); ledger.TryBegin(0, out writer).ThrowIfFailure(); ledger.TryPreparePublish(0).ThrowIfFailure(); ledger.TryPublish(0).ThrowIfFailure();
        }
        private sealed class Factory : IDeterministicMemoryFactory
        {
            public DeterministicBackend Backend => DeterministicBackend.Managed;
            internal IDeterministicMemory Last;
            public IDeterministicMemory Allocate(int bytes) { Last = ManagedDeterministicMemoryFactory.Instance.Allocate(bytes); return Last; }
        }
        private sealed class World : IDisposable
        {
            internal readonly Factory Factory = new Factory();
            internal readonly DeterministicRuntimeContext Context;
            internal readonly DeterministicCell<int> Cell = new DeterministicCell<int>();
            internal readonly DeterministicStateRegistry Registry;
            internal World()
            {
                Context = new DeterministicRuntimeContext(DeterministicBackend.Managed, Factory);
                Registry = new DeterministicStateRegistry(Context, new DeterministicCompatibilityProfile(1, 2, 3, 4, 5), 1);
                Registry.TryRegister(ManagedStateBindings.Cell(1, Cell, default(Int32Policy), 1, 256, 2048)).ThrowIfFailure(); Registry.TryFreeze().ThrowIfFailure();
            }
            public void Dispose() { Registry.Dispose(); Cell.Dispose(); }
        }
        [TestCase(37L)]
        [TestCase(long.MaxValue - 263)]
        public void ReplayBinarySearchMatchesIrregularCheckpointOracleBeforeAndAfterOffsetImport(long initial)
        {
            using var world = new World(); using var archive = new DeterministicReplayArchive<int, Int32Policy>(world.Context, 123, 65536, 512, 2);
            using var loaded = new DeterministicReplayArchive<int, Int32Policy>(world.Context, 123, 65536, 512, 2);
            archive.TryBegin(world.Registry, initial).ThrowIfFailure(); var checkpoints = new List<long> { initial }; var indices = new List<int> { 0 };
            for (int i = 0; i < 263; i++)
            {
                if (i > 0 && (i % 17 == 0 || i % 29 == 0))
                { world.Cell.Value = i; indices.Add(archive.Count); checkpoints.Add(initial + i); archive.TryAppendCheckpoint(world.Registry, initial + i).ThrowIfFailure(); }
                var inputs = i % 5 == 0 ? Array.Empty<DeterministicPlayerInput<int>>() : new[] { new DeterministicPlayerInput<int>(new PlayerId(1), i), new DeterministicPlayerInput<int>(new PlayerId(9), -i) };
                archive.TryAppendInputs(initial + i, inputs, (ulong)i + 77).ThrowIfFailure();
            }
            byte* packet = stackalloc byte[65536]; var writer = new DeterministicWriter(packet, 65536, 7); archive.TryWriteCanonical(ref writer).ThrowIfFailure();
            var reader = new DeterministicReader(packet, writer.Position, 7); loaded.TryReadCanonical(ref reader, world.Registry).ThrowIfFailure(); Assert.AreEqual(writer.Position, reader.Position);
            foreach (var owner in new[] { archive, loaded })
            {
                for (int i = 0; i <= 263; i++)
                {
                    long tick = initial + i; int expected = checkpoints.Count - 1; while (checkpoints[expected] > tick) expected--;
                    owner.TryFindCheckpoint(tick, out int index, out long boundary).ThrowIfFailure(); Assert.AreEqual(checkpoints[expected], boundary); Assert.AreEqual(indices[expected], index);
                    var s = owner.TryReadInputs(tick, out var inputs, out ulong hash);
                    if (i == 263) Assert.AreEqual(DeterministicStatus.NotFound, s);
                    else
                    { s.ThrowIfFailure(); Assert.AreEqual((ulong)i + 77, hash); Assert.AreEqual(i % 5 == 0 ? 0 : 2, inputs.Length); if (inputs.Length != 0) { Assert.AreEqual(i, inputs[0].Value); Assert.AreEqual(-i, inputs[1].Value); } }
                }
                Assert.AreEqual(DeterministicStatus.NotFound, owner.TryReadInputs(initial - 1, out _, out _));
                Assert.AreEqual(DeterministicStatus.OutOfRange, owner.TryFindCheckpoint(initial - 1, out _, out _));
            }
        }
        [Test]
        public void ReplayImportSupportsAliasedPacketsAndRejectsLateCorruptionWithoutReplacingPriorData()
        {
            using var world = new World(); using var source = new DeterministicReplayArchive<int, Int32Policy>(world.Context, 123, 16384, 256, 1);
            source.TryBegin(world.Registry, 0).ThrowIfFailure();
            for (int i = 0; i < 100; i++) source.TryAppendInputs(i, new[] { new DeterministicPlayerInput<int>(new PlayerId(1), i) }, (ulong)i).ThrowIfFailure();
            byte* packet = stackalloc byte[16384]; var writer = new DeterministicWriter(packet, 16384); source.TryWriteCanonical(ref writer).ThrowIfFailure();
            using var target = new DeterministicReplayArchive<int, Int32Policy>(world.Context, 123, 16384, 256, 1); byte* ownerBytes = world.Factory.Last.Pointer;
            target.TryBegin(world.Registry, 999).ThrowIfFailure(); byte* before = stackalloc byte[1024]; var old = new DeterministicWriter(before, 1024); target.TryWriteCanonical(ref old).ThrowIfFailure();
            byte* borrowed = ownerBytes + 1024; for (int i = 0; i < writer.Position; i++) borrowed[i] = packet[i]; borrowed[writer.Position - 1] ^= 1;
            var reader = new DeterministicReader(borrowed, writer.Position); Assert.AreEqual(DeterministicStatus.InvalidEncoding, target.TryReadCanonical(ref reader, world.Registry)); Assert.AreEqual(0, reader.Position);
            byte* check = stackalloc byte[16384]; var unchanged = new DeterministicWriter(check, 16384); target.TryWriteCanonical(ref unchanged).ThrowIfFailure();
            Assert.AreEqual(old.Position, unchanged.Position); for (int i = 0; i < old.Position; i++) Assert.AreEqual(before[i], check[i]);
            borrowed[writer.Position - 1] ^= 1; reader = new DeterministicReader(borrowed, writer.Position); target.TryReadCanonical(ref reader, world.Registry).ThrowIfFailure();
            var roundtrip = new DeterministicWriter(check, 16384); target.TryWriteCanonical(ref roundtrip).ThrowIfFailure(); Assert.AreEqual(writer.Position, roundtrip.Position);
            for (int i = 0; i < writer.Position; i++) Assert.AreEqual(packet[i], check[i]);
            target.TryAppendInputs(100, new[] { new DeterministicPlayerInput<int>(new PlayerId(1), 101) }, 101).ThrowIfFailure(); target.TryReadInputs(100, out var inputs, out _).ThrowIfFailure(); Assert.AreEqual(101, inputs[0].Value);
        }
        private struct GateCodec : IDeterministicCodec<int>
        {
            internal int* Failure;
            public uint SchemaId => 987;
            public ushort Version => 1;
            public int Measure(in int value) => 4;
            public DeterministicStatus TryWrite(ref DeterministicWriter writer, in int value)
            {
                // Inputs have a record-relative offset of 17, independent of the archive's used length.
                if (writer.Position != 17) return DeterministicStatus.ProtocolConflict;
                var s = writer.TryUInt((uint)value, 4); if (!s.IsSuccess()) return s;
                return !writer.IsMeasuring && *Failure != 0 ? DeterministicStatus.Pending : DeterministicStatus.None;
            }
            public DeterministicStatus TryRead(ref DeterministicReader reader, out int value)
            { var s = reader.TryUInt(4, out ulong raw); value = (int)raw; return s; }
        }
        [Test]
        public void FailedAppendTouchesOnlyUnusedTailAndPreservesCodecRelativeCursor()
        {
            using var world = new World(); int fail = 0; var codec = new GateCodec { Failure = &fail };
            using var archive = new DeterministicReplayArchive<int, GateCodec>(world.Context, 123, 4096, 16, 1, codec);
            archive.TryBegin(world.Registry, 0).ThrowIfFailure(); var inputs = new[] { new DeterministicPlayerInput<int>(new PlayerId(1), 42) };
            archive.TryAppendInputs(0, inputs, 77).ThrowIfFailure(); byte* before = stackalloc byte[4096]; byte* after = stackalloc byte[4096];
            var writer = new DeterministicWriter(before, 4096); archive.TryWriteCanonical(ref writer).ThrowIfFailure();
            fail = 1; Assert.AreEqual(DeterministicStatus.Pending, archive.TryAppendInputs(1, inputs, 88)); Assert.AreEqual(2, archive.Count); Assert.AreEqual(1, archive.NextTick);
            var unchanged = new DeterministicWriter(after, 4096); archive.TryWriteCanonical(ref unchanged).ThrowIfFailure(); Assert.AreEqual(writer.Position, unchanged.Position);
            for (int i = 0; i < writer.Position; i++) Assert.AreEqual(before[i], after[i]);
            archive.TryReadInputs(0, out var previous, out ulong hash).ThrowIfFailure(); Assert.AreEqual(42, previous[0].Value); Assert.AreEqual(77UL, hash);
            fail = 0; archive.TryAppendInputs(1, inputs, 88).ThrowIfFailure(); archive.TryReadInputs(1, out var appended, out hash).ThrowIfFailure(); Assert.AreEqual(42, appended[0].Value); Assert.AreEqual(88UL, hash);
        }
    }
}
