using System;
using System.IO;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedQuaternionReplayTests
    {
        [Test] public void VersionedCorpusCoversEveryPrecisionArithmeticMathNormalizationAndRounding()
        {
            var samples = FixedQuaternionReplayCorpus.Create(); Assert.AreEqual(FixedQuaternionReplayCorpus.ExpectedCases, samples.Length);
            string trace = Environment.GetEnvironmentVariable("FIXED_QUATERNION_TRACE");
            using (TextWriter writer = string.IsNullOrEmpty(trace) ? null : new StreamWriter(trace))
            {
                if (writer != null) writer.NewLine="\n";
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < samples.Length; i++) { var result = FixedQuaternionReplayCorpus.Evaluate(samples[i]); hash = FixedQuaternionReplayCorpus.Mix(hash, FixedQuaternionReplayCorpus.Hash(result)); if (writer != null) FixedQuaternionReplayCorpus.WriteTraceRow(writer, i, result); }
                TestContext.Progress.WriteLine("FIXED_QUATERNION_REPLAY version=" + FixedQuaternionReplayCorpus.Version + " cases=" + samples.Length + " checksum=" + hash);
                Assert.AreEqual(FixedQuaternionReplayCorpus.ExpectedChecksum, hash);
            }
        }
    }
}
