using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicOptimization")]
    public sealed class CollectionOptimizationTests
    {
        private struct Triple
        {
            internal int A, B, C;
            internal Triple(int value) { A = value; B = ~value; C = unchecked(value * 104729); }
        }
        [TestCase(64)] [TestCase(128)]
        public void StructMovesHandlePagesWhoseElementCountIsNotAPowerOfTwo(int pageBytes)
        {
            using (var list = new DeterministicList<Triple>(new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: pageBytes)))
            {
                var expected = new List<Triple>();
                for (int i = 0; i < 64; i++) { var value = new Triple(i); list.Add(value); expected.Add(value); }
                list.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    for (int step = 0; step < 128; step++)
                    {
                        int index = step * 17 % expected.Count; list.RemoveAt(index); expected.RemoveAt(index);
                        index = step * 29 % (expected.Count + 1); var value = new Triple(step + 1000);
                        list.Insert(index, value); expected.Insert(index, value);
                        CollectionAssert.AreEqual(expected, list);
                    }
                    list.TryRestore(snapshot).ThrowIfFailure();
                    for (int i = 0; i < 64; i++) Assert.AreEqual(new Triple(i), list[i]);
                }
                finally { snapshot.Dispose(); }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.CopyOnWrite, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.FullPerTick, AllocationPolicy.Grow)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, AllocationPolicy.Grow)]
        [TestCase(SnapshotPolicy.CopyOnWrite, AllocationPolicy.Grow)]
        public void OverlappingListMovesAcrossPagesMatchListAndRetainedSnapshot(SnapshotPolicy policy, AllocationPolicy allocation)
        {
            var options = new BufferOptions(allocation == AllocationPolicy.Fixed ? 129 : 0, 129, allocation, policy, 2, 64);
            using (var actual = new DeterministicList<int>(options))
            {
                var initial = new List<int>();
                for (int i = 0; i < 73; i++) { initial.Add(i * 104729); actual.Add(initial[i]); }
                actual.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    var expected = new List<int>(initial); uint random = 0x714c973d;
                    for (int step = 0; step < 512; step++)
                    {
                        random = unchecked(random * 1664525u + 1013904223u);
                        if (step % 3 != 0 && expected.Count < 129)
                        {
                            int index = (int)(random % (uint)(expected.Count + 1)), value = unchecked((int)random);
                            actual.Insert(index, value); expected.Insert(index, value);
                        }
                        else
                        {
                            int index = (int)(random % (uint)expected.Count);
                            actual.RemoveAt(index); expected.RemoveAt(index);
                        }
                        CollectionAssert.AreEqual(expected, actual, "step " + step);
                        if (step % 53 == 52)
                        {
                            actual.TryRestore(snapshot).ThrowIfFailure(); expected = new List<int>(initial);
                            CollectionAssert.AreEqual(expected, actual);
                        }
                    }
                    for (int i = 0; i < initial.Count; i++)
                    { snapshot.TryGet(i, out int value).ThrowIfFailure(); Assert.AreEqual(initial[i], value); }
                }
                finally { snapshot.Dispose(); }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.CopyOnWrite, AllocationPolicy.Fixed)]
        [TestCase(SnapshotPolicy.FullPerTick, AllocationPolicy.Grow)]
        [TestCase(SnapshotPolicy.KeyframesAndDeltas, AllocationPolicy.Grow)]
        [TestCase(SnapshotPolicy.CopyOnWrite, AllocationPolicy.Grow)]
        public void SortedMovesPreserveOrderValuesFailuresAndRollback(SnapshotPolicy policy, AllocationPolicy allocation)
        {
            var options = new BufferOptions(allocation == AllocationPolicy.Fixed ? 129 : 0, 129, allocation, policy, 2, 64);
            using (var actual = new DeterministicSortedList<int, int, Int32Policy>(options))
            {
                var expected = new SortedDictionary<int, int>();
                for (int i = 128; i >= 0; i--) { actual.Add(i * 2, -i); expected.Add(i * 2, -i); }
                actual.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    for (int step = 0; step < 387; step++)
                    {
                        int index = step % 129, parity = step / 129 % 2, oldKey = index * 2 + parity, nextKey = index * 2 + 1 - parity;
                        Assert.IsTrue(actual.Remove(oldKey)); expected.Remove(oldKey);
                        actual.Add(nextKey, step); expected.Add(nextKey, step);
                        int rank = 0;
                        foreach (var pair in expected)
                        { actual.TryGetAt(rank++, out var value).ThrowIfFailure(); Assert.AreEqual(pair, value); }
                    }
                    actual.TryRestore(snapshot).ThrowIfFailure();
                    ulong revision = actual.Revision;
                    Assert.AreEqual(DeterministicStatus.DuplicateKey, actual.TryAdd(0, 999));
                    Assert.AreEqual(DeterministicStatus.CapacityExceeded, actual.TryAdd(int.MaxValue, 999));
                    Assert.AreEqual(revision, actual.Revision);
                    for (int i = 0; i < 129; i++)
                    { actual.TryGetAt(i, out var pair).ThrowIfFailure(); Assert.AreEqual(i * 2, pair.Key); Assert.AreEqual(-i, pair.Value); }
                }
                finally { snapshot.Dispose(); }
            }
        }

        private sealed class ReservationTrace { internal int PrepareCalls, Writes; }
        private struct RejectingBuffer : IDeterministicBuffer<int>
        {
            internal int[] Values; internal ReservationTrace Trace;
            public int Capacity => Values.Length;
            public ulong Revision => 0;
            public bool IsCreated => true;
            public int Read(int index) => Values[index];
            public void Write(int index, in int value) { Trace.Writes++; Values[index] = value; }
            public DeterministicStatus CanPrepareWrite(int capacity, int start, int count)
                => start == Values.Length - 1 ? DeterministicStatus.SnapshotCapacityExceeded : DeterministicStatus.None;
            public DeterministicStatus PrepareWrite(int capacity, int start, int count) { Trace.PrepareCalls++; return DeterministicStatus.None; }
            public void CommitWrite() { }
        }
        [Test]
        public void SwapBackPreflightsBothDestinationsBeforePreparingOrWriting()
        {
            var trace = new ReservationTrace(); var values = new[] { 10, 20, 30 };
            var buffer = new RejectingBuffer { Values = values, Trace = trace }; var state = new SequenceState { Count = 3 };
            Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded, SequenceKernel<int, RejectingBuffer>.TryRemoveAt(ref buffer, ref state, 0, true));
            Assert.AreEqual(0, trace.PrepareCalls); Assert.AreEqual(0, trace.Writes); Assert.AreEqual(3, state.Count);
            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, values);
        }

        [TestCase(0)] [TestCase(15)] [TestCase(16)] [TestCase(31)] [TestCase(63)]
        public void SwapBackClonesOnlyWrittenPagesAndPreservesSnapshot(int index)
        {
            using (var list = new DeterministicList<int>(new BufferOptions(64, snapshots: SnapshotPolicy.CopyOnWrite, snapshotSlots: 1, pageBytes: 64)))
            {
                for (int i = 0; i < 64; i++) list.Add(i);
                list.TryCapture(out var snapshot).ThrowIfFailure();
                try
                {
                    var field = list.Storage.Owner.GetType().GetField("pages", BindingFlags.Instance | BindingFlags.NonPublic);
                    var before = (Array)((Array)field.GetValue(list.Storage.Owner)).Clone();
                    list.RemoveAtSwapBack(index); var after = (Array)field.GetValue(list.Storage.Owner);
                    for (int page = 0; page < 4; page++)
                        Assert.AreEqual(page != index / 16 && page != 3, ReferenceEquals(before.GetValue(page), after.GetValue(page)), "page " + page);
                    Assert.AreEqual(63, list.Count);
                    for (int i = 0; i < 64; i++)
                    { snapshot.TryGet(i, out int value).ThrowIfFailure(); Assert.AreEqual(i, value); }
                    list.TryRestore(snapshot).ThrowIfFailure(); CollectionAssert.AreEqual(Range(64), list);
                }
                finally { snapshot.Dispose(); }
            }
        }

        [TestCase(SnapshotPolicy.FullPerTick)] [TestCase(SnapshotPolicy.KeyframesAndDeltas)]
        public void GrowSnapshotsAllocateOccupiedSlotsAndSurviveExpansionAndStaleLease(SnapshotPolicy policy)
        {
            using (var list = new DeterministicList<int>(new BufferOptions(0, 1048576, AllocationPolicy.Grow, policy, 2)))
            {
                var owner = list.Storage.Owner;
                var field = owner.GetType().GetField("snapshotValues", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.AreEqual(0, PayloadBytes((Array)field.GetValue(owner)));
                Assert.AreEqual(0, owner.MemoryStats.ReservedPayloadBytes);
                list.TryCapture(out var empty).ThrowIfFailure();
                for (int i = 0; i < 37; i++) list.Add(i);
                list.TryCapture(out var retained).ThrowIfFailure(); int capacity = list.Capacity;
                Assert.AreEqual(capacity * sizeof(int), PayloadBytes((Array)field.GetValue(owner)));
                Assert.AreEqual(capacity * sizeof(int) * 2L, owner.MemoryStats.ReservedPayloadBytes);
                owner.TryReserveSnapshotSlots(4).ThrowIfFailure();
                Assert.AreEqual(capacity * sizeof(int), PayloadBytes((Array)field.GetValue(owner)));
                Assert.AreEqual(capacity * sizeof(int) * 2L, owner.MemoryStats.ReservedPayloadBytes);
                list.TryRestore(empty).ThrowIfFailure(); Assert.AreEqual(0, list.Count); Assert.AreEqual(0, list.Capacity);
                empty.Dispose(); list.Add(999); list.TryCapture(out var replacement).ThrowIfFailure();
                empty.Dispose(); Assert.IsTrue(replacement.IsCreated);
                for (int i = 1; i < 129; i++) list.Add(-i);
                list.TryRestore(retained).ThrowIfFailure(); CollectionAssert.AreEqual(Range(37), list);
                retained.Dispose(); replacement.Dispose();
            }
        }
        private static long PayloadBytes(Array snapshots)
        { long bytes = 0; foreach (Array values in snapshots) if (values != null) bytes += values.LongLength * sizeof(int); return bytes; }
        private static int[] Range(int count) { var values = new int[count]; for (int i = 0; i < count; i++) values[i] = i; return values; }

        private struct CountingHash : IDeterministicEquality<int>
        {
            internal static int Calls; internal bool Collisions;
            public bool IsValid => true;
            public bool Equals(in int a, in int b) => a == b;
            public ulong Hash(in int value) { Calls++; return Collisions ? 0 : default(Int32Policy).Hash(value); }
        }
        [TestCase(false)] [TestCase(true)]
        public void EachDictionaryOperationHashesOnceIncludingGrowthReplacementAndMisses(bool collisions)
        {
            using (var dictionary = new DeterministicDictionary<int, int, CountingHash>(
                new BufferOptions(0, 129, AllocationPolicy.Grow, snapshotSlots: 1), new CountingHash { Collisions = collisions }))
            {
                CountingHash.Calls = 0;
                for (int i = 0; i < 129; i++) dictionary.Add(i, i * 3);
                Assert.AreEqual(129, CountingHash.Calls);
                for (int i = 0; i < 129; i++) dictionary[i] = -i;
                Assert.AreEqual(258, CountingHash.Calls);
                for (int i = 0; i < 129; i++) { Assert.AreEqual(-i, dictionary[i]); Assert.IsFalse(dictionary.TryGetValue(i + 129, out _)); }
                Assert.AreEqual(516, CountingHash.Calls);
                for (int i = 0; i < 129; i++) Assert.IsTrue(dictionary.Remove(i));
                Assert.AreEqual(645, CountingHash.Calls); Assert.AreEqual(0, dictionary.Count);
            }
        }
    }
}

