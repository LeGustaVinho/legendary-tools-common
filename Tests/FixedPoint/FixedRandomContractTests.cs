using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedRandomContractTests
    {
        [OneTimeSetUp] public void Configure() => FixedRuntime.Initialize();
        private static IEnumerable<int> Cases() { for (int i = 0; i < FixedRandomCorpus.Count; i++) yield return i; }
        [TestCaseSource(nameof(Cases))]
        public void V1PublicApiRawStatusesStateAndWireMatchFrozenContract(int index)
        {
            var result = FixedRandomCorpus.Evaluate(FixedRandomCorpus.Create(index));
            Assert.LessOrEqual(result.Count, FixedRandomCorpus.Result.Capacity);
            Assert.AreEqual(FixedRandomCorpus.ExpectedCases[index], result.Digest());
        }
        [Test]
        public void CompleteCorpusChecksumIsVersioned()
        {
            ulong checksum = 14695981039346656037UL;
            for (int i = 0; i < FixedRandomCorpus.Count; i++) checksum = FixedRandomCorpus.Evaluate(FixedRandomCorpus.Create(i)).Digest(checksum);
            Assert.AreEqual(1, FixedRandomCorpus.Version); Assert.AreEqual(FixedRandomCorpus.ExpectedChecksum, checksum);
        }
        [Test]
        public unsafe void NumericSpatialCollectionAndPointerCodecKernelsAllocateNoManagedMemoryAfterWarmup()
        {
            var random = FixedRandom.Create(12); byte* bytes = stackalloc byte[64]; int* values = stackalloc int[4];
            Fixed64* weights = stackalloc Fixed64[3]; ulong* prefix = stackalloc ulong[3];
            weights[0] = Fixed64.Zero; weights[1] = Fixed64.FromRaw(1); weights[2] = Fixed64.FromRaw(3);
            var profile = new FixedRandomNetworkProfile(FixedRuntime.Settings);
            FixedRandomWeights.TryBuild(weights, 3, prefix, 3, out var table, out _);
            for (int i = 0; i < 30; i++) Exercise(ref random, bytes, values, table, profile);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Exercise(ref random, bytes, values, table, profile);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
        }
        private static unsafe void Exercise(ref FixedRandom random, byte* bytes, int* values, FixedRandomWeightedTable table, FixedRandomNetworkProfile profile)
        {
            random.NextFixed32(); random.NextUFixed32(); random.NextFixed64(); random.NextUFixed64(); random.OnUnitSphere(); random.InsideUnitCircle();
            random.TryWeightedIndex(table, out _, out _); random.TryShuffle(values, 4, out _); random.TryFillBytes(bytes, 8, out _);
            var writer = new FixedBufferWriter(bytes, 64); FixedRandomNetwork.TryWriteState(ref writer, random.CaptureState(), profile, out _);
            var reader = new FixedBufferReader(bytes, 64); FixedRandomNetwork.TryReadState(ref reader, profile, out _, out _);
        }
    }
}
