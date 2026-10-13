using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedQuaternionBurstTests
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct CorpusJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<FixedQuaternionReplayCorpus.Sample> Samples;
            internal NativeArray<FixedQuaternionReplayCorpus.Result> Results;
            public void Execute(int index) { var result = FixedQuaternionReplayCorpus.Evaluate(Samples[index]); MarkManaged(ref result.Managed); Results[index] = result; }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(1)] [TestCase(17)]
        public void NativeBurstMatchesAllRawComponentsStatusesDescriptorsAndNetworkBytes(int batch)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var samples = new NativeArray<FixedQuaternionReplayCorpus.Sample>(FixedQuaternionReplayCorpus.Create(), Allocator.TempJob))
            using (var managed = new NativeArray<FixedQuaternionReplayCorpus.Result>(samples.Length, Allocator.TempJob))
            using (var native = new NativeArray<FixedQuaternionReplayCorpus.Result>(samples.Length, Allocator.TempJob))
            {
                var reference = new CorpusJob { Samples = samples, Results = managed }; for (int i = 0; i < samples.Length; i++) reference.Execute(i);
                new CorpusJob { Samples = samples, Results = native }.Schedule(samples.Length, batch).Complete();
                for (int i = 0; i < samples.Length; i++) { var a = managed[i]; var b = native[i]; Assert.AreEqual(1, a.Managed); Assert.AreEqual(0, b.Managed); a.Managed = 0; Assert.AreEqual(a, b, "sample " + i); }
            }
        }
        private struct PublicResult { internal FixedQuaternion Q; internal FixedVector3 Vector; internal Fixed64 Angle; internal FixedStatus Status; internal int Managed; }
        [BurstCompile(CompileSynchronously = true)]
        private struct PublicApiJob : IJob
        {
            internal NativeArray<PublicResult> Results;
            public void Execute()
            {
                var q = FixedQuaternion.Euler(10,20,30);
                var r = FixedQuaternion.LookRotation(FixedVector3.right, FixedVector3.up);
                q = FixedQuaternion.Slerp(q, r, Fixed64.FromRatio(1, 3));
                var result = new PublicResult { Q = (q * FixedQuaternion.Inverse(q)).normalized, Vector = q * FixedVector3.forward, Angle = FixedQuaternion.Angle(q, r, out var status), Status = status };
                MarkManaged(ref result.Managed); Results[0] = result;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [BurstCompile(CompileSynchronously = true)]
        private struct SequenceJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<FixedQuaternionSequenceCorpus.Sample> Samples;
            [NativeDisableParallelForRestriction] internal NativeArray<FixedQuaternionSequenceCorpus.Result> Results;
            internal bool Reverse;
            public void Execute(int index)
            {
                int sequence = Reverse ? Samples.Length - 1 - index : index;
                var sample = Samples[sequence]; var state = FixedQuaternionSequenceCorpus.Initialize(sample);
                for (int tick = 0; tick < FixedQuaternionSequenceCorpus.Steps; tick++)
                {
                    var result = FixedQuaternionSequenceCorpus.Advance(sample, ref state);
                    MarkManaged(ref result.Managed);
                    Results[sequence * FixedQuaternionSequenceCorpus.Steps + tick] = result;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value = 1;
        }
        [TestCase(1, false)] [TestCase(17, true)]
        public void DependentStateAndWireFeedbackMatchNativeBurstInBothSchedulingOrders(int batch, bool reverse)
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var samples = new NativeArray<FixedQuaternionSequenceCorpus.Sample>(FixedQuaternionSequenceCorpus.Create(), Allocator.Persistent))
            using (var managed = new NativeArray<FixedQuaternionSequenceCorpus.Result>(FixedQuaternionSequenceCorpus.ExpectedCases, Allocator.Persistent))
            using (var native = new NativeArray<FixedQuaternionSequenceCorpus.Result>(FixedQuaternionSequenceCorpus.ExpectedCases, Allocator.Persistent))
            {
                var reference = new SequenceJob { Samples = samples, Results = managed };
                for (int i = 0; i < samples.Length; i++) reference.Execute(i);
                new SequenceJob { Samples = samples, Results = native, Reverse = reverse }.Schedule(samples.Length, batch).Complete();
                ulong checksum = 14695981039346656037UL;
                for (int i = 0; i < native.Length; i++)
                {
                    var a = managed[i]; var b = native[i];
                    Assert.AreEqual(1, a.Managed); Assert.AreEqual(0, b.Managed); a.Managed = 0;
                    Assert.AreEqual(a, b, "dependent step " + i);
                    checksum = FixedQuaternionReplayCorpus.Mix(checksum, FixedQuaternionSequenceCorpus.Hash(b));
                }
                Assert.AreEqual(FixedQuaternionSequenceCorpus.ExpectedChecksum, checksum);
            }
        }
        [Test] public void PublicFacadeUsesTheFrozenRuntimeInNativeJobs()
        {
            FixedRuntime.Initialize(); Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var a = new NativeArray<PublicResult>(1, Allocator.TempJob)) using (var b = new NativeArray<PublicResult>(1, Allocator.TempJob))
            { new PublicApiJob { Results = a }.Execute(); new PublicApiJob { Results = b }.Schedule().Complete(); var reference = a[0]; var native = b[0]; Assert.AreEqual(1, reference.Managed); Assert.AreEqual(0, native.Managed); reference.Managed = 0; Assert.AreEqual(reference, native); }
        }
    }
}
