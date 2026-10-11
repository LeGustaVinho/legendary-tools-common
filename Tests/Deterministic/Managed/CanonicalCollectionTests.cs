using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe sealed class CanonicalCollectionTests
    {
        private enum SignedExample : short { Negative = -32, Positive = 15 }
        private static void RoundTrip<T, TCodec>(T value, TCodec codec) where T : unmanaged where TCodec : unmanaged, IDeterministicCodec<T>
        {
            byte* bytes = stackalloc byte[64]; var writer = new DeterministicWriter(bytes, 64); codec.TryWrite(ref writer, value).ThrowIfFailure();
            var measure = DeterministicWriter.CreateMeasure(); codec.TryWrite(ref measure, value).ThrowIfFailure();
            Assert.AreEqual(writer.Position, measure.Position); Assert.AreEqual(writer.Position, codec.Measure(value));
            var reader = new DeterministicReader(bytes, writer.Position); codec.TryRead(ref reader, out var actual).ThrowIfFailure(); Assert.AreEqual(value, actual);
        }
        [Test]
        public void PrimitiveAndEnumPoliciesPreserveBoundaryValues()
        {
            RoundTrip(byte.MaxValue, default(BytePolicy)); RoundTrip(sbyte.MinValue, default(SBytePolicy));
            RoundTrip(short.MinValue, default(Int16Policy)); RoundTrip(ushort.MaxValue, default(UInt16Policy));
            RoundTrip(uint.MaxValue, default(UInt32Policy)); RoundTrip(long.MinValue, default(Int64Policy)); RoundTrip(long.MaxValue, default(Int64Policy));
            RoundTrip(char.MaxValue, default(CharPolicy)); RoundTrip(true, default(BooleanPolicy)); RoundTrip(false, default(BooleanPolicy));
            var codec = new EnumPolicy<SignedExample>(0x11001, true); RoundTrip(SignedExample.Negative, codec);
            Assert.Less(codec.Compare(SignedExample.Negative, SignedExample.Positive), 0);
            Assert.Less(default(PrimitiveComparer<long>).Compare(long.MinValue, long.MaxValue), 0);
            Assert.AreEqual(default(PrimitiveEquality<short>).Hash(short.MinValue), default(Int16Policy).Hash(short.MinValue));
        }
        [Test]
        public void WidthChecksBooleanValidationAndOverlappingByteWritesAreAtomic()
        {
            byte* bytes = stackalloc byte[8]; for (int i = 0; i < 8; i++) bytes[i] = (byte)i;
            var writer = new DeterministicWriter(bytes, 8); Assert.AreEqual(DeterministicStatus.OutOfRange, writer.TryUInt(256, 1)); Assert.AreEqual(0, writer.Position);
            writer = new DeterministicWriter(bytes, 8, 2); writer.TryBytes(bytes, 6).ThrowIfFailure();
            for (int i = 0; i < 6; i++) Assert.AreEqual(i, bytes[i + 2]);
            bytes[0] = 2; var reader = new DeterministicReader(bytes, 1);
            Assert.AreEqual(DeterministicStatus.InvalidEncoding, default(BooleanPolicy).TryRead(ref reader, out _)); Assert.AreEqual(0, reader.Position);
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CanonicalListAndSlotMapRestoreFutureHandlesAndCapacity(SnapshotPolicy snapshots)
        {
            byte* bytes = stackalloc byte[1024]; byte* workspace = stackalloc byte[4096]; var codec = default(Int32Policy);
            using (var list = new DeterministicList<int>(new BufferOptions(0, 15, AllocationPolicy.Grow, snapshots, 2, 64)))
            using (var slots = new DeterministicSlotMap<int>(new BufferOptions(0, 15, AllocationPolicy.Grow, snapshots, 2, 64)))
            {
                list.Add(3); list.Add(-9); list.Add(101); var first = slots.Add(30); var second = slots.Add(40); slots.Remove(first);
                var writer = new DeterministicWriter(bytes, 1024); list.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); slots.TryWriteCanonical(ref writer, codec).ThrowIfFailure();
                int capacity = list.Capacity; var next = slots.Add(50); list.Add(201); list.Add(202); slots.Clear();
                var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(workspace, 4096);
                list.TryReadCanonical(ref reader, ref scratch, codec).ThrowIfFailure(); slots.TryReadCanonical(ref reader, ref scratch, codec).ThrowIfFailure();
                Assert.AreEqual(capacity, list.Capacity); CollectionAssert.AreEqual(new[] { 3, -9, 101 }, list); Assert.AreEqual(40, slots[second]); Assert.AreEqual(next, slots.Add(50));
            }
        }
        [Test]
        public void TruncatedOrMismatchedCheckpointNeverChangesCollectionOrReader()
        {
            byte* bytes = stackalloc byte[256]; byte* workspace = stackalloc byte[1024]; var codec = default(Int32Policy);
            using (var list = new DeterministicList<int>(4))
            {
                list.Add(7); var writer = new DeterministicWriter(bytes, 256); list.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); list[0] = 99;
                var iterator = list.GetEnumerator(); var reader = new DeterministicReader(bytes, writer.Position - 1); var scratch = new CanonicalScratch(workspace, 1024);
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, list.TryReadCanonical(ref reader, ref scratch, codec));
                Assert.AreEqual(0, reader.Position); Assert.AreEqual(0, scratch.Position); Assert.AreEqual(99, list[0]); Assert.IsTrue(iterator.MoveNext());
                bytes[0] ^= 1; reader = new DeterministicReader(bytes, writer.Position);
                Assert.AreEqual(DeterministicStatus.ProfileMismatch, list.TryReadCanonical(ref reader, ref scratch, codec)); Assert.AreEqual(99, list[0]);
                var tooSmall = new DeterministicWriter(bytes, 1); bytes[0] = 0xaa;
                Assert.AreEqual(DeterministicStatus.BufferTooSmall, list.TryWriteCanonical(ref tooSmall, codec)); Assert.AreEqual(0xaa, bytes[0]); Assert.AreEqual(0, tooSmall.Position);
            }
        }
        private struct FakeBuffer<T> : IDeterministicImportBuffer<T> where T : unmanaged
        {
            internal T[] Items;
            public bool IsCreated => true;
            public int Capacity => Items.Length;
            public ulong Revision => 0;
            public T Read(int index) => Items[index];
            public void Write(int index, in T value) => Items[index] = value;
            public void CommitWrite() { }
            public DeterministicStatus CanPrepareWrite(int capacity, int start, int count) => DeterministicStatus.None;
            public DeterministicStatus PrepareWrite(int capacity, int start, int count) => DeterministicStatus.None;
            public DeterministicStatus CanReplaceCapacity(int capacity) => DeterministicStatus.None;
            public DeterministicStatus PrepareReplace(int capacity) => DeterministicStatus.None;
        }
        [Test]
        public void CyclicFreeListIsRejectedBeforePublishingSlotMap()
        {
            byte* bytes = stackalloc byte[512]; byte* workspace = stackalloc byte[1024];
            var fake = new FakeBuffer<SlotMapNode<int>> { Items = new[] { new SlotMapNode<int> { Generation = 2, NextFree = 1 }, default } };
            var state = new SlotMapState { Used = 1, FreeHead = 1, FreeTail = 1 }; var writer = new DeterministicWriter(bytes, 512);
            StagedBufferWire<SlotMapNode<int>, SlotMapState, SlotMapNodeCodec<int, Int32Policy>, SlotMapStateCodec, SlotMapWireLayout<int>, FakeBuffer<SlotMapNode<int>>>.TryWrite(
                ref writer, ref fake, state, DeterministicCollectionKind.SlotMap, default, default, default).ThrowIfFailure();
            using (var slots = new DeterministicSlotMap<int>(2))
            {
                var valid = slots.Add(7); var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(workspace, 1024);
                Assert.AreEqual(DeterministicStatus.InvalidEncoding, slots.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)));
                Assert.AreEqual(7, slots[valid]); Assert.AreEqual(1, slots.Count); Assert.AreEqual(0, reader.Position);
            }
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CheckpointWriteReadAndValidationAllocateNoGarbageWithFixedMemory(SnapshotPolicy snapshots)
        {
            byte* bytes = stackalloc byte[2048]; byte* workspace = stackalloc byte[4096];
            using (var list = new DeterministicList<int>(new BufferOptions(32, snapshots: snapshots, pageBytes: 64)))
            {
                for (int i = 0; i < 16; i++) list.Add(i); for (int i = 0; i < 20; i++) Cycle(list, bytes, workspace);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 500; i++) Cycle(list, bytes, workspace);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.AreEqual(0L, allocated);
            }
        }
        private static void Cycle(DeterministicList<int> list, byte* bytes, byte* workspace)
        {
            var writer = new DeterministicWriter(bytes, 2048); list.TryWriteCanonical(ref writer, default(Int32Policy)).ThrowIfFailure();
            var reader = new DeterministicReader(bytes, writer.Position); var scratch = new CanonicalScratch(workspace, 4096);
            list.TryValidateCanonical(ref reader, ref scratch, default(Int32Policy)).ThrowIfFailure();
            reader.Position = 0; scratch.Position = 0; list[0] = -1; list.TryReadCanonical(ref reader, ref scratch, default(Int32Policy)).ThrowIfFailure();
            if (list[0] != 0) throw new InvalidOperationException();
        }
    }
}
