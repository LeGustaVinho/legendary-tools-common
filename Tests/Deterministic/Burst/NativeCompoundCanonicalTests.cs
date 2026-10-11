using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe sealed class NativeCompoundCanonicalTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct CompoundJob : IJob
        {
            internal NativeDeterministicDictionary<int, int, Int32Policy> Map;
            internal NativeDeterministicHashSet<int, Int32Policy> Set;
            internal NativeDeterministicSortedSet<int, Int32Policy> Sorted;
            internal NativeDeterministicSparseSet<int> Sparse;
            internal NativeDeterministicMultiMap<int, int, Int32Policy> Multi;
            internal NativeDeterministicLinkedList<int> Linked;
            internal NativeDeterministicBitSet Bits;
            internal NativeDeterministicQueue<int> Queue;
            internal NativeArray<byte> Bytes, Workspace;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                Map.Add(5, 10); Map.Add(3, 30); Map.Add(1, 100); Map.Remove(3); Map.Add(7, 70);
                Set.Add(4); Set.Add(2); Set.Add(6); Set.Remove(2); Set.Add(2); Sorted.Add(5); Sorted.Add(1); Sorted.Add(7); Sorted.Remove(5);
                Sparse.Add(new StableHandle(9, 1), 90); Sparse.Add(new StableHandle(2, 3), 20); Sparse.Add(new StableHandle(7, 1), 70); Sparse.Remove(new StableHandle(2, 3)); Sparse.Add(new StableHandle(4, 1), 40);
                Multi.Add(3, 31); Multi.Add(1, 11); Multi.Add(3, 32); Multi.RemoveAll(1); Multi.Add(1, 13);
                var a = Linked.AddLast(10); Linked.AddFirst(5); Linked.AddAfter(a, 15); Bits[0] = true; Bits[64] = true; Bits[128] = true;
                for (int i = 1; i <= 5; i++) Queue.Enqueue(i); Queue.Dequeue(); Queue.Dequeue(); Queue.Enqueue(6); Queue.Enqueue(7);
                var codec = default(Int32Policy); var writer = new DeterministicWriter((byte*)Bytes.GetUnsafePtr(), Bytes.Length);
                Results[1] = (int)Map.TryWriteCanonical(ref writer, codec, codec); Results[2] = (int)Set.TryWriteCanonical(ref writer, codec); Results[3] = (int)Sorted.TryWriteCanonical(ref writer, codec);
                Results[4] = (int)Sparse.TryWriteCanonical(ref writer, codec); Results[5] = (int)Multi.TryWriteCanonical(ref writer, codec, codec); Results[6] = (int)Linked.TryWriteCanonical(ref writer, codec);
                Results[7] = (int)Bits.TryWriteCanonical(ref writer); Results[8] = (int)Queue.TryWriteCanonical(ref writer, codec); Results[9] = writer.Position;
                Map.Clear(); Set.Clear(); Sorted.Clear(); Sparse.Clear(); Multi.Clear(); Linked.Clear(); Bits.Clear(); Queue.Clear();
                var reader = new DeterministicReader((byte*)Bytes.GetUnsafePtr(), writer.Position); var scratch = new CanonicalScratch((byte*)Workspace.GetUnsafePtr(), Workspace.Length);
                Results[10] = (int)Map.TryReadCanonical(ref reader, ref scratch, codec, codec); Results[11] = (int)Set.TryReadCanonical(ref reader, ref scratch, codec); Results[12] = (int)Sorted.TryReadCanonical(ref reader, ref scratch, codec);
                Results[13] = (int)Sparse.TryReadCanonical(ref reader, ref scratch, codec); Results[14] = (int)Multi.TryReadCanonical(ref reader, ref scratch, codec, codec); Results[15] = (int)Linked.TryReadCanonical(ref reader, ref scratch, codec);
                Results[16] = (int)Bits.TryReadCanonical(ref reader, ref scratch); Results[17] = (int)Queue.TryReadCanonical(ref reader, ref scratch, codec);
                Results[18] = Map[7]; Results[19] = Sparse[new StableHandle(4, 1)]; Results[20] = Multi.Count; Results[21] = Linked.Count; Results[22] = Bits.Count; Results[23] = Queue.Dequeue();
                Results[24] = (int)Multi.TryRemoveValue(3, 31, codec); Results[25] = (int)Linked.TryFind(10, codec, out _);
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void CompoundCollectionsEmitMatchingBytesAndRestoreInBurst(SnapshotPolicy snapshots)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled); var options = new BufferOptions(16, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64); var codec = default(Int32Policy);
            var expected = new byte[8192]; int size;
            using (var map = new DeterministicDictionary<int, int, Int32Policy>(options)) using (var set = new DeterministicHashSet<int, Int32Policy>(options))
            using (var sorted = new DeterministicSortedSet<int, Int32Policy>(options)) using (var sparse = new DeterministicSparseSet<int>(options, options))
            using (var multi = new DeterministicMultiMap<int, int, Int32Policy>(options, options)) using (var linked = new DeterministicLinkedList<int>(options))
            using (var bits = new DeterministicBitSet(129, snapshots, 2)) using (var queue = new DeterministicQueue<int>(new BufferOptions(5, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64)))
            {
                map.Add(5, 10); map.Add(3, 30); map.Add(1, 100); map.Remove(3); map.Add(7, 70);
                set.Add(4); set.Add(2); set.Add(6); set.Remove(2); set.Add(2); sorted.Add(5); sorted.Add(1); sorted.Add(7); sorted.Remove(5);
                sparse.Add(new StableHandle(9, 1), 90); sparse.Add(new StableHandle(2, 3), 20); sparse.Add(new StableHandle(7, 1), 70); sparse.Remove(new StableHandle(2, 3)); sparse.Add(new StableHandle(4, 1), 40);
                multi.Add(3, 31); multi.Add(1, 11); multi.Add(3, 32); multi.RemoveAll(1); multi.Add(1, 13);
                var a = linked.AddLast(10); linked.AddFirst(5); linked.AddAfter(a, 15); bits[0] = true; bits[64] = true; bits[128] = true;
                for (int i = 1; i <= 5; i++) queue.Enqueue(i); queue.Dequeue(); queue.Dequeue(); queue.Enqueue(6); queue.Enqueue(7);
                fixed (byte* p = expected)
                {
                    var writer = new DeterministicWriter(p, expected.Length); map.TryWriteCanonical(ref writer, codec, codec).ThrowIfFailure(); set.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); sorted.TryWriteCanonical(ref writer, codec).ThrowIfFailure();
                    sparse.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); multi.TryWriteCanonical(ref writer, codec, codec).ThrowIfFailure(); linked.TryWriteCanonical(ref writer, codec).ThrowIfFailure();
                    bits.TryWriteCanonical(ref writer).ThrowIfFailure(); queue.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); size = writer.Position;
                }
            }
            using (var map = new NativeDeterministicDictionary<int, int, Int32Policy>(options, Allocator.Persistent)) using (var set = new NativeDeterministicHashSet<int, Int32Policy>(options, Allocator.Persistent))
            using (var sorted = new NativeDeterministicSortedSet<int, Int32Policy>(options, Allocator.Persistent)) using (var sparse = new NativeDeterministicSparseSet<int>(options, options, Allocator.Persistent))
            using (var multi = new NativeDeterministicMultiMap<int, int, Int32Policy>(options, options, Allocator.Persistent)) using (var linked = new NativeDeterministicLinkedList<int>(options, Allocator.Persistent))
            using (var bits = new NativeDeterministicBitSet(129, Allocator.Persistent, snapshots, 2)) using (var queue = new NativeDeterministicQueue<int>(new BufferOptions(5, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64), Allocator.Persistent))
            using (var bytes = new NativeArray<byte>(8192, Allocator.TempJob)) using (var workspace = new NativeArray<byte>(32768, Allocator.TempJob)) using (var results = new NativeArray<int>(26, Allocator.TempJob))
            {
                new CompoundJob { Map = map, Set = set, Sorted = sorted, Sparse = sparse, Multi = multi, Linked = linked, Bits = bits, Queue = queue, Bytes = bytes, Workspace = workspace, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0]); for (int i = 1; i <= 8; i++) Assert.AreEqual(0, results[i], "write " + i);
                Assert.AreEqual(size, results[9]); for (int i = 0; i < size; i++) Assert.AreEqual(expected[i], bytes[i], "byte " + i);
                for (int i = 10; i <= 17; i++) Assert.AreEqual(0, results[i], "read " + i);
                CollectionAssert.AreEqual(new[] { 70, 40, 3, 3, 3, 3, 0, 0 }, new[] { results[18], results[19], results[20], results[21], results[22], results[23], results[24], results[25] });
            }
        }
    }
}
