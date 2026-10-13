using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedBoundsBurstTests
    {
        [OneTimeSetUp] public void Initialize()
        { FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [BurstCompile(CompileSynchronously = true)]
        private struct ParityJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] internal NativeArray<FixedBoundsReplayCorpus.Result> Results;
            internal int Reverse;
            public void Execute(int index)
            {
                if (Reverse != 0) index = Results.Length - 1 - index;
                var result = FixedBoundsReplayCorpus.Evaluate(index); MarkManaged(ref result.Managed); Results[index] = result;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(1)] [TestCase(17)]
        public void EveryRepresentationAndNumericConfigurationMatchesNativeRegardlessOfScheduling(int batchSize)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using var managed = new NativeArray<FixedBoundsReplayCorpus.Result>(FixedBoundsReplayCorpus.Count, Allocator.Persistent);
            using var native = new NativeArray<FixedBoundsReplayCorpus.Result>(FixedBoundsReplayCorpus.Count, Allocator.Persistent);
            var reference = new ParityJob { Results = managed }; for (int i = 0; i < managed.Length; i++) reference.Execute(i);
            new ParityJob { Results = native, Reverse = 1 }.Schedule(native.Length, batchSize).Complete();
            ulong checksum = 14695981039346656037UL;
            for (int i = 0; i < native.Length; i++)
            { Assert.AreEqual(1, managed[i].Managed); Assert.AreEqual(0, native[i].Managed); Assert.AreEqual(managed[i].Hash, native[i].Hash, "Case " + i); checksum = FixedBoundsReplayCorpus.Mix(checksum, native[i].Hash); }
            Assert.AreEqual(FixedBoundsReplayCorpus.ExpectedChecksum, checksum);
        }
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct PublicJob : IJob
        {
            internal NativeArray<ulong> Result;
            public void Execute()
            {
                var box = FixedBounds3.FromMinMax(FixedVector3.FromRaw(-11, -21, -31), FixedVector3.FromRaw(17, 25, 39));
                box.TrySetCenter(box.center, out var status); box.TryExpand(Fixed64.FromRaw(3), out status);
                box.TryEncapsulate(FixedVector3.FromRaw(40, 50, 60), out status); box.TrySetExtents(FixedVector3.FromRaw(40, 50, 60), out status);
                var point = box.ClosestPoint(FixedVector3.one); var d = box.SqrDistance(FixedVector3.one, out status);
                box.TryIntersectRay(new FixedRay3(FixedVector3.zero, FixedVector3.right), out bool hit, out var t, out status);
                byte* bytes = stackalloc byte[60]; var profile = new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(new FixedNetworkProfile(FixedRuntime.Settings, FixedKind.Signed64), 3));
                var writer = new FixedBufferWriter(bytes, 60); bool written = FixedBoundsNetwork.TryWrite(ref writer, box, profile, out status);
                var reader = new FixedBufferReader(bytes, writer.BytesWritten); bool read = FixedBoundsNetwork.TryRead(ref reader, profile, out FixedBounds3 restored, out status);
                var flat = FixedBounds2.FromCenterExtents(FixedVector2.zero, FixedVector2.one); flat.TrySetSize(new FixedVector2(2, 4), out status);
                flat.Encapsulate(new FixedVector2(3, 4)); flat.IntersectRay(new FixedRay2(FixedVector2.zero, FixedVector2.right), out var t2);
                Result[0] = unchecked((ulong)box.GetHashCode()); Result[1] = unchecked((ulong)flat.GetHashCode()); Result[2] = (ulong)d.RawValue;
                Result[3] = (ulong)t.RawValue; Result[4] = (ulong)t2.RawValue; Result[5] = (ulong)point.x.RawValue;
                Result[6] = written && read && restored == box ? 1UL : 0UL; Result[7] = hit ? 1UL : 0UL;
                int managed = 0; MarkManaged(ref managed); Result[8] = (ulong)managed;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [Test] public void PublicFacadeReadsFrozenSharedStaticAndPreservesNativeLayout()
        {
            Assert.AreEqual(32, UnsafeUtility.SizeOf<FixedBounds2>()); Assert.AreEqual(48, UnsafeUtility.SizeOf<FixedBounds3>()); Assert.IsTrue(BurstCompiler.IsEnabled);
            using var managed = new NativeArray<ulong>(9, Allocator.TempJob); using var native = new NativeArray<ulong>(9, Allocator.TempJob);
            new PublicJob { Result = managed }.Execute(); new PublicJob { Result = native }.Schedule().Complete();
            Assert.AreEqual(1, managed[8]); Assert.AreEqual(0, native[8]); for (int i = 0; i < 8; i++) Assert.AreEqual(managed[i], native[i]); Assert.AreEqual(1, native[6]);
        }
        [BurstCompile(CompileSynchronously = true)]
        private unsafe struct StressJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] internal NativeArray<FixedBoundsStressCorpus.Result> Results;
            [NativeDisableParallelForRestriction] internal NativeArray<FixedBoundsStressCorpus.Record> Trace;
            internal int Permutation;
            public void Execute(int index)
            {
                if (Permutation == 1) index = Results.Length - 1 - index;
                else if (Permutation == 2) index = (index * 17 + 23) % Results.Length; // gcd(17, 960) = 1.
                var records = (FixedBoundsStressCorpus.Record*)NativeArrayUnsafeUtility.GetUnsafePtr(Trace) + index * FixedBoundsStressCorpus.Steps;
                var result = FixedBoundsStressCorpus.Evaluate(index, records); MarkManaged(ref result.Managed); Results[index] = result;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(1, 0)] [TestCase(17, 1)] [TestCase(64, 2)]
        public unsafe void EverySequenceRawStatusCursorAndPacketMatchesNativeAtEveryStep(int batchSize, int permutation)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using var managed = new NativeArray<FixedBoundsStressCorpus.Result>(FixedBoundsStressCorpus.Count, Allocator.Persistent);
            using var native = new NativeArray<FixedBoundsStressCorpus.Result>(managed.Length, Allocator.Persistent);
            using var managedTrace = new NativeArray<FixedBoundsStressCorpus.Record>(managed.Length * FixedBoundsStressCorpus.Steps, Allocator.Persistent);
            using var nativeTrace = new NativeArray<FixedBoundsStressCorpus.Record>(managedTrace.Length, Allocator.Persistent);
            var reference = new StressJob { Results = managed, Trace = managedTrace }; for (int i = 0; i < managed.Length; i++) reference.Execute(i);
            new StressJob { Results = native, Trace = nativeTrace, Permutation = permutation }.Schedule(native.Length, batchSize).Complete();
            ulong checksum = 14695981039346656037UL;
            var a = (FixedBoundsStressCorpus.Record*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(managedTrace);
            var b = (FixedBoundsStressCorpus.Record*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(nativeTrace);
            for (int i = 0; i < native.Length; i++)
            {
                Assert.AreEqual(1, managed[i].Managed); Assert.AreEqual(0, native[i].Managed); Assert.AreEqual(0, managed[i].Errors, "Managed sequence " + i); Assert.AreEqual(0, native[i].Errors, "Native sequence " + i);
                Assert.AreEqual(32, managed[i].Replayed); Assert.AreEqual(managed[i].Replayed, native[i].Replayed); Assert.AreEqual(managed[i].Hash, native[i].Hash, "Sequence " + i);
                for (int tick = 0; tick < FixedBoundsStressCorpus.Steps; tick++)
                {
                    int at = i * FixedBoundsStressCorpus.Steps + tick;
                    if (!FixedBoundsStressCorpus.Equal(a + at, b + at)) Assert.Fail("Direct trace mismatch: sequence " + i + ", step " + tick);
                }
                checksum = FixedBoundsReplayCorpus.Mix(checksum, native[i].Hash);
            }
            Assert.AreEqual(FixedBoundsStressCorpus.ExpectedChecksum, checksum);
        }
    }
}
