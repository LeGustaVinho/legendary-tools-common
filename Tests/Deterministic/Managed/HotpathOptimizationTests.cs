using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicHotpathOptimization")]
    public sealed class HotpathOptimizationTests
    {
        [Test]
        public void IntegerBitOperationsMatchScalarReferenceIncludingEverySingleBit()
        {
            var values = new List<ulong> { 0, ulong.MaxValue, 0xaaaaaaaaaaaaaaaaUL, 0x5555555555555555UL };
            for (int bit = 0; bit < 64; bit++) { values.Add(1UL << bit); values.Add(~(1UL << bit)); values.Add((1UL << bit) - 1); }
            ulong state = 0x123456789abcdefUL;
            for (int i = 0; i < 10000; i++) { state ^= state << 13; state ^= state >> 7; state ^= state << 17; values.Add(state); }
            foreach (ulong value in values)
            {
                int count = 0, leading = 64, trailing = 64;
                for (int bit = 0; bit < 64; bit++) if ((value & (1UL << bit)) != 0) { count++; leading = 63 - bit; if (trailing == 64) trailing = bit; }
                Assert.AreEqual(count, DeterministicBits.Count(value)); Assert.AreEqual(leading, DeterministicBits.LeadingZeroCount(value)); Assert.AreEqual(trailing, DeterministicBits.TrailingZeroCount(value));
            }
        }
        [TestCase(0)] [TestCase(1)] [TestCase(63)] [TestCase(64)] [TestCase(65)] [TestCase(129)] [TestCase(4097)]
        public void BitsetQueriesMatchBooleanModelAcrossEveryStartAndRetainedRoot(int length)
        {
            using (var bits = new DeterministicBitSet(length, SnapshotPolicy.CopyOnWrite, 2))
            {
                var expected = new bool[length]; int count = 0;
                for (int i = 0; i < length; i++) if ((i % 31) == 0 || (i & 63) == 63 || i == length - 1) { expected[i] = true; bits[i] = true; count++; }
                bits.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    Assert.AreEqual(count, bits.Count);
                    for (int start = 0; start <= length; start++)
                    {
                        int next = -1; for (int i = start; i < length; i++) if (expected[i]) { next = i; break; }
                        Assert.AreEqual(next, bits.NextSetBit(start));
                    }
                    for (int i = 0; i < length; i++) bits[i] = !expected[i];
                    Assert.AreEqual(length - count, bits.Count);
                    bits.TryRestore(root).ThrowIfFailure(); Assert.AreEqual(count, bits.Count);
                    for (int i = 0; i < length; i++) Assert.AreEqual(expected[i], bits[i]);
                }
                finally { root.Dispose(); }
            }
        }
        [TestCase(64, 1024)] [TestCase(64, 65536)] [TestCase(512, 1048576)]
        public void OrderedSparseWalkPreservesGenerationsAfterSwapRemovalMutationRestoreAndReset(int count, int universe)
        {
            using (var set = new DeterministicSparseSet<int>(new BufferOptions(count, snapshotSlots: 1), new BufferOptions(universe, snapshotSlots: 1)))
            {
                var model = new SortedDictionary<uint, SparseSetEntry<int>>();
                for (int i = 0; i < count; i++)
                {
                    int logical = i * 73 & (count - 1); uint index = (uint)((long)logical * (universe - 1) / (count - 1));
                    var entry = new SparseSetEntry<int>(new StableHandle(index, (uint)(logical % 7 + 1)), unchecked((int)index * 101));
                    set.Add(entry.Handle, entry.Value); model.Add(index, entry);
                }
                AssertOrdered(set, model); set.TryCapture(out var root).ThrowIfFailure();
                try
                {
                    var iterator = set.OrderedByEntity.GetEnumerator(); Assert.IsTrue(iterator.MoveNext());
                    var first = model[0]; set.TryRemove(first.Handle).ThrowIfFailure();
                    Assert.AreEqual(DeterministicStatus.Invalidated, iterator.TryMoveNext(out _)); model.Remove(0);
                    var replacement = new StableHandle(0, first.Handle.Generation + 1); set.Add(replacement, -17); model.Add(0, new SparseSetEntry<int>(replacement, -17)); AssertOrdered(set, model);
                    set.TryRestore(root).ThrowIfFailure(); model[0] = first; AssertOrdered(set, model);
                    iterator = set.OrderedByEntity.GetEnumerator(); while (iterator.MoveNext()) { }
                    Assert.IsFalse(iterator.MoveNext()); iterator.Reset(); Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(first.Handle, iterator.Current.Handle);
                    set.Clear(); Assert.IsFalse(set.OrderedByEntity.GetEnumerator().MoveNext());
                }
                finally { root.Dispose(); }
            }
        }
        private static void AssertOrdered(DeterministicSparseSet<int> set, SortedDictionary<uint, SparseSetEntry<int>> model)
        {
            var iterator = set.OrderedByEntity.GetEnumerator();
            foreach (var pair in model) { Assert.IsTrue(iterator.MoveNext()); Assert.AreEqual(pair.Value.Handle, iterator.Current.Handle); Assert.AreEqual(pair.Value.Value, iterator.Current.Value); }
            Assert.IsFalse(iterator.MoveNext());
        }
        [TestCase(64)] [TestCase(4096)]
        public void GrowCowDirectoryExpansionPreservesLeasesAfterRestoreAndAddingSlots(int pageBytes)
        {
            using (var list = new DeterministicList<int>(new BufferOptions(0, 65536, AllocationPolicy.Grow, SnapshotPolicy.CopyOnWrite, 2, pageBytes)))
            {
                for (int i = 0; i < 17; i++) list.Add(i * 37);
                list.TryCapture(out var small).ThrowIfFailure(); var large = default(ManagedSequenceSnapshot<int>); var extra = default(ManagedSequenceSnapshot<int>);
                try
                {
                    for (int i = 17; i < 4097; i++) list.Add(i * 37); list.TryCapture(out large).ThrowIfFailure();
                    list.Storage.Owner.TryReserveSnapshotSlots(5).ThrowIfFailure();
                    list.TryRestore(small).ThrowIfFailure(); list[0] = -123; list.TryCapture(out extra).ThrowIfFailure();
                    small.TryGet(0, out int original).ThrowIfFailure(); large.TryGet(0, out int originalLarge).ThrowIfFailure(); Assert.AreEqual(0, original); Assert.AreEqual(0, originalLarge);
                    list.TryRestore(large).ThrowIfFailure(); Assert.AreEqual(4097, list.Count);
                    for (int i = 0; i < list.Count; i++) Assert.AreEqual(i * 37, list[i]);
                }
                finally { small.Dispose(); large.Dispose(); extra.Dispose(); }
            }
        }
    }
}
