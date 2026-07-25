using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Random;
using NUnit.Framework;

namespace LegendaryTools.Tests.Deterministic
{
    public sealed class DeterministicRngTests
    {
        [Test]
        public void NextIntInclusive_SupportsEntireIntDomain()
        {
            DeterministicRng rng = new DeterministicRng(123UL);

            Assert.DoesNotThrow(() => rng.NextIntInclusive(int.MinValue, int.MaxValue));
        }

        [Test]
        public void NextInt_SupportsRangesWhoseDifferenceExceedsIntMaxValue()
        {
            DeterministicRng rng = new DeterministicRng(456UL);

            for (int i = 0; i < 32; i++)
            {
                int value = rng.NextInt(int.MinValue, int.MaxValue);
                Assert.That(value, Is.GreaterThanOrEqualTo(int.MinValue));
                Assert.That(value, Is.LessThan(int.MaxValue));
            }
        }

        [Test]
        public void DefaultInstance_ThrowsInsteadOfProducingDegenerateZeros()
        {
            DeterministicRng rng = default;

            Assert.Throws<InvalidOperationException>(() => rng.NextUInt());
            Assert.Throws<InvalidOperationException>(() => rng.Advance(0UL));
            Assert.Throws<InvalidOperationException>(() => rng.Seed(1UL));
        }

        [Test]
        public void RefShuffle_AdvancesOriginalStructState()
        {
            DeterministicRng rng = new DeterministicRng(789UL);
            RngState before = rng.State;
            var values = new List<int> { 1, 2, 3, 4, 5 };

            rng.Shuffle(values);

            Assert.AreNotEqual(before, rng.State);
        }

        [Test]
        public void RefPick_AdvancesOriginalStructState()
        {
            DeterministicRng rng = new DeterministicRng(999UL);
            RngState before = rng.State;
            IReadOnlyList<int> values = new[] { 1, 2, 3 };

            _ = rng.Pick(values);

            Assert.AreNotEqual(before, rng.State);
        }

        [Test]
        public void IntegerWeightedPick_AdvancesOriginalStructState()
        {
            DeterministicRng rng = new DeterministicRng(1001UL);
            RngState before = rng.State;
            IReadOnlyList<ulong> weights = new ulong[] { 1UL, 2UL, 3UL };

            int index = rng.PickWeightedIndex(weights);

            Assert.That(index, Is.InRange(0, 2));
            Assert.AreNotEqual(before, rng.State);
        }

        [Test]
        public void IntegerWeightedPick_RejectsOverflowingWeightSum()
        {
            DeterministicRng rng = new DeterministicRng(1002UL);
            IReadOnlyList<ulong> weights = new ulong[] { ulong.MaxValue, 1UL };

            Assert.Throws<OverflowException>(() => rng.PickWeightedIndex(weights));
        }

        [Test]
        public void NextFloat_NeverReturnsExclusiveAdjacentUpperBound()
        {
            DeterministicRng rng = new DeterministicRng(1003UL);
            const float min = 1f;
            const float adjacentMax = 1.00000011920928955078125f;

            for (int i = 0; i < 128; i++)
                Assert.AreEqual(min, rng.NextFloat(min, adjacentMax));
        }

        [Test]
        public void NextFloat_RejectsNonFiniteBounds()
        {
            DeterministicRng rng = new DeterministicRng(1004UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextFloat(float.NegativeInfinity, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextFloat(0f, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextFloat(float.NaN, 1f));
        }
    }
}
