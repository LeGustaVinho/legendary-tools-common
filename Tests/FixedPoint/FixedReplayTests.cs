using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
#endif

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedReplayTests
    {
        [Test]
        public void MixedReplayIsIdenticalInPortableIntrinsicAndAutoArithmetic()
        {
            ulong checksum = 14695981039346656037UL;
            ulong inputChecksum = checksum;
            for (int i = 0; i < FixedReplayCorpus.Count; i++)
            {
                var sample = FixedReplayCorpus.Create(i);
                inputChecksum = FixedReplayCorpus.DigestInput(sample, inputChecksum);
                var portable = FixedReplayCorpus.Evaluate(sample);
                var intrinsic = FixedReplayCorpus.Evaluate(FixedReplayCorpus.Create(i, FixedArithmeticMode.Intrinsic));
                var automatic = FixedReplayCorpus.Evaluate(FixedReplayCorpus.Create(i, FixedArithmeticMode.Auto));
                Assert.AreEqual(94, portable.Count);
                Assert.IsTrue(portable.Same(ref intrinsic), "Portable/Intrinsic sample " + i);
                Assert.IsTrue(portable.Same(ref automatic), "Portable/Auto sample " + i);
                checksum = portable.Digest(checksum);
            }
            TestContext.Progress.WriteLine("FIXED_REPLAY version=" + FixedReplayCorpus.Version + " cases=" + FixedReplayCorpus.Count + " inputs=" + inputChecksum + " checksum=" + checksum);
            Assert.AreEqual(FixedReplayCorpus.ExpectedInputChecksum, inputChecksum, "Replay input generation differs across runtimes.");
            Assert.AreEqual(FixedReplayCorpus.ExpectedChecksum, checksum, "Versioned replay output changed.");
        }
#if !FIXEDPOINT_STANDALONE
        [BurstCompile(CompileSynchronously = true)]
        private struct ReplayJob : IJob
        {
            [ReadOnly] internal NativeArray<FixedReplayCorpus.Sample> Samples;
            internal NativeArray<FixedReplayCorpus.Result> Results;
            public void Execute()
            {
                for (int i = 0; i < Samples.Length; i++)
                {
                    var result = FixedReplayCorpus.Evaluate(Samples[i]);
                    MarkManaged(ref result.Managed); Results[i] = result;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int flag) => flag = 1;
        }
        [Test]
        public void MixedReplayMatchesNativeBurstInEveryResultFieldAndWireByte()
        {
            Assert.IsTrue(BurstCompiler.IsEnabled);
            using (var samples = new NativeArray<FixedReplayCorpus.Sample>(FixedReplayCorpus.Count, Allocator.TempJob))
            using (var managed = new NativeArray<FixedReplayCorpus.Result>(FixedReplayCorpus.Count, Allocator.TempJob))
            using (var native = new NativeArray<FixedReplayCorpus.Result>(FixedReplayCorpus.Count, Allocator.TempJob))
            {
                var writable = samples;
                ulong inputChecksum = 14695981039346656037UL;
                for (int i = 0; i < samples.Length; i++)
                {
                    var sample = FixedReplayCorpus.Create(i, (FixedArithmeticMode)(i % 3));
                    writable[i] = sample; inputChecksum = FixedReplayCorpus.DigestInput(sample, inputChecksum);
                }
                Assert.AreEqual(FixedReplayCorpus.ExpectedInputChecksum, inputChecksum);
                new ReplayJob { Samples = samples, Results = managed }.Execute();
                new ReplayJob { Samples = samples, Results = native }.Schedule().Complete();
                ulong checksum = 14695981039346656037UL;
                for (int i = 0; i < samples.Length; i++)
                {
                    var a = managed[i]; var b = native[i];
                    Assert.AreEqual(1, a.Managed); Assert.AreEqual(0, b.Managed, "Managed fallback sample " + i);
                    Assert.IsTrue(a.Same(ref b), "Managed/native replay sample " + i);
                    checksum = b.Digest(checksum);
                }
                Assert.AreEqual(FixedReplayCorpus.ExpectedChecksum, checksum);
                TestContext.Progress.WriteLine("FIXED_REPLAY_BURST cases=" + samples.Length + " checksum=" + checksum);
            }
        }
#endif
    }
}
