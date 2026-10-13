namespace LegendaryTools.FixedPoint
{
    internal struct FixedBoundsBits
    {
        internal FixedVectorBits First, Second;
        internal FixedBoundsBits(FixedVectorBits first, FixedVectorBits second) { First = first; Second = second; }
        internal static FixedBoundsBits Invalid => new FixedBoundsBits(FixedVectorKernel.NaN, FixedVectorKernel.NaN);
    }

    internal enum FixedBoundsPart : byte { Min, Max, Center, Size, Extents }

    /// <summary>Shared integer geometry. State arguments make every numerical/representation configuration testable without resetting globals.</summary>
    internal static class FixedBoundsKernel
    {
        private const FixedKind Kind = FixedKind.Signed64;
        private static bool Ready(int dimensions, FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            status = !numeric.IsInitialized || !geometry.IsInitialized ? FixedStatus.Uninitialized :
                dimensions != 2 && dimensions != 3 ? FixedStatus.InvalidConfiguration : FixedStatus.None;
            return status.IsSuccess();
        }
        internal static FixedQ Raw(ulong bits, FixedRuntimeState state)
        { var v = new FixedFormat(Kind, state).Decode(bits); return new FixedQ(FixedBigUInt.From(v.Magnitude), v.Negative && v.Magnitude != 0); }
        private static long Integer(ulong bits) => bits == 0x7FFFFFFFFFFFFFFEUL ? 0 : unchecked((long)bits);
        private static FixedBoundsInteger Coordinate(ulong bits)
        {
            long value = Integer(bits);
            return new FixedBoundsInteger(new FixedUInt128(value < 0 ? unchecked(0UL - (ulong)value) : (ulong)value), value < 0);
        }
        private static FixedBoundsInteger Twice(FixedBoundsInteger value) => new FixedBoundsInteger(value.Magnitude.ShiftLeft(1), value.Negative);
        private static bool Finite(FixedVectorBits value, int dimensions, FixedRuntimeState state)
        {
            // The three nonfinite Signed64 encodings are consecutive. Negative zero remains finite.
            for (int i = 0; i < dimensions; i++) if (unchecked(value.Get(i) - 0x7FFFFFFFFFFFFFFFUL) <= 2UL) return false;
            return true;
        }
        private static bool NonNegative(FixedBoundsInteger value) => !value.Negative || value.IsZero;
        private static bool Fits(FixedBoundsInteger value, FixedRuntimeState state)
        {
            var f = new FixedFormat(Kind, state);
            return value.Magnitude.Hi == 0 && value.Magnitude.Lo <= (value.Negative ? f.MaxNegative : f.MaxPositive);
        }
        private static ulong Pack(FixedBoundsInteger value, FixedRuntimeState state, ref FixedStatus status)
        {
            var f = new FixedFormat(Kind, state);
            if (!Fits(value, state)) { status |= FixedStatus.Overflow; return f.NaN; }
            return f.Finite(value.Magnitude.Lo, value.Negative && !value.IsZero);
        }
        // Numerator is twice a coordinate. Round the lower bound down and the upper bound up.
        private static FixedBoundsInteger HalfOut(FixedBoundsInteger numerator, bool upper, ref FixedStatus status)
        {
            var q = numerator.Magnitude.ShiftRight(1);
            if ((numerator.Magnitude.Lo & 1UL) != 0)
            {
                status |= FixedStatus.Inexact;
                if (upper != numerator.Negative) q = FixedUInt128.Add(q, 1);
            }
            return new FixedBoundsInteger(q, numerator.Negative && !q.IsZero);
        }
        internal static bool Validate(FixedBoundsBits value, int dimensions, FixedRuntimeState numeric,
            FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            if (!Ready(dimensions, numeric, geometry, out status)) return false;
            if (!Finite(value.First, dimensions, numeric) || !Finite(value.Second, dimensions, numeric))
            { status = FixedStatus.InvalidDomain; return false; }
            for (int i = 0; i < dimensions; i++)
            {
                if (geometry.Settings.Representation == FixedBoundsRepresentation.MinMax)
                {
                    if (Integer(value.First.Get(i)) > Integer(value.Second.Get(i)))
                    { status = FixedStatus.InvalidDomain; return false; }
                }
                else
                {
                    long c = Integer(value.First.Get(i)), e = Integer(value.Second.Get(i));
                    if (e < 0) { status = FixedStatus.InvalidDomain; return false; }
                    // Unsigned differences cover the full finite interval without signed overflow.
                    if ((ulong)e > unchecked((ulong)c - (ulong)(long.MinValue + 2)) ||
                        (ulong)e > unchecked((ulong)(long.MaxValue - 2) - (ulong)c))
                    { status = FixedStatus.Overflow; return false; }
                }
            }
            return true;
        }
        internal static void Corners(FixedBoundsBits value, int dimensions, FixedRuntimeState numeric,
            FixedBoundsRuntimeState geometry, out FixedVectorBits min, out FixedVectorBits max)
        {
            min = value.First; max = value.Second;
            if (geometry.Settings.Representation == FixedBoundsRepresentation.MinMax) return;
            for (int i = 0; i < dimensions; i++)
            {
                // Callers validate first: both endpoints fit Signed64 and computed zero is positive.
                long c = Integer(value.First.Get(i)), e = Integer(value.Second.Get(i));
                min.Set(i, unchecked((ulong)(c - e)));
                max.Set(i, unchecked((ulong)(c + e)));
            }
        }
        internal static bool FromMinMax(FixedVectorBits min, FixedVectorBits max, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBoundsBits value, out FixedStatus status)
        {
            value = FixedBoundsBits.Invalid;
            var minMaxState = new FixedBoundsRuntimeState(FixedBoundsSettings.Default);
            if (!Ready(dimensions, numeric, geometry, out status) ||
                !Validate(new FixedBoundsBits(min, max), dimensions, numeric, minMaxState, out status)) return false;
            if (geometry.Settings.Representation == FixedBoundsRepresentation.MinMax)
            { value = new FixedBoundsBits(min, max); return true; }
            FixedVectorBits center = default, extents = default;
            for (int i = 0; i < dimensions; i++)
            {
                var lo = Coordinate(min.Get(i)); var hi = Coordinate(max.Get(i));
                ulong c = FixedBoundsInteger.Round(FixedBoundsInteger.Add(lo, hi), 2, numeric, ref status);
                if (!status.IsSuccess()) return false;
                var cq = Coordinate(c); var left = FixedBoundsInteger.Subtract(cq, lo); var right = FixedBoundsInteger.Subtract(hi, cq);
                center.Set(i, c);
                extents.Set(i, Pack(FixedBoundsInteger.Compare(left, right) > 0 ? left : right, numeric, ref status));
            }
            if (!status.IsSuccess()) return false;
            var candidate = new FixedBoundsBits(center, extents);
            if (!Validate(candidate, dimensions, numeric, geometry, out FixedStatus validation)) { status |= validation; return false; }
            value = candidate; return true;
        }
        internal static bool FromCenterExtents(FixedVectorBits center, FixedVectorBits extents, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBoundsBits value, out FixedStatus status)
        {
            value = FixedBoundsBits.Invalid;
            var centeredState = new FixedBoundsRuntimeState(new FixedBoundsSettings(FixedBoundsRepresentation.CenterExtents));
            if (!Ready(dimensions, numeric, geometry, out status) ||
                !Validate(new FixedBoundsBits(center, extents), dimensions, numeric, centeredState, out status)) return false;
            var candidate = new FixedBoundsBits(center, extents);
            if (geometry.Settings.Representation == FixedBoundsRepresentation.MinMax)
            { Corners(candidate, dimensions, numeric, centeredState, out var lo, out var hi); candidate = new FixedBoundsBits(lo, hi); }
            value = candidate; return true;
        }
        internal static bool FromCenterSize(FixedVectorBits center, FixedVectorBits size, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBoundsBits value, out FixedStatus status)
        {
            value = FixedBoundsBits.Invalid;
            if (!Ready(dimensions, numeric, geometry, out status)) return false;
            if (!Finite(center, dimensions, numeric) || !Finite(size, dimensions, numeric)) { status = FixedStatus.InvalidDomain; return false; }
            FixedVectorBits a = default, b = default;
            for (int i = 0; i < dimensions; i++)
            {
                var c = Coordinate(center.Get(i)); var s = Coordinate(size.Get(i));
                if (!NonNegative(s)) { status |= FixedStatus.InvalidDomain; return false; }
                if (geometry.Settings.Representation == FixedBoundsRepresentation.CenterExtents)
                { a.Set(i, center.Get(i)); b.Set(i, Pack(HalfOut(s, true, ref status), numeric, ref status)); }
                else
                {
                    a.Set(i, Pack(HalfOut(FixedBoundsInteger.Subtract(Twice(c), s), false, ref status), numeric, ref status));
                    b.Set(i, Pack(HalfOut(FixedBoundsInteger.Add(Twice(c), s), true, ref status), numeric, ref status));
                }
            }
            if (!status.IsSuccess()) return false;
            var candidate = new FixedBoundsBits(a, b);
            if (!Validate(candidate, dimensions, numeric, geometry, out FixedStatus validation)) { status |= validation; return false; }
            value = candidate; return true;
        }
        internal static FixedVectorBits Get(FixedBoundsBits value, FixedBoundsPart part, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            if (!Validate(value, dimensions, numeric, geometry, out status)) return FixedVectorKernel.NaN;
            bool centered = geometry.Settings.Representation == FixedBoundsRepresentation.CenterExtents;
            if (centered && part == FixedBoundsPart.Center || !centered && part == FixedBoundsPart.Min) return value.First;
            if (centered && part == FixedBoundsPart.Extents || !centered && part == FixedBoundsPart.Max) return value.Second;
            Corners(value, dimensions, numeric, geometry, out var min, out var max);
            if (part == FixedBoundsPart.Min) return min;
            if (part == FixedBoundsPart.Max) return max;
            FixedVectorBits result = default;
            for (int i = 0; i < dimensions; i++)
            {
                var lo = Coordinate(min.Get(i)); var hi = Coordinate(max.Get(i));
                var n = part == FixedBoundsPart.Center ? FixedBoundsInteger.Add(lo, hi) : FixedBoundsInteger.Subtract(hi, lo);
                result.Set(i, FixedBoundsInteger.Round(n, part == FixedBoundsPart.Size ? 1UL : 2UL, numeric, ref status));
            }
            return result;
        }
        private static bool Same(FixedVectorBits a, FixedVectorBits b, int dimensions, FixedRuntimeState numeric)
        {
            for (int i = 0; i < dimensions; i++) if (Integer(a.Get(i)) != Integer(b.Get(i))) return false;
            return true;
        }
        internal static bool Set(FixedBoundsBits original, FixedBoundsPart part, FixedVectorBits input, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBoundsBits result, out FixedStatus status)
        {
            result = original;
            if (!Validate(original, dimensions, numeric, geometry, out status)) return false;
            if (!Finite(input, dimensions, numeric)) { status = FixedStatus.InvalidDomain; return false; }
            var current = Get(original, part, dimensions, numeric, geometry, out var currentStatus);
            if (currentStatus.IsSuccess() && Same(current, input, dimensions, numeric)) return true;
            Corners(original, dimensions, numeric, geometry, out var lo, out var hi);
            FixedBoundsBits candidate;
            if (part == FixedBoundsPart.Min || part == FixedBoundsPart.Max)
            {
                if (!FromMinMax(part == FixedBoundsPart.Min ? input : lo, part == FixedBoundsPart.Max ? input : hi,
                    dimensions, numeric, geometry, out candidate, out status)) return false;
            }
            else if (geometry.Settings.Representation == FixedBoundsRepresentation.CenterExtents)
            {
                bool ok = part == FixedBoundsPart.Size ? FromCenterSize(original.First, input, dimensions, numeric, geometry, out candidate, out status) :
                    FromCenterExtents(part == FixedBoundsPart.Center ? input : original.First,
                        part == FixedBoundsPart.Extents ? input : original.Second, dimensions, numeric, geometry, out candidate, out status);
                if (!ok) return false;
            }
            else
            {
                FixedVectorBits a = default, b = default;
                for (int i = 0; i < dimensions; i++)
                {
                    var min = Coordinate(lo.Get(i)); var max = Coordinate(hi.Get(i)); var v = Coordinate(input.Get(i));
                    var center2 = part == FixedBoundsPart.Center ? Twice(v) : FixedBoundsInteger.Add(min, max);
                    var width = part == FixedBoundsPart.Center ? FixedBoundsInteger.Subtract(max, min) : part == FixedBoundsPart.Size ? v : Twice(v);
                    if (!NonNegative(width)) { status |= FixedStatus.InvalidDomain; return false; }
                    a.Set(i, Pack(HalfOut(FixedBoundsInteger.Subtract(center2, width), false, ref status), numeric, ref status));
                    b.Set(i, Pack(HalfOut(FixedBoundsInteger.Add(center2, width), true, ref status), numeric, ref status));
                }
                if (!status.IsSuccess()) return false;
                candidate = new FixedBoundsBits(a, b);
            }
            if (!Validate(candidate, dimensions, numeric, geometry, out FixedStatus validation)) { status |= validation; return false; }
            result = candidate; return true;
        }
        internal static bool Expand(FixedBoundsBits original, FixedVectorBits amount, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBoundsBits result, out FixedStatus status)
        {
            result = original;
            if (!Validate(original, dimensions, numeric, geometry, out status)) return false;
            if (!Finite(amount, dimensions, numeric)) { status = FixedStatus.InvalidDomain; return false; }
            if (Same(amount, default, dimensions, numeric)) return true;
            Corners(original, dimensions, numeric, geometry, out var min, out var max);
            var candidate = original;
            for (int i = 0; i < dimensions; i++)
            {
                var lo = Coordinate(min.Get(i)); var hi = Coordinate(max.Get(i)); var delta = Coordinate(amount.Get(i));
                var width = FixedBoundsInteger.Add(FixedBoundsInteger.Subtract(hi, lo), delta);
                if (!NonNegative(width)) { status |= FixedStatus.InvalidDomain; return false; }
                if (geometry.Settings.Representation == FixedBoundsRepresentation.CenterExtents)
                    candidate.Second.Set(i, Pack(HalfOut(width, true, ref status), numeric, ref status));
                else
                {
                    candidate.First.Set(i, Pack(HalfOut(FixedBoundsInteger.Subtract(Twice(lo), delta), false, ref status), numeric, ref status));
                    candidate.Second.Set(i, Pack(HalfOut(FixedBoundsInteger.Add(Twice(hi), delta), true, ref status), numeric, ref status));
                }
            }
            if (!status.IsSuccess()) return false;
            if (!Validate(candidate, dimensions, numeric, geometry, out FixedStatus validation)) { status |= validation; return false; }
            result = candidate; return true;
        }
        internal static bool Encapsulate(FixedBoundsBits original, FixedVectorBits min, FixedVectorBits max, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBoundsBits result, out FixedStatus status)
        {
            result = original;
            if (!Validate(original, dimensions, numeric, geometry, out status)) return false;
            if (!Finite(min, dimensions, numeric) || !Finite(max, dimensions, numeric)) { status = FixedStatus.InvalidDomain; return false; }
            Corners(original, dimensions, numeric, geometry, out var lo, out var hi);
            var a = lo; var b = hi;
            for (int i = 0; i < dimensions; i++)
            {
                a.Set(i, FixedScalarKernel.MinMax(lo.Get(i), min.Get(i), false, false, false, Kind, numeric));
                b.Set(i, FixedScalarKernel.MinMax(hi.Get(i), max.Get(i), true, false, false, Kind, numeric));
            }
            if (Same(a, lo, dimensions, numeric) && Same(b, hi, dimensions, numeric)) return true;
            if (!FromMinMax(a, b, dimensions, numeric, geometry, out var candidate, out status)) return false;
            result = candidate; return true;
        }
        internal static bool Contains(FixedBoundsBits value, FixedVectorBits point, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out bool contains, out FixedStatus status)
        {
            contains = false;
            if (!Validate(value, dimensions, numeric, geometry, out status)) return false;
            if (!Finite(point, dimensions, numeric)) { status = FixedStatus.InvalidDomain; return false; }
            Corners(value, dimensions, numeric, geometry, out var lo, out var hi);
            for (int i = 0; i < dimensions; i++)
                if (Integer(point.Get(i)) < Integer(lo.Get(i)) || Integer(point.Get(i)) > Integer(hi.Get(i))) return true;
            contains = true; return true;
        }
        internal static bool Intersects(FixedBoundsBits a, FixedBoundsBits b, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out bool intersects, out FixedStatus status)
        {
            intersects = false;
            if (!Validate(a, dimensions, numeric, geometry, out status) || !Validate(b, dimensions, numeric, geometry, out status)) return false;
            Corners(a, dimensions, numeric, geometry, out var amin, out var amax);
            Corners(b, dimensions, numeric, geometry, out var bmin, out var bmax);
            for (int i = 0; i < dimensions; i++)
                if (Integer(amin.Get(i)) > Integer(bmax.Get(i)) || Integer(amax.Get(i)) < Integer(bmin.Get(i))) return true;
            intersects = true; return true;
        }
        internal static FixedVectorBits ClosestPoint(FixedBoundsBits value, FixedVectorBits point, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            if (!Validate(value, dimensions, numeric, geometry, out status)) return FixedVectorKernel.NaN;
            if (!Finite(point, dimensions, numeric)) { status = FixedStatus.InvalidDomain; return FixedVectorKernel.NaN; }
            Corners(value, dimensions, numeric, geometry, out var lo, out var hi);
            var result = point;
            for (int i = 0; i < dimensions; i++)
            {
                if (Integer(point.Get(i)) < Integer(lo.Get(i))) result.Set(i, lo.Get(i));
                else if (Integer(point.Get(i)) > Integer(hi.Get(i))) result.Set(i, hi.Get(i));
            }
            return result;
        }
        internal static ulong SqrDistance(FixedBoundsBits value, FixedVectorBits point, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedStatus status)
        {
            if (!TrySqrDistanceRaw(value, point, dimensions, numeric, geometry, out var sum, out status))
                return FixedVectorKernel.NaN.X;
            // Three squares can require 130 bits. Keep the wide fallback for that full-range case.
            if (sum.Get(4) == 0)
                return FixedBoundsInteger.Round(new FixedBoundsInteger(new FixedUInt128(sum.Low,
                    (ulong)sum.Get(2) | ((ulong)sum.Get(3) << 32))), numeric.Scale64, numeric, ref status);
            return FixedVectorKernel.RoundRatio(new FixedQ(sum), FixedBigUInt.From(numeric.Scale64), numeric, ref status);
        }
        /// <summary>Unquantized squared raw distance; up to 130 significant bits in three dimensions.</summary>
        internal static bool TrySqrDistanceRaw(FixedBoundsBits value, FixedVectorBits point, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, out FixedBigUInt sum, out FixedStatus status)
        {
            sum = default;
            var closest = ClosestPoint(value, point, dimensions, numeric, geometry, out status);
            if (!status.IsSuccess()) return false;
            FixedUInt128 low = default;
            uint carry = 0;
            for (int i = 0; i < dimensions; i++)
            {
                long a = Integer(point.Get(i)), b = Integer(closest.Get(i));
                ulong delta = a >= b ? unchecked((ulong)a - (ulong)b) : unchecked((ulong)b - (ulong)a);
                var square = FixedUInt128.Multiply(delta, delta, numeric.Settings.ArithmeticMode);
                low = FixedUInt128.Add(low, square, out bool overflow);
                if (overflow) carry++;
            }
            sum = FixedBigUInt.From(low);
            sum.Set(4, carry);
            return true;
        }
        internal struct Fraction
        {
            internal FixedQ Numerator;
            internal ulong Denominator;
            internal Fraction(FixedQ n, ulong d) { Numerator = n; Denominator = d; }
        }
        internal static int Compare(Fraction a, Fraction b)
        {
            // Raw endpoint differences fit ulong, including segments spanning the whole finite interval.
            if (a.Numerator.Magnitude.BitLength <= 64 && b.Numerator.Magnitude.BitLength <= 64)
                return FixedBoundsInteger.Compare(
                    new FixedBoundsInteger(FixedUInt128.Multiply(a.Numerator.Magnitude.Low, b.Denominator, FixedArithmeticMode.Portable), a.Numerator.Negative),
                    new FixedBoundsInteger(FixedUInt128.Multiply(b.Numerator.Magnitude.Low, a.Denominator, FixedArithmeticMode.Portable), b.Numerator.Negative));
            return FixedQ.Compare(
                new FixedQ(FixedBigUInt.Multiply(a.Numerator.Magnitude, FixedBigUInt.From(b.Denominator)), a.Numerator.Negative),
                new FixedQ(FixedBigUInt.Multiply(b.Numerator.Magnitude, FixedBigUInt.From(a.Denominator)), b.Numerator.Negative));
        }
        internal static bool IntersectRay(FixedBoundsBits value, FixedVectorBits origin, FixedVectorBits direction, int dimensions,
            FixedRuntimeState numeric, FixedBoundsRuntimeState geometry, bool computeDistance,
            out bool hit, out ulong distance, out FixedStatus status)
        {
            hit = false; distance = 0;
            if (!Validate(value, dimensions, numeric, geometry, out status)) return false;
            if (!Finite(origin, dimensions, numeric) || !Finite(direction, dimensions, numeric)) { status = FixedStatus.InvalidDomain; return false; }
            Corners(value, dimensions, numeric, geometry, out var min, out var max);
            if (!IntersectCorners(min, max, origin, Raw(direction.X, numeric), Raw(direction.Y, numeric),
                Raw(direction.Z, numeric), dimensions, numeric, out var enter, out _, out _)) return true;
            if (computeDistance)
            {
                var scaled = new FixedBoundsInteger(FixedUInt128.Multiply(enter.Numerator.Magnitude.Low, numeric.Scale64,
                    numeric.Settings.ArithmeticMode), enter.Numerator.Negative);
                distance = FixedBoundsInteger.Round(scaled, enter.Denominator, numeric, ref status);
                if (!status.IsSuccess()) return false;
            }
            hit = true; return true;
        }
        /// <summary>Exact slabs on validated corners. Direction can be a wide endpoint difference for segments.</summary>
        internal static bool IntersectCorners(FixedVectorBits min, FixedVectorBits max, FixedVectorBits origin,
            FixedQ dx, FixedQ dy, FixedQ dz, int dimensions, FixedRuntimeState numeric,
            out Fraction enter, out Fraction exit, out bool hasExit)
        {
            enter = new Fraction(default, 1); exit = default; hasExit = false;
            for (int i = 0; i < dimensions; i++)
            {
                var o = Raw(origin.Get(i), numeric); var d = i == 0 ? dx : i == 1 ? dy : dz;
                var lo = Raw(min.Get(i), numeric); var hi = Raw(max.Get(i), numeric);
                if (d.IsZero) { if (FixedQ.Compare(o, lo) < 0 || FixedQ.Compare(o, hi) > 0) return false; continue; }
                var n = FixedQ.Subtract(d.Negative ? hi : lo, o); var f = FixedQ.Subtract(d.Negative ? lo : hi, o);
                if (d.Negative) { n = n.Negated; f = f.Negated; }
                var near = new Fraction(n, d.Magnitude.Low); var far = new Fraction(f, d.Magnitude.Low);
                if (Compare(near, enter) > 0) enter = near;
                if (!hasExit || Compare(far, exit) < 0) { exit = far; hasExit = true; }
                if (Compare(enter, exit) > 0) return false;
            }
            return true;
        }
    }
}
