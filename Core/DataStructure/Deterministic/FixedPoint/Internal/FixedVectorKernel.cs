namespace LegendaryTools.FixedPoint
{
    internal struct FixedVectorBits
    {
        internal ulong X, Y, Z;
        internal FixedVectorBits(ulong x, ulong y, ulong z = 0) { X = x; Y = y; Z = z; }
        internal ulong Get(int index) => index == 0 ? X : index == 1 ? Y : Z;
        internal void Set(int index, ulong value) { if (index == 0) X = value; else if (index == 1) Y = value; else Z = value; }
    }

    // FixedQ is also used here as an exact signed integer when fractionBits is zero.
    internal struct FixedVectorQ
    {
        internal FixedQ X, Y, Z;
        internal FixedVectorQ(FixedQ x, FixedQ y, FixedQ z = default) { X = x; Y = y; Z = z; }
        internal FixedQ Get(int index) => index == 0 ? X : index == 1 ? Y : Z;
        internal bool IsZero => X.IsZero && Y.IsZero && Z.IsZero;
    }

    internal static partial class FixedVectorKernel
    {
        private const FixedKind Kind = FixedKind.Signed64;
        internal static FixedVectorBits NaN => new FixedVectorBits(0x8000000000000000UL, 0x8000000000000000UL, 0x8000000000000000UL);
        private static bool Initialized(FixedRuntimeState state, out FixedStatus status)
        { status = state.IsInitialized ? FixedStatus.None : FixedStatus.Uninitialized; return state.IsInitialized; }
        private static bool Finite(FixedVectorBits a, int dimensions, FixedRuntimeState state)
        {
            var f = new FixedFormat(Kind, state);
            for (int i = 0; i < dimensions; i++) if (!f.Decode(a.Get(i)).IsFinite) return false;
            return true;
        }
        private static bool RequireFinite(FixedVectorBits a, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Initialized(state, out status)) return false;
            if (Finite(a, dimensions, state)) return true;
            status = FixedStatus.InvalidDomain; return false;
        }
        private static FixedQ Raw(ulong bits, FixedRuntimeState state)
        { FixedDecoded v = new FixedFormat(Kind, state).Decode(bits); return new FixedQ(FixedBigUInt.From(v.Magnitude), v.Negative); }
        private static FixedVectorQ Raw(FixedVectorBits v, FixedRuntimeState state) => new FixedVectorQ(Raw(v.X, state), Raw(v.Y, state), Raw(v.Z, state));
        private static FixedQ Product(FixedQ a, FixedQ b, FixedRuntimeState state)
        {
            FixedBigUInt magnitude = a.Magnitude.BitLength <= 64 && b.Magnitude.BitLength <= 64
                ? FixedBigUInt.From(FixedUInt128.Multiply(a.Magnitude.Low, b.Magnitude.Low, state.Settings.ArithmeticMode))
                : FixedBigUInt.Multiply(a.Magnitude, b.Magnitude);
            return new FixedQ(magnitude, a.Negative ^ b.Negative);
        }
        private static FixedQ Sum(FixedQ a, FixedQ b, FixedRounding rounding)
        {
            FixedQ sum = FixedQ.Add(a, b);
            if (sum.IsZero && a.Negative != b.Negative) sum.Negative = rounding == FixedRounding.TowardNegativeInfinity;
            return sum;
        }
        private static FixedQ RawDot(FixedVectorQ a, FixedVectorQ b, int dimensions, FixedRuntimeState state)
        {
            FixedQ sum = Sum(Product(a.X, b.X, state), Product(a.Y, b.Y, state), state.Settings.Rounding);
            return dimensions == 2 ? sum : Sum(sum, Product(a.Z, b.Z, state), state.Settings.Rounding);
        }
        private static FixedVectorQ RawCross(FixedVectorQ a, FixedVectorQ b, FixedRuntimeState state) => new FixedVectorQ(
            Sum(Product(a.Y, b.Z, state), Product(a.Z, b.Y, state).Negated, state.Settings.Rounding),
            Sum(Product(a.Z, b.X, state), Product(a.X, b.Z, state).Negated, state.Settings.Rounding),
            Sum(Product(a.X, b.Y, state), Product(a.Y, b.X, state).Negated, state.Settings.Rounding));
        private static FixedBigUInt Squares(FixedVectorQ a, int dimensions, FixedRuntimeState state) => RawDot(a, a, dimensions, state).Magnitude;
        private static FixedVectorQ SubtractRaw(FixedVectorQ a, FixedVectorQ b, FixedRounding rounding) => new FixedVectorQ(
            Sum(a.X, b.X.Negated, rounding), Sum(a.Y, b.Y.Negated, rounding), Sum(a.Z, b.Z.Negated, rounding));

        // Exact rational rounding, including decimal midpoint ties and all overflow policies.
        internal static ulong RoundRatio(FixedQ numerator, FixedBigUInt denominator, FixedRuntimeState state, ref FixedStatus status)
        {
            FixedBigUInt q = FixedBigUInt.DivRem(numerator.Magnitude, denominator, out FixedBigUInt rem);
            if (!rem.IsZero)
            {
                status |= FixedStatus.Inexact;
                FixedRounding rounding = state.Settings.Rounding;
                int comparison = FixedBigUInt.Compare(FixedBigUInt.ShiftLeft(rem, 1), denominator);
                bool increment = rounding == FixedRounding.TowardPositiveInfinity && !numerator.Negative ||
                    rounding == FixedRounding.TowardNegativeInfinity && numerator.Negative ||
                    (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) &&
                    (comparison > 0 || comparison == 0 && (rounding == FixedRounding.AwayFromZero || q.Bit(0)));
                if (increment) q = FixedBigUInt.Add(q, FixedBigUInt.From(1));
            }
            return FixedMathKernel.PackBig(q, numerator.Negative, new FixedFormat(Kind, state), state.Settings, ref status);
        }

        // Round sqrt(numerator/denominator) by comparing squared midpoint boundaries.
        // No rounded magnitude is used as the divisor of normalization.
        internal static ulong RoundRootRatio(FixedBigUInt numerator, FixedBigUInt denominator, bool negative,
            FixedRuntimeState state, ref FixedStatus status)
        {
            FixedBigUInt root = FixedBigUInt.Sqrt(FixedBigUInt.Divide(numerator, denominator));
            FixedBigUInt square = FixedBigUInt.Multiply(FixedBigUInt.Multiply(root, root), denominator);
            if (FixedBigUInt.Compare(square, numerator) != 0)
            {
                status |= FixedStatus.Inexact;
                FixedRounding rounding = state.Settings.Rounding;
                FixedBigUInt midpoint = FixedBigUInt.Add(FixedBigUInt.ShiftLeft(root, 1), FixedBigUInt.From(1));
                int comparison = FixedBigUInt.Compare(FixedBigUInt.ShiftLeft(numerator, 2),
                    FixedBigUInt.Multiply(FixedBigUInt.Multiply(midpoint, midpoint), denominator));
                bool increment = rounding == FixedRounding.TowardPositiveInfinity && !negative ||
                    rounding == FixedRounding.TowardNegativeInfinity && negative ||
                    (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) &&
                    (comparison > 0 || comparison == 0 && (rounding == FixedRounding.AwayFromZero || root.Bit(0)));
                if (increment) root = FixedBigUInt.Add(root, FixedBigUInt.From(1));
            }
            return FixedMathKernel.PackBig(root, negative, new FixedFormat(Kind, state), state.Settings, ref status);
        }

        internal static FixedVectorBits Binary(FixedVectorBits a, FixedVectorBits b, int dimensions, FixedBinary operation,
            FixedRuntimeState state, out FixedStatus status)
        {
            FixedVectorBits result = default; status = FixedStatus.None;
            for (int i = 0; i < dimensions; i++)
            { result.Set(i, FixedKernel.Binary(a.Get(i), b.Get(i), Kind, state, operation, out FixedStatus s)); status |= s; }
            return result;
        }
        internal static FixedVectorBits Negate(FixedVectorBits a, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            FixedVectorBits result = default; status = FixedStatus.None;
            for (int i = 0; i < dimensions; i++)
            { result.Set(i, FixedKernel.Negate(a.Get(i), Kind, state, out FixedStatus s)); status |= s; }
            return result;
        }
        internal static ulong Dot(FixedVectorBits a, FixedVectorBits b, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Initialized(state, out status)) return NaN.X;
            if (Finite(a, dimensions, state) && Finite(b, dimensions, state))
                return RoundRatio(RawDot(Raw(a, state), Raw(b, state), dimensions, state), FixedBigUInt.From(state.Scale64), state, ref status);
            FixedVectorBits products = Binary(a, b, dimensions, FixedBinary.Multiply, state, out status);
            ulong result = FixedKernel.Binary(products.X, products.Y, Kind, state, FixedBinary.Add, out FixedStatus s); status |= s;
            if (dimensions == 3) { result = FixedKernel.Binary(result, products.Z, Kind, state, FixedBinary.Add, out s); status |= s; }
            return result;
        }
        internal static FixedVectorBits Cross(FixedVectorBits a, FixedVectorBits b, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(a, 3, state, out status)) return NaN;
            if (!Finite(b, 3, state)) { status |= FixedStatus.InvalidDomain; return NaN; }
            FixedVectorQ cross = RawCross(Raw(a, state), Raw(b, state), state);
            FixedBigUInt scale = FixedBigUInt.From(state.Scale64); FixedVectorBits result = default;
            for (int i = 0; i < 3; i++) result.Set(i, RoundRatio(cross.Get(i), scale, state, ref status));
            return result;
        }
        internal static ulong Magnitude(FixedVectorBits a, int dimensions, bool squared, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Initialized(state, out status)) return NaN.X;
            var f = new FixedFormat(Kind, state); bool infinite = false;
            for (int i = 0; i < dimensions; i++)
            {
                FixedDecoded v = f.Decode(a.Get(i));
                if (v.IsNaN) { status = FixedStatus.InvalidDomain; return f.NaN; }
                infinite |= !v.IsFinite;
            }
            if (infinite) return f.PositiveInfinity;
            FixedBigUInt sum = Squares(Raw(a, state), dimensions, state);
            return squared ? RoundRatio(new FixedQ(sum), FixedBigUInt.From(state.Scale64), state, ref status) :
                RoundRootRatio(sum, FixedBigUInt.From(1), false, state, ref status);
        }
        internal static FixedVectorBits Normalize(FixedVectorBits a, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(a, dimensions, state, out status)) return NaN;
            return NormalizeRaw(Raw(a, state), dimensions, state.Scale64, state, ref status);
        }
        private static FixedVectorBits NormalizeRaw(FixedVectorQ a, int dimensions, ulong lengthRaw, FixedRuntimeState state, ref FixedStatus status)
        {
            FixedBigUInt sum = Squares(a, dimensions, state); if (sum.IsZero) return default;
            FixedVectorBits result = default;
            for (int i = 0; i < dimensions; i++)
            {
                FixedQ component = a.Get(i);
                FixedBigUInt scaled = FixedBigUInt.Multiply(component.Magnitude, FixedBigUInt.From(lengthRaw));
                result.Set(i, RoundRootRatio(FixedBigUInt.Multiply(scaled, scaled), sum, component.Negative, state, ref status));
            }
            return result;
        }
        internal static ulong Distance(FixedVectorBits a, FixedVectorBits b, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            if (!Initialized(state, out status)) return NaN.X;
            if (!Finite(a, dimensions, state) || !Finite(b, dimensions, state))
            {
                FixedVectorBits difference = Binary(b, a, dimensions, FixedBinary.Subtract, state, out status);
                ulong result = Magnitude(difference, dimensions, false, state, out FixedStatus s); status |= s; return result;
            }
            FixedVectorQ delta = SubtractRaw(Raw(b, state), Raw(a, state), state.Settings.Rounding);
            return RoundRootRatio(Squares(delta, dimensions, state), FixedBigUInt.From(1), false, state, ref status);
        }
        internal static FixedVectorBits Lerp(FixedVectorBits a, FixedVectorBits b, ulong amount, int dimensions, bool clamp,
            FixedRuntimeState state, out FixedStatus status)
        {
            FixedVectorBits result = default; status = FixedStatus.None;
            var f = new FixedFormat(Kind, state); FixedDecoded t = f.Decode(amount);
            if (state.IsInitialized && t.IsFinite && Finite(a, dimensions, state) && Finite(b, dimensions, state))
            {
                if (t.IsZero || clamp && t.Negative) return a;
                if (!t.Negative && (t.Magnitude == f.Scale || clamp && t.Magnitude > f.Scale)) return b;
            }
            for (int i = 0; i < dimensions; i++)
            { result.Set(i, FixedScalarKernel.Evaluate(a.Get(i), b.Get(i), amount, clamp ? FixedScalar.Lerp : FixedScalar.LerpUnclamped, Kind, state, out FixedStatus s)); status |= s; }
            return result;
        }
        internal static FixedVectorBits Project(FixedVectorBits vector, FixedVectorBits normal, bool plane, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(vector, 3, state, out status)) return NaN;
            if (!Finite(normal, 3, state)) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedVectorQ v = Raw(vector, state), n = Raw(normal, state);
            FixedBigUInt denominator = Squares(n, 3, state);
            if (denominator.IsZero) return plane ? vector : default;
            FixedQ dot = RawDot(v, n, 3, state); FixedVectorBits result = default;
            for (int i = 0; i < 3; i++)
            {
                FixedQ numerator = Product(n.Get(i), dot, state);
                if (plane) numerator = Sum(Product(v.Get(i), new FixedQ(denominator), state), numerator.Negated, state.Settings.Rounding);
                result.Set(i, RoundRatio(numerator, denominator, state, ref status));
            }
            return result;
        }
        internal static FixedVectorBits Reflect(FixedVectorBits vector, FixedVectorBits normal, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(vector, dimensions, state, out status)) return NaN;
            if (!Finite(normal, dimensions, state)) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedVectorQ v = Raw(vector, state), n = Raw(normal, state); FixedQ dot = RawDot(v, n, dimensions, state);
            FixedBigUInt scaleSquared = FixedBigUInt.From(FixedUInt128.Multiply(state.Scale64, state.Scale64, state.Settings.ArithmeticMode));
            FixedVectorBits result = default;
            for (int i = 0; i < dimensions; i++)
            {
                FixedQ numerator = Sum(Product(v.Get(i), new FixedQ(scaleSquared), state), Product(n.Get(i), dot, state).Shifted(1).Negated, state.Settings.Rounding);
                result.Set(i, RoundRatio(numerator, scaleSquared, state, ref status));
            }
            return result;
        }
        internal static FixedVectorBits ClampMagnitude(FixedVectorBits vector, ulong limit, int dimensions, FixedRuntimeState state, out FixedStatus status)
        {
            if (!RequireFinite(vector, dimensions, state, out status)) return NaN;
            var f = new FixedFormat(Kind, state); FixedDecoded length = f.Decode(limit);
            if (length.Class == FixedClass.PositiveInfinity) return vector;
            if (!length.IsFinite || length.Negative && !length.IsZero) { status = FixedStatus.InvalidDomain; return NaN; }
            FixedVectorQ v = Raw(vector, state);
            if (FixedBigUInt.Compare(Squares(v, dimensions, state), Product(Raw(limit, state), Raw(limit, state), state).Magnitude) <= 0) return vector;
            return NormalizeRaw(v, dimensions, length.Magnitude, state, ref status);
        }
    }
}
