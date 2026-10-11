using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedRandomRegressionTests
    {
        [OneTimeSetUp] public void Configure() => FixedRuntime.Initialize();

        // BigInteger is deliberately independent of the runtime's fixed-width multiplication and Mix64.
        private static readonly BigInteger Mask32 = (BigInteger.One << 32) - 1;
        private static readonly BigInteger Mask64 = (BigInteger.One << 64) - 1;
        private static ulong Mix(ulong input)
        {
            BigInteger value = input;
            value = ((value ^ (value >> 30)) * BigInteger.Parse("13787848793156543929")) & Mask64;
            value = ((value ^ (value >> 27)) * BigInteger.Parse("10723151780598845931")) & Mask64;
            return (ulong)(value ^ (value >> 31));
        }
        private static uint[] ReferenceBlock(ulong key, ulong sequence, ulong block)
        {
            BigInteger[] counter = { block & uint.MaxValue, block >> 32, sequence & uint.MaxValue, sequence >> 32 };
            BigInteger lo = key & uint.MaxValue, hi = key >> 32;
            for (int round = 0; round < 10; round++)
            {
                BigInteger left = counter[0] * BigInteger.Parse("3528531795");
                BigInteger right = counter[2] * BigInteger.Parse("3449720151");
                counter = new[] { (right >> 32) ^ counter[1] ^ lo, right & Mask32, (left >> 32) ^ counter[3] ^ hi, left & Mask32 };
                lo = (lo + 2654435769UL) & Mask32; hi = (hi + 3144134277UL) & Mask32;
            }
            return new[] { (uint)counter[0], (uint)counter[1], (uint)counter[2], (uint)counter[3] };
        }
        private static uint ReferenceWord(FixedRandomState state, ref ulong position)
        {
            uint value = ReferenceBlock(state.Key, state.SequenceId, position >> 2)[position & 3];
            position++; return value;
        }
        private static ulong ReferenceBounded(FixedRandomState state, ref ulong position, BigInteger bound)
        {
            if (bound == 1) return 0;
            int bits = bound <= uint.MaxValue ? 32 : 64;
            BigInteger threshold = (BigInteger.One << bits) % bound;
            while (true)
            {
                BigInteger sample = ReferenceWord(state, ref position);
                if (bits == 64) sample |= (BigInteger)ReferenceWord(state, ref position) << 32;
                if (sample >= threshold) return (ulong)(sample % bound);
            }
        }
        private static ulong Bits(BigInteger number, int bits) => (ulong)(number & ((BigInteger.One << bits) - 1));

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        [TestCase(8)] [TestCase(9)] [TestCase(10)] [TestCase(11)]
        [TestCase(12)] [TestCase(13)] [TestCase(14)] [TestCase(15)]
        public void SeedDerivationAndHighCountersMatchIndependentBigIntegerPhilox(int index)
        {
            ulong seed = index == 0 ? 0 : index == 15 ? ulong.MaxValue : unchecked((ulong)index * 0x9e3779b97f4a7c15UL);
            ulong stream = index == 0 ? 0 : (1UL << (index * 4)) ^ 0x8000000000000000UL;
            ulong sequence = (index & 1) == 0 ? 0 : ulong.MaxValue - (ulong)index;
            var random = (index & 1) == 0 ? FixedRandom.Create(seed, stream) : FixedRandom.ForEvent(seed, stream, sequence);
            ulong key = Mix(seed ^ Mix(stream) ^ ((index & 1) == 0 ? 0UL : 1UL));
            Assert.AreEqual(new FixedRandomState(key, sequence, 0), random.CaptureState());
            for (int i = 0; i < 128; i++)
            {
                ulong block = i == 0 ? 0 : i == 1 ? 1 : i == 2 ? (ulong.MaxValue >> 2) - 1 :
                    unchecked((ulong)i * 0xd1342543de82ef95UL) & ((ulong.MaxValue >> 2) - 1);
                uint[] expected = ReferenceBlock(key, sequence, block);
                random.Seek(block << 2);
                Assert.AreEqual(expected[0] | (ulong)expected[1] << 32, random.NextUInt64());
                Assert.AreEqual(expected[2], random.NextUInt32()); Assert.AreEqual(expected[3], random.NextUInt32());
                Assert.AreEqual((block << 2) + 4, random.Position);
            }
        }

        private static IEnumerable<TestCaseData> RawRanges()
        {
            foreach (FixedKind kind in Enum.GetValues(typeof(FixedKind)))
                foreach (int precision in new[] { 0, 3, 9, 18 })
                    foreach (bool inclusive in new[] { false, true })
                        yield return new TestCaseData(kind, precision, inclusive);
        }
        [TestCaseSource(nameof(RawRanges))]
        public void FixedRawRangesAndUnitsMatchIndependentUnbiasedReduction(FixedKind kind, int precision, bool inclusive)
        {
            int bits = kind < FixedKind.Signed64 ? 32 : 64;
            bool signed = kind == FixedKind.Signed32 || kind == FixedKind.Signed64;
            BigInteger min = signed ? -(BigInteger.One << (bits - 1)) + 2 : BigInteger.Zero;
            BigInteger max = (BigInteger.One << (signed ? bits - 1 : bits)) - (signed ? 3 : 5);
            var settings = new FixedSettings(Math.Min(precision, 9), precision, rounding: (FixedRounding)(precision % 5));
            var state = new FixedRuntimeState(settings);
            var random = FixedRandom.ForEvent(ulong.MaxValue, (ulong)kind + 1, 1UL << 63);
            var identity = random.CaptureState(); ulong cursor = 0;
            BigInteger width = max - min + (inclusive ? 1 : 0);
            for (int i = 0; i < 96; i++)
            {
                ulong expected = Bits(min + ReferenceBounded(identity, ref cursor, width), bits);
                Assert.IsTrue(FixedRandomKernel.Range(ref random, Bits(min, bits), Bits(max, bits), kind, state, inclusive, out ulong raw, out var status));
                Assert.AreEqual(FixedStatus.None, status); Assert.AreEqual(expected, raw); Assert.AreEqual(cursor, random.Position);
                int p = bits == 32 ? settings.DecimalPlaces32 : settings.DecimalPlaces64;
                expected = ReferenceBounded(identity, ref cursor, BigInteger.Pow(10, p));
                Assert.IsTrue(FixedRandomKernel.Unit(ref random, kind, state, out raw, out status));
                Assert.AreEqual(expected, raw); Assert.AreEqual(FixedStatus.None, status); Assert.AreEqual(cursor, random.Position);
            }
        }

        [Test]
        public void IntegerBoundsAroundWordWidthTransitionsMatchReferenceConsumption()
        {
            ulong[] bounds = { 1, 2, 3, 127, 128, 129, 0x80000001UL, uint.MaxValue, (ulong)uint.MaxValue + 1, (ulong)uint.MaxValue + 2, 1UL << 63, (1UL << 63) + 1, ulong.MaxValue };
            foreach (ulong bound in bounds)
            {
                var random = FixedRandom.Create(69, 1UL << 63); var identity = random.CaptureState(); ulong cursor = 0;
                for (int i = 0; i < 64; i++)
                {
                    ulong expected = ReferenceBounded(identity, ref cursor, bound);
                    Assert.AreEqual(expected, random.Range(0UL, bound)); Assert.AreEqual(cursor, random.Position);
                }
            }
        }

        [Test]
        public void ForcedRejectionAtTheLastWordRollsBackAndDoesNotLeakAnIndex()
        {
            bool exercised = false;
            for (ulong seed = 0; seed < 256 && !exercised; seed++)
            {
                var random = FixedRandom.Create(seed); var identity = random.CaptureState(); ulong cursor = ulong.MaxValue - 1;
                uint word = ReferenceWord(identity, ref cursor);
                if (word >= 0x7fffffffU) continue;
                random.Seek(ulong.MaxValue - 1); var before = random.CaptureState();
                Assert.IsFalse(random.TryRange(0U, 0x80000001U, out _, out var status));
                Assert.AreEqual(FixedStatus.OutOfRange, status); Assert.AreEqual(before, random.CaptureState());
                Assert.IsTrue(random.TryNextUInt32(out uint actual, out status)); Assert.AreEqual(word, actual);
                Assert.IsFalse(random.TryPickIndex(2, out int index, out status)); Assert.AreEqual(-1, index);
                exercised = true;
            }
            Assert.IsTrue(exercised, "The test must actually exercise rejection, not just exhaustion.");
        }

        [TestCase(FixedKind.Signed32)] [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)] [TestCase(FixedKind.Unsigned64)]
        public void FixedBatchesAtExhaustionLeaveEveryDestinationElementUntouched(FixedKind kind)
        {
            Fixed32* a = stackalloc Fixed32[4]; UFixed32* b = stackalloc UFixed32[4];
            Fixed64* c = stackalloc Fixed64[4]; UFixed64* d = stackalloc UFixed64[4];
            for (ulong remaining = 0; remaining < 3; remaining++)
            {
                var random = FixedRandom.Create(23); random.Seek(ulong.MaxValue - remaining); var before = random.CaptureState();
                for (int i = 0; i < 4; i++) { a[i] = Fixed32.FromRaw(777); b[i] = UFixed32.FromRaw(777); c[i] = Fixed64.FromRaw(777); d[i] = UFixed64.FromRaw(777); }
                bool ok; FixedStatus status;
                switch (kind)
                {
                    case FixedKind.Signed32: ok = random.TryFill(a, 4, out status); break;
                    case FixedKind.Unsigned32: ok = random.TryFill(b, 4, out status); break;
                    case FixedKind.Signed64: ok = random.TryFill(c, 4, out status); break;
                    default: ok = random.TryFill(d, 4, out status); break;
                }
                Assert.IsFalse(ok); Assert.AreEqual(FixedStatus.OutOfRange, status); Assert.AreEqual(before, random.CaptureState());
                for (int i = 0; i < 4; i++) { Assert.AreEqual(777, a[i].RawValue); Assert.AreEqual(777U, b[i].RawValue); Assert.AreEqual(777L, c[i].RawValue); Assert.AreEqual(777UL, d[i].RawValue); }
            }
        }

        [TestCase(FixedKind.Signed32, 0)] [TestCase(FixedKind.Signed32, 6)]
        [TestCase(FixedKind.Unsigned32, 0)] [TestCase(FixedKind.Unsigned32, 6)]
        [TestCase(FixedKind.Signed64, 0)] [TestCase(FixedKind.Signed64, 6)]
        [TestCase(FixedKind.Unsigned64, 0)] [TestCase(FixedKind.Unsigned64, 6)]
        public void AUnitWeightAmongZerosAndNegativeZerosIsConstantEvenAfterExhaustion(FixedKind kind, int selected)
        {
            var state = new FixedRuntimeState(FixedSettings.Default); var f = new FixedFormat(kind, state);
            ulong* weights = stackalloc ulong[7]; ulong* prefix = stackalloc ulong[9];
            for (int i = 0; i < 7; i++) Put(weights, i, kind, i == selected ? 1UL : (i & 1) == 0 ? 0UL : f.NegativeZero);
            for (int i = 0; i < 9; i++) prefix[i] = 0xbadUL;
            Assert.IsTrue(FixedRandomWeightsKernel.Build(weights, 7, kind, state, prefix, 9, out var table, out var status));
            Assert.AreEqual(1UL, table.Total);
            for (int i = 0; i < 7; i++) Assert.AreEqual(i < selected ? 0UL : 1UL, prefix[i]);
            Assert.AreEqual(0xbadUL, prefix[7]); Assert.AreEqual(0xbadUL, prefix[8]);
            var random = FixedRandom.Create(10); random.Seek(ulong.MaxValue); var before = random.CaptureState();
            Assert.IsTrue(FixedRandomWeightsKernel.Select(ref random, weights, 7, kind, state, out int index, out status)); Assert.AreEqual(selected, index);
            Assert.IsTrue(random.TryWeightedIndex(table, out index, out status)); Assert.AreEqual(selected, index); Assert.AreEqual(before, random.CaptureState());
        }
        private static void Put(ulong* weights, int index, FixedKind kind, ulong raw)
        { if (kind < FixedKind.Signed64) ((uint*)weights)[index] = (uint)raw; else weights[index] = raw; }

        [TestCase(FixedKind.Signed32)] [TestCase(FixedKind.Unsigned32)]
        [TestCase(FixedKind.Signed64)] [TestCase(FixedKind.Unsigned64)]
        public void EverySpecialWeightFailsAtomicallyAndOverlappingPrefixBuffersAreRejected(FixedKind kind)
        {
            var state = new FixedRuntimeState(FixedSettings.Default); var f = new FixedFormat(kind, state);
            ulong* weights = stackalloc ulong[4]; ulong* prefix = stackalloc ulong[4];
            foreach (ulong invalid in new[] { f.NaN, f.PositiveInfinity, f.NegativeInfinity })
            {
                Put(weights, 0, kind, 3); Put(weights, 1, kind, invalid);
                for (int i = 0; i < 4; i++) prefix[i] = 17;
                Assert.IsFalse(FixedRandomWeightsKernel.Build(weights, 2, kind, state, prefix, 4, out _, out var status));
                Assert.AreEqual(FixedStatus.InvalidDomain, status); for (int i = 0; i < 4; i++) Assert.AreEqual(17UL, prefix[i]);
                var random = FixedRandom.Create(35); var before = random.CaptureState();
                Assert.IsFalse(FixedRandomWeightsKernel.Select(ref random, weights, 2, kind, state, out int index, out status));
                Assert.AreEqual(-1, index); Assert.AreEqual(before, random.CaptureState());
            }
            Put(weights, 0, kind, 1); Put(weights, 1, kind, 3);
            ulong original = weights[0];
            Assert.IsFalse(FixedRandomWeightsKernel.Build(weights, 2, kind, state, weights, 4, out _, out var overlap));
            Assert.AreEqual(FixedStatus.InvalidDomain, overlap); Assert.AreEqual(original, weights[0]);
        }
    }
}
