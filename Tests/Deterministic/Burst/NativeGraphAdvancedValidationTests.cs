using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class NativeGraphAdvancedValidationTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct PersistentDeltaJob : IJob
        {
            internal NativePersistentDeterministicGraph<int, int> Store;
            internal NativeArray<byte> Delta, Scratch;
            internal NativeArray<int> Status;
            internal int Length;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Status[0] = managed;
                Status[1] = (int)Store.TryCaptureCurrent(out var empty);
                var reader = new DeterministicReader((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Delta), Length);
                var scratch = new CanonicalScratch((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(Scratch), Scratch.Length);
                Status[2] = (int)Store.TryWithDelta(empty, ref reader, ref scratch, default(Int32Policy), default(Int32Policy), out var populated);
                Status[3] = populated.AsReadOnly().NodeCount; Status[4] = Store.Current.NodeCount;
                Status[5] = (int)Store.TrySelectCurrent(populated); reader.Position = 0; scratch.Position = 0;
                Status[6] = (int)Store.TryApplyDelta(ref reader, ref scratch, default(Int32Policy), default(Int32Policy), true);
                Status[7] = Store.Current.NodeCount; Status[8] = populated.AsReadOnly().NodeCount;
                empty.Dispose(); populated.Dispose(); Status[9] = 1234;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void PersistentDeltaBranchesAndReversesInsideBurst()
        {
            var options = new GraphOptions(4, 4); using var source = new DeterministicGraph<int, int>(options);
            var bytes = new byte[4096]; var delta = new byte[4096]; var scratchBytes = new byte[65536]; int length;
            fixed (byte* b = bytes) fixed (byte* d = delta) fixed (byte* w = scratchBytes)
            {
                var canonical = new DeterministicWriter(b, bytes.Length); source.TryWriteCanonical(ref canonical, default(Int32Policy), default(Int32Policy)).ThrowIfFailure();
                source.AddNode(10); var writer = new DeterministicWriter(d, delta.Length); var scratch = new CanonicalScratch(w, scratchBytes.Length);
                source.TryWriteDelta(new GraphCheckpoint(b, canonical.Position), ref writer, ref scratch, default(Int32Policy), default(Int32Policy), out _, GraphDeltaMode.Reversible).ThrowIfFailure(); length = writer.Position;
            }
            using var store = new NativePersistentDeterministicGraph<int, int>(options, new GraphPersistenceOptions(4), Allocator.Persistent);
            using var nativeDelta = new NativeArray<byte>(delta, Allocator.TempJob); using var nativeScratch = new NativeArray<byte>(65536, Allocator.TempJob); using var status = new NativeArray<int>(10, Allocator.TempJob);
            new PersistentDeltaJob { Store = store, Delta = nativeDelta, Scratch = nativeScratch, Status = status, Length = length }.Schedule().Complete();
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 1, 0, 0, 0, 0, 1, 1234 }, status.ToArray());
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct ReserveJob : IJob
        {
            internal NativePersistentDeterministicGraph<int, int> Store;
            internal NativeArray<int> Result;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Result[0] = managed;
                Result[1] = (int)Store.TryReserveVersionCapacity(3); Result[2] = Store.VersionCapacity; Result[3] = 1234;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void VersionPoolExpansionIsDeferredToTheHost()
        {
            using var store = new NativePersistentDeterministicGraph<int, int>(new GraphOptions(3, 3), new GraphPersistenceOptions(1, 4, AllocationPolicy.Grow), Allocator.Persistent);
            using var result = new NativeArray<int>(4, Allocator.TempJob); new ReserveJob { Store = store, Result = result }.Schedule().Complete();
            CollectionAssert.AreEqual(new[] { 0, (int)DeterministicStatus.BackendUnavailable, 1, 1234 }, result.ToArray());
            store.TryReserveVersionCapacity(3).ThrowIfFailure(); Assert.AreEqual(4, store.VersionCapacity);
        }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void SnapshotViewsDetectDisposedOwnersBeforeDereferencingTheirStorage()
        {
            var graph = new NativeDeterministicGraph<int, int>(2, 2, Allocator.Persistent); graph.AddNode(1); graph.TryCapture(out var snapshot).ThrowIfFailure(); graph.Dispose();
            Assert.That(() => snapshot.AsReadOnly().Check(), Throws.InstanceOf<System.InvalidOperationException>());
        }
#endif
    }
}
