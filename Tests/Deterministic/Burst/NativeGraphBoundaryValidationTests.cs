using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeGraphBoundaryValidationTests
    {
        public static IEnumerable<TestCaseData> Profiles()
        {
            foreach (GraphDirection direction in Enum.GetValues(typeof(GraphDirection)))
            foreach (GraphCyclePolicy cycles in Enum.GetValues(typeof(GraphCyclePolicy)))
            foreach (SnapshotPolicy snapshots in Enum.GetValues(typeof(SnapshotPolicy)))
            foreach (AllocationPolicy allocation in Enum.GetValues(typeof(AllocationPolicy)))
                yield return new TestCaseData(direction, cycles, snapshots, allocation);
        }
        [TestCaseSource(nameof(Profiles))]
        public void WidePayloadRetirementSnapshotsDeltasAndCsrMatchManagedInsideBurst(GraphDirection direction, GraphCyclePolicy cycles, SnapshotPolicy snapshots, AllocationPolicy allocation)
        {
            var options = new GraphOptions(new BufferOptions(allocation == AllocationPolicy.Fixed ? 8 : 1, 8, allocation, snapshots, 2, 64),
                new BufferOptions(allocation == AllocationPolicy.Fixed ? 16 : 1, 16, allocation, snapshots, 2, 64), direction, cycles, true, true);
            int alignment = ((int)direction * 4 + (int)cycles * 2 + (int)snapshots + (int)allocation) & 7;
            var report = GraphBoundaryCorpus.Run(options, alignment);
            Assert.AreEqual(30, report.Operations); Assert.Greater(report.BytesCompared, 0);
            TestContext.WriteLine($"boundary hash={report.Hash:x16}; alignment={alignment}; bytes={report.BytesCompared}");
        }
    }
}
