using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeSequenceTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct SequenceJob : IJob
        {
            internal NativeDeterministicList<int> List;
            internal NativeArray<long> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                for (int i = 0; i < 32; i++) if (!Check(List.TryAdd(i))) return;
                if (!Check(List.TryCapture(out var snapshot))) return;
                if (!Check(List.TrySet(0, -999)) || !Check(List.TryInsert(4, 123)) || !Check(List.TryRemoveAt(7))) return;
                if (!Check(snapshot.TryGet(0, out int old))) return;
                Results[2] = old;
                if (!Check(List.TryRestore(snapshot))) return;
                snapshot.Dispose();
                long sum = 0; foreach (int value in List) sum += value;
                Results[3] = sum; Results[4] = List.IndexOf(17, new Int32Policy());
                byte* bytes = stackalloc byte[256]; var writer = new DeterministicWriter(bytes, 256); var codec = new Int32Policy();
                for (int i = 0; i < List.Count; i++) { int value = List[i]; if (!Check(codec.TryWrite(ref writer, value))) return; }
                Results[5] = unchecked((long)DeterministicHash.Xxh64(bytes, writer.Position)); Results[6] = writer.Position;
            }
            private bool Check(DeterministicStatus status) { if (status.IsSuccess()) return true; Results[1] = (long)status; return false; }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }

        [TestCase(SnapshotPolicy.FullPerTick)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        [TestCase(SnapshotPolicy.CopyOnWrite)]
        public unsafe void SharedSequenceKernelMatchesGcStorageInNativeBurst(SnapshotPolicy snapshots)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled, "Native execution must be verified, not a managed fallback.");
            var options = new BufferOptions(64, snapshots: snapshots, snapshotSlots: 3, pageBytes: 64);
            using (var list = new NativeDeterministicList<int>(options, Allocator.Persistent))
            using (var results = new NativeArray<long>(7, Allocator.TempJob))
            using (var managed = new DeterministicList<int>(options))
            {
                new SequenceJob { List = list, Results = results }.Schedule().Complete();
                Assert.AreEqual(0, results[0], "BurstDiscard marker proves native compilation.");
                Assert.AreEqual(0, results[1], "Unexpected deterministic status.");
                Assert.AreEqual(0, results[2]); Assert.AreEqual(496, results[3]); Assert.AreEqual(17, results[4]);
                Assert.AreEqual(32, list.Count, "Copies used by jobs share metadata.");
                for (int i = 0; i < 32; i++) managed.Add(i);
                var bytes = new byte[256];
                fixed (byte* p = bytes)
                {
                    var writer = new DeterministicWriter(p, bytes.Length); var codec = new Int32Policy();
                    foreach (int value in managed) codec.TryWrite(ref writer, value).ThrowIfFailure();
                    Assert.AreEqual(writer.Position, results[6]); Assert.AreEqual(unchecked((long)DeterministicHash.Xxh64(p, writer.Position)), results[5]);
                }
                for (int i = 0; i < 32; i++) Assert.AreEqual(managed[i], list[i]);
            }
        }

        [Test]
        public void NativeViewsAreInvalidatedByMutationAndRestore()
        {
            var list = new NativeDeterministicList<int>(4, Allocator.Persistent);
            try
            {
                list.Add(1); var view = list.AsReadOnly(); list.Add(2);
                Assert.AreEqual(DeterministicStatus.Invalidated, view.TryGet(0, out _));
                list.TryCapture(out var snapshot).ThrowIfFailure(); var enumerator = list.GetEnumerator();
                list.TryRestore(snapshot).ThrowIfFailure(); Assert.AreEqual(DeterministicStatus.Invalidated, enumerator.TryMoveNext(out _));
                var old = snapshot; snapshot.Dispose(); list.TryCapture(out var replacement).ThrowIfFailure();
                old.Dispose(); Assert.IsTrue(replacement.IsCreated); Assert.IsFalse(old.IsCreated);
                replacement.Dispose();
                Assert.AreEqual(DeterministicStatus.OutOfRange, list.TryRemoveAt(-1));
            }
            finally { list.Dispose(); }
        }
    }
}
