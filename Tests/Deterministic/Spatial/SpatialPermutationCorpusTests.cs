using System;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialPermutationCorpusTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [TestCase(2)] [TestCase(3)]
        public void DifferentMutationOrdersAndReplayCompareEveryObservationWord(int dimensions)
        {
            ulong checksum = dimensions == 2 ? Run<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>() : Run<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>();
            TestContext.Progress.WriteLine($"Spatial permutation corpus: mode={FixedBoundsRuntime.Settings.Representation} dimensions={dimensions} checksum={checksum}");
            if (FixedRuntime.Settings.NumericEquals(FixedSettings.Default)) Assert.AreEqual(SpatialPermutationCorpus.ExpectedChecksum(dimensions), checksum);
        }
        private static ulong Run<TBounds, TAccess, TCodec>() where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds>
            where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            ulong hash = 1;
            for (int scenario = 0; scenario < SpatialCorpus.Cases / 2; scenario++)
            {
                ulong[] reference = null; int length = 0;
                for (int permutation = 0; permutation < 3; permutation++)
                {
                    using var index = new DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(SpatialCorpus.Options(scenario));
                    var owner = new SpatialManagedCorpusOwner<TBounds, TAccess, TCodec> { Index = index };
                    var words = new ulong[SpatialPermutationCorpus.Capacity];
                    fixed (ulong* output = words)
                    {
                        SpatialPermutationCorpus.Evaluate<TBounds, TAccess, SpatialManagedCorpusOwner<TBounds, TAccess, TCodec>>(ref owner, scenario / 6, permutation, output, words.Length, out int count, out int replayed).ThrowIfFailure();
                        Assert.AreEqual(16, replayed);
                        if (permutation == 0) { reference = words; length = count; hash = DeterministicHash.Mix(hash ^ SpatialCorpus.Hash(output, count)); }
                        else { Assert.AreEqual(length, count); for (int i = 0; i < count; i++) Assert.AreEqual(reference[i], words[i], $"scenario={scenario} permutation={permutation} word={i}"); }
                    }
                }
            }
            return hash;
        }
    }
}
