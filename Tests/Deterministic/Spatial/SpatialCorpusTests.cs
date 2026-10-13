using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialCorpusTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static ulong Run<TBounds, TAccess, TCodec>() where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsAccess<TBounds> where TCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            var trace = new ulong[SpatialCorpus.Capacity]; ulong checksum = 1; long total = 0;
            fixed (ulong* data = trace) for (int scenario = 0; scenario < SpatialCorpus.Cases / 2; scenario++)
            {
                using var index = new DeterministicSpatialIndex<int, int, TBounds, TAccess, Int32Policy>(SpatialCorpus.Options(scenario));
                var owner = new SpatialManagedCorpusOwner<TBounds, TAccess, TCodec> { Index = index };
                SpatialCorpus.Evaluate<TBounds, TAccess, SpatialManagedCorpusOwner<TBounds, TAccess, TCodec>>(ref owner, scenario / 6, data, trace.Length, out int words, out int replayed).ThrowIfFailure();
                Assert.AreEqual(SpatialCorpus.Steps - SpatialCorpus.Checkpoint - 1, replayed); total += words;
                checksum = DeterministicHash.Mix(checksum ^ SpatialCorpus.Hash(data, words));
            }
            TestContext.Progress.WriteLine($"Spatial corpus: mode={FixedBoundsRuntime.Settings.Representation} dimensions={default(TAccess).Dimensions} checksum={checksum} words={total}");
            if (FixedRuntime.Settings.NumericEquals(FixedSettings.Default)) Assert.AreEqual(SpatialCorpus.ExpectedChecksum(default(TAccess).Dimensions), checksum);
            return checksum;
        }
        [Test] public void PublicCorpusRestoresEveryRecordedObservationAndReportsItsVersionedDigest()
        {
            ulong a = Run<FixedBounds2, SpatialBounds2Access, FixedBounds2Policy>();
            ulong b = Run<FixedBounds3, SpatialBounds3Access, FixedBounds3Policy>();
            TestContext.Progress.WriteLine("Spatial corpus combined checksum=" + DeterministicHash.Mix(a ^ b));
        }
    }
}
