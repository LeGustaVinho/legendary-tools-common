using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedBoundsOptimizationTests
    {
        private const long Lowest = long.MinValue + 2, Highest = long.MaxValue - 2;
        private const ulong NegativeZero = 0x7FFFFFFFFFFFFFFEUL;
        private static FixedVectorBits V(long value) => new FixedVectorBits(unchecked((ulong)value), unchecked((ulong)value), unchecked((ulong)value));
        private static FixedBoundsRuntimeState Geometry(FixedBoundsRepresentation representation) => new FixedBoundsRuntimeState(new FixedBoundsSettings(representation));
        private static FixedUInt128 Wide(BigInteger value) => new FixedUInt128((ulong)(value & ulong.MaxValue), (ulong)(value >> 64));
        private static BigInteger Integer(FixedBigUInt value)
        {
            BigInteger result = 0;
            for (int limb = 15; limb >= 0; limb--) result = (result << 32) | value.Get(limb);
            return result;
        }
        private static ulong Pack(BigInteger value, bool negativeZero, FixedOverflow overflow, out FixedStatus status)
        {
            status = FixedStatus.None;
            if (value >= Lowest && value <= Highest)
                return value.IsZero && negativeZero ? NegativeZero : unchecked((ulong)(long)value);
            status = FixedStatus.Overflow;
            switch (overflow)
            {
                case FixedOverflow.NaN: return 0x8000000000000000UL;
                case FixedOverflow.Saturate: return unchecked((ulong)(value.Sign < 0 ? Lowest : Highest));
                case FixedOverflow.WrapFinite:
                    BigInteger period = (BigInteger)Highest - Lowest + 1;
                    BigInteger offset = (value - Lowest) % period;
                    if (offset.Sign < 0) offset += period;
                    return unchecked((ulong)(long)(offset + Lowest));
                default: return value.Sign < 0 ? 0x8000000000000001UL : 0x7FFFFFFFFFFFFFFFUL;
            }
        }

        [TestCase(2)] [TestCase(3)]
        public void CenterExtentsValidationUsesExactFiniteLimitsAndPreservesErrorPriority(int dimensions)
        {
            var numeric = new FixedRuntimeState(FixedSettings.Default);
            var geometry = Geometry(FixedBoundsRepresentation.CenterExtents);
            long[] centers = { Lowest, Lowest + 1, Lowest + 2, -5, -1, 0, 1, 5, Highest - 2, Highest - 1, Highest };
            long[] extents = { -1, 0, 1, 2, 5, Highest - 1, Highest };
            foreach (long center in centers) foreach (long extent in extents)
            {
                var bounds = new FixedBoundsBits(V(center), V(extent));
                BigInteger lo = (BigInteger)center - extent, hi = (BigInteger)center + extent;
                var expected = extent < 0 ? FixedStatus.InvalidDomain : lo < Lowest || hi > Highest ? FixedStatus.Overflow : FixedStatus.None;
                Assert.AreEqual(expected == FixedStatus.None, FixedBoundsKernel.Validate(bounds, dimensions, numeric, geometry, out var status));
                Assert.AreEqual(expected, status);
                if (expected == FixedStatus.None)
                {
                    var min = FixedBoundsKernel.Get(bounds, FixedBoundsPart.Min, dimensions, numeric, geometry, out status);
                    var max = FixedBoundsKernel.Get(bounds, FixedBoundsPart.Max, dimensions, numeric, geometry, out status);
                    for (int axis = 0; axis < dimensions; axis++)
                    { Assert.AreEqual(lo, FixedBoundsTests.Integer(min.Get(axis))); Assert.AreEqual(hi, FixedBoundsTests.Integer(max.Get(axis))); }
                }
            }
            foreach (ulong special in new[] { 0x7FFFFFFFFFFFFFFFUL, 0x8000000000000000UL, 0x8000000000000001UL })
            {
                // A nonfinite last axis takes precedence over an overflow on the first axis.
                var bounds = new FixedBoundsBits(V(Highest), V(1)); bounds.Second.Set(dimensions - 1, special);
                Assert.IsFalse(FixedBoundsKernel.Validate(bounds, dimensions, numeric, geometry, out var status));
                Assert.AreEqual(FixedStatus.InvalidDomain, status);
            }
            var zero = new FixedBoundsBits(V(0), V(0)); zero.First.X = zero.Second.X = NegativeZero;
            Assert.IsTrue(FixedBoundsKernel.Validate(zero, dimensions, numeric, geometry, out var zeroStatus));
            Assert.AreEqual(FixedStatus.None, zeroStatus);
            Assert.AreEqual(0UL, FixedBoundsKernel.Get(zero, FixedBoundsPart.Min, dimensions, numeric, geometry, out _).X);
            Assert.AreEqual(NegativeZero, FixedBoundsKernel.Get(zero, FixedBoundsPart.Center, dimensions, numeric, geometry, out _).X);
        }

        [Test]
        public void NarrowRatioMatchesBigIntegerForRoundingOverflowAndSignedZero()
        {
            Assert.AreEqual(24, sizeof(FixedBoundsInteger));
            Assert.AreEqual(68, sizeof(FixedQ));
            BigInteger[] magnitudes = { 0, 1, 2, 3, (BigInteger.One << 63) - 1, (BigInteger.One << 64) - 5,
                (BigInteger.One << 65) - 9, (BigInteger.One << 66) - 1, (BigInteger.One << 127) - 1 };
            foreach (var rounding in (FixedRounding[])Enum.GetValues(typeof(FixedRounding)))
            foreach (var overflow in (FixedOverflow[])Enum.GetValues(typeof(FixedOverflow)))
            foreach (ulong denominator in new[] { 1UL, 2UL, 3UL, 1000UL, 1000000000000000000UL, ulong.MaxValue })
            foreach (var magnitude in magnitudes) foreach (bool negative in new[] { false, true })
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, 18, rounding, overflow));
                BigInteger signed = negative ? -magnitude : magnitude;
                BigInteger expectedValue = FixedBoundsTests.Round(signed, denominator, rounding);
                ulong expected = Pack(expectedValue, negative && expectedValue.IsZero, overflow, out var expectedStatus);
                if (magnitude % denominator != 0) expectedStatus |= FixedStatus.Inexact;
                var status = FixedStatus.None;
                ulong actual = FixedBoundsInteger.Round(new FixedBoundsInteger(Wide(magnitude), negative), denominator, numeric, ref status);
                Assert.AreEqual(expectedStatus, status); Assert.AreEqual(expected, actual);
            }
        }

        [TestCase(2)] [TestCase(3)]
        public void RawDistanceRetainsAllCarriesBeyond128Bits(int dimensions)
        {
            foreach (var arithmetic in (FixedArithmeticMode[])Enum.GetValues(typeof(FixedArithmeticMode)))
            foreach (long point in new[] { Lowest, Lowest + 1, -1L, 0L, 1L, Highest - 1, Highest })
            foreach (long endpoint in new[] { Lowest, Lowest + 1, 0L, Highest - 1, Highest })
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, 18, arithmeticMode: arithmetic));
                var box = new FixedBoundsBits(V(endpoint), V(endpoint));
                BigInteger delta = (BigInteger)point - endpoint;
                Assert.IsTrue(FixedBoundsKernel.TrySqrDistanceRaw(box, V(point), dimensions, numeric,
                    Geometry(FixedBoundsRepresentation.MinMax), out var raw, out var status));
                Assert.AreEqual(FixedStatus.None, status);
                Assert.AreEqual(delta * delta * dimensions, Integer(raw));
            }
        }

        [Test]
        public void ExactFractionOrderingHandlesUnsignedFullWidthAndWideFallback()
        {
            BigInteger[] numerators = { 0, 1, -1, (BigInteger.One << 63) - 1, -((BigInteger.One << 64) - 5),
                (BigInteger.One << 64) - 5, BigInteger.One << 80, -(BigInteger.One << 80) };
            foreach (var a in numerators) foreach (var b in numerators)
            foreach (ulong ad in new[] { 1UL, 3UL, ulong.MaxValue }) foreach (ulong bd in new[] { 1UL, 7UL, ulong.MaxValue })
            {
                var af = new FixedBoundsKernel.Fraction(new FixedQ(FixedBigUInt.From(Wide(BigInteger.Abs(a))), a.Sign < 0), ad);
                var bf = new FixedBoundsKernel.Fraction(new FixedQ(FixedBigUInt.From(Wide(BigInteger.Abs(b))), b.Sign < 0), bd);
                Assert.AreEqual(Math.Sign((a * bd).CompareTo(b * ad)), Math.Sign(FixedBoundsKernel.Compare(af, bf)));
            }
            Assert.AreEqual(0, FixedBoundsKernel.Compare(new FixedBoundsKernel.Fraction(new FixedQ(default, true), 1),
                new FixedBoundsKernel.Fraction(default, ulong.MaxValue)));
        }

        // Previous vector-stage composition is the byte/cursor oracle for the new single-stage publisher.
        private static bool VectorStageWrite(ref FixedBufferWriter writer, FixedBoundsBits box, int dimensions,
            FixedBoundsNetworkProfile profile, FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            var value = FixedBoundsNetworkKernel.Canonicalize(box, dimensions, profile, numeric, geometry, out status);
            if (!status.IsSuccess()) return false;
            if (profile.First.X.Codec != FixedCodec.BitPacked && (writer.BitPosition & 7) != 0)
            { status |= FixedStatus.InvalidEncoding; return false; }
            byte* packet = stackalloc byte[60]; for (int i = 0; i < 60; i++) packet[i] = 0;
            var staged = new FixedBufferWriter(packet, 60);
            bool ok = FixedVectorNetworkKernel.Write(ref staged, value.First, dimensions, profile.First, numeric, out var first); status |= first;
            if (!ok) return false;
            ok = FixedVectorNetworkKernel.Write(ref staged, value.Second, dimensions, profile.Second, numeric, out var second); status |= second;
            if (!ok) return false;
            if (!writer.Has(staged.BitPosition)) { status |= FixedStatus.BufferTooSmall; return false; }
            for (int bit = 0; bit < staged.BitPosition; bit++) writer.WriteBits((ulong)((packet[bit / 8] >> (bit % 8)) & 1), 1);
            return true;
        }

        [TestCase(2)] [TestCase(3)]
        public void SingleStagePublicationMatchesPreviousBytesStatusesAndAtomicCursors(int dimensions)
        {
            byte* actual = stackalloc byte[80]; byte* expected = stackalloc byte[80];
            foreach (var representation in (FixedBoundsRepresentation[])Enum.GetValues(typeof(FixedBoundsRepresentation)))
            foreach (var codec in (FixedCodec[])Enum.GetValues(typeof(FixedCodec))) foreach (bool lossy in new[] { false, true })
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, 3)); var geometry = Geometry(representation);
                var axis = new FixedNetworkProfile(numeric.Settings, FixedKind.Signed64, codec, lossy ? 0 : 3, lossy,
                    codec == FixedCodec.BitPacked ? unchecked((ulong)-100000L) : 0, codec == FixedCodec.BitPacked ? 100000UL : 0);
                var fields = new FixedVectorNetworkProfile(axis, dimensions); var profile = new FixedBoundsNetworkProfile(representation, fields, fields);
                var box = new FixedBoundsBits(V(-1051), V(2077)); box.First.Y = NegativeZero;
                for (int offset = 0; offset < 16; offset++) for (int capacity = 0; capacity <= 70; capacity++)
                {
                    for (int i = 0; i < 80; i++) actual[i] = expected[i] = (byte)(0xA5 ^ i);
                    var aw = new FixedBufferWriter(actual + 1, capacity, offset); var ew = new FixedBufferWriter(expected + 1, capacity, offset);
                    bool aok = FixedBoundsNetworkKernel.Write(ref aw, box, dimensions, profile, numeric, geometry, out var ast);
                    bool eok = VectorStageWrite(ref ew, box, dimensions, profile, numeric, geometry, out var est);
                    Assert.AreEqual(eok, aok); Assert.AreEqual(est, ast); Assert.AreEqual(ew.BitPosition, aw.BitPosition);
                    for (int i = 0; i < 80; i++) Assert.AreEqual(expected[i], actual[i], "Canary byte " + i);
                }
            }
        }
    }
}
