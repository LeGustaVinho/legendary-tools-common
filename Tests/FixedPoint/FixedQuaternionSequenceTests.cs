using System;
using System.IO;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedQuaternionSequenceTests
    {
        [Test]
        public void SeededDependentChainsMatchEveryArithmeticBackendAndTheirVersionedTrace()
        {
            var samples = FixedQuaternionSequenceCorpus.Create();
            Assert.AreEqual(FixedQuaternionSequenceCorpus.ExpectedSequences, samples.Length);
            string trace = Environment.GetEnvironmentVariable("FIXED_QUATERNION_SEQUENCE_TRACE");
            using (TextWriter writer = string.IsNullOrEmpty(trace) ? null : new StreamWriter(trace))
            {
                if (writer != null) writer.NewLine = "\n";
                ulong checksum = 14695981039346656037UL;
                int index = 0;
                for (int sequence = 0; sequence < samples.Length; sequence += 3)
                {
                    var a = FixedQuaternionSequenceCorpus.Initialize(samples[sequence]);
                    var b = FixedQuaternionSequenceCorpus.Initialize(samples[sequence + 1]);
                    var c = FixedQuaternionSequenceCorpus.Initialize(samples[sequence + 2]);
                    var results = new FixedQuaternionSequenceCorpus.Result[3, FixedQuaternionSequenceCorpus.Steps];
                    for (int tick = 0; tick < FixedQuaternionSequenceCorpus.Steps; tick++)
                    {
                        var first = FixedQuaternionSequenceCorpus.Advance(samples[sequence], ref a);
                        var portable = FixedQuaternionSequenceCorpus.Advance(samples[sequence + 1], ref b);
                        var intrinsic = FixedQuaternionSequenceCorpus.Advance(samples[sequence + 2], ref c);
                        Assert.AreEqual(first, portable, $"portable sequence={sequence} tick={tick}");
                        Assert.AreEqual(first, intrinsic, $"intrinsic sequence={sequence} tick={tick}");
                        results[0, tick] = first; results[1, tick] = portable; results[2, tick] = intrinsic;
                    }
                    for (int backend = 0; backend < 3; backend++)
                    for (int tick = 0; tick < FixedQuaternionSequenceCorpus.Steps; tick++)
                    {
                        checksum = FixedQuaternionReplayCorpus.Mix(checksum, FixedQuaternionSequenceCorpus.Hash(results[backend, tick]));
                        if (writer != null) FixedQuaternionSequenceCorpus.WriteTraceRow(writer, index, results[backend, tick]);
                        index++;
                    }
                }
                Assert.AreEqual(FixedQuaternionSequenceCorpus.ExpectedCases, index);
                TestContext.Progress.WriteLine($"FIXED_QUATERNION_SEQUENCE version={FixedQuaternionSequenceCorpus.Version} cases={index} checksum={checksum}");
                Assert.AreEqual(FixedQuaternionSequenceCorpus.ExpectedChecksum, checksum);
            }
        }

        [Test]
        public void RawCheckpointAndRandomStateResumeEveryFollowingTickExactly()
        {
            byte* bytes = stackalloc byte[32];
            foreach (var sample in FixedQuaternionSequenceCorpus.Create())
            {
                var original = FixedQuaternionSequenceCorpus.Initialize(sample);
                for (int tick = 0; tick < 16; tick++) FixedQuaternionSequenceCorpus.Advance(sample, ref original);
                var state = new FixedRuntimeState(sample.Settings);
                var profile = new FixedQuaternionNetworkProfile(sample.Settings);
                var writer = new FixedBufferWriter(bytes, 32);
                Assert.IsTrue(FixedQuaternionNetworkKernel.Write(ref writer, original.Quaternion, profile, state, out _));
                var reader = new FixedBufferReader(bytes, 32);
                Assert.IsTrue(FixedQuaternionNetworkKernel.Read(ref reader, profile, state, out var restoredQuaternion, out _));
                var restored = new FixedQuaternionSequenceCorpus.State {
                    Quaternion = restoredQuaternion, Random = original.Random, Hash = original.Hash, Tick = original.Tick };
                for (int tick = 16; tick < FixedQuaternionSequenceCorpus.Steps; tick++)
                    Assert.AreEqual(FixedQuaternionSequenceCorpus.Advance(sample, ref original), FixedQuaternionSequenceCorpus.Advance(sample, ref restored), $"p={sample.Settings.DecimalPlaces64} tick={tick}");
            }
        }
    }
}
