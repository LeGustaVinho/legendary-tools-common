using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedRandomReplayStressTests
    {
        [OneTimeSetUp] public void Configure() => FixedRuntime.Initialize();
        private static IEnumerable<int> Cases() { for (int i = 0; i < FixedRandomReplayStress.Cases; i++) yield return i; }
        [TestCaseSource(nameof(Cases))]
        public void LongMixedReplayPreservesRawStatusesWirePositionsAndFrozenHash(int index)
        {
            var first = FixedRandomReplayStress.Evaluate(index); var replay = FixedRandomReplayStress.Evaluate(index);
            Assert.AreEqual(0, first.Failures, "Invariant failures at " + index);
            Assert.LessOrEqual(first.Count, FixedRandomReplayStress.Result.Capacity);
            Assert.IsTrue(first.Same(ref replay)); Assert.AreEqual(FixedRandomReplayStress.ExpectedCases[index], first.Digest());
        }
        [Test]
        public void FullLongReplayChecksumMatchesItsSeparateFrozenContract()
        {
            ulong checksum = 14695981039346656037UL;
            for (int i = 0; i < FixedRandomReplayStress.Cases; i++) checksum = FixedRandomReplayStress.Evaluate(i).Digest(checksum);
            Assert.AreEqual(FixedRandomReplayStress.ExpectedChecksum, checksum);
            Assert.AreEqual(16080504445963702515UL, FixedRandomCorpus.ExpectedChecksum, "Original v1 fixture must remain unchanged.");
        }
    }
}
