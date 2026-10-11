using System;
using System.Globalization;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedVectorReferenceTests
    {
        [Test] public void VersionedInputGenerationIsStableAndIncludesEveryPrecisionAndArithmeticMode()
        {
            ulong hash = 14695981039346656037UL; int sequences = 0;
            var samples = FixedVectorReplayCorpus.Create(); var coverage = new bool[19, 3, 2];
            foreach (var sample in samples)
            {
                hash = FixedVectorReplayCorpus.Mix(hash, FixedVectorReplayCorpus.HashInput(sample));
                coverage[sample.Settings.DecimalPlaces64, (int)sample.Settings.ArithmeticMode, sample.Dimensions - 2] = true;
                if (sample.Operation == 23) sequences++;
            }
            TestContext.Progress.WriteLine("FIXED_VECTOR_INPUTS checksum=" + hash + " sequences=" + sequences);
            Assert.AreEqual(FixedVectorReplayCorpus.ExpectedInputChecksum, hash);
            Assert.AreEqual(FixedVectorReplayCorpus.SequenceCases, sequences);
            foreach (bool covered in coverage) Assert.IsTrue(covered);
        }
        [Test] public void SequentialSnapshotsRestartExactlyFromSavedPositionVelocityAndTick()
        {
            foreach (var sample in FixedVectorReplayCorpus.Create())
            {
                if (sample.Operation != 23) continue;
                FixedVectorBits position = sample.A, velocity = default, restoredPosition = default, restoredVelocity = default;
                for (int tick = 0; tick < FixedVectorReplayCorpus.SequenceTicks; tick++)
                {
                    if (tick == 16) { restoredPosition = position; restoredVelocity = velocity; }
                    var result = FixedVectorReplayCorpus.SequenceStep(sample, tick, ref position, ref velocity);
                    Assert.IsTrue(result.Status.IsSuccess()); Assert.IsTrue(result.WriteStatus.IsSuccess()); Assert.IsTrue(result.ReadStatus.IsSuccess());
                    if (tick < 16) continue;
                    var restored = FixedVectorReplayCorpus.SequenceStep(sample, tick, ref restoredPosition, ref restoredVelocity);
                    Assert.AreEqual(result, restored, "Restored tick " + tick + "/p=" + sample.Settings.DecimalPlaces64);
                }
            }
        }
        [Test] public void VectorMathMatchesIndependent160DigitReferencesInBothProfiles()
        {
            string[] lines = FixedVectorMathReference.Cases.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (FixedMathProfile profile in Enum.GetValues(typeof(FixedMathProfile)))
            foreach (string line in lines)
            {
                string[] f = line.Split(','); var values = new long[f.Length];
                for (int i = 0; i < f.Length; i++) values[i] = long.Parse(f[i], CultureInfo.InvariantCulture);
                var settings = new FixedSettings(3, (int)values[0], mathProfile: profile);
                var sample = new FixedVectorReplayCorpus.Sample {
                    Settings = settings, Dimensions = (int)values[1], Operation = (int)values[2],
                    A = new FixedVectorBits(unchecked((ulong)values[3]), unchecked((ulong)values[4]), unchecked((ulong)values[5])),
                    B = new FixedVectorBits(unchecked((ulong)values[6]), unchecked((ulong)values[7]), unchecked((ulong)values[8])), Amount = (ulong)values[9]
                };
                var result = FixedVectorReplayCorpus.Evaluate(sample); var format = new FixedFormat(FixedKind.Signed64, new FixedRuntimeState(settings));
                int budget = profile == FixedMathProfile.Accurate ? 1 : 4;
                for (int i = 0; i < 3; i++)
                {
                    Compare(result.Vector.Get(i), values[10 + i], format, budget, line + " vector " + i + " " + profile);
                    Compare(result.Velocity.Get(i), values[13 + i], format, budget, line + " velocity " + i + " " + profile);
                }
                Compare(result.Scalar, values[16], format, budget, line + " scalar " + profile);
            }
        }
        private static void Compare(ulong actual, long expected, FixedFormat format, int budget, string context)
        {
            FixedDecoded a = format.Decode(actual), e = format.Decode(unchecked((ulong)expected));
            if (!e.IsFinite) { Assert.AreEqual(e.Class, a.Class, context); return; }
            Assert.IsTrue(a.IsFinite, context);
            System.Numerics.BigInteger av = a.Negative ? -(System.Numerics.BigInteger)a.Magnitude : a.Magnitude;
            System.Numerics.BigInteger ev = e.Negative ? -(System.Numerics.BigInteger)e.Magnitude : e.Magnitude;
            Assert.LessOrEqual(System.Numerics.BigInteger.Abs(av - ev), new System.Numerics.BigInteger(budget), context);
        }
        [Test] public unsafe void VectorKernelsAndPointerCodecsAllocateNoManagedMemoryAfterWarmup()
        {
            var samples = FixedVectorReplayCorpus.Create();
            for (int i = 0; i < 100; i++) FixedVectorReplayCorpus.Evaluate(samples[i]);
            long before = GC.GetAllocatedBytesForCurrentThread(); ulong checksum = 0;
            for (int i = 0; i < samples.Length; i++) checksum ^= FixedVectorReplayCorpus.Hash(FixedVectorReplayCorpus.Evaluate(samples[i]));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated); GC.KeepAlive(checksum);
        }
        [Test] public void VersionedVectorReplayProducesAStableCrossRuntimeChecksum()
        {
            ulong checksum = 14695981039346656037UL;
            var samples = FixedVectorReplayCorpus.Create();
            for (int i = 0; i < samples.Length; i++) checksum = FixedVectorReplayCorpus.Mix(checksum, FixedVectorReplayCorpus.Hash(FixedVectorReplayCorpus.Evaluate(samples[i])));
            TestContext.Progress.WriteLine("FIXED_VECTOR_REPLAY version=" + FixedVectorReplayCorpus.Version + " cases=" + samples.Length + " checksum=" + checksum);
            Assert.AreEqual(FixedVectorReplayCorpus.ExpectedCases, samples.Length);
            Assert.AreEqual(FixedVectorReplayCorpus.ExpectedChecksum, checksum);
        }
    }
}
