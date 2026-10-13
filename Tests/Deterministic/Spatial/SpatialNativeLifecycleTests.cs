#if !FIXEDPOINT_STANDALONE
using System;
using LegendaryTools.FixedPoint;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialNativeLifecycleTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [Test] public void DefaultAndDisposedNativeOwnersReturnDisposedBeforeDereferencingTheirHeader()
        {
            var empty = default(NativeDeterministicSpatialIndex3<int, int, Int32Policy>);
            Assert.AreEqual(DeterministicStatus.Disposed, empty.TryAdd(1, default, 0));
            Assert.AreEqual(DeterministicStatus.Disposed, empty.TryRebuild());
            var owner = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(2), Allocator.Persistent);
            owner.Dispose(); Assert.AreEqual(DeterministicStatus.Disposed, owner.TryClear());
            Assert.AreEqual(DeterministicStatus.Disposed, owner.TryRebuild());
        }
        private static FixedBounds3 Box(int x) => FixedBounds3.FromMinMax(FixedVector3.FromRaw(x, 0, 0), FixedVector3.FromRaw(x + 8, 8, 8));
        [BurstCompile(CompileSynchronously = true)] private struct CompactCodecJob : IJob
        {
            internal FixedBoundsNetworkProfile Profile;
            internal NativeArray<byte> Packet;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int marker = 0; Managed(ref marker); Result[0] = marker;
                var codec = new SpatialBoundsNetworkCodec<FixedBounds3, SpatialBounds3NetworkAccess>(Profile);
                FixedBounds3.TryFromStorage(new FixedVector3(Fixed64.NegativeZero, Fixed64.Zero, Fixed64.NegativeZero), FixedVector3.FromRaw(4, 6, 8), out var box, out _);
                var writer = new DeterministicWriter((byte*)Packet.GetUnsafePtr(), Packet.Length);
                Result[1] = (int)codec.TryWrite(ref writer, box); var reader = new DeterministicReader((byte*)Packet.GetUnsafeReadOnlyPtr(), writer.Position);
                Result[1] |= (int)codec.TryRead(ref reader, out var restored); Result[2] = default(SpatialBounds3NetworkAccess).RawEquals(box, restored) ? 1 : 0;
                Result[3] = writer.Position == reader.Position ? 1 : 0;
            }
            [BurstDiscard] private static void Managed(ref int marker) => marker = 1;
        }
        [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.FixedWidth)] [TestCase(FixedCodec.BitPacked)]
        public void CompactLogicalCodecsExecuteInBurstAndPreserveRawFields(FixedCodec kind)
        {
            var field = new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64, kind,
                lowerRaw: kind == FixedCodec.BitPacked ? unchecked((ulong)-1000L) : 0, upperRaw: kind == FixedCodec.BitPacked ? 1000UL : 0);
            using var packet = new NativeArray<byte>(96, Allocator.TempJob); using var result = new NativeArray<int>(4, Allocator.TempJob);
            new CompactCodecJob { Profile = new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(field, 3)), Packet = packet, Result = result }.Schedule().Complete();
            Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(1, result[2]); Assert.AreEqual(1, result[3]);
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct ReadJob : IJob
        {
            [ReadOnly] internal NativeSpatialReadOnly<int, int, FixedBounds3, SpatialBounds3Access, Int32Policy> Index;
            internal NativeArray<byte> Workspace;
            internal NativeArray<SpatialHit<int>> Hits;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (int)Index.TryQuery(SpatialQuery.ForNearest(FixedVector3.zero, 8),
                    new SpatialQueryScratch((byte*)Workspace.GetUnsafePtr(), Workspace.Length), (SpatialHit<int>*)Hits.GetUnsafePtr(), Hits.Length, out int count);
                Result[2] = count;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [Test] public void NativeBorrowedViewsAllowParallelBurstReadersAndRejectWritersAndDisposedAliases()
        {
            var index = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(32), Allocator.Persistent);
            for (int i = 0; i < 32; i++) index.TryAdd(i, Box(i * 16), i).ThrowIfFailure();
            var view = index.AsReadOnly(); index.TryMeasureQueryScratch(out int bytes).ThrowIfFailure();
            using var workA = new NativeArray<byte>(bytes, Allocator.TempJob); using var workB = new NativeArray<byte>(bytes, Allocator.TempJob);
            using var hitsA = new NativeArray<SpatialHit<int>>(32, Allocator.TempJob); using var hitsB = new NativeArray<SpatialHit<int>>(32, Allocator.TempJob);
            using var resultA = new NativeArray<int>(3, Allocator.TempJob); using var resultB = new NativeArray<int>(3, Allocator.TempJob);
            try
            {
                var a = new ReadJob { Index = view, Workspace = workA, Hits = hitsA, Result = resultA }.Schedule();
                var b = new ReadJob { Index = view, Workspace = workB, Hits = hitsB, Result = resultB }.Schedule();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.Throws<InvalidOperationException>(() => index.TrySetData(0, 77));
#endif
                JobHandle.CombineDependencies(a, b).Complete();
                Assert.AreEqual(0, resultA[0]); Assert.AreEqual(0, resultB[0]); Assert.AreEqual(0, resultA[1]); Assert.AreEqual(0, resultB[1]);
                Assert.AreEqual(8, resultA[2]); for (int i = 0; i < 8; i++) Assert.AreEqual(hitsA[i].Key, hitsB[i].Key);
                index.TrySetData(0, 77).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.Invalidated, view.Check());
                var alias = index; var disposedView = index.AsReadOnly(); index.Dispose();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.Throws<ObjectDisposedException>(() => disposedView.Check());
                Assert.Throws<ObjectDisposedException>(() => alias.TryGet(0, out _));
#endif
            }
            finally { if (index.IsCreated) index.Dispose(); }
        }
        [TestCase(SpatialRollbackPolicy.Compact)]
        [TestCase(SpatialRollbackPolicy.FastRestore)]
        public void NativeSnapshotsAndCanonicalBytesRestoreLogicalBounds(SpatialRollbackPolicy rollback)
        {
            var options = new SpatialIndexOptions(new BufferOptions(16, snapshotSlots: 2, pageBytes: 128), SpatialIndexStrategy.SparseGrid,
                rollback: rollback, cellSize: Fixed64.FromRaw(8), maxCellsPerObject: 8);
            var native = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(options, Allocator.Persistent);
            using var managed = new DeterministicSpatialIndex3<int, int, Int32Policy>(options);
            try
            {
                for (int i = 0; i < 10; i++) { native.TryAdd(i, Box(i * 12), i).ThrowIfFailure(); managed.TryAdd(i, Box(i * 12), i).ThrowIfFailure(); }
                native.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    Assert.AreEqual(rollback == SpatialRollbackPolicy.FastRestore, snapshot.IncludesAcceleration);
                    native.TryRemove(2).ThrowIfFailure(); native.TryUpdateBounds(3, Box(1000)).ThrowIfFailure(); native.TryRestore(snapshot).ThrowIfFailure();
                    native.TryGet(3, out var restored).ThrowIfFailure(); Assert.AreEqual(Box(36), restored.Bounds); Assert.AreEqual(10, native.Count);
                    var packetA = new byte[16384]; var packetB = new byte[16384];
                    fixed (byte* a = packetA) fixed (byte* b = packetB)
                    {
                        var wa = new DeterministicWriter(a, packetA.Length); var wb = new DeterministicWriter(b, packetB.Length);
                        native.Value.TryWriteCanonical(ref wa, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)).ThrowIfFailure();
                        managed.TryWriteCanonical(ref wb, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)).ThrowIfFailure();
                        Assert.AreEqual(wb.Position, wa.Position); for (int i = 0; i < wa.Position; i++) Assert.AreEqual(a[i], b[i]);
                        native.TryClear().ThrowIfFailure(); var scratchBytes = new byte[32768];
                        fixed (byte* work = scratchBytes)
                        {
                            var reader = new DeterministicReader(b, wb.Position); var scratch = new CanonicalScratch(work, scratchBytes.Length);
                            native.Value.TryReadCanonical(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), default(FixedBounds3Policy)).ThrowIfFailure();
                            native.TryGet(3, out restored).ThrowIfFailure(); Assert.AreEqual(Box(36), restored.Bounds);
                        }
                    }
                }
                finally { snapshot.Dispose(); }
            }
            finally { native.Dispose(); }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void NativeRuntimeRestoresEvictedAndRetainedAccelerators(SnapshotPolicy policy)
        {
            var context = new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler, new NativeDeterministicMemoryFactory());
            FixedBoundsDeterministicProfile.TryFingerprint(out ulong numeric).ThrowIfFailure();
            var profile = new DeterministicCompatibilityProfile(1, numeric, 1, 1, 1);
            var index = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(new BufferOptions(16, snapshotSlots: 2), rollback: SpatialRollbackPolicy.FastRestore), Allocator.Persistent);
            try
            {
                using var registry = new DeterministicStateRegistry(context, profile, 1);
                var binding = SpatialStateBindings.Native3(1, index, default(Int32Policy), default(Int32Policy), 1, 16384, 32768);
                registry.TryRegister(binding).ThrowIfFailure(); registry.TryFreeze().ThrowIfFailure();
                using var history = new DeterministicWorldHistory(context, registry.MaxCanonicalBytes, 8, policy, 2, 64);
                index.TryAdd(42, Box(0), 0).ThrowIfFailure();
                for (int tick = 0; tick < 9; tick++) { index.TryUpdateBounds(42, Box(tick * 50)).ThrowIfFailure(); index.TrySetData(42, tick).ThrowIfFailure(); history.TryCapture(registry, tick).ThrowIfFailure(); }
                history.TryRestore(registry, 3).ThrowIfFailure(); index.TryGet(42, out var restored).ThrowIfFailure(); Assert.AreEqual(3, restored.Data); Assert.AreEqual(Box(150), restored.Bounds); Assert.AreEqual(0, binding.LocalRestoreCount);
                history.TryRestore(registry, 8).ThrowIfFailure(); index.TryGet(42, out restored).ThrowIfFailure(); Assert.AreEqual(8, restored.Data); Assert.AreEqual(Box(400), restored.Bounds); Assert.AreEqual(1, binding.LocalRestoreCount);
                history.TryReset().ThrowIfFailure(); Assert.AreEqual(0, binding.LocalHistoryCount);
            }
            finally { index.Dispose(); }
        }
        private struct Source : IDeterministicSpatialSource<int, int, FixedBounds3>
        {
            [ReadOnly] internal NativeArray<SpatialRecord<int, int, FixedBounds3>> Rows;
            [ReadOnly] internal NativeArray<ulong> Version;
            internal ulong Token;
            public ulong Identity => Token;
            public int Count => Rows.Length;
            public ulong Revision => Version[0];
            public DeterministicStatus TryGetAt(int ordinal, out SpatialRecord<int, int, FixedBounds3> record)
            { record = default; if ((uint)ordinal >= (uint)Rows.Length) return DeterministicStatus.OutOfRange; record = Rows[ordinal]; return DeterministicStatus.None; }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct SynchronizeJob : IJob
        {
            internal NativeDeterministicSpatialIndex3<int, int, Int32Policy> Index;
            internal Source Source;
            internal NativeArray<byte> Workspace;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (int)Index.TrySynchronize(Source); if (Result[1] != 0) return;
                var reader = Index.AsReadOnly(Source);
                Result[1] = (int)reader.TryGet(10, out var record); Result[2] = record.Data;
                Result[1] |= (int)reader.TryGetPairs(new SpatialPairScratch((byte*)Workspace.GetUnsafePtr(), Workspace.Length), out var pairs);
                int count = 0;
                while (true) { Result[1] |= (int)pairs.TryNext(out _, out bool present); if (!present) break; count++; }
                Result[3] = count;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(SpatialIndexStrategy.DynamicBvh)]
        [TestCase(SpatialIndexStrategy.SparseGrid)]
        [TestCase(SpatialIndexStrategy.StaticBvh)]
        public void DerivedSynchronizationAndStreamingPairsRunInBurst(SpatialIndexStrategy strategy)
        {
            var index = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(4, strategy, SpatialStateOwnership.Derived, cellSize: Fixed64.FromRaw(8)), Allocator.Persistent);
            var rows = new NativeArray<SpatialRecord<int, int, FixedBounds3>>(2, Allocator.TempJob); var version = new NativeArray<ulong>(1, Allocator.TempJob);
            using var result = new NativeArray<int>(4, Allocator.TempJob);
            index.TryMeasurePairScratch(out int bytes).ThrowIfFailure(); using var work = new NativeArray<byte>(bytes, Allocator.TempJob);
            try
            {
                rows[0] = new SpatialRecord<int, int, FixedBounds3>(10, default, 123); rows[1] = new SpatialRecord<int, int, FixedBounds3>(20, default, 456); version[0] = 1;
                var source = new Source { Rows = rows, Version = version, Token = SpatialSourceIdentity.Create() };
                new SynchronizeJob { Index = index, Source = source, Workspace = work, Result = result }.Schedule().Complete();
                Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(123, result[2]); Assert.AreEqual(1, result[3]);
                var view = index.AsReadOnly(source); version[0] = 2; Assert.AreEqual(DeterministicStatus.Invalidated, view.Check());
            }
            finally { version.Dispose(); rows.Dispose(); index.Dispose(); }
        }
    }
}
#endif
