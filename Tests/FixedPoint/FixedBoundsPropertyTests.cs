using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    /// <summary>Independent integer/rational oracles; no floating point or production rounding helpers.</summary>
    public sealed class FixedBoundsPropertyTests
    {
        private const long Highest = long.MaxValue - 2;
        private const long Lowest = long.MinValue + 2;
        private static readonly int[] Precisions = { 0, 3, 9, 18 };
        [OneTimeSetUp] public void Initialize()
        { FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static BigInteger Raw(ulong value) => FixedBoundsTests.Integer(value);
        private static BigInteger Round(BigInteger value, BigInteger divisor, FixedRounding mode) => FixedBoundsTests.Round(value, divisor, mode);
        private static BigInteger FloorHalf(BigInteger value) => Round(value, 2, FixedRounding.TowardNegativeInfinity);
        private static BigInteger CeilHalf(BigInteger value) => Round(value, 2, FixedRounding.TowardPositiveInfinity);
        private static FixedVectorBits Vector(BigInteger[] values)
        { var v = default(FixedVectorBits); for (int i = 0; i < values.Length; i++) v.Set(i, unchecked((ulong)(long)values[i])); return v; }
        private static ulong Next(ref ulong seed)
        {
            seed = unchecked(seed + 0x9E3779B97F4A7C15UL); ulong v = seed;
            v = unchecked((v ^ (v >> 30)) * 0xBF58476D1CE4E5B9UL);
            v = unchecked((v ^ (v >> 27)) * 0x94D049BB133111EBUL); return v ^ (v >> 31);
        }
        private static long Finite(ref ulong seed, int sample)
        {
            ulong v = Next(ref seed);
            switch (sample % 8)
            {
                case 0: return Lowest + (long)(v % 8);
                case 1: return Highest - (long)(v % 8);
                case 2: return (long)(v % 9) - 4;
                default: long n = (long)(v % (ulong)Highest); return (v & 1) == 0 ? n : -n;
            }
        }
        private static void Same(FixedBoundsBits a, FixedBoundsBits b, int dimensions)
        { for (int i = 0; i < dimensions; i++) { Assert.AreEqual(a.First.Get(i), b.First.Get(i)); Assert.AreEqual(a.Second.Get(i), b.Second.Get(i)); } }
        private static void Corners(FixedBoundsBits box, int dimensions, FixedBoundsRepresentation representation, out BigInteger[] min, out BigInteger[] max)
        {
            min = new BigInteger[dimensions]; max = new BigInteger[dimensions];
            for (int i = 0; i < dimensions; i++)
            {
                var a = Raw(box.First.Get(i)); var b = Raw(box.Second.Get(i));
                min[i] = representation == FixedBoundsRepresentation.MinMax ? a : a - b;
                max[i] = representation == FixedBoundsRepresentation.MinMax ? b : a + b;
            }
        }
        private static void AssertCorners(FixedBoundsBits box, BigInteger[] min, BigInteger[] max, FixedRuntimeState numeric, FixedBoundsRuntimeState geometry)
        {
            var lo = FixedBoundsKernel.Get(box, FixedBoundsPart.Min, min.Length, numeric, geometry, out var status);
            Assert.AreEqual(FixedStatus.None, status);
            var hi = FixedBoundsKernel.Get(box, FixedBoundsPart.Max, min.Length, numeric, geometry, out status);
            Assert.AreEqual(FixedStatus.None, status);
            for (int i = 0; i < min.Length; i++) { Assert.AreEqual(min[i], Raw(lo.Get(i)), "Lower axis " + i); Assert.AreEqual(max[i], Raw(hi.Get(i)), "Upper axis " + i); }
        }
        [TestCase(2)] [TestCase(3)]
        public void MutationSequencesAgreeWithEndpointEnvelopesAcrossTheConfigurationMatrix(int dimensions)
        {
            foreach (int precision in Precisions) foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow))) foreach (FixedArithmeticMode arithmetic in Enum.GetValues(typeof(FixedArithmeticMode)))
            foreach (FixedBoundsRepresentation representation in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, precision, rounding, overflow, arithmeticMode: arithmetic));
                var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(representation));
                BigInteger[] min = new BigInteger[dimensions], max = new BigInteger[dimensions];
                for (int i = 0; i < dimensions; i++) { min[i] = -27 - 6 * i; max[i] = 31 + 8 * i; }
                Assert.IsTrue(FixedBoundsKernel.FromMinMax(Vector(min), Vector(max), dimensions, numeric, geometry, out var box, out _));
                ulong seed = 0x734F09CEB42A9D11UL;
                for (int tick = 0; tick < 48; tick++)
                {
                    var original = box; Corners(box, dimensions, representation, out min, out max);
                    var input = new BigInteger[dimensions]; var nextMin = (BigInteger[])min.Clone(); var nextMax = (BigInteger[])max.Clone();
                    int operation = tick % 8; FixedBoundsPart part = (FixedBoundsPart)(tick % 5); bool expectedOk = true, inexact = false;
                    if (operation == 0)
                    {
                        // Growing/shrinking by a total size uses the exact geometric centre, even for odd spans.
                        for (int i = 0; i < dimensions; i++)
                        {
                            input[i] = (long)(Next(ref seed) % 11) - 5;
                            var width = max[i] - min[i] + input[i];
                            if (width < 0) expectedOk = false;
                            inexact |= !input[i].IsEven;
                            if (representation == FixedBoundsRepresentation.MinMax)
                            { nextMin[i] = FloorHalf(2 * min[i] - input[i]); nextMax[i] = CeilHalf(2 * max[i] + input[i]); }
                            else { var e = CeilHalf(width); var c = Raw(box.First.Get(i)); nextMin[i] = c - e; nextMax[i] = c + e; }
                        }
                        bool ok = FixedBoundsKernel.Expand(box, Vector(input), dimensions, numeric, geometry, out box, out var status);
                        Assert.AreEqual(expectedOk, ok, "Expand tick " + tick);
                        if (ok) Assert.AreEqual(inexact ? FixedStatus.Inexact : FixedStatus.None, status);
                        else { Assert.IsTrue((status & FixedStatus.InvalidDomain) != 0); Same(original, box, dimensions); }
                    }
                    else
                    {
                        bool self = operation == 7;
                        for (int i = 0; i < dimensions; i++)
                        {
                            var c = Round(min[i] + max[i], 2, rounding); var e = Round(max[i] - min[i], 2, rounding);
                            var current = part == FixedBoundsPart.Min ? min[i] : part == FixedBoundsPart.Max ? max[i] : part == FixedBoundsPart.Center ? c : part == FixedBoundsPart.Size ? max[i] - min[i] : e;
                            input[i] = self ? current : part == FixedBoundsPart.Min ? min[i] - 1 - Next(ref seed) % 7 : part == FixedBoundsPart.Max ? max[i] + 1 + Next(ref seed) % 7 :
                                part == FixedBoundsPart.Center ? (long)(Next(ref seed) % 81) - 40 : 1 + Next(ref seed) % 31;
                            if (self) continue;
                            if (part == FixedBoundsPart.Min || part == FixedBoundsPart.Max)
                            {
                                if (part == FixedBoundsPart.Min) nextMin[i] = input[i]; else nextMax[i] = input[i];
                                if (representation == FixedBoundsRepresentation.CenterExtents)
                                {
                                    var rounded = Round(nextMin[i] + nextMax[i], 2, rounding); var extent = BigInteger.Max(rounded - nextMin[i], nextMax[i] - rounded);
                                    inexact |= !(nextMin[i] + nextMax[i]).IsEven; nextMin[i] = rounded - extent; nextMax[i] = rounded + extent;
                                }
                            }
                            else if (representation == FixedBoundsRepresentation.CenterExtents)
                            {
                                c = part == FixedBoundsPart.Center ? input[i] : Raw(box.First.Get(i));
                                e = part == FixedBoundsPart.Extents ? input[i] : part == FixedBoundsPart.Size ? CeilHalf(input[i]) : Raw(box.Second.Get(i));
                                inexact |= part == FixedBoundsPart.Size && !input[i].IsEven; nextMin[i] = c - e; nextMax[i] = c + e;
                            }
                            else
                            {
                                var doubledCentre = part == FixedBoundsPart.Center ? 2 * input[i] : min[i] + max[i];
                                var width = part == FixedBoundsPart.Center ? max[i] - min[i] : part == FixedBoundsPart.Size ? input[i] : 2 * input[i];
                                nextMin[i] = FloorHalf(doubledCentre - width); nextMax[i] = CeilHalf(doubledCentre + width);
                                inexact |= !(doubledCentre - width).IsEven;
                            }
                        }
                        // A numerical self-assignment must preserve raw storage and must not widen an odd span.
                        var exposed = FixedBoundsKernel.Get(box, part, dimensions, numeric, geometry, out _);
                        bool noOp = true; for (int i = 0; i < dimensions; i++) noOp &= Raw(exposed.Get(i)) == input[i];
                        if (noOp) { nextMin = min; nextMax = max; inexact = false; }
                        Assert.IsTrue(FixedBoundsKernel.Set(box, part, Vector(input), dimensions, numeric, geometry, out box, out var status));
                        Assert.AreEqual(inexact ? FixedStatus.Inexact : FixedStatus.None, status, "Set tick " + tick + " part " + part);
                        if (self) Same(original, box, dimensions);
                    }
                    if (expectedOk) AssertCorners(box, nextMin, nextMax, numeric, geometry);
                    Assert.IsTrue(FixedBoundsKernel.Validate(box, dimensions, numeric, geometry, out _));
                }
            }
        }
        private static bool RayOracle(BigInteger[] min, BigInteger[] max, BigInteger[] origin, BigInteger[] direction, out BigInteger n, out BigInteger d)
        {
            n = 0; d = 1; BigInteger? exit = null; BigInteger exitD = 1;
            for (int i = 0; i < min.Length; i++)
            {
                if (direction[i] == 0) { if (origin[i] < min[i] || origin[i] > max[i]) return false; continue; }
                var near = min[i] - origin[i]; var far = max[i] - origin[i]; var denominator = direction[i];
                if (denominator < 0) { denominator = -denominator; var swap = near; near = -far; far = -swap; }
                if (near * d > n * denominator) { n = near; d = denominator; }
                if (!exit.HasValue || far * exitD < exit.Value * denominator) { exit = far; exitD = denominator; }
                if (n * exitD > exit.Value * d) return false;
            }
            return true;
        }
        private static ulong PackNonnegative(BigInteger value, FixedOverflow overflow)
        {
            if (value <= Highest) return (ulong)value;
            if (overflow == FixedOverflow.Saturate) return (ulong)Highest;
            if (overflow == FixedOverflow.NaN) return 0x8000000000000000UL;
            if (overflow == FixedOverflow.Infinity) return 0x7FFFFFFFFFFFFFFFUL;
            // Finite wrapping is reduction into [Lowest, Highest], excluding all four special raw patterns.
            var width = new BigInteger(Highest) - Lowest + 1;
            return unchecked((ulong)(long)((value - Lowest) % width + Lowest));
        }
        [TestCase(2)] [TestCase(3)]
        public void FullWidthDistanceAndRayFractionsMatchBigIntegerIncludingOverflowResults(int dimensions)
        {
            foreach (int precision in Precisions) foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow))) foreach (FixedArithmeticMode arithmetic in Enum.GetValues(typeof(FixedArithmeticMode)))
            foreach (FixedBoundsRepresentation representation in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, precision, rounding, overflow, arithmeticMode: arithmetic));
                var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(representation)); ulong seed = 0x8FA70D05C2B91367UL;
                for (int sample = 0; sample < 24; sample++)
                {
                    var min = new BigInteger[dimensions]; var max = new BigInteger[dimensions]; var origin = new BigInteger[dimensions]; var direction = new BigInteger[dimensions];
                    for (int i = 0; i < dimensions; i++)
                    {
                        // Even-width intervals remain exact under both representations; endpoints exercise opposite signs and 65-bit differences.
                        long c = Finite(ref seed, sample + i) / 2, e = (long)(Next(ref seed) % (ulong)(Highest / 2));
                        min[i] = new BigInteger(c) - e; max[i] = new BigInteger(c) + e;
                        origin[i] = Finite(ref seed, sample + i + 1);
                        direction[i] = sample % 3 == 0 ? (long)(Next(ref seed) % 5) - 2 : Finite(ref seed, sample + i + 4);
                    }
                    if (sample == 0) { min[0] = Highest - 2; max[0] = Highest; origin[0] = Lowest; direction[0] = 1; for (int i = 1; i < dimensions; i++) { origin[i] = 0; min[i] = -2; max[i] = 2; direction[i] = 0; } }
                    if (sample == 1) { for (int i = 0; i < dimensions; i++) { min[i] = Highest - 2; max[i] = Highest; origin[i] = Lowest; direction[i] = Highest - i; } }
                    Assert.IsTrue(FixedBoundsKernel.FromMinMax(Vector(min), Vector(max), dimensions, numeric, geometry, out var box, out var construction)); Assert.AreEqual(FixedStatus.None, construction);
                    BigInteger sum = 0;
                    for (int i = 0; i < dimensions; i++) { var delta = origin[i] - BigInteger.Max(min[i], BigInteger.Min(max[i], origin[i])); sum += delta * delta; }
                    var expected = Round(sum, numeric.Scale64, rounding);
                    var expectedStatus = sum % numeric.Scale64 == 0 ? FixedStatus.None : FixedStatus.Inexact;
                    if (expected > Highest) expectedStatus |= FixedStatus.Overflow;
                    ulong result = FixedBoundsKernel.SqrDistance(box, Vector(origin), dimensions, numeric, geometry, out var status);
                    Assert.AreEqual(expectedStatus, status); Assert.AreEqual(PackNonnegative(expected, overflow), result, "Distance sample " + sample);
                    bool contact = RayOracle(min, max, origin, direction, out var n, out var d);
                    Assert.IsTrue(FixedBoundsKernel.IntersectRay(box, Vector(origin), Vector(direction), dimensions, numeric, geometry, false, out bool hit, out ulong t, out status));
                    Assert.AreEqual(contact, hit); Assert.AreEqual(0UL, t); Assert.AreEqual(FixedStatus.None, status);
                    expected = contact ? Round(n * numeric.Scale64, d, rounding) : BigInteger.Zero;
                    expectedStatus = contact && n * numeric.Scale64 % d != 0 ? FixedStatus.Inexact : FixedStatus.None;
                    if (expected > Highest) expectedStatus |= FixedStatus.Overflow;
                    bool ok = FixedBoundsKernel.IntersectRay(box, Vector(origin), Vector(direction), dimensions, numeric, geometry, true, out hit, out t, out status);
                    Assert.AreEqual(expectedStatus.IsSuccess(), ok); Assert.AreEqual(contact && ok, hit); Assert.AreEqual(expectedStatus, status);
                    Assert.AreEqual(PackNonnegative(expected, overflow), t, "Ray sample " + sample);
                }
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void FailuresOnTheLastAxisPreserveEveryStoredBitUnderEveryOverflowPolicy(int dimensions)
        {
            foreach (FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow))) foreach (FixedBoundsRepresentation representation in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(new FixedSettings(3, 3, overflow: overflow)); var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(representation));
                var c = new BigInteger[dimensions]; var e = new BigInteger[dimensions]; for (int i = 0; i < dimensions; i++) e[i] = 2;
                c[dimensions - 1] = Highest - 2;
                Assert.IsTrue(FixedBoundsKernel.FromCenterExtents(Vector(c), Vector(e), dimensions, numeric, geometry, out var box, out _));
                for (int part = 0; part < 5; part++)
                {
                    var input = FixedBoundsKernel.Get(box, (FixedBoundsPart)part, dimensions, numeric, geometry, out _); input.X = 17; input.Set(dimensions - 1, 0x8000000000000000UL);
                    Assert.IsFalse(FixedBoundsKernel.Set(box, (FixedBoundsPart)part, input, dimensions, numeric, geometry, out var failed, out var status)); Assert.AreEqual(FixedStatus.InvalidDomain, status); Same(box, failed, dimensions);
                }
                var delta = new BigInteger[dimensions]; for (int i = 0; i < dimensions; i++) delta[i] = 3;
                Assert.IsFalse(FixedBoundsKernel.Expand(box, Vector(delta), dimensions, numeric, geometry, out var expanded, out var error)); Assert.IsTrue((error & FixedStatus.Overflow) != 0); Same(box, expanded, dimensions);
                c[dimensions - 1] = Highest;
                Assert.IsFalse(FixedBoundsKernel.Set(box, FixedBoundsPart.Center, Vector(c), dimensions, numeric, geometry, out var moved, out error)); Assert.IsTrue((error & FixedStatus.Overflow) != 0); Same(box, moved, dimensions);
                delta[dimensions - 1] = -5;
                Assert.IsFalse(FixedBoundsKernel.Expand(box, Vector(delta), dimensions, numeric, geometry, out expanded, out error)); Assert.IsTrue((error & FixedStatus.InvalidDomain) != 0); Same(box, expanded, dimensions);
            }
        }
        [TestCase(2)] [TestCase(3)]
        public void AxisPermutationsAndTranslationsPreserveQueriesAndExactRayParameters(int dimensions)
        {
            foreach (FixedBoundsRepresentation representation in Enum.GetValues(typeof(FixedBoundsRepresentation)))
            {
                var numeric = new FixedRuntimeState(FixedSettings.Default); var geometry = new FixedBoundsRuntimeState(new FixedBoundsSettings(representation)); ulong seed = 0x230614AB097E52F8UL;
                for (int sample = 0; sample < 256; sample++)
                {
                    var min = new BigInteger[dimensions]; var max = new BigInteger[dimensions]; var p = new BigInteger[dimensions]; var d = new BigInteger[dimensions];
                    var shiftedMin = new BigInteger[dimensions]; var shiftedMax = new BigInteger[dimensions]; var shiftedP = new BigInteger[dimensions]; var permutedD = new BigInteger[dimensions];
                    for (int i = 0; i < dimensions; i++) { min[i] = (long)(Next(ref seed) % 201) - 100; max[i] = min[i] + 2 * (Next(ref seed) % 20); p[i] = (long)(Next(ref seed) % 401) - 200; d[i] = (long)(Next(ref seed) % 13) - 6; }
                    for (int i = 0; i < dimensions; i++) { int from = (i + 1) % dimensions; long shift = (long)(Next(ref seed) % 20001) - 10000; shiftedMin[i] = min[from] + shift; shiftedMax[i] = max[from] + shift; shiftedP[i] = p[from] + shift; permutedD[i] = d[from]; }
                    Assert.IsTrue(FixedBoundsKernel.FromMinMax(Vector(min), Vector(max), dimensions, numeric, geometry, out var a, out _));
                    Assert.IsTrue(FixedBoundsKernel.FromMinMax(Vector(shiftedMin), Vector(shiftedMax), dimensions, numeric, geometry, out var b, out _));
                    FixedBoundsKernel.Contains(a, Vector(p), dimensions, numeric, geometry, out bool ca, out var sa); FixedBoundsKernel.Contains(b, Vector(shiftedP), dimensions, numeric, geometry, out bool cb, out var sb); Assert.AreEqual(ca, cb); Assert.AreEqual(sa, sb);
                    ulong da = FixedBoundsKernel.SqrDistance(a, Vector(p), dimensions, numeric, geometry, out sa), db = FixedBoundsKernel.SqrDistance(b, Vector(shiftedP), dimensions, numeric, geometry, out sb); Assert.AreEqual(da, db); Assert.AreEqual(sa, sb);
                    bool oa = FixedBoundsKernel.IntersectRay(a, Vector(p), Vector(d), dimensions, numeric, geometry, true, out ca, out da, out sa), ob = FixedBoundsKernel.IntersectRay(b, Vector(shiftedP), Vector(permutedD), dimensions, numeric, geometry, true, out cb, out db, out sb);
                    Assert.AreEqual(oa, ob); Assert.AreEqual(ca, cb); Assert.AreEqual(da, db); Assert.AreEqual(sa, sb);
                }
            }
        }
        [Test] public unsafe void SequenceCorpusRestoresTheCheckpointAndReproducesEveryObservation()
        {
            ulong checksum = 14695981039346656037UL;
            for (int i = 0; i < FixedBoundsStressCorpus.Count; i++)
            {
                var result = FixedBoundsStressCorpus.Evaluate(i);
                Assert.AreEqual(0, result.Errors, "Sequence " + i); Assert.AreEqual(FixedBoundsStressCorpus.Steps - FixedBoundsStressCorpus.CheckpointStep, result.Replayed);
                checksum = FixedBoundsReplayCorpus.Mix(checksum, result.Hash);
            }
            TestContext.WriteLine("Bounds sequence corpus: cases=" + FixedBoundsStressCorpus.Count + " steps=" + FixedBoundsStressCorpus.Steps + " checksum=" + checksum);
            Assert.AreEqual(FixedBoundsStressCorpus.ExpectedChecksum, checksum);
        }
    }
}
