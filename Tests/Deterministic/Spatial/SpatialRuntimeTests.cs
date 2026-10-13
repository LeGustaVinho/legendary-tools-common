using System;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialRuntimeTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static DeterministicCompatibilityProfile Profile()
        { FixedBoundsDeterministicProfile.TryFingerprint(out ulong numeric).ThrowIfFailure(); return new DeterministicCompatibilityProfile(1, numeric, 1, 1, 1); }
        private static FixedBounds3 Box(int position) => FixedBounds3.FromMinMax(FixedVector3.FromRaw(position, 0, 0), FixedVector3.FromRaw(position + 8, 8, 8));
        private sealed class SourceState : DeterministicStateBinding
        {
            internal int Position, Completions;
            internal ulong Revision;
            internal readonly ulong Identity = SpatialSourceIdentity.Create();
            internal int Reads;
            internal bool FailCompletion;
            internal uint[] Dependencies;
            internal SourceState(uint id = 9) : base(id, 0xade341, 4, 0) { }
            internal void Set(int position) { Position = position; Revision++; }
            public override DeterministicBackend Backend => DeterministicBackend.Managed;
            public override int DependencyCount => Dependencies == null ? 0 : Dependencies.Length;
            public override uint DependencyStateId(int ordinal) => Dependencies[ordinal];
            public override DeterministicStatus TryWrite(ref DeterministicWriter writer) => writer.TryUInt((uint)Position, 4);
            public override DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch) => reader.TryUInt(4, out _);
            public override DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch)
            { var status = reader.TryUInt(4, out ulong value); if (status.IsSuccess()) Set((int)value); return status; }
            public override DeterministicStatus TryCompleteRestore(object owner, long tick, ulong lease)
            { Completions++; if (!FailCompletion) return DeterministicStatus.None; FailCompletion = false; return DeterministicStatus.OutOfRange; }
        }
        private struct Source : IDeterministicSpatialSource<int, int, FixedBounds3>
        {
            internal SourceState State;
            internal bool Duplicate, Fail, ShiftKeys, Throw;
            public ulong Identity => State.Identity;
            public int Count => 3;
            public ulong Revision => State.Revision;
            public DeterministicStatus TryGetAt(int ordinal, out SpatialRecord<int, int, FixedBounds3> record)
            {
                record = default; if (Fail && ordinal == 2) return DeterministicStatus.OutOfRange;
                if (Throw && State.Reads++ == 4) throw new InvalidOperationException("Source failed after publishing a changed record.");
                if ((uint)ordinal >= 3) return DeterministicStatus.OutOfRange;
                record = new SpatialRecord<int, int, FixedBounds3>(Duplicate ? 0 : ordinal + (ShiftKeys ? State.Position : 0), Box(State.Position + ordinal * 10), State.Position, new SpatialMetadata((uint)ordinal));
                return DeterministicStatus.None;
            }
        }
        private static DeterministicSpatialIndex3<int, int, Int32Policy> Derived(SpatialIndexStrategy strategy, SpatialRollbackPolicy rollback)
            => new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(new BufferOptions(8, snapshotSlots: 2, pageBytes: 128),
                strategy, SpatialStateOwnership.Derived, rollback, Fixed64.FromRaw(8), maxCellsPerObject: 8));
        private static DeterministicStateBinding Bind(DeterministicSpatialIndex3<int, int, Int32Policy> index, Source source, ulong rule = 1)
            => SpatialStateBindings.ManagedDerived(1, index, source, new uint[] { 9 }, rule, 1);
        [Test] public void EqualRevisionsFromDifferentSourceOwnersCannotValidateTheWrongProjection()
        {
            var a = new SourceState(); var b = new SourceState(); a.Set(20); b.Set(500);
            var first = new Source { State = a }; var second = new Source { State = b };
            Assert.AreEqual(first.Revision, second.Revision); Assert.AreNotEqual(first.Identity, second.Identity);
            using var index = Derived(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact);
            index.TrySynchronize(first).ThrowIfFailure(); var old = index.AsReadOnly(first);
            Assert.AreEqual(DeterministicStatus.Invalidated, index.AsReadOnly(second).Check());
            index.TrySynchronize(second).ThrowIfFailure(); index.AsReadOnly(second).TryGet(1, out var record).ThrowIfFailure();
            Assert.AreEqual(500, record.Data); Assert.AreEqual(DeterministicStatus.Invalidated, old.Check());
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)] [TestCase(SpatialIndexStrategy.SparseGrid)] [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void ThrowingManagedSourcesRestoreChangedRecordsAndReleaseTheirTransaction(SpatialIndexStrategy strategy)
        {
            var state = new SourceState(); var source = new Source { State = state }; state.Set(20);
            using var index = Derived(strategy, SpatialRollbackPolicy.Compact);
            index.TrySynchronize(source).ThrowIfFailure(); state.Set(200); source.Throw = true;
            Assert.Throws<InvalidOperationException>(() => index.TrySynchronize(source));
            var preserved = index.Storage.ReadEntry(index.Storage.Find(1)).Value;
            Assert.AreEqual(20, preserved.Data); Assert.AreEqual(Box(30), preserved.Bounds); Assert.AreEqual(3, index.Count);
            // Both public slots are still available: the throwing source did not strand a transaction lease.
            index.TryCapture(out var a).ThrowIfFailure(); index.TryCapture(out var b).ThrowIfFailure(); b.Dispose(); a.Dispose();
            source.Throw = false; index.TrySynchronize(source).ThrowIfFailure();
            index.AsReadOnly(source).TryGet(1, out preserved).ThrowIfFailure(); Assert.AreEqual(200, preserved.Data);
        }
        [Test] public void UnregisteredAndCyclicSourceDependenciesAreRejectedBeforeFreeze()
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var state = new SourceState(); var source = new Source { State = state };
            using var index = Derived(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact);
            using var missing = new DeterministicStateRegistry(context, Profile(), 2);
            missing.TryRegister(Bind(index, source)).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.InvalidConfiguration, missing.TryFreeze()); Assert.IsFalse(missing.IsFrozen);
            state.Dependencies = new uint[] { 1 };
            using var cyclic = new DeterministicStateRegistry(context, Profile(), 2);
            cyclic.TryRegister(Bind(index, source)).ThrowIfFailure(); cyclic.TryRegister(state).ThrowIfFailure();
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, cyclic.TryFreeze()); Assert.IsFalse(cyclic.IsFrozen);
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void FullCapacitySourceMembershipCanBeReplacedAtomically(SpatialIndexStrategy strategy)
        {
            var state = new SourceState(); var source = new Source { State = state, ShiftKeys = true };
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(3, strategy, SpatialStateOwnership.Derived, cellSize: Fixed64.FromRaw(8)));
            state.Set(20); index.TrySynchronize(source).ThrowIfFailure(); var old = index.AsReadOnly(source);
            state.Set(40); index.TrySynchronize(source).ThrowIfFailure(); Assert.AreEqual(3, index.Count);
            var reader = index.AsReadOnly(source); Assert.AreEqual(DeterministicStatus.NotFound, reader.TryGet(20, out _));
            reader.TryGet(41, out var value).ThrowIfFailure(); Assert.AreEqual(40, value.Data);
            Assert.AreEqual(DeterministicStatus.Invalidated, old.Check());
        }
        private static byte[] Encode(DeterministicStateRegistry registry, long tick = 0)
        {
            registry.TryEncode(tick, out byte* bytes, out int count).ThrowIfFailure(); var packet = new byte[count];
            for (int i = 0; i < count; i++) packet[i] = bytes[i]; return packet;
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void DerivedSourcesAreAtomicAndBorrowedViewsDetectStaleSources(SpatialIndexStrategy strategy)
        {
            var state = new SourceState(); var source = new Source { State = state }; state.Set(20);
            using var index = Derived(strategy, SpatialRollbackPolicy.Compact);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, index.TryAdd(99, Box(0), 1));
            index.TrySynchronize(source).ThrowIfFailure(); var view = index.AsReadOnly(source);
            view.TryGet(1, out var value).ThrowIfFailure(); Assert.AreEqual(20, value.Data);
            Assert.AreEqual(DeterministicStatus.InvalidConfiguration, index.AsReadOnly().Check());
            state.Set(100); Assert.AreEqual(DeterministicStatus.Invalidated, view.Check());
            source.Duplicate = true;
            Assert.AreEqual(DeterministicStatus.DuplicateKey, index.TrySynchronize(source)); Assert.AreEqual(3, index.Count);
            source.Duplicate = false; source.Fail = true;
            Assert.AreEqual(DeterministicStatus.OutOfRange, index.TrySynchronize(source));
            source.Fail = false; index.TrySynchronize(source).ThrowIfFailure();
            index.AsReadOnly(source).TryGet(1, out value).ThrowIfFailure(); Assert.AreEqual(100, value.Data);
            Assert.AreEqual(DeterministicStatus.Invalidated, view.Check());
        }
        [TestCase(SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialRollbackPolicy.FastRestore)]
        public void DerivedDescriptorsAvoidDuplicateRecordsAndRestoreAfterAllSources(SpatialRollbackPolicy rollback)
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var state = new SourceState(); var source = new Source { State = state };
            using var index = Derived(SpatialIndexStrategy.DynamicBvh, rollback);
            using var registry = new DeterministicStateRegistry(context, Profile(), 2);
            var binding = Bind(index, source); registry.TryRegister(binding).ThrowIfFailure(); registry.TryRegister(state).ThrowIfFailure(); registry.TryFreeze().ThrowIfFailure();
            state.Set(20); var packet = Encode(registry, 3);
            // LDW header26 + two entry headers16 + descriptor4 + source4 + footer8.
            Assert.AreEqual(74, packet.Length);
            state.Set(100); registry.TryEncode(4, out _, out _).ThrowIfFailure(); var oldView = index.AsReadOnly(source);
            fixed (byte* bytes = packet)
            { var reader = new DeterministicReader(bytes, packet.Length); registry.TryReadCanonical(ref reader, out long tick).ThrowIfFailure(); Assert.AreEqual(3, tick); }
            Assert.AreEqual(20, state.Position); Assert.AreEqual(DeterministicStatus.Invalidated, oldView.Check());
            index.AsReadOnly(source).TryGet(2, out var record).ThrowIfFailure(); Assert.AreEqual(20, record.Data);
            Assert.AreEqual(Box(40), record.Bounds); Assert.AreEqual(1, state.Completions);
            var failed = Encode(registry, 3); state.Set(100); registry.TryEncode(4, out _, out _).ThrowIfFailure(); state.FailCompletion = true;
            fixed (byte* bytes = failed)
            { var reader = new DeterministicReader(bytes, failed.Length); Assert.AreEqual(DeterministicStatus.OutOfRange, registry.TryReadCanonical(ref reader, out _)); Assert.AreEqual(0, reader.Position); }
            Assert.IsFalse(registry.IsFaulted); Assert.AreEqual(100, state.Position);
            index.AsReadOnly(source).TryGet(2, out record).ThrowIfFailure(); Assert.AreEqual(100, record.Data);
            Assert.AreEqual(3, state.Completions);
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void LocalLeaseEvictionFallsBackAndTruncationReleasesExactOwners(SnapshotPolicy historyPolicy)
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var state = new SourceState(); var source = new Source { State = state };
            using var index = Derived(SpatialIndexStrategy.StaticBvh, SpatialRollbackPolicy.FastRestore);
            using var registry = new DeterministicStateRegistry(context, Profile(), 2);
            var binding = Bind(index, source); registry.TryRegister(binding).ThrowIfFailure(); registry.TryRegister(state).ThrowIfFailure(); registry.TryFreeze().ThrowIfFailure();
            using var history = new DeterministicWorldHistory(context, registry.MaxCanonicalBytes, 8, historyPolicy, 2, 64);
            for (int tick = 0; tick < 12; tick++) { state.Set(tick * 100); history.TryCapture(registry, tick).ThrowIfFailure(); }
            Assert.AreEqual(2, binding.LocalHistoryCount);
            history.TryRestore(registry, 5).ThrowIfFailure(); Assert.AreEqual(500, state.Position); Assert.AreEqual(0, binding.LocalRestoreCount);
            index.AsReadOnly(source).TryGet(0, out var item).ThrowIfFailure(); Assert.AreEqual(500, item.Data);
            history.TryRestore(registry, 11).ThrowIfFailure(); Assert.AreEqual(1, binding.LocalRestoreCount);
            index.AsReadOnly(source).TryGet(0, out item).ThrowIfFailure(); Assert.AreEqual(1100, item.Data);
            history.TryTruncate(10).ThrowIfFailure(); Assert.AreEqual(1, binding.LocalHistoryCount);
            state.Set(42); history.TryCapture(registry, 11).ThrowIfFailure(); history.TryRestore(registry, 11).ThrowIfFailure();
            Assert.AreEqual(42, state.Position); index.AsReadOnly(source).TryGet(1, out item).ThrowIfFailure(); Assert.AreEqual(42, item.Data);
            history.TryReset().ThrowIfFailure(); Assert.AreEqual(0, binding.LocalHistoryCount);
            history.TryCapture(registry, 100).ThrowIfFailure(); history.Dispose(); Assert.AreEqual(0, binding.LocalHistoryCount);
        }
        [Test] public void StrategyAndRetentionDoNotChangeDerivedBytesButSourceRulesDo()
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.Managed);
            var state = new SourceState(); var source = new Source { State = state }; state.Set(500);
            using var a = Derived(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact);
            using var b = Derived(SpatialIndexStrategy.SparseGrid, SpatialRollbackPolicy.FastRestore);
            using var left = new DeterministicStateRegistry(context, Profile(), 2); using var right = new DeterministicStateRegistry(context, Profile(), 2);
            var bindingA = Bind(a, source); var bindingB = Bind(b, source);
            Assert.AreEqual(bindingA.Signature, bindingB.Signature); Assert.AreNotEqual(bindingA.Signature, Bind(b, source, 2).Signature);
            left.TryRegister(bindingA).ThrowIfFailure(); left.TryRegister(state).ThrowIfFailure(); left.TryFreeze().ThrowIfFailure();
            right.TryRegister(state).ThrowIfFailure(); right.TryRegister(bindingB).ThrowIfFailure(); right.TryFreeze().ThrowIfFailure();
            CollectionAssert.AreEqual(Encode(left, 6), Encode(right, 6));
        }
        [Test] public void SynchronizationAndQueriesAllocateNoGcAfterWarmup()
        {
            var state = new SourceState(); var source = new Source { State = state };
            using var index = Derived(SpatialIndexStrategy.DynamicBvh, SpatialRollbackPolicy.Compact);
            index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure(); var arena = new byte[bytes]; var hits = new SpatialHit<int>[8];
            fixed (byte* work = arena) fixed (SpatialHit<int>* output = hits)
            {
                var scratch = new SpatialQueryScratch(work, bytes); var query = SpatialQuery.ForNearest(FixedVector3.zero, 3);
                for (int i = 0; i < 10; i++) { state.Set(i); index.TrySynchronize(source).ThrowIfFailure(); index.AsReadOnly(source).TryQuery(query, scratch, output, 8, out _).ThrowIfFailure(); }
                long before = GC.GetAllocatedBytesForCurrentThread(); int error = 0;
                for (int i = 0; i < 1000; i++)
                { state.Set(i); error |= (int)index.TrySynchronize(source); error |= (int)index.AsReadOnly(source).TryQuery(query, scratch, output, 8, out _); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0, error); Assert.AreEqual(0, allocated);
            }
        }
    }
}
