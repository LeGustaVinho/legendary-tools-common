using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicHotpathOptimization")]
    public sealed unsafe class NativeHotpathOptimizationTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct BitJob : IJob
        {
            [ReadOnly] internal NativeArray<ulong> Values;
            internal NativeArray<int> Result;
            [BurstDiscard] private static void Managed(ref int marker) { marker = 1; }
            public void Execute()
            {
                int marker = 0; Managed(ref marker); Result[0] = marker;
                for (int i = 0; i < Values.Length; i++)
                { Result[1 + i * 3] = DeterministicBits.Count(Values[i]); Result[2 + i * 3] = DeterministicBits.LeadingZeroCount(Values[i]); Result[3 + i * 3] = DeterministicBits.TrailingZeroCount(Values[i]); }
            }
        }
        [Test]
        public void BurstBitOperationsMatchScalarReferenceForZeroSinglesComplementsAndRandomWords()
        {
            using (var input = new NativeArray<ulong>(1024, Allocator.Persistent))
            using (var output = new NativeArray<int>(1 + input.Length * 3, Allocator.Persistent))
            {
                ulong state = 0x9abcdef012345678UL;
                var fill = input;
                for (int i = 0; i < input.Length; i++)
                {
                    state ^= state << 13; state ^= state >> 7; state ^= state << 17;
                    fill[i] = i == 0 ? 0 : i == 1 ? ulong.MaxValue : i < 66 ? 1UL << (i - 2) : i < 130 ? ~(1UL << (i - 66)) : state;
                }
                new BitJob { Values = input, Result = output }.Schedule().Complete();
                Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, output[0]);
                for (int i = 0; i < input.Length; i++)
                {
                    int count = 0, leading = 64, trailing = 64;
                    for (int bit = 0; bit < 64; bit++) if ((input[i] & (1UL << bit)) != 0) { count++; leading = 63 - bit; if (trailing == 64) trailing = bit; }
                    Assert.AreEqual(count, output[1 + i * 3]); Assert.AreEqual(leading, output[2 + i * 3]); Assert.AreEqual(trailing, output[3 + i * 3]);
                }
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct OrderedJob : IJob
        {
            [ReadOnly] internal NativeDeterministicSparseSet<int> Set;
            internal NativeArray<SparseSetEntry<int>> Output;
            internal NativeArray<int> Marker;
            [BurstDiscard] private static void Managed(ref int marker) { marker = 1; }
            public void Execute()
            {
                int marker = 0; Managed(ref marker); Marker[0] = marker; int i = 0;
                var e = Set.OrderedByEntity.GetEnumerator(); while (e.MoveNext()) Output[i++] = e.Current;
                Marker[1] = i;
            }
        }
        [TestCase(64, 1024)] [TestCase(64, 65536)] [TestCase(512, 1048576)]
        public void OrderedSparseWalkSupportsConcurrentReadOnlyBurstJobsAndSnapshotRestore(int count, int universe)
        {
            using (var set = new NativeDeterministicSparseSet<int>(new BufferOptions(count, snapshotSlots: 1), new BufferOptions(universe, snapshotSlots: 1), Allocator.Persistent))
            using (var a = new NativeArray<SparseSetEntry<int>>(count, Allocator.Persistent))
            using (var b = new NativeArray<SparseSetEntry<int>>(count, Allocator.Persistent))
            using (var ma = new NativeArray<int>(2, Allocator.Persistent))
            using (var mb = new NativeArray<int>(2, Allocator.Persistent))
            {
                for (int i = 0; i < count; i++) { int rank = i * 73 & (count - 1); uint index = (uint)((long)rank * (universe - 1) / (count - 1)); set.TryAdd(new StableHandle(index, (uint)(rank % 7 + 1)), unchecked((int)index * 101)).ThrowIfFailure(); }
                set.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    var ja = new OrderedJob { Set = set, Output = a, Marker = ma }; var jb = new OrderedJob { Set = set, Output = b, Marker = mb };
                    var ha = ja.Schedule(); var hb = jb.Schedule(); JobHandle.CombineDependencies(ha, hb).Complete();
                    Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, ma[0]); Assert.AreEqual(0, mb[0]); Assert.AreEqual(count, ma[1]); Assert.AreEqual(count, mb[1]);
                    for (int i = 0; i < count; i++)
                    { uint index = (uint)((long)i * (universe - 1) / (count - 1)); Assert.AreEqual(new StableHandle(index, (uint)(i % 7 + 1)), a[i].Handle); Assert.AreEqual(unchecked((int)index * 101), a[i].Value); Assert.AreEqual(a[i].Handle, b[i].Handle); Assert.AreEqual(a[i].Value, b[i].Value); }
                    set.TryRemove(new StableHandle(0, 1)).ThrowIfFailure(); set.TryRestore(root).ThrowIfFailure();
                    ja.Schedule().Complete(); Assert.AreEqual(count, ma[1]); Assert.AreEqual(new StableHandle(0, 1), a[0].Handle);
                }
                finally { root.Dispose(); }
            }
        }
        [TestCase(64)] [TestCase(4096)]
        public void GrowCowDirectoryReallocationAndSlotExpansionPreserveExistingRootsAndPageAddresses(int pageBytes)
        {
            using (var list = new NativeDeterministicList<int>(new BufferOptions(0, 65536, AllocationPolicy.Grow, SnapshotPolicy.CopyOnWrite, 2, pageBytes), Allocator.Persistent))
            {
                for (int i = 0; i < 17; i++) list.TryAdd(i * 37).ThrowIfFailure();
                list.TryCapture(out var small).ThrowIfFailure(); var large = default(NativeSequenceSnapshot<int>); var extra = default(NativeSequenceSnapshot<int>);
                try
                {
                    var storage = list.Storage; var originalPage = storage.Data->Pool[storage.Data->Pages[0]].Values;
                    for (int i = 17; i < 4097; i++) list.TryAdd(i * 37).ThrowIfFailure();
                    list.TryCapture(out large).ThrowIfFailure();
                    Assert.AreEqual((ulong)originalPage, (ulong)storage.Data->Pool[storage.Data->SnapshotPages[(long)small.Slot * storage.Data->MaxPages]].Values);
                    storage.TryReserveSnapshotSlots(5).ThrowIfFailure();
                    for (int i = 0; i < small.Count; i++) { small.TryGet(i, out int value).ThrowIfFailure(); Assert.AreEqual(i * 37, value); }
                    list.TryRestore(small).ThrowIfFailure(); list.TrySet(0, -123).ThrowIfFailure(); list.TryCapture(out extra).ThrowIfFailure();
                    small.TryGet(0, out int original).ThrowIfFailure(); large.TryGet(0, out int originalLarge).ThrowIfFailure(); Assert.AreEqual(0, original); Assert.AreEqual(0, originalLarge);
                    list.TryRestore(large).ThrowIfFailure(); Assert.AreEqual(4097, list.Count);
                    for (int i = 0; i < list.Count; i++) Assert.AreEqual(i * 37, list[i]);
                }
                finally { small.Dispose(); large.Dispose(); extra.Dispose(); }
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct GrowJob : IJob
        {
            internal NativeDeterministicList<int> List;
            internal NativeArray<int> Result;
            [BurstDiscard] private static void Managed(ref int marker) { marker = 1; }
            public void Execute()
            {
                int marker = 0; Managed(ref marker); Result[0] = marker; int status = 0;
                for (int i = List.Count; i < 2049; i++) status |= (int)List.TryAdd(i * 37);
                Result[1] = status;
            }
        }
        [Test]
        public void CowDirectoryGrowthRunsInsideBurstAndPreservesRetainedRoot()
        {
            using (var list = new NativeDeterministicList<int>(new BufferOptions(0, 65536, AllocationPolicy.Grow, SnapshotPolicy.CopyOnWrite, 1, 64), Allocator.Persistent))
            using (var result = new NativeArray<int>(2, Allocator.Persistent))
            {
                for (int i = 0; i < 17; i++) list.TryAdd(i * 37).ThrowIfFailure();
                list.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    new GrowJob { List = list, Result = result }.Schedule().Complete();
                    Assert.IsTrue(BurstCompiler.IsEnabled); Assert.AreEqual(0, result[0]); Assert.AreEqual(0, result[1]); Assert.AreEqual(2049, list.Count);
                    for (int i = 0; i < list.Count; i++) Assert.AreEqual(i * 37, list[i]);
                    for (int i = 0; i < root.Count; i++) { root.TryGet(i, out int value).ThrowIfFailure(); Assert.AreEqual(i * 37, value); }
                }
                finally { root.Dispose(); }
            }
        }
    }
}
