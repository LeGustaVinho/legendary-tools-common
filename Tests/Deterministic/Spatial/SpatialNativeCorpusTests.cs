#if !FIXEDPOINT_STANDALONE
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public class SpatialNativeCorpusTests
    {
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        [Test] public void NativePublicCorpusMatchesEveryManagedWordAndTheStandaloneGolden()
        { TestContext.Progress.WriteLine(SpatialPlayerValidation.VerifyInEditor()); }
    }
}
#endif
