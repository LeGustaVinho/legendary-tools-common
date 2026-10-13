using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe sealed class ManagedBatchBoundaryRegressionTests
    {
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SparseRestoreDuringDenseBatchPreservesMetadataValuesAndLease(SnapshotPolicy policy)
        {
            using var set = new DeterministicSparseSet<int>(Options(4, policy), Options(8, policy));
            var first = new StableHandle(1, 1); var second = new StableHandle(2, 3);
            set.TryAdd(first, 10).ThrowIfFailure(); set.TryCapture(out var snapshot).ThrowIfFailure();
            using (snapshot)
            {
                set.TryAdd(second, 20).ThrowIfFailure();
                set.TryBeginDenseWriteBatch(1, out var batch).ThrowIfFailure();
                batch.TryAcquirePartition(0, out var range).ThrowIfFailure(); range.TrySet(0, 100).ThrowIfFailure();
                ulong revision = set.Revision;
                try
                {
                    Assert.That(set.TryRestore(snapshot), Is.EqualTo(DeterministicStatus.Invalidated));
                    Assert.That(set.Count, Is.EqualTo(2)); Assert.That(set.Revision, Is.EqualTo(revision));
                    range.TryGet(0, out var row).ThrowIfFailure(); Assert.That(row.Handle, Is.EqualTo(first)); Assert.That(row.Value, Is.EqualTo(100));
                    range.TrySet(1, 200).ThrowIfFailure();
                    snapshot.TryGet(first, out int retained).ThrowIfFailure(); Assert.That(retained, Is.EqualTo(10));
                }
                finally { range.TryRelease().ThrowIfFailure(); batch.TryComplete().ThrowIfFailure(); }
                Assert.That(set.Count, Is.EqualTo(2)); Assert.That(set[first], Is.EqualTo(100)); Assert.That(set[second], Is.EqualTo(200));
                set.TryRestore(snapshot).ThrowIfFailure(); Assert.That(set.Count, Is.EqualTo(1)); Assert.That(set[first], Is.EqualTo(10));
                Assert.That(set.TryGet(second, out _), Is.EqualTo(DeterministicStatus.NotFound));
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void SparseRestoreChecksBothOwnersBeforeRestoringEitherBuffer(SnapshotPolicy policy)
        {
            using var set = new DeterministicSparseSet<int>(Options(4, policy), Options(8, policy));
            var first = new StableHandle(1, 1); var second = new StableHandle(2, 3);
            set.TryAdd(first, 10).ThrowIfFailure(); set.TryCapture(out var snapshot).ThrowIfFailure();
            using (snapshot)
            {
                set.TrySet(first, 100).ThrowIfFailure(); set.TryAdd(second, 20).ThrowIfFailure();
                // An internal sparse-owner batch checks the same shared publication boundary as the public dense batch.
                set.Sparse.Owner.TryBeginBatch(set.Sparse.Capacity, 1, out var batch).ThrowIfFailure();
                batch.TryAcquirePartition(0, out var range).ThrowIfFailure(); ulong revision = set.Revision;
                try
                {
                    Assert.That(set.TryRestore(snapshot), Is.EqualTo(DeterministicStatus.Invalidated));
                    Assert.That(set.Count, Is.EqualTo(2)); Assert.That(set.Revision, Is.EqualTo(revision));
                }
                finally { range.TryRelease().ThrowIfFailure(); batch.TryComplete().ThrowIfFailure(); }
                Assert.That(set[first], Is.EqualTo(100)); Assert.That(set[second], Is.EqualTo(20));
                set.TryRestore(snapshot).ThrowIfFailure(); Assert.That(set[first], Is.EqualTo(10)); Assert.That(set.Count, Is.EqualTo(1));
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick, 0)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, 7)]
        [TestCase(SnapshotPolicy.CopyOnWrite, 0)]
        [TestCase(SnapshotPolicy.CopyOnWrite, 7)]
        public void ArrayCanonicalOperationsRejectBatchBeforeTouchingBuffers(SnapshotPolicy policy, int start)
        {
            using var values = new DeterministicArray<int>(Options(4, policy)); values[0] = 10;
            byte* packet = stackalloc byte[1024]; var encoded = new DeterministicWriter(packet, 1024, start);
            values.TryWriteCanonical(ref encoded, default(Int32Policy)).ThrowIfFailure();
            values.TryBeginWriteBatch(1, out var batch).ThrowIfFailure(); batch.TryAcquirePartition(0, out var range).ThrowIfFailure();
            range.TrySet(0, 100).ThrowIfFailure();
            byte* destination = stackalloc byte[1024]; byte* workspace = stackalloc byte[1024];
            Fill(destination, 1024, 0xa5); Fill(workspace, 1024, 0xcc);
            try
            {
                var writer = new DeterministicWriter(destination, 1024, start);
                Assert.That(values.TryWriteCanonical(ref writer, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated));
                Assert.That(writer.Position, Is.EqualTo(start)); AssertFilled(destination, 1024, 0xa5);
                var measure = DeterministicWriter.CreateMeasure();
                Assert.That(values.TryWriteCanonical(ref measure, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated));
                Assert.That(measure.Position, Is.Zero);
                var reader = new DeterministicReader(packet, encoded.Position, start); var scratch = new CanonicalScratch(workspace, 1024); scratch.Position = 5;
                Assert.That(values.TryValidateCanonical(ref reader, ref scratch, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated));
                Assert.That(reader.Position, Is.EqualTo(start)); Assert.That(scratch.Position, Is.EqualTo(5)); AssertFilled(workspace, 1024, 0xcc);
                Assert.That(values.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated));
                Assert.That(reader.Position, Is.EqualTo(start)); Assert.That(scratch.Position, Is.EqualTo(5)); AssertFilled(workspace, 1024, 0xcc);
                range.TryGet(0, out int current).ThrowIfFailure(); Assert.That(current, Is.EqualTo(100));
            }
            finally { range.TryRelease().ThrowIfFailure(); batch.TryComplete().ThrowIfFailure(); }
            Assert.That(values[0], Is.EqualTo(100));
            var completed = new DeterministicWriter(destination, 1024, start); values.TryWriteCanonical(ref completed, default(Int32Policy)).ThrowIfFailure();
            var restored = new DeterministicReader(packet, encoded.Position, start); var restoreScratch = new CanonicalScratch(workspace, 1024);
            values.TryReadCanonical(ref restored, ref restoreScratch, default(Int32Policy)).ThrowIfFailure(); Assert.That(values[0], Is.EqualTo(10));
        }

        [TestCase(false, 0)] [TestCase(false, 7)]
        [TestCase(true, 0)] [TestCase(true, 7)]
        public void SparseCanonicalOperationsRejectEitherOwnerBatchBeforeTouchingBuffers(bool sparseOwner, int start)
        {
            using var set = new DeterministicSparseSet<int>(4, 8); var entity = new StableHandle(1, 2); set.TryAdd(entity, 10).ThrowIfFailure();
            byte* packet = stackalloc byte[1024]; var encoded = new DeterministicWriter(packet, 1024, start);
            set.TryWriteCanonical(ref encoded, default(Int32Policy)).ThrowIfFailure(); set.TrySet(entity, 100).ThrowIfFailure();
            ManagedSparseWriteBatch<int> denseBatch = default; ManagedSparseWriteRange<int> denseRange = default;
            ManagedWriteBatch<SparseSetIndex> sparseBatch = default; ManagedWriteRange<SparseSetIndex> sparseRange = default;
            if (sparseOwner) { set.Sparse.Owner.TryBeginBatch(set.Sparse.Capacity, 1, out sparseBatch).ThrowIfFailure(); sparseBatch.TryAcquirePartition(0, out sparseRange).ThrowIfFailure(); }
            else { set.TryBeginDenseWriteBatch(1, out denseBatch).ThrowIfFailure(); denseBatch.TryAcquirePartition(0, out denseRange).ThrowIfFailure(); }
            byte* destination = stackalloc byte[1024]; byte* workspace = stackalloc byte[1024];
            Fill(destination, 1024, 0xa5); Fill(workspace, 1024, 0xcc);
            try
            {
                var writer = new DeterministicWriter(destination, 1024, start);
                Assert.That(set.TryWriteCanonical(ref writer, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated));
                Assert.That(writer.Position, Is.EqualTo(start)); AssertFilled(destination, 1024, 0xa5);
                var measure = DeterministicWriter.CreateMeasure();
                Assert.That(set.TryWriteCanonical(ref measure, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated)); Assert.That(measure.Position, Is.Zero);
                var reader = new DeterministicReader(packet, encoded.Position, start); var scratch = new CanonicalScratch(workspace, 1024); scratch.Position = 5;
                Assert.That(set.TryValidateCanonical(ref reader, ref scratch, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated));
                Assert.That(reader.Position, Is.EqualTo(start)); Assert.That(scratch.Position, Is.EqualTo(5)); AssertFilled(workspace, 1024, 0xcc);
                Assert.That(set.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)), Is.EqualTo(DeterministicStatus.Invalidated));
                Assert.That(reader.Position, Is.EqualTo(start)); Assert.That(scratch.Position, Is.EqualTo(5)); AssertFilled(workspace, 1024, 0xcc);
                Assert.That(set.Count, Is.EqualTo(1));
            }
            finally
            {
                if (sparseOwner) { sparseRange.TryRelease().ThrowIfFailure(); sparseBatch.TryComplete().ThrowIfFailure(); }
                else { denseRange.TryRelease().ThrowIfFailure(); denseBatch.TryComplete().ThrowIfFailure(); }
            }
            Assert.That(set[entity], Is.EqualTo(100));
            var completed = new DeterministicWriter(destination, 1024, start); set.TryWriteCanonical(ref completed, default(Int32Policy)).ThrowIfFailure();
            var restored = new DeterministicReader(packet, encoded.Position, start); var restoreScratch = new CanonicalScratch(workspace, 1024);
            set.TryReadCanonical(ref restored, ref restoreScratch, default(Int32Policy)).ThrowIfFailure(); Assert.That(set[entity], Is.EqualTo(10));
        }
        private static BufferOptions Options(int capacity, SnapshotPolicy policy) => new BufferOptions(capacity, snapshots: policy, pageBytes: 64);
        private static void Fill(byte* buffer, int length, byte value) { for (int i = 0; i < length; i++) buffer[i] = value; }
        private static void AssertFilled(byte* buffer, int length, byte value)
        { for (int i = 0; i < length; i++) if (buffer[i] != value) Assert.Fail("Buffer changed at byte " + i); }
    }
}
