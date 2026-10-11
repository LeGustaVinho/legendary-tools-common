using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedRandomTests
    {
        [OneTimeSetUp] public void Configure() => FixedRuntime.Initialize();

        [Test]
        public void OfficialPhiloxKnownAnswersAndPublicWordOrder()
        {
            Check(0, 0, 0, 0x6627e8d5U, 0xe169c58dU, 0xbc57ac4cU, 0x9b00dbd8U);
            Check(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, 0x408f276dU, 0x41c83b0eU, 0xa20bc7c6U, 0x6d5451fdU);
            Check(0x299f31d0a4093822UL, 0x0370734413198a2eUL, 0x85a308d3243f6a88UL, 0xd16cfe09U, 0x94fdccebU, 0x5001e420U, 0x24126ea1U);
            var random = FixedRandom.Create(0);
            Assert.AreEqual(0xe169c58d6627e8d5UL, random.NextUInt64());
            Assert.AreEqual(0x9b00dbd8bc57ac4cUL, random.NextUInt64());
            Assert.AreEqual(4UL, random.Position);
            Assert.AreEqual(FixedRandomState.Bytes, Marshal.SizeOf<FixedRandomState>());
            Assert.AreEqual(16, Marshal.SizeOf<FixedPointVector2>());
            Assert.AreEqual(24, Marshal.SizeOf<FixedPointVector3>());
        }
        private static void Check(ulong key, ulong sequence, ulong block, uint a, uint b, uint c, uint d)
        {
            FixedRandomKernel.Block(key, sequence, block, out uint x, out uint y, out uint z, out uint w);
            Assert.AreEqual(a, x); Assert.AreEqual(b, y); Assert.AreEqual(c, z); Assert.AreEqual(d, w);
        }

        [Test]
        public void DefaultSnapshotsCopiesSeekingAndExhaustionAreExplicit()
        {
            var zero = default(FixedRandom); var seeded = FixedRandom.Create(0);
            Assert.AreEqual(zero.NextUInt64(), seeded.NextUInt64());
            var random = FixedRandom.Create(123, 456);
            for (ulong position = 0; position < 9; position++)
            {
                random.Seek(position); var saved = random.CaptureState(); var restored = default(FixedRandom); restored.RestoreState(saved);
                var copy = random;
                for (int i = 0; i < 12; i++) { Assert.AreEqual(random.NextUInt64(), restored.NextUInt64()); Assert.AreEqual(random.CaptureState(), restored.CaptureState()); }
                copy.AdvanceChecked(24); Assert.AreEqual(random.CaptureState(), copy.CaptureState());
            }
            random.Seek(ulong.MaxValue - 1); var before = random.CaptureState();
            Assert.IsFalse(random.TryNextUInt64(out _, out FixedStatus status)); Assert.AreEqual(FixedStatus.OutOfRange, status); Assert.AreEqual(before, random.CaptureState());
            Assert.IsFalse(random.TryAdvance(2, out _)); Assert.AreEqual(before, random.CaptureState());
            Assert.IsTrue(random.TryNextUInt32(out _, out _)); Assert.IsTrue(random.IsExhausted);
            Assert.IsFalse(random.TryNextUInt32(out _, out _));
            before = random.CaptureState();
            Assert.IsFalse(random.TryPickIndex(2, out int index, out status));
            Assert.AreEqual(-1, index); Assert.AreEqual(FixedStatus.OutOfRange, status);
            Assert.AreEqual(before, random.CaptureState());
        }

        [Test]
        public void EventsAreReproducibleRegardlessOfOrderAndStreamsAreSeparated()
        {
            ulong[] outcomes = new ulong[80];
            for (int i = 0; i < outcomes.Length; i++) { var r = FixedRandom.ForEvent(987, 42, (ulong)i); outcomes[i] = r.NextUInt64(); }
            for (int i = outcomes.Length - 1; i >= 0; i--) { var r = FixedRandom.ForEvent(987, 42, (ulong)i); Assert.AreEqual(outcomes[i], r.NextUInt64()); }
            var a = FixedRandom.Create(987, 42); var b = FixedRandom.Create(987, 43); var c = FixedRandom.ForEvent(987, 42, 0);
            Assert.AreNotEqual(a.CaptureState().Key, b.CaptureState().Key); Assert.AreNotEqual(a.CaptureState().Key, c.CaptureState().Key);
            Assert.AreNotEqual(outcomes[0], outcomes[1]);
        }

        [Test]
        public void RejectionAndFullWidthRangesMatchIndependentKnownSamples()
        {
            var r = FixedRandom.Create(0);
            Assert.AreEqual(0x6169c58cU, r.Range(0U, 0x80000001U)); Assert.AreEqual(2UL, r.Position);
            var baseline = FixedRandom.Create(83); r = baseline;
            ulong word = baseline.NextUInt64(); Assert.AreEqual(word, r.RangeInclusive(0UL, ulong.MaxValue));
            r = FixedRandom.Create(83); var raw = r; uint u = raw.NextUInt32();
            Assert.AreEqual(unchecked(int.MinValue + (int)u), r.RangeInclusive(int.MinValue, int.MaxValue));
            for (int i = 0; i < 4000; i++)
            {
                Assert.That(r.Range(long.MinValue, long.MaxValue), Is.LessThan(long.MaxValue));
                Assert.That(r.Range(ulong.MaxValue - 17, ulong.MaxValue), Is.InRange(ulong.MaxValue - 17, ulong.MaxValue - 1));
            }
        }

        [TestCase(0, 0)] [TestCase(3, 3)] [TestCase(9, 9)] [TestCase(9, 18)]
        public void AllFixedFamiliesUseRawGridAndNeverGenerateReservedCodes(int p32, int p64)
        {
            var state = new FixedRuntimeState(new FixedSettings(p32, p64));
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
            {
                var f = new FixedFormat(kind, state); var random = FixedRandom.Create((ulong)kind + 17);
                for (int i = 0; i < 300; i++)
                {
                    Assert.IsTrue(FixedRandomKernel.Unit(ref random, kind, state, out ulong unit, out var status)); Assert.AreEqual(FixedStatus.None, status);
                    Assert.Less(unit, f.Scale);
                    Assert.IsTrue(FixedRandomKernel.Range(ref random, f.MinValue, f.MaxPositive, kind, state, true, out ulong raw, out status));
                    Assert.IsTrue(f.Decode(raw).IsFinite); Assert.AreNotEqual(f.NegativeZero, raw);
                    var decoded = f.Decode(raw);
                    BigInteger numeric = decoded.Negative ? -(BigInteger)decoded.Magnitude : decoded.Magnitude;
                    Assert.IsTrue(numeric >= -(BigInteger)f.MaxNegative && numeric <= f.MaxPositive);
                }
            }
        }

        [Test]
        public void InvalidArgumentsAndConstantResultsDoNotConsumeState()
        {
            var r = FixedRandom.Create(100); var before = r.CaptureState();
            Assert.IsFalse(r.TryRange(5, 4, out _, out _)); Assert.IsFalse(r.TryRange(Fixed64.NaN, Fixed64.One, out _, out _));
            Assert.IsFalse(r.TryChance(Fixed64.PositiveInfinity, out _, out _)); Assert.IsFalse(r.TryChanceRatio(2, 1, out _, out _));
            Assert.IsFalse(r.TryRollDice(2, int.MaxValue, 0, out _, out _)); Assert.IsFalse(r.TryPickIndex(0, out _, out _));
            Assert.AreEqual(before, r.CaptureState());
            Assert.AreEqual(8, r.Range(8, 8));
            Assert.IsFalse(r.Chance(Fixed32.Zero)); Assert.IsTrue(r.Chance(UFixed64.One)); Assert.AreEqual(1, r.RollDie(1));
            Assert.AreEqual(int.MaxValue, r.RollDice(int.MaxValue, 1));
            Assert.AreEqual(-10, r.RollDice(0, 6, -10));
            Assert.IsTrue(r.Advance(0));
            Assert.AreEqual(before, r.CaptureState());
            var noRuntime = default(FixedRuntimeState); var copy = r;
            Assert.IsFalse(FixedRandomKernel.Unit(ref copy, FixedKind.Signed64, noRuntime, out _, out var status)); Assert.AreEqual(FixedStatus.Uninitialized, status);
        }

        [Test]
        public void ShuffleSamplingAndManagedReferenceChoicesRespectTheirContracts()
        {
            var r = FixedRandom.Create(777); int[] values = new int[100]; for (int i = 0; i < values.Length; i++) values[i] = i;
            r.Shuffle(values.AsSpan()); var shuffled = (int[])values.Clone(); Array.Sort(values); for (int i = 0; i < values.Length; i++) Assert.AreEqual(i, values[i]);
            Assert.AreNotEqual(values, shuffled);
            int[] output = new int[20], scratch = new int[100]; r.SampleWithoutReplacement(100, output.AsSpan(), scratch.AsSpan());
            Array.Sort(output); for (int i = 0; i < output.Length; i++) { Assert.That(output[i], Is.InRange(0, 99)); if (i > 0) Assert.AreNotEqual(output[i - 1], output[i]); }
            string[] names = { "loot", "spawn", "critical" }; Assert.Contains(r.Choose<string>(names.AsSpan()), names);
            r.Seek(ulong.MaxValue - 1); var before = r.CaptureState(); int[] original = { 1, 2, 3, 4 };
            Assert.IsFalse(r.TryShuffle(original.AsSpan(), out _)); CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, original); Assert.AreEqual(before, r.CaptureState());
            Assert.IsFalse(r.TrySampleWithoutReplacement(4, original.AsSpan(), original.AsSpan(), out _));
        }

        [Test]
        public void WeightedTablesMatchLinearSelectionAndZeroWeightsAreExcluded()
        {
            Fixed64[] weights = { Fixed64.Zero, Fixed64.One, Fixed64.FromInt(3), Fixed64.Zero }; ulong[] prefix = new ulong[4];
            Assert.IsTrue(FixedRandomWeights.TryBuild(weights.AsSpan(), prefix.AsSpan(), out FixedRandomWeightView table, out _));
            var linear = FixedRandom.Create(1); var indexed = linear; int ones = 0;
            for (int i = 0; i < 20000; i++)
            {
                int a = linear.WeightedIndex(weights.AsSpan()), b = indexed.WeightedIndex(table);
                Assert.AreEqual(a, b); Assert.That(a, Is.InRange(1, 2)); if (a == 1) ones++;
            }
            Assert.That(ones, Is.InRange(4600, 5400)); Assert.AreEqual(linear.CaptureState(), indexed.CaptureState());
            Fixed64[] bad = { Fixed64.One, Fixed64.FromInt(-1) }; Array.Fill(prefix, 99UL); var before = linear.CaptureState();
            Assert.IsFalse(FixedRandomWeights.TryBuild(bad.AsSpan(), prefix.AsSpan(), out _, out _)); Assert.AreEqual(99UL, prefix[0]);
            Assert.IsFalse(linear.TryWeightedIndex(bad.AsSpan(), out _, out _)); Assert.AreEqual(before, linear.CaptureState());
            UFixed64[] overflow = { UFixed64.MaxValue, UFixed64.MaxValue };
            Assert.IsFalse(FixedRandomWeights.TryBuild(overflow.AsSpan(), prefix.AsSpan(), out _, out var status)); Assert.AreEqual(FixedStatus.Overflow, status);
        }

        [Test]
        public void ChanceAndDiceHaveDeterministicReasonableFrequencies()
        {
            var r = FixedRandom.Create(99); int hits = 0; int[] dice = new int[6];
            for (int i = 0; i < 20000; i++) { if (r.Chance(Fixed64.FromRatio(1, 4))) hits++; dice[r.RollDie(6) - 1]++; }
            Assert.That(hits, Is.InRange(4600, 5400)); foreach (int count in dice) Assert.That(count, Is.InRange(2900, 3800));
            for (int i = 0; i < 100; i++) Assert.That(r.RollDice(3, 6, -2), Is.InRange(1, 16));
        }

        [Test]
        public void ByteAndScalarBatchesEqualRepeatedScalarCalls()
        {
            var a = FixedRandom.Create(31); var b = a; byte[] bytes = new byte[39];
            a.FillBytes(bytes.AsSpan()); for (int i = 0; i < bytes.Length; i++) Assert.AreEqual((byte)b.NextUInt32(), bytes[i]); Assert.AreEqual(a.CaptureState(), b.CaptureState());
            Fixed64[] values = new Fixed64[39]; a.Fill(values.AsSpan()); for (int i = 0; i < values.Length; i++) Assert.AreEqual(b.NextFixed64(), values[i]);
            Assert.AreEqual(a.CaptureState(), b.CaptureState());
            a.Seek(ulong.MaxValue - 1); Array.Fill(values, Fixed64.One); var before = a.CaptureState();
            Assert.IsFalse(a.TryFill(values.AsSpan(), out _)); foreach (var v in values) Assert.AreEqual(Fixed64.One, v); Assert.AreEqual(before, a.CaptureState());
        }
    }
}
