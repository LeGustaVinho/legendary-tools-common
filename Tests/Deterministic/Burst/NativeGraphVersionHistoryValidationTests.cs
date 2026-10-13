using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeGraphVersionHistoryValidationTests
    {
        public static IEnumerable<TestCaseData> Profiles()
        {
            foreach (GraphDirection direction in Enum.GetValues(typeof(GraphDirection)))
            foreach (GraphVersionRetention retention in Enum.GetValues(typeof(GraphVersionRetention)))
            foreach (AllocationPolicy allocation in Enum.GetValues(typeof(AllocationPolicy)))
                yield return new TestCaseData(direction, retention, allocation);
        }
        [TestCaseSource(nameof(Profiles))]
        public void LongPersistentHistorySurvivesFailedBatchesAndLeaseReuseInsideBurst(GraphDirection direction, GraphVersionRetention retention, AllocationPolicy allocation)
        {
            var result = GraphVersionHistoryCorpus.Run(direction, retention, allocation);
            Assert.AreEqual(192, result.Branches); Assert.AreEqual(96, result.RejectedBatches);
            TestContext.WriteLine($"history hash={result.Hash:x16}; branches={result.Branches}; rejected batches={result.RejectedBatches}");
        }
    }
}
