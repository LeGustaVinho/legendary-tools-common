using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed class NativeGraphDeterminismValidationTests
    {
        public static IEnumerable<TestCaseData> Profiles()
        {
            foreach (GraphDirection direction in Enum.GetValues(typeof(GraphDirection)))
            foreach (GraphCyclePolicy cycles in Enum.GetValues(typeof(GraphCyclePolicy)))
            foreach (SnapshotPolicy snapshots in Enum.GetValues(typeof(SnapshotPolicy)))
            foreach (AllocationPolicy allocation in Enum.GetValues(typeof(AllocationPolicy)))
            foreach (uint seed in new[] { 0x13579bdfU, 0xdeadbeefU })
                yield return new TestCaseData(direction, cycles, snapshots, allocation, seed);
        }
        [TestCaseSource(nameof(Profiles))]
        public void ConcurrentBurstPeersMatchEveryManagedByteAndReplayAfterRollback(GraphDirection direction, GraphCyclePolicy cycles,
            SnapshotPolicy snapshots, AllocationPolicy allocation, uint seed)
        {
            var options = new GraphOptions(new BufferOptions(allocation == AllocationPolicy.Fixed ? 24 : 1, 24, allocation, snapshots, 2, 64),
                new BufferOptions(allocation == AllocationPolicy.Fixed ? 48 : 0, 48, allocation, snapshots, 2, 64), direction, cycles, true, true);
            var report = GraphDeterminismCorpus.Run(options, seed, 1024, (seed == 0xdeadbeefU) ^ (direction == GraphDirection.Undirected));
            Assert.AreEqual(1024, report.Operations); Assert.AreEqual(2048, report.ReplayedOperations);
            Assert.Greater(report.BytesCompared, 0); Assert.Greater(report.AlgorithmComparisons, 0);
            TestContext.WriteLine($"seed={seed:x8}; hash={report.Hash:x16}; bytes={report.BytesCompared}; successful={report.SuccessfulMutations}; rejected={report.RejectedMutations}");
        }
    }
}
