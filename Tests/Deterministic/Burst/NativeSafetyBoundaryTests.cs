using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeSafetyBoundaryTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SearchJob : IJob
        {
            internal NativeDeterministicList<float> List;
            internal NativeArray<int> Results;
            public void Execute()
            {
                int managed = 0; MarkManaged(ref managed); Results[0] = managed;
                Results[1] = (int)List.TryIndexOf(1f, default(PrimitiveEquality<float>), out int index); Results[2] = index;
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void NativeSearchReportsUnsupportedPolicyWithoutManagedFallback()
        {
            using (var list = new NativeDeterministicList<float>(4, Allocator.Persistent)) using (var results = new NativeArray<int>(3, Allocator.TempJob))
            {
                new SearchJob { List = list, Results = results }.Schedule().Complete(); CollectionAssert.AreEqual(new[] { 0, (int)DeterministicStatus.UnsupportedType, -1 }, results.ToArray());
            }
        }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void BorrowedSnapshotChecksReleasedOwnerBeforeDereferencingStorage()
        {
            var list = new NativeDeterministicList<int>(4, Allocator.Persistent); list.Add(7); list.TryCapture(out var snapshot).ThrowIfFailure(); list.Dispose();
            Assert.That(() => { _ = snapshot.IsCreated; }, Throws.InstanceOf<InvalidOperationException>());
            Assert.That(() => snapshot.TryGet(0, out _), Throws.InstanceOf<InvalidOperationException>());
        }
#endif
        [Test]
        public void RingBufferRejectsGrowPolicyInBothBackends()
        {
            var options = new BufferOptions(1, 3, AllocationPolicy.Grow);
            Assert.Throws<ArgumentException>(() => new NativeDeterministicRingBuffer<int>(options, Allocator.Persistent));
            Assert.Throws<ArgumentException>(() => new DeterministicRingBuffer<int>(options));
        }
    }
}
