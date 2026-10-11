using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe sealed class NativeCanonicalTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct WireJob : IJob
        {
            internal NativeDeterministicList<int> List;
            internal NativeDeterministicSlotMap<int> Slots;
            internal NativeDeterministicSortedDictionary<int, int, Int32Policy> Tree;
            internal NativeDeterministicPriorityQueue<int, int, Int32Policy> Queue;
            internal NativeArray<byte> Bytes, Workspace;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                List.Add(3); List.Add(-9); List.Add(101); var first = Slots.Add(30); Slots.Add(40); Slots.Remove(first);
                for (int i = 0; i < 5; i++) Tree.Add(i, i * 10); Tree.Remove(2); Queue.Enqueue(7, 2); Queue.Enqueue(8, 2); Queue.Enqueue(9, 1); Queue.Dequeue();
                var codec = default(Int32Policy); var writer = new DeterministicWriter((byte*)Bytes.GetUnsafePtr(), Bytes.Length);
                Results[1] = (int)List.TryWriteCanonical(ref writer, codec); Results[2] = (int)Slots.TryWriteCanonical(ref writer, codec);
                Results[3] = (int)Tree.TryWriteCanonical(ref writer, codec, codec); Results[4] = (int)Queue.TryWriteCanonical(ref writer, codec, codec); Results[5] = writer.Position;
                List.Clear(); Slots.Clear(); Tree.Clear(); Queue.Clear();
                var reader = new DeterministicReader((byte*)Bytes.GetUnsafePtr(), writer.Position); var scratch = new CanonicalScratch((byte*)Workspace.GetUnsafePtr(), Workspace.Length);
                Results[6] = (int)List.TryReadCanonical(ref reader, ref scratch, codec); Results[7] = (int)Slots.TryReadCanonical(ref reader, ref scratch, codec);
                Results[8] = (int)Tree.TryReadCanonical(ref reader, ref scratch, codec, codec); Results[9] = (int)Queue.TryReadCanonical(ref reader, ref scratch, codec, codec);
                Results[10] = List[0]; Results[11] = (int)Slots.Add(50).Generation; Results[12] = Queue.Dequeue(); Results[13] = Tree.Count;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public void BothBackendsEmitIdenticalCanonicalBytesAndNativeReadsInBurst(SnapshotPolicy snapshots)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled, "Burst must be enabled to validate native execution.");
            var options = new BufferOptions(15, snapshots: snapshots, snapshotSlots: 2, pageBytes: 64); var codec = default(Int32Policy);
            var expected = new byte[4096]; int size;
            using (var list = new DeterministicList<int>(options)) using (var slots = new DeterministicSlotMap<int>(options))
            using (var tree = new DeterministicSortedDictionary<int, int, Int32Policy>(options)) using (var queue = new DeterministicPriorityQueue<int, int, Int32Policy>(options))
            {
                list.Add(3); list.Add(-9); list.Add(101); var first = slots.Add(30); slots.Add(40); slots.Remove(first);
                for (int i = 0; i < 5; i++) tree.Add(i, i * 10); tree.Remove(2); queue.Enqueue(7, 2); queue.Enqueue(8, 2); queue.Enqueue(9, 1); queue.Dequeue();
                fixed (byte* p = expected)
                {
                    var writer = new DeterministicWriter(p, expected.Length); list.TryWriteCanonical(ref writer, codec).ThrowIfFailure(); slots.TryWriteCanonical(ref writer, codec).ThrowIfFailure();
                    tree.TryWriteCanonical(ref writer, codec, codec).ThrowIfFailure(); queue.TryWriteCanonical(ref writer, codec, codec).ThrowIfFailure(); size = writer.Position;
                }
            }
            using (var list = new NativeDeterministicList<int>(options, Allocator.Persistent)) using (var slots = new NativeDeterministicSlotMap<int>(options, Allocator.Persistent))
            using (var tree = new NativeDeterministicSortedDictionary<int, int, Int32Policy>(options, Allocator.Persistent)) using (var queue = new NativeDeterministicPriorityQueue<int, int, Int32Policy>(options, Allocator.Persistent))
            using (var bytes = new NativeArray<byte>(4096, Allocator.TempJob)) using (var workspace = new NativeArray<byte>(8192, Allocator.TempJob)) using (var results = new NativeArray<int>(14, Allocator.TempJob))
            {
                new WireJob { List = list, Slots = slots, Tree = tree, Queue = queue, Bytes = bytes, Workspace = workspace, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0]); for (int i = 1; i <= 4; i++) Assert.AreEqual(0, results[i]);
                Assert.AreEqual(size, results[5]); for (int i = 0; i < size; i++) Assert.AreEqual(expected[i], bytes[i], "byte " + i);
                for (int i = 6; i <= 9; i++) Assert.AreEqual(0, results[i]);
                Assert.AreEqual(3, results[10]); Assert.AreEqual(2, results[11]); Assert.AreEqual(7, results[12]); Assert.AreEqual(4, results[13]);
            }
        }
    }
}
